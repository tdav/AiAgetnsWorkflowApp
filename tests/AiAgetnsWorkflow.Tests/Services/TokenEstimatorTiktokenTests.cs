using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;

namespace AiAgetnsWorkflow.Tests.Services;

public class TokenEstimatorTiktokenTests
{
    private static ContextBudgetConfiguration TiktokenBudget() => new()
    {
        Tokenizer = ContextBudgetConfiguration.TiktokenTokenizerName,
    };

    [Test]
    public void Estimate_Tiktoken_CountsExactCl100kTokens()
    {
        // cl100k_base: ["Hello", ",", " world", "!"]
        TokenEstimator.Estimate("Hello, world!", TiktokenBudget()).Should().Be(4);
    }

    [Test]
    public void Estimate_Tiktoken_EmptyText_ReturnsZero()
    {
        TokenEstimator.Estimate(string.Empty, TiktokenBudget()).Should().Be(0);
        TokenEstimator.Estimate(null, TiktokenBudget()).Should().Be(0);
    }

    [Test]
    public void Estimate_CharsMode_UsesHeuristic()
    {
        var budget = new ContextBudgetConfiguration
        {
            Tokenizer = ContextBudgetConfiguration.CharsTokenizerName,
            CharsPerToken = 4.0,
        };
        TokenEstimator.Estimate(new string('a', 200), budget).Should().Be(50);
    }

    [Test]
    public void GetTruncationIndex_Tiktoken_KeptPrefixFitsBudget()
    {
        var budget = TiktokenBudget();
        var text = string.Join(" ", Enumerable.Range(0, 200).Select(i => $"word{i}"));

        var index = TokenEstimator.GetTruncationIndex(text, 20, budget);

        index.Should().BeGreaterThan(0).And.BeLessThan(text.Length);
        TokenEstimator.Estimate(text[..index], budget).Should().BeLessThanOrEqualTo(20);
    }

    [Test]
    public void GetTruncationIndex_NonPositiveBudget_ReturnsZero()
    {
        TokenEstimator.GetTruncationIndex("some text", 0, TiktokenBudget()).Should().Be(0);
    }
}
