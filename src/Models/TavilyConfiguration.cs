namespace MagenticWorkflowApp.Models;

/// <summary>
/// Configuration for Tavily web search API. Bound from "Tavily" section in appsettings.json.
/// API key should be supplied via User Secrets: dotnet user-secrets set "Tavily:ApiKey" "tvly-..."
/// </summary>
public class TavilyConfiguration
{
    public string ApiKey { get; set; } = string.Empty;
    public int MaxResults { get; set; } = 5;
    public string SearchDepth { get; set; } = "advanced"; // "basic" | "advanced"
    public bool IncludeAnswer { get; set; } = true;
    public string Endpoint { get; set; } = "https://api.tavily.com/search";
}
