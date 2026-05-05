namespace MagenticWorkflowApp.Models;

/// <summary>
/// Root configuration for workflow loaded from JSON
/// </summary>
public class WorkflowConfiguration
{
    public string WorkflowType { get; set; } = "Magentic"; // Magentic, Sequential, Concurrent, Conditional, DeepResearch
    public string Task { get; set; } = string.Empty;
    public ManagerConfiguration Manager { get; set; } = new();
    public List<AgentConfiguration> Agents { get; set; } = new();
    public OrchestrationConfiguration? Orchestration { get; set; }
    public Dictionary<string, string> Settings { get; set; } = new();
    public List<McpServerConfiguration> McpServers { get; set; } = new();
    public DeepResearchConfiguration? DeepResearch { get; set; }
}
