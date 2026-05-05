namespace MagenticWorkflowApp.Models;

/// <summary>
/// Configuration for Serper.dev web search API. Bound from "Serper" section in appsettings.json.
/// API key should be supplied via User Secrets:
///   dotnet user-secrets set "Serper:ApiKey" "..." --project src
/// </summary>
public class SerperConfiguration
{
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Default search endpoint. Other Serper endpoints (news, scholar, images) can be selected
    /// at call time via the plugin's <c>endpoint</c> argument.
    /// </summary>
    public string Endpoint { get; set; } = "https://google.serper.dev/search";

    /// <summary>UI / language code, e.g. "ru", "en". Maps to Serper "hl" parameter.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Geo target, e.g. "us", "ru". Maps to Serper "gl" parameter (optional).</summary>
    public string? Country { get; set; }

    /// <summary>Number of organic results to return per query (1..20).</summary>
    public int MaxResults { get; set; } = 5;
}
