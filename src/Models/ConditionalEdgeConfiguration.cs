namespace MagenticWorkflowApp.Models;

/// <summary>
/// Configuration for conditional edge with selection logic
/// </summary>
public class ConditionalEdgeConfiguration
{
    public string From { get; set; } = string.Empty;
    public List<string> ToOptions { get; set; } = new();
    public string SelectionFunction { get; set; } = string.Empty; // Name of the function or condition
    public Dictionary<string, object> Parameters { get; set; } = new();
}
