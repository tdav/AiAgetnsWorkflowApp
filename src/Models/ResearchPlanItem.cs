namespace MagenticWorkflowApp.Models;

/// <summary>
/// One sub-question produced by the Planner agent. Each item is dispatched
/// to a Researcher agent. Init-only properties guarantee non-null defaults
/// when deserialized from a JSON object that omits keys (which LLMs do).
/// </summary>
public sealed record ResearchPlanItem
{
    public string SubQuestion { get; init; } = string.Empty;
    public List<string> SearchHints { get; init; } = new();
}
