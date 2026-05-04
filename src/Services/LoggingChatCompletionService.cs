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
    internal const int TextTruncationLimit = 1000;
    internal const string TruncationSuffix = "… (truncated)";
    internal const string ManagerAgentName = "Manager";

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

    public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    private static string Truncate(string text)
        => text.Length <= TextTruncationLimit ? text : text.Substring(0, TextTruncationLimit) + TruncationSuffix;
}
