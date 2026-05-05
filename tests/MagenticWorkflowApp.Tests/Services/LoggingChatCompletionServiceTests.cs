using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MagenticWorkflowApp.Services;
using MagenticWorkflowApp.Tests.TestDoubles;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;

namespace MagenticWorkflowApp.Tests.Services;

public class LoggingChatCompletionServiceTests
{
    private static (LoggingChatCompletionService sut, FakeChatCompletionService inner, RecordingActivityLogger activity)
        CreateSut(string agentName = "AgentX", bool stripReasoning = false)
    {
        var inner = new FakeChatCompletionService();
        var activity = new RecordingActivityLogger();
        var sut = new LoggingChatCompletionService(inner, agentName, activity, stripReasoning);
        return (sut, inner, activity);
    }

    [Fact]
    public async Task NonStreaming_StripReasoning_RemovesThinkBlockFromContent()
    {
        var (sut, inner, activity) = CreateSut("AgentX", stripReasoning: true);
        inner.NonStreamingResult = new List<ChatMessageContent>
        {
            new(AuthorRole.Assistant, "before <think>secret reasoning here</think> after")
        };

        var result = await sut.GetChatMessageContentsAsync(new ChatHistory());

        Assert.Equal("before  after", result[0].Content);
        var completed = activity.Calls.Single(c => c.Method == "OnTurnCompleted");
        Assert.Equal("before  after", completed.Arg2);
    }

    [Fact]
    public async Task NonStreaming_StripReasoning_RemovesGemmaPipeThinkBlock()
    {
        var (sut, inner, activity) = CreateSut("AgentX", stripReasoning: true);
        inner.NonStreamingResult = new List<ChatMessageContent>
        {
            new(AuthorRole.Assistant, "answer <|think|>chain of thought<|/think|> done")
        };

        var result = await sut.GetChatMessageContentsAsync(new ChatHistory());

        Assert.Equal("answer  done", result[0].Content);
    }

    [Fact]
    public async Task NonStreaming_StripDisabled_KeepsReasoningIntact()
    {
        var (sut, inner, _) = CreateSut("AgentX", stripReasoning: false);
        inner.NonStreamingResult = new List<ChatMessageContent>
        {
            new(AuthorRole.Assistant, "x <think>y</think> z")
        };

        var result = await sut.GetChatMessageContentsAsync(new ChatHistory());

        Assert.Equal("x <think>y</think> z", result[0].Content);
    }

    [Fact]
    public async Task Streaming_StripReasoning_OmitsTokensInsideThinkBlockEvenAcrossDeltas()
    {
        var (sut, inner, activity) = CreateSut("AgentX", stripReasoning: true);
        inner.StreamingChunks = new List<StreamingChatMessageContent>
        {
            new(AuthorRole.Assistant, "Hi <thi"),
            new(AuthorRole.Assistant, "nk>internal"),
            new(AuthorRole.Assistant, " reasoning</thi"),
            new(AuthorRole.Assistant, "nk> bye"),
        };

        var collected = new System.Text.StringBuilder();
        await foreach (var d in sut.GetStreamingChatMessageContentsAsync(new ChatHistory()))
        {
            if (!string.IsNullOrEmpty(d.Content)) collected.Append(d.Content);
        }

        Assert.Equal("Hi  bye", collected.ToString());
        var chunks = activity.Calls.Where(c => c.Method == "OnChunk").Select(c => c.Arg2).ToArray();
        Assert.Equal("Hi  bye", string.Concat(chunks));
        Assert.DoesNotContain(chunks, c => (c ?? string.Empty).Contains("internal", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(chunks, c => (c ?? string.Empty).Contains("reasoning", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Streaming_ManagerWithStrip_OnManagerDecisionExcludesReasoning()
    {
        var (sut, inner, activity) = CreateSut("Manager", stripReasoning: true);
        inner.StreamingChunks = new List<StreamingChatMessageContent>
        {
            new(AuthorRole.Assistant, "{\"plan\":\"X\"}<think>"),
            new(AuthorRole.Assistant, "private"),
            new(AuthorRole.Assistant, "</think>"),
        };

        await foreach (var _ in sut.GetStreamingChatMessageContentsAsync(new ChatHistory())) { }

        var decision = activity.Calls.Single(c => c.Method == "OnManagerDecision");
        Assert.Equal("{\"plan\":\"X\"}", decision.Arg2);
    }

    [Fact]
    public async Task NonStreaming_Success_EmitsTurnStartedAndCompleted()
    {
        var (sut, inner, activity) = CreateSut("AgentX");
        inner.NonStreamingResult = new List<ChatMessageContent>
        {
            new(AuthorRole.Assistant, "hello")
        };

        var result = await sut.GetChatMessageContentsAsync(new ChatHistory());

        Assert.Single(result);
        Assert.Equal("hello", result[0].Content);
        Assert.Equal(1, inner.NonStreamingCallCount);

        Assert.Collection(activity.Calls,
            c => Assert.Equal(("OnTurnStarted", "AgentX", null), (c.Method, c.Arg1, c.Arg2)),
            c => Assert.Equal(("OnTurnCompleted", "AgentX", "hello"), (c.Method, c.Arg1, c.Arg2)));

        Assert.DoesNotContain(activity.Calls, c => c.Method == "OnManagerDecision");
        Assert.DoesNotContain(activity.Calls, c => c.Method == "OnExecutorFailed");
    }

    [Fact]
    public async Task NonStreaming_ManagerRole_EmitsOnManagerDecisionInsteadOfTurnCompleted()
    {
        var (sut, inner, activity) = CreateSut("Manager");
        inner.NonStreamingResult = new List<ChatMessageContent>
        {
            new(AuthorRole.Assistant, "{ \"plan\": \"do X\" }")
        };

        await sut.GetChatMessageContentsAsync(new ChatHistory());

        Assert.Collection(activity.Calls,
            c => Assert.Equal(("OnTurnStarted", "Manager"), (c.Method, c.Arg1)),
            c => Assert.Equal(("OnManagerDecision", "Manager", "{ \"plan\": \"do X\" }"), (c.Method, c.Arg1, c.Arg2)));
        Assert.DoesNotContain(activity.Calls, c => c.Method == "OnTurnCompleted");
    }

    [Fact]
    public async Task NonStreaming_InnerThrows_EmitsOnExecutorFailedAndRethrows()
    {
        var (sut, inner, activity) = CreateSut("AgentX");
        var ex = new InvalidOperationException("boom");
        inner.ThrowOnNonStreaming = ex;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.GetChatMessageContentsAsync(new ChatHistory()));
        Assert.Same(ex, thrown);

        Assert.Collection(activity.Calls,
            c => Assert.Equal(("OnTurnStarted", "AgentX"), (c.Method, c.Arg1)),
            c => Assert.Equal(("OnExecutorFailed", "AgentX"), (c.Method, c.Arg1)));
        var failedCall = activity.Calls.Last();
        Assert.Same(ex, failedCall.Ex);
        Assert.DoesNotContain(activity.Calls, c => c.Method == "OnTurnCompleted");
        Assert.DoesNotContain(activity.Calls, c => c.Method == "OnManagerDecision");
    }

    [Fact]
    public async Task NonStreaming_OperationCanceled_NoExecutorFailedEmission()
    {
        var (sut, inner, activity) = CreateSut("AgentX");
        inner.ThrowOnNonStreaming = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.GetChatMessageContentsAsync(new ChatHistory()));

        Assert.Single(activity.Calls, c => c.Method == "OnTurnStarted" && c.Arg1 == "AgentX");
        Assert.DoesNotContain(activity.Calls, c => c.Method == "OnExecutorFailed");
        Assert.DoesNotContain(activity.Calls, c => c.Method == "OnTurnCompleted");
    }

    [Fact]
    public async Task Streaming_Success_EmitsChunkPerDeltaAndTurnCompletedAtEnd()
    {
        var (sut, inner, activity) = CreateSut("AgentX");
        inner.StreamingChunks = new List<StreamingChatMessageContent>
        {
            new(AuthorRole.Assistant, "Hel"),
            new(AuthorRole.Assistant, "lo "),
            new(AuthorRole.Assistant, "world"),
        };

        var collected = new List<string?>();
        await foreach (var d in sut.GetStreamingChatMessageContentsAsync(new ChatHistory()))
        {
            collected.Add(d.Content);
        }

        Assert.Equal(new[] { "Hel", "lo ", "world" }, collected);
        Assert.Equal(1, inner.StreamingCallCount);

        Assert.Collection(activity.Calls,
            c => Assert.Equal(("OnTurnStarted", "AgentX"), (c.Method, c.Arg1)),
            c => Assert.Equal(("OnChunk", "AgentX", "Hel"), (c.Method, c.Arg1, c.Arg2)),
            c => Assert.Equal(("OnChunk", "AgentX", "lo "), (c.Method, c.Arg1, c.Arg2)),
            c => Assert.Equal(("OnChunk", "AgentX", "world"), (c.Method, c.Arg1, c.Arg2)),
            c => Assert.Equal(("OnTurnCompleted", "AgentX", null), (c.Method, c.Arg1, c.Arg2)));
    }

    [Fact]
    public async Task Streaming_ManagerRole_BuffersChunksAndEmitsOnManagerDecisionAtEnd()
    {
        var (sut, inner, activity) = CreateSut("Manager");
        inner.StreamingChunks = new List<StreamingChatMessageContent>
        {
            new(AuthorRole.Assistant, "{\"step\":"),
            new(AuthorRole.Assistant, "1}"),
        };

        var collected = new List<string?>();
        await foreach (var d in sut.GetStreamingChatMessageContentsAsync(new ChatHistory()))
        {
            collected.Add(d.Content);
        }

        Assert.Equal(new[] { "{\"step\":", "1}" }, collected);
        Assert.DoesNotContain(activity.Calls, c => c.Method == "OnChunk");
        Assert.DoesNotContain(activity.Calls, c => c.Method == "OnTurnCompleted");
        Assert.Collection(activity.Calls,
            c => Assert.Equal(("OnTurnStarted", "Manager"), (c.Method, c.Arg1)),
            c => Assert.Equal(("OnManagerDecision", "Manager", "{\"step\":1}"), (c.Method, c.Arg1, c.Arg2)));
    }

    [Fact]
    public async Task Streaming_InnerThrowsMidStream_EmitsTwoChunksThenExecutorFailed()
    {
        var (sut, inner, activity) = CreateSut("AgentX");
        inner.StreamingChunks = new List<StreamingChatMessageContent>
        {
            new(AuthorRole.Assistant, "first"),
            new(AuthorRole.Assistant, "second"),
            new(AuthorRole.Assistant, "third"),
        };
        var ex = new InvalidOperationException("mid-stream boom");
        inner.ThrowAfterStreamingChunkCount = 2;
        inner.StreamingException = ex;

        var collected = new List<string?>();
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var d in sut.GetStreamingChatMessageContentsAsync(new ChatHistory()))
            {
                collected.Add(d.Content);
            }
        });
        Assert.Same(ex, thrown);
        Assert.Equal(new[] { "first", "second" }, collected);

        Assert.Collection(activity.Calls,
            c => Assert.Equal(("OnTurnStarted", "AgentX"), (c.Method, c.Arg1)),
            c => Assert.Equal(("OnChunk", "AgentX", "first"), (c.Method, c.Arg1, c.Arg2)),
            c => Assert.Equal(("OnChunk", "AgentX", "second"), (c.Method, c.Arg1, c.Arg2)),
            c => Assert.Equal(("OnExecutorFailed", "AgentX"), (c.Method, c.Arg1)));
        Assert.Same(ex, activity.Calls.Last().Ex);
        Assert.DoesNotContain(activity.Calls, c => c.Method == "OnTurnCompleted");
    }

    [Fact]
    public async Task NonStreaming_LongText_TruncatesEmittedDisplayButReturnsFullToCaller()
    {
        var (sut, inner, activity) = CreateSut("AgentX");
        var longText = new string('x', 5000);
        inner.NonStreamingResult = new List<ChatMessageContent>
        {
            new(AuthorRole.Assistant, longText)
        };

        var result = await sut.GetChatMessageContentsAsync(new ChatHistory());

        Assert.Equal(longText, result[0].Content);
        Assert.Equal(5000, result[0].Content!.Length);

        var expectedDisplay = new string('x', LoggingChatCompletionService.TextTruncationLimit)
            + LoggingChatCompletionService.TruncationSuffix;
        var completedCall = activity.Calls.Single(c => c.Method == "OnTurnCompleted");
        Assert.Equal("AgentX", completedCall.Arg1);
        Assert.Equal(expectedDisplay, completedCall.Arg2);
    }

    [Fact]
    public void Attributes_ReturnsInnerReference()
    {
        var (sut, inner, _) = CreateSut("AgentX");
        Assert.Same(inner.Attributes, sut.Attributes);
    }
}
