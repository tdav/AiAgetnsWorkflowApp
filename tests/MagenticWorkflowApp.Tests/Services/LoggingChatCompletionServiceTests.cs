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
        CreateSut(string agentName = "AgentX")
    {
        var inner = new FakeChatCompletionService();
        var activity = new RecordingActivityLogger();
        var sut = new LoggingChatCompletionService(inner, agentName, activity);
        return (sut, inner, activity);
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
}
