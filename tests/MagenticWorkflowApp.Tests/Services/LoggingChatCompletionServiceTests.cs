using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MagenticWorkflowApp.Services;
using MagenticWorkflowApp.Tests.TestDoubles;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

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

    [Test]
    public async Task NonStreaming_StripReasoning_RemovesThinkBlockFromContent()
    {
        var (sut, inner, activity) = CreateSut("AgentX", stripReasoning: true);
        inner.NonStreamingResult = new List<ChatMessageContent>
        {
            new(AuthorRole.Assistant, "before <think>secret reasoning here</think> after")
        };

        var result = await sut.GetChatMessageContentsAsync(new ChatHistory());

        result[0].Content.Should().Be("before  after");
        var completed = activity.Calls.Single(c => c.Method == "OnTurnCompleted");
        completed.Arg2.Should().Be("before  after");
    }

    [Test]
    public async Task NonStreaming_StripReasoning_RemovesGemmaPipeThinkBlock()
    {
        var (sut, inner, activity) = CreateSut("AgentX", stripReasoning: true);
        inner.NonStreamingResult = new List<ChatMessageContent>
        {
            new(AuthorRole.Assistant, "answer <|think|>chain of thought<|/think|> done")
        };

        var result = await sut.GetChatMessageContentsAsync(new ChatHistory());

        result[0].Content.Should().Be("answer  done");
    }

    [Test]
    public async Task NonStreaming_StripDisabled_KeepsReasoningIntact()
    {
        var (sut, inner, _) = CreateSut("AgentX", stripReasoning: false);
        inner.NonStreamingResult = new List<ChatMessageContent>
        {
            new(AuthorRole.Assistant, "x <think>y</think> z")
        };

        var result = await sut.GetChatMessageContentsAsync(new ChatHistory());

        result[0].Content.Should().Be("x <think>y</think> z");
    }

    [Test]
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

        collected.ToString().Should().Be("Hi  bye");
        var chunks = activity.Calls.Where(c => c.Method == "OnChunk").Select(c => c.Arg2).ToArray();
        string.Concat(chunks).Should().Be("Hi  bye");
        chunks.Should().NotContain(c => (c ?? string.Empty).Contains("internal", StringComparison.OrdinalIgnoreCase));
        chunks.Should().NotContain(c => (c ?? string.Empty).Contains("reasoning", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
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
        decision.Arg2.Should().Be("{\"plan\":\"X\"}");
    }

    [Test]
    public async Task NonStreaming_Success_EmitsTurnStartedAndCompleted()
    {
        var (sut, inner, activity) = CreateSut("AgentX");
        inner.NonStreamingResult = new List<ChatMessageContent>
        {
            new(AuthorRole.Assistant, "hello")
        };

        var result = await sut.GetChatMessageContentsAsync(new ChatHistory());

        result.Should().ContainSingle();
        result[0].Content.Should().Be("hello");
        inner.NonStreamingCallCount.Should().Be(1);

        activity.Calls.Should().SatisfyRespectively(
            c => (c.Method, c.Arg1, c.Arg2).Should().Be(("OnTurnStarted", "AgentX", (string?)null)),
            c => (c.Method, c.Arg1, c.Arg2).Should().Be(("OnTurnCompleted", "AgentX", "hello")));

        activity.Calls.Should().NotContain(c => c.Method == "OnManagerDecision");
        activity.Calls.Should().NotContain(c => c.Method == "OnExecutorFailed");
    }

    [Test]
    public async Task NonStreaming_ManagerRole_EmitsOnManagerDecisionInsteadOfTurnCompleted()
    {
        var (sut, inner, activity) = CreateSut("Manager");
        inner.NonStreamingResult = new List<ChatMessageContent>
        {
            new(AuthorRole.Assistant, "{ \"plan\": \"do X\" }")
        };

        await sut.GetChatMessageContentsAsync(new ChatHistory());

        activity.Calls.Should().SatisfyRespectively(
            c => (c.Method, c.Arg1).Should().Be(("OnTurnStarted", "Manager")),
            c => (c.Method, c.Arg1, c.Arg2).Should().Be(("OnManagerDecision", "Manager", "{ \"plan\": \"do X\" }")));
        activity.Calls.Should().NotContain(c => c.Method == "OnTurnCompleted");
    }

    [Test]
    public async Task NonStreaming_InnerThrows_EmitsOnExecutorFailedAndRethrows()
    {
        var (sut, inner, activity) = CreateSut("AgentX");
        var ex = new InvalidOperationException("boom");
        inner.ThrowOnNonStreaming = ex;

        var act = () => sut.GetChatMessageContentsAsync(new ChatHistory());
        var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        thrown.Should().BeSameAs(ex);

        activity.Calls.Should().SatisfyRespectively(
            c => (c.Method, c.Arg1).Should().Be(("OnTurnStarted", "AgentX")),
            c => (c.Method, c.Arg1).Should().Be(("OnExecutorFailed", "AgentX")));
        activity.Calls.Last().Ex.Should().BeSameAs(ex);
        activity.Calls.Should().NotContain(c => c.Method == "OnTurnCompleted");
        activity.Calls.Should().NotContain(c => c.Method == "OnManagerDecision");
    }

    [Test]
    public async Task NonStreaming_OperationCanceled_NoExecutorFailedEmission()
    {
        var (sut, inner, activity) = CreateSut("AgentX");
        inner.ThrowOnNonStreaming = new OperationCanceledException();

        var act = () => sut.GetChatMessageContentsAsync(new ChatHistory());
        await act.Should().ThrowAsync<OperationCanceledException>();

        activity.Calls.Should().ContainSingle(c => c.Method == "OnTurnStarted" && c.Arg1 == "AgentX");
        activity.Calls.Should().NotContain(c => c.Method == "OnExecutorFailed");
        activity.Calls.Should().NotContain(c => c.Method == "OnTurnCompleted");
    }

    [Test]
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

        collected.Should().Equal("Hel", "lo ", "world");
        inner.StreamingCallCount.Should().Be(1);

        activity.Calls.Should().SatisfyRespectively(
            c => (c.Method, c.Arg1).Should().Be(("OnTurnStarted", "AgentX")),
            c => (c.Method, c.Arg1, c.Arg2).Should().Be(("OnChunk", "AgentX", "Hel")),
            c => (c.Method, c.Arg1, c.Arg2).Should().Be(("OnChunk", "AgentX", "lo ")),
            c => (c.Method, c.Arg1, c.Arg2).Should().Be(("OnChunk", "AgentX", "world")),
            c => (c.Method, c.Arg1, c.Arg2).Should().Be(("OnTurnCompleted", "AgentX", (string?)null)));
    }

    [Test]
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

        collected.Should().Equal("{\"step\":", "1}");
        activity.Calls.Should().NotContain(c => c.Method == "OnChunk");
        activity.Calls.Should().NotContain(c => c.Method == "OnTurnCompleted");
        activity.Calls.Should().SatisfyRespectively(
            c => (c.Method, c.Arg1).Should().Be(("OnTurnStarted", "Manager")),
            c => (c.Method, c.Arg1, c.Arg2).Should().Be(("OnManagerDecision", "Manager", "{\"step\":1}")));
    }

    [Test]
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
        Func<Task> act = async () =>
        {
            await foreach (var d in sut.GetStreamingChatMessageContentsAsync(new ChatHistory()))
            {
                collected.Add(d.Content);
            }
        };
        var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        thrown.Should().BeSameAs(ex);
        collected.Should().Equal("first", "second");

        activity.Calls.Should().SatisfyRespectively(
            c => (c.Method, c.Arg1).Should().Be(("OnTurnStarted", "AgentX")),
            c => (c.Method, c.Arg1, c.Arg2).Should().Be(("OnChunk", "AgentX", "first")),
            c => (c.Method, c.Arg1, c.Arg2).Should().Be(("OnChunk", "AgentX", "second")),
            c => (c.Method, c.Arg1).Should().Be(("OnExecutorFailed", "AgentX")));
        activity.Calls.Last().Ex.Should().BeSameAs(ex);
        activity.Calls.Should().NotContain(c => c.Method == "OnTurnCompleted");
    }

    [Test]
    public async Task NonStreaming_LongText_TruncatesEmittedDisplayButReturnsFullToCaller()
    {
        var (sut, inner, activity) = CreateSut("AgentX");
        var longText = new string('x', 5000);
        inner.NonStreamingResult = new List<ChatMessageContent>
        {
            new(AuthorRole.Assistant, longText)
        };

        var result = await sut.GetChatMessageContentsAsync(new ChatHistory());

        result[0].Content.Should().Be(longText);
        result[0].Content!.Length.Should().Be(5000);

        var expectedDisplay = new string('x', LoggingChatCompletionService.TextTruncationLimit)
            + LoggingChatCompletionService.TruncationSuffix;
        var completedCall = activity.Calls.Single(c => c.Method == "OnTurnCompleted");
        completedCall.Arg1.Should().Be("AgentX");
        completedCall.Arg2.Should().Be(expectedDisplay);
    }

    [Test]
    public void Attributes_ReturnsInnerReference()
    {
        var (sut, inner, _) = CreateSut("AgentX");
        sut.Attributes.Should().BeSameAs(inner.Attributes);
    }
}
