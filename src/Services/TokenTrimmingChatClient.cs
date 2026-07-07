using System.Runtime.CompilerServices;
using System.Text;
using MagenticWorkflowApp.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services;

/// <summary>
/// <see cref="IChatClient"/> middleware enforcing the context budget on every model call:
/// truncates oversized tool results, keeps a sliding window over the history
/// (system prompt + first task message + newest tail) and, by default, compresses
/// the evicted middle into a short digest produced by the inner client.
/// Applied by <see cref="ChatClientProvider"/> to every chat client, so all
/// workflow types and MCP/plugin tool outputs are bounded uniformly.
/// </summary>
public sealed class TokenTrimmingChatClient : DelegatingChatClient
{
    private readonly Func<ContextBudgetConfiguration> budgetAccessor;
    private readonly ILogger logger;
    private readonly int? historyWindowOverride;
    private readonly SemaphoreSlim summaryLock = new(1, 1);

    // ponytail: one logical conversation per client instance (matches how agents are
    // built in this app); the boundary fingerprint resets the cache if that breaks.
    private string? cachedSummary;
    private string? cachedBoundaryFingerprint;

    public TokenTrimmingChatClient(
        IChatClient innerClient,
        Func<ContextBudgetConfiguration> budgetAccessor,
        ILogger<TokenTrimmingChatClient> logger,
        int? historyWindowOverride = null)
        : base(innerClient)
    {
        this.budgetAccessor = budgetAccessor;
        this.logger = logger;
        this.historyWindowOverride = historyWindowOverride;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var trimmed = await TrimAsync(messages, cancellationToken).ConfigureAwait(false);
        return await base.GetResponseAsync(trimmed, options, cancellationToken).ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var trimmed = await TrimAsync(messages, cancellationToken).ConfigureAwait(false);
        await foreach (var update in base.GetStreamingResponseAsync(trimmed, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    internal async Task<List<ChatMessage>> TrimAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var budget = budgetAccessor();
        var list = new List<ChatMessage>();
        foreach (var message in messages)
        {
            list.Add(TruncateToolResults(message, budget));
        }

        var window = historyWindowOverride ?? budget.HistoryWindowMessages;
        if (window <= 0)
        {
            window = int.MaxValue;
        }
        if (list.Count <= window && EstimateWindow(list, 0, 0, budget) <= budget.MaxInputTokens)
        {
            return list;
        }

        // Head: leading system messages + the first non-system (task) message.
        var head = 0;
        while (head < list.Count && list[head].Role == ChatRole.System)
        {
            head++;
        }
        if (head < list.Count)
        {
            head++;
        }

        // Tail: newest messages fitting the window; shrink further while over the token budget.
        var tailStart = Math.Max(head, list.Count - Math.Max(1, window - head));
        while (tailStart < list.Count - 1 && EstimateWindow(list, head, tailStart, budget) > budget.MaxInputTokens)
        {
            tailStart++;
        }
        // Never start the tail with orphaned tool results: extend the tail backwards
        // to include the assistant message that issued the tool call(s). Slight window
        // overshoot is acceptable — a split call/result pair is rejected by the API.
        while (tailStart > head && list[tailStart].Role == ChatRole.Tool)
        {
            tailStart--;
        }

        if (tailStart <= head)
        {
            return list;
        }

        var evictedCount = tailStart - head;
        var result = new List<ChatMessage>(list.Count - evictedCount + 1);
        result.AddRange(list.Take(head));

        if (budget.UseSummarization)
        {
            var summary = await SummarizeEvictedAsync(list, head, tailStart, budget, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                result.Add(new ChatMessage(ChatRole.User, "[Summary of earlier conversation]\n" + summary));
            }
        }

        result.AddRange(list.Skip(tailStart));

        logger.LogInformation(
            "Context trimmed: {Evicted} message(s) evicted, {Kept} kept, strategy {Strategy}",
            evictedCount, result.Count, budget.UseSummarization ? "summarize" : "truncate");
        return result;
    }

    private async Task<string?> SummarizeEvictedAsync(
        List<ChatMessage> list, int head, int tailStart, ContextBudgetConfiguration budget, CancellationToken cancellationToken)
    {
        var fingerprint = $"{head}:{tailStart}:{GetMessageText(list[tailStart - 1]).GetHashCode()}";

        await summaryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (fingerprint == cachedBoundaryFingerprint)
            {
                return cachedSummary;
            }

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(cachedSummary))
            {
                sb.AppendLine("[Previous summary] " + cachedSummary);
            }
            var maxChars = (int)(budget.MaxInputTokens * budget.CharsPerToken / 2);
            for (var i = head; i < tailStart && sb.Length < maxChars; i++)
            {
                var text = GetMessageText(list[i]);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }
                sb.Append(list[i].Role.Value).Append(": ").AppendLine(Truncate(text, 2000));
            }

            try
            {
                var response = await InnerClient.GetResponseAsync(
                    new[]
                    {
                        new ChatMessage(ChatRole.System,
                            "Summarize the following conversation excerpt into a compact digest that preserves " +
                            "facts, decisions, names and numbers. Reply with the summary only."),
                        new ChatMessage(ChatRole.User, sb.ToString()),
                    },
                    new ChatOptions { MaxOutputTokens = budget.SummaryMaxTokens },
                    cancellationToken).ConfigureAwait(false);

                cachedSummary = response.Text;
                cachedBoundaryFingerprint = fingerprint;
                return cachedSummary;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "History summarization failed — falling back to plain truncation for this call");
                return null;
            }
        }
        finally
        {
            summaryLock.Release();
        }
    }

    private static ChatMessage TruncateToolResults(ChatMessage message, ContextBudgetConfiguration budget)
    {
        if (budget.MaxToolResultTokens <= 0)
        {
            return message;
        }

        List<AIContent>? newContents = null;
        for (var i = 0; i < message.Contents.Count; i++)
        {
            if (message.Contents[i] is not FunctionResultContent functionResult)
            {
                continue;
            }
            var text = functionResult.Result?.ToString();
            if (text is null)
            {
                continue;
            }
            var tokens = TokenEstimator.Estimate(text, budget);
            if (tokens <= budget.MaxToolResultTokens)
            {
                continue;
            }

            var maxChars = TokenEstimator.GetTruncationIndex(text, budget.MaxToolResultTokens, budget);
            var truncated = text[..maxChars] + $"\n…[truncated ~{tokens - budget.MaxToolResultTokens} tokens]";
            newContents ??= new List<AIContent>(message.Contents);
            newContents[i] = new FunctionResultContent(functionResult.CallId, truncated);
        }

        return newContents is null
            ? message
            : new ChatMessage(message.Role, newContents) { AuthorName = message.AuthorName };
    }

    private static int EstimateWindow(List<ChatMessage> list, int head, int tailStart, ContextBudgetConfiguration budget)
    {
        var total = 0;
        for (var i = 0; i < list.Count; i++)
        {
            if (i >= head && i < tailStart)
            {
                continue; // would be evicted
            }
            total += TokenEstimator.Estimate(GetMessageText(list[i]), budget) + 8;
        }
        return total;
    }

    private static string GetMessageText(ChatMessage message)
    {
        var sb = new StringBuilder();
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent text:
                    sb.Append(text.Text);
                    break;
                case FunctionResultContent result:
                    sb.Append(result.Result?.ToString());
                    break;
                case FunctionCallContent call:
                    sb.Append(call.Name).Append(' ');
                    break;
            }
        }
        return sb.ToString();
    }

    private static string Truncate(string s, int maxLength)
        => s.Length <= maxLength ? s : s[..maxLength] + "…";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            summaryLock.Dispose();
        }
        base.Dispose(disposing);
    }
}
