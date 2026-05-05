using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
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

    private static readonly Regex ReasoningBlockRegex = new(
        @"<\|?think\|?>[\s\S]*?<\|?/think\|?>|<think[\s\S]*?</think>|<\|think\|>[\s\S]*?<\|/think\|>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IChatCompletionService inner;
    private readonly string agentName;
    private readonly IAgentActivityLogger activity;
    private readonly bool isManager;
    private readonly bool stripReasoning;

    public LoggingChatCompletionService(
        IChatCompletionService inner,
        string agentName,
        IAgentActivityLogger activity,
        bool stripReasoning = false)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.agentName = agentName ?? throw new ArgumentNullException(nameof(agentName));
        this.activity = activity ?? throw new ArgumentNullException(nameof(activity));
        this.isManager = string.Equals(agentName, ManagerAgentName, StringComparison.Ordinal);
        this.stripReasoning = stripReasoning;
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

        if (stripReasoning)
        {
            for (int i = 0; i < result.Count; i++)
            {
                var msg = result[i];
                if (!string.IsNullOrEmpty(msg.Content))
                {
                    var stripped = StripReasoning(msg.Content!);
                    if (!ReferenceEquals(stripped, msg.Content)) msg.Content = stripped;
                }
            }
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

        var managerBuffer = isManager ? new StringBuilder() : null;
        var stripState = stripReasoning ? new StreamingStripState() : null;
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

                string? emitted = delta.Content;
                if (stripState is not null && !string.IsNullOrEmpty(emitted))
                {
                    emitted = stripState.Push(emitted!);
                    delta.Content = emitted;
                }

                if (managerBuffer is not null)
                {
                    if (!string.IsNullOrEmpty(emitted)) managerBuffer.Append(emitted);
                }
                else if (!string.IsNullOrEmpty(emitted))
                {
                    activity.OnChunk(agentName, emitted!);
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

    internal static string StripReasoning(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (text.IndexOf("think", StringComparison.OrdinalIgnoreCase) < 0) return text;
        var stripped = ReasoningBlockRegex.Replace(text, string.Empty);
        return stripped == text ? text : stripped;
    }

    internal sealed class StreamingStripState
    {
        private readonly StringBuilder pending = new();
        private bool insideThink;

        public string Push(string delta)
        {
            pending.Append(delta);
            var sb = new StringBuilder();
            int i = 0;
            var src = pending.ToString();

            while (i < src.Length)
            {
                if (!insideThink)
                {
                    int open = FindOpenTag(src, i, out int openLen);
                    if (open < 0)
                    {
                        int safe = ComputeSafeBoundary(src, i);
                        sb.Append(src, i, safe - i);
                        i = safe;
                        break;
                    }
                    sb.Append(src, i, open - i);
                    i = open + openLen;
                    insideThink = true;
                }
                else
                {
                    int close = FindCloseTag(src, i, out int closeLen);
                    if (close < 0)
                    {
                        int lastLt = src.LastIndexOf('<');
                        i = lastLt >= i ? lastLt : src.Length;
                        break;
                    }
                    i = close + closeLen;
                    insideThink = false;
                }
            }

            pending.Clear();
            if (i < src.Length) pending.Append(src, i, src.Length - i);
            return sb.ToString();
        }

        private static int FindOpenTag(string s, int start, out int len)
        {
            len = 0;
            int i1 = s.IndexOf("<think>", start, StringComparison.OrdinalIgnoreCase);
            int i2 = s.IndexOf("<|think|>", start, StringComparison.OrdinalIgnoreCase);
            int idx = MinNonNegative(i1, i2);
            if (idx == i1 && i1 >= 0) len = "<think>".Length;
            else if (idx == i2 && i2 >= 0) len = "<|think|>".Length;
            return idx;
        }

        private static int FindCloseTag(string s, int start, out int len)
        {
            len = 0;
            int i1 = s.IndexOf("</think>", start, StringComparison.OrdinalIgnoreCase);
            int i2 = s.IndexOf("<|/think|>", start, StringComparison.OrdinalIgnoreCase);
            int idx = MinNonNegative(i1, i2);
            if (idx == i1 && i1 >= 0) len = "</think>".Length;
            else if (idx == i2 && i2 >= 0) len = "<|/think|>".Length;
            return idx;
        }

        private static int ComputeSafeBoundary(string s, int from)
        {
            int last = s.LastIndexOf('<');
            if (last < from) return s.Length;
            return last;
        }

        private static int MinNonNegative(int a, int b)
        {
            if (a < 0) return b;
            if (b < 0) return a;
            return a < b ? a : b;
        }
    }
}
