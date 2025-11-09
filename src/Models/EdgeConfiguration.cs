namespace MagenticWorkflowApp.Models;

/// <summary>
/// Configuration for edge between agents
/// </summary>
public class EdgeConfiguration
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string? Label { get; set; }
}
