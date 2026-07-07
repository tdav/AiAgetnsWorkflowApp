using System.Text.Json;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgetnsWorkflow.Tests.Services;

public class SelectionFunctionTests
{
    private static KeywordSelectionFunction Keyword() =>
        new(NullLogger<KeywordSelectionFunction>.Instance);

    private static ConditionalEdgeConfiguration Edge(params string[] toOptions) => new()
    {
        From = "TicketClassifierAgent",
        ToOptions = toOptions.ToList(),
        SelectionFunction = "classify_ticket_type",
    };

    [Test]
    [Arguments("The ticket is technical: driver crash.", "TechnicalSupportAgent")]
    [Arguments("Category: billing (confidence 0.93)", "BillingSupportAgent")]
    [Arguments("This is a general question about hours.", "GeneralInquiryAgent")]
    public void SelectTarget_MatchesDerivedStem(string output, string expected)
    {
        var edge = Edge("TechnicalSupportAgent", "BillingSupportAgent", "GeneralInquiryAgent");

        Keyword().SelectTarget(edge, output).Should().Be(expected);
    }

    [Test]
    public void SelectTarget_NoMatch_FallsBackToFirstOption()
    {
        var edge = Edge("TechnicalSupportAgent", "BillingSupportAgent");

        Keyword().SelectTarget(edge, "completely unrelated output").Should().Be("TechnicalSupportAgent");
    }

    [Test]
    public void SelectTarget_ExplicitKeywordMap_WinsOverStems()
    {
        var edge = Edge("TechnicalSupportAgent", "BillingSupportAgent");
        edge.Parameters["keywords"] = JsonSerializer.SerializeToElement(
            new Dictionary<string, string> { ["refund"] = "BillingSupportAgent" });

        Keyword().SelectTarget(edge, "Customer asks about a refund").Should().Be("BillingSupportAgent");
    }

    [Test]
    [Arguments("TechnicalSupportAgent", "technical")]
    [Arguments("BillingSupportAgent", "billing")]
    [Arguments("GeneralInquiryAgent", "general")]
    [Arguments("Agent", "agent")]
    public void Stem_TakesFirstCamelCaseToken(string name, string expected)
    {
        KeywordSelectionFunction.Stem(name).Should().Be(expected);
    }

    [Test]
    public void Registry_ResolvesByNameCaseInsensitive_AndFallsBack()
    {
        var keyword = Keyword();
        var registry = new SelectionFunctionRegistry(
            new ISelectionFunction[] { keyword },
            NullLogger<SelectionFunctionRegistry>.Instance);

        registry.Resolve("KEYWORDMATCH").Should().BeSameAs(keyword);
        registry.Resolve("classify_ticket_type").Should().BeSameAs(keyword); // unknown → default
        registry.Resolve(null).Should().BeSameAs(keyword);
    }

    [Test]
    public void Registry_WithoutDefaultFunction_Throws()
    {
        var act = () => new SelectionFunctionRegistry(
            Array.Empty<ISelectionFunction>(),
            NullLogger<SelectionFunctionRegistry>.Instance);

        act.Should().Throw<InvalidOperationException>();
    }
}
