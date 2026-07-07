using AiAgetnsWorkflow.Tests.Fakes;
using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgetnsWorkflow.Tests.Services;

public class TokenTrimmingChatClientTests
{
    private static TokenTrimmingChatClient CreateSut(FakeChatClient inner, ContextBudgetConfiguration budget)
        => new(inner, () => budget, NullLogger<TokenTrimmingChatClient>.Instance);

    private static List<ChatMessage> BuildHistory(int middleCount)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "system prompt"),
            new(ChatRole.User, "first task message"),
        };
        for (var i = 0; i < middleCount; i++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"mid-{i}"));
        }
        return messages;
    }

    [Test]
    public async Task TrimAsync_ToolResultOverBudget_IsTruncatedWithMarker()
    {
        var budget = new ContextBudgetConfiguration
        {
            MaxToolResultTokens = 10,
            CharsPerToken = 4.0,
            Strategy = ContextBudgetConfiguration.TruncateStrategy,
            // deterministic chars math for exact assertions below
            Tokenizer = ContextBudgetConfiguration.CharsTokenizerName,
        };
        var inner = new FakeChatClient();
        using var sut = CreateSut(inner, budget);

        var longResult = new string('a', 200); // 50 tokens > 10
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("call-1", longResult) }),
        };

        var trimmed = await sut.TrimAsync(messages, CancellationToken.None);

        var content = trimmed[0].Contents.OfType<FunctionResultContent>().Single();
        var text = content.Result!.ToString()!;
        text.Should().StartWith(new string('a', 40));   // 10 tokens * 4 chars
        text.Should().NotContain(new string('a', 41));
        text.Should().Contain("…[truncated ~40 tokens]");
    }

    [Test]
    public async Task TrimAsync_TruncateStrategy_KeepsSystemFirstMessageAndNewestTail()
    {
        var budget = new ContextBudgetConfiguration
        {
            HistoryWindowMessages = 5,
            Strategy = ContextBudgetConfiguration.TruncateStrategy,
        };
        var inner = new FakeChatClient();
        using var sut = CreateSut(inner, budget);

        var messages = BuildHistory(middleCount: 10); // 12 messages total

        var trimmed = await sut.TrimAsync(messages, CancellationToken.None);

        trimmed.Should().HaveCount(5);
        trimmed[0].Text.Should().Be("system prompt");
        trimmed[1].Text.Should().Be("first task message");
        trimmed.Skip(2).Select(m => m.Text).Should().Equal("mid-7", "mid-8", "mid-9");
        trimmed.Should().NotContain(m => m.Text.Contains("[Summary"));
        inner.CallCount.Should().Be(0); // truncate strategy never calls the inner client
    }

    [Test]
    public async Task TrimAsync_SummarizeStrategy_InsertsSummaryProducedByInnerClient()
    {
        var budget = new ContextBudgetConfiguration
        {
            HistoryWindowMessages = 5,
            Strategy = ContextBudgetConfiguration.SummarizeStrategy,
        };
        var inner = new FakeChatClient { ResponseText = "digest of the past" };
        using var sut = CreateSut(inner, budget);

        var trimmed = await sut.TrimAsync(BuildHistory(middleCount: 10), CancellationToken.None);

        var summary = trimmed.Single(m => m.Text.StartsWith("[Summary of earlier conversation]"));
        summary.Role.Should().Be(ChatRole.User);
        summary.Text.Should().Contain("digest of the past");

        inner.CallCount.Should().Be(1);
        var summarizationRequest = inner.Requests.Single();
        summarizationRequest[0].Role.Should().Be(ChatRole.System);
        summarizationRequest[0].Text.Should().StartWith("Summarize the following conversation excerpt");
    }

    [Test]
    public async Task TrimAsync_RepeatedIdenticalCall_UsesCachedSummary()
    {
        var budget = new ContextBudgetConfiguration
        {
            HistoryWindowMessages = 5,
            Strategy = ContextBudgetConfiguration.SummarizeStrategy,
        };
        var inner = new FakeChatClient { ResponseText = "cached digest" };
        using var sut = CreateSut(inner, budget);

        var messages = BuildHistory(middleCount: 10);

        var first = await sut.TrimAsync(messages, CancellationToken.None);
        var second = await sut.TrimAsync(messages, CancellationToken.None);

        inner.CallCount.Should().Be(1); // summarizer called once, second call served from cache
        second.Single(m => m.Text.StartsWith("[Summary")).Text.Should().Contain("cached digest");
        second.Should().HaveCount(first.Count);
    }

    [Test]
    public async Task TrimAsync_UnderBudget_PassesHistoryThroughUntouched()
    {
        var budget = new ContextBudgetConfiguration(); // defaults: window 40, 32k tokens
        var inner = new FakeChatClient();
        using var sut = CreateSut(inner, budget);

        var messages = BuildHistory(middleCount: 3); // 5 short messages

        var trimmed = await sut.TrimAsync(messages, CancellationToken.None);

        trimmed.Select(m => m.Text).Should().Equal(messages.Select(m => m.Text));
        inner.CallCount.Should().Be(0);
    }
}
