using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MagenticWorkflowApp.Tests.TestDoubles;

internal sealed class FakeChatCompletionService : IChatCompletionService
{
    public IReadOnlyDictionary<string, object?> Attributes { get; } =
        new Dictionary<string, object?> { ["fake"] = true };

    public List<ChatMessageContent>? NonStreamingResult { get; set; }
    public Exception? ThrowOnNonStreaming { get; set; }

    public List<StreamingChatMessageContent>? StreamingChunks { get; set; }
    public int? ThrowAfterStreamingChunkCount { get; set; }
    public Exception? StreamingException { get; set; }

    public int NonStreamingCallCount { get; private set; }
    public int StreamingCallCount { get; private set; }

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        NonStreamingCallCount++;
        if (ThrowOnNonStreaming is not null)
        {
            throw ThrowOnNonStreaming;
        }
        IReadOnlyList<ChatMessageContent> result = NonStreamingResult ?? new List<ChatMessageContent>();
        return Task.FromResult(result);
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StreamingCallCount++;
        var chunks = StreamingChunks ?? new List<StreamingChatMessageContent>();
        for (int i = 0; i < chunks.Count; i++)
        {
            if (ThrowAfterStreamingChunkCount is int n && i == n && StreamingException is not null)
            {
                throw StreamingException;
            }
            yield return chunks[i];
            await Task.Yield();
        }
        if (ThrowAfterStreamingChunkCount is int m && m >= chunks.Count && StreamingException is not null)
        {
            throw StreamingException;
        }
    }
}
