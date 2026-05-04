using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MagenticWorkflowApp.Interfaces;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MagenticWorkflowApp.Services;

public sealed class LoggingChatCompletionService : IChatCompletionService
{
    public const int TextTruncationLimit = 1000;
    public const string TruncationSuffix = "… (truncated)";
    public const string ManagerAgentName = "Manager";

    private readonly IChatCompletionService inner;
    private readonly string agentName;
    private readonly IAgentActivityLogger activity;
    private readonly bool isManager;

    public LoggingChatCompletionService(
        IChatCompletionService inner,
        string agentName,
        IAgentActivityLogger activity)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.agentName = agentName ?? throw new ArgumentNullException(nameof(agentName));
        this.activity = activity ?? throw new ArgumentNullException(nameof(activity));
        this.isManager = string.Equals(agentName, ManagerAgentName, StringComparison.Ordinal);
    }

    public IReadOnlyDictionary<string, object?> Attributes => inner.Attributes;

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        activity.OnTurnStarted(agentName);

        IReadOnlyList<ChatMessageContent> result;
        try
        {
            result = await inner.GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            activity.OnExecutorFailed(agentName, ex);
            throw;
        }

        var fullText = string.Concat(result.Select(c => c.Content ?? string.Empty));
        var displayText = Truncate(fullText);

        if (isManager)
        {
            activity.OnManagerDecision(ManagerAgentName, displayText);
        }
        else
        {
            activity.OnTurnCompleted(agentName, displayText);
        }

        return result;
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        activity.OnTurnStarted(agentName);

        var managerBuffer = isManager ? new System.Text.StringBuilder() : null;
        IAsyncEnumerator<StreamingChatMessageContent> enumerator;
        try
        {
            enumerator = inner
                .GetStreamingChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            activity.OnExecutorFailed(agentName, ex);
            throw;
        }

        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    activity.OnExecutorFailed(agentName, ex);
                    throw;
                }

                if (!hasNext) break;
                var delta = enumerator.Current;
                if (managerBuffer is not null)
                {
                    if (!string.IsNullOrEmpty(delta.Content)) managerBuffer.Append(delta.Content);
                }
                else if (!string.IsNullOrEmpty(delta.Content))
                {
                    activity.OnChunk(agentName, delta.Content!);
                }
                yield return delta;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        if (isManager)
        {
            activity.OnManagerDecision(ManagerAgentName, Truncate(managerBuffer!.ToString()));
        }
        else
        {
            activity.OnTurnCompleted(agentName);
        }
    }

    private static string Truncate(string text)
        => text.Length <= TextTruncationLimit ? text : text.Substring(0, TextTruncationLimit) + TruncationSuffix;
}
