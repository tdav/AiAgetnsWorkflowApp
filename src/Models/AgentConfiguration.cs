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
    public List<string> McpServers { get; set; } = new();
    public List<string> Plugins { get; set; } = new();
    public bool EnableThinking { get; set; } = false;
    public int ThinkingBudgetTokens { get; set; } = 1024;

    /// <summary>Optional cap on model output tokens; null keeps provider default.</summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>Optional sampling temperature; null keeps provider default.</summary>
    public float? Temperature { get; set; }
}
