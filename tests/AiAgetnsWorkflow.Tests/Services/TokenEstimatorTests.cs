using MagenticWorkflowApp.Services;

namespace AiAgetnsWorkflow.Tests.Services;

public class TokenEstimatorTests
{
    [Test]
    public void Estimate_EmptyOrNull_ReturnsZero()
    {
        TokenEstimator.Estimate(null).Should().Be(0);
        TokenEstimator.Estimate(string.Empty).Should().Be(0);
    }

    [Test]
    [Arguments(4, 1)]    // exactly one token
    [Arguments(5, 2)]    // ceiling: 5/4 -> 2
    [Arguments(8, 2)]
    [Arguments(9, 3)]
    public void Estimate_DefaultRatio_UsesCeiling(int charCount, int expectedTokens)
    {
        TokenEstimator.Estimate(new string('x', charCount)).Should().Be(expectedTokens);
    }

    [Test]
    public void Estimate_CustomCharsPerToken_IsRespected()
    {
        TokenEstimator.Estimate(new string('x', 10), charsPerToken: 2.0).Should().Be(5);
        TokenEstimator.Estimate(new string('x', 10), charsPerToken: 10.0).Should().Be(1);
    }

    [Test]
    [Arguments(0.0)]
    [Arguments(-1.5)]
    public void Estimate_NonPositiveRatio_FallsBackToDefault(double ratio)
    {
        TokenEstimator.Estimate(new string('x', 8), ratio).Should().Be(2); // 8 / 4.0
    }

    [Test]
    public void EstimateChars_NegativeCount_ReturnsZero()
    {
        TokenEstimator.EstimateChars(-5).Should().Be(0);
    }
}
