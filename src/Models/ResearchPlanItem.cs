namespace MagenticWorkflowApp.Models;

/// <summary>
/// One sub-question produced by the Planner agent. Each item is dispatched
/// to a Researcher agent.
/// </summary>
public sealed record ResearchPlanItem(
    string SubQuestion,
    List<string> SearchHints);
