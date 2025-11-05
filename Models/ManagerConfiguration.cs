namespace MagenticWorkflowApp.Models;

/// <summary>
/// Configuration for Magentic manager
/// </summary>
public class ManagerConfiguration
{
    public string ModelId { get; set; } = "gpt-4";
    public int MaxRoundCount { get; set; } = 10;
    public int MaxStallCount { get; set; } = 3;
    public int MaxResetCount { get; set; } = 2;
    public bool EnablePlanReview { get; set; } = false;
}
