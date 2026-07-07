using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;

namespace AiAgetnsWorkflow.Tests.Services;

public class DeepResearchBudgetTests
{
    private static ResearchFinding Finding(string question, int sourceCount, string summary) => new(
        question,
        Enumerable.Range(0, sourceCount)
            .Select(i => new ResearchSource($"title-{i}", $"https://example.com/{i}", "snippet"))
            .ToList(),
        summary);

    [Test]
    public void BuildFindingsDigest_Empty_ReturnsEmptyString()
    {
        DeepResearchOrchestrator.BuildFindingsDigest(Array.Empty<ResearchFinding>()).Should().BeEmpty();
    }

    [Test]
    public void BuildFindingsDigest_FormatsOneLinePerFinding()
    {
        var findings = new[]
        {
            Finding("What is X?", 2, "X is a thing.\nWith a second line."),
            Finding("What is Y?", 1, "Y explained."),
        };

        var digest = DeepResearchOrchestrator.BuildFindingsDigest(findings);

        var lines = digest.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        lines.Should().HaveCount(2);
        lines[0].Should().Be("- What is X? (2 sources): X is a thing. With a second line.");
        lines[1].Should().Be("- What is Y? (1 sources): Y explained.");
    }

    [Test]
    public void BuildFindingsDigest_LongSummary_TruncatedTo120Chars()
    {
        var digest = DeepResearchOrchestrator.BuildFindingsDigest(
            new[] { Finding("Q", 0, new string('s', 500)) });

        digest.Should().StartWith("- Q (0 sources): " + new string('s', 120));
        digest.Should().EndWith("…");
    }

    [Test]
    public void SelectFindingsWithinBudget_AllFit_KeepsEverythingInOrder()
    {
        var findings = new[]
        {
            Finding("Q1", 0, "short one"),
            Finding("Q2", 0, "short two"),
            Finding("Q3", 0, "short three"),
        };
        var budget = new ContextBudgetConfiguration { MaxInputTokens = 32000 };

        var selected = DeepResearchOrchestrator.SelectFindingsWithinBudget(findings, budget, out var omitted);

        selected.Select(f => f.SubQuestion).Should().Equal("Q1", "Q2", "Q3");
        omitted.Should().BeEmpty();
    }

    [Test]
    public void SelectFindingsWithinBudget_OverBudget_KeepsNewestPreservesOrder()
    {
        // Each finding serializes to ~4000+ chars => ~1000+ tokens; limit = 0.6 * 3700 = 2220,
        // so exactly two newest findings fit.
        var findings = new[]
        {
            Finding("Q1", 0, new string('a', 4000)),
            Finding("Q2", 0, new string('b', 4000)),
            Finding("Q3", 0, new string('c', 4000)),
        };
        var budget = new ContextBudgetConfiguration { MaxInputTokens = 3700, CharsPerToken = 4.0 };

        var selected = DeepResearchOrchestrator.SelectFindingsWithinBudget(findings, budget, out var omitted);

        selected.Select(f => f.SubQuestion).Should().Equal("Q2", "Q3"); // newest, original order
        omitted.Select(f => f.SubQuestion).Should().Equal("Q1");
    }

    [Test]
    public void SelectFindingsWithinBudget_TinyBudget_AlwaysKeepsNewestFinding()
    {
        var findings = new[]
        {
            Finding("Q1", 0, new string('a', 4000)),
            Finding("Q2", 0, new string('b', 4000)),
        };
        var budget = new ContextBudgetConfiguration { MaxInputTokens = 10, CharsPerToken = 4.0 };

        var selected = DeepResearchOrchestrator.SelectFindingsWithinBudget(findings, budget, out var omitted);

        selected.Select(f => f.SubQuestion).Should().Equal("Q2");
        omitted.Select(f => f.SubQuestion).Should().Equal("Q1");
    }
}
