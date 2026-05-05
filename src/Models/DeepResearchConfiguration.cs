namespace MagenticWorkflowApp.Models;

/// <summary>
/// DeepResearch workflow configuration. Defines five role agents
/// (Clarifier → Planner → N×Researcher → Critic → Synthesizer)
/// and pipeline-level controls.
/// </summary>
public class DeepResearchConfiguration
{
    public AgentConfiguration Clarifier { get; set; } = new();
    public AgentConfiguration Planner { get; set; } = new();
    public AgentConfiguration Researcher { get; set; } = new();
    public AgentConfiguration Critic { get; set; } = new();
    public AgentConfiguration Synthesizer { get; set; } = new();

    public int MaxResearchIterations { get; set; } = 2;
    public int ChatReducerWindow { get; set; } = 10;
    public int MaxParallelResearchers { get; set; } = 4;
    public int MaxClarifierTurns { get; set; } = 5;

    public string SessionsDir { get; set; } = "./sessions";
    public string ReportsDir { get; set; } = "./reports";

    /// <summary>
    /// If set, attempt to resume a prior session by id.
    /// </summary>
    public string? ResumeSessionId { get; set; }
}
