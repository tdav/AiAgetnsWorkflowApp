namespace MagenticWorkflowApp.Models;

/// <summary>
/// Configuration for orchestration patterns (Sequential, Concurrent, Conditional)
/// </summary>
public class OrchestrationConfiguration
{
    public string? StartAgent { get; set; }
    public List<EdgeConfiguration> Edges { get; set; } = new();
    public List<ConditionalEdgeConfiguration> ConditionalEdges { get; set; } = new();
    public ConcurrentConfiguration? Concurrent { get; set; }
}
