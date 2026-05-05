namespace MagenticWorkflowApp.Models;

/// <summary>
/// One source returned by Tavily and cited by a Researcher.
/// </summary>
public sealed record ResearchSource(
    string Title,
    string Url,
    string Snippet);
