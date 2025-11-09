using System.Collections.Generic;

namespace MagenticWorkflowApp.Models;

/// <summary>
/// Configuration for individual agent
/// </summary>
public class AgentConfiguration
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string ModelId { get; set; } = "gpt-4";
    public List<string> Tools { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}
