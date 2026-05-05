using MagenticWorkflowApp.Models;

namespace MagenticWorkflowApp.Interfaces;

/// <summary>
/// Orchestrator for the DeepResearch workflow:
/// Clarifier → Planner → N×Researcher → Critic → Synthesizer.
/// </summary>
public interface IDeepResearchOrchestrator
{
    Task ExecuteAsync(WorkflowConfiguration config, CancellationToken cancellationToken = default);
}
