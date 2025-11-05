namespace MagenticWorkflowApp.Models;

/// <summary>
/// Configuration for concurrent execution
/// </summary>
public class ConcurrentConfiguration
{
    public List<string> ParticipantAgents { get; set; } = new();
    public string AggregationStrategy { get; set; } = "Collect"; // Collect, Merge, Vote
}
