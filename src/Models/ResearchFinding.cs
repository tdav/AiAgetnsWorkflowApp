namespace MagenticWorkflowApp.Models;

/// <summary>
/// Output of a single Researcher run for one ResearchPlanItem.
/// </summary>
public sealed record ResearchFinding(
    string SubQuestion,
    List<ResearchSource> Sources,
    string Summary);
