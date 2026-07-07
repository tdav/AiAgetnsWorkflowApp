using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Services.Executors;
using Microsoft.Extensions.AI;

namespace AiAgetnsWorkflow.Tests.Services;

public class ConcurrentAggregationTests
{
    private static List<ChatMessage> Answer(string author, string text) => new()
    {
        new ChatMessage(ChatRole.User, "task"),
        new ChatMessage(ChatRole.Assistant, text) { AuthorName = author },
    };

    [Test]
    [Arguments("Collect", true)]
    [Arguments("collect", true)]
    [Arguments("Merge", false)]
    [Arguments("Vote", false)]
    public void ResolveAggregator_KnownStrategies(string strategy, bool expectNull)
    {
        var aggregator = ConcurrentWorkflowExecutor.ResolveAggregator(strategy);

        (aggregator is null).Should().Be(expectNull);
    }

    [Test]
    public void ResolveAggregator_UnknownStrategy_Throws()
    {
        var act = () => ConcurrentWorkflowExecutor.ResolveAggregator("Median");

        act.Should().Throw<WorkflowValidationException>().WithMessage("*Median*");
    }

    [Test]
    public void MergeAggregator_JoinsAllAnswersWithAuthorLabels()
    {
        var results = new List<List<ChatMessage>>
        {
            Answer("OptimistAgent", "Yes."),
            Answer("SkepticAgent", "Maybe."),
        };

        var merged = ConcurrentWorkflowExecutor.MergeAggregator(results);

        merged.Should().HaveCount(1);
        merged[0].Role.Should().Be(ChatRole.Assistant);
        merged[0].Text.Should().Contain("OptimistAgent: Yes.").And.Contain("SkepticAgent: Maybe.");
    }

    [Test]
    public void VoteAggregator_MajorityAnswerWins()
    {
        var results = new List<List<ChatMessage>>
        {
            Answer("A", "42"),
            Answer("B", "41"),
            Answer("C", " 42 "), // normalized comparison: whitespace/case-insensitive
        };

        var voted = ConcurrentWorkflowExecutor.VoteAggregator(results);

        voted.Should().HaveCount(1);
        voted[0].Text.Trim().Should().Be("42");
    }

    [Test]
    public void VoteAggregator_Tie_KeepsParticipantOrder()
    {
        var results = new List<List<ChatMessage>>
        {
            Answer("A", "first"),
            Answer("B", "second"),
        };

        ConcurrentWorkflowExecutor.VoteAggregator(results)[0].Text.Should().Be("first");
    }

    [Test]
    public void VoteAggregator_NoTextAnswers_ReturnsEmpty()
    {
        var results = new List<List<ChatMessage>> { new() { new ChatMessage(ChatRole.Assistant, "") } };

        ConcurrentWorkflowExecutor.VoteAggregator(results).Should().BeEmpty();
    }
}
