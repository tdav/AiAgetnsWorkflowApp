using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MagenticWorkflowApp.Plugins;

/// <summary>
/// Web-search plugin backed by Tavily API. Exposed to AIAgents through
/// <see cref="IAgentPlugin.AsAITools"/>; not registered as an SK function.
/// </summary>
public sealed class TavilySearchPlugin : IAgentPlugin
{
    private static readonly JsonSerializerOptions ResponseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptions<TavilyConfiguration> _options;
    private readonly ILogger<TavilySearchPlugin> _logger;

    public TavilySearchPlugin(
        IHttpClientFactory httpFactory,
        IOptions<TavilyConfiguration> options,
        ILogger<TavilySearchPlugin> logger)
    {
        _httpFactory = httpFactory;
        _options = options;
        _logger = logger;
    }

    public string Name => "TavilySearchPlugin";

    public IEnumerable<AITool> AsAITools()
    {
        yield return AIFunctionFactory.Create(
            WebSearchAsync,
            name: "web_search",
            description: "Search the public web via Tavily and return up to N JSON-formatted results with title, url and snippet.");
    }

    [Description("Search the public web for the given query. Returns JSON with 'answer' (string) and 'results' (array of {title,url,snippet}).")]
    public async Task<string> WebSearchAsync(
        [Description("The natural-language search query.")] string query,
        [Description("Maximum results to return (1..10). Defaults to configuration value.")] int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "{\"error\":\"query is empty\"}";
        }

        var cfg = _options.Value;
        if (string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            _logger.LogError("Tavily:ApiKey is not configured. Set it via 'dotnet user-secrets set \"Tavily:ApiKey\" \"tvly-...\"'");
            return "{\"error\":\"Tavily:ApiKey is not configured\"}";
        }

        var requested = Math.Clamp(maxResults ?? cfg.MaxResults, 1, 10);

        var payload = new
        {
            api_key = cfg.ApiKey,
            query = query,
            search_depth = cfg.SearchDepth,
            max_results = requested,
            include_answer = cfg.IncludeAnswer
        };

        try
        {
            using var client = _httpFactory.CreateClient("tavily");
            client.Timeout = TimeSpan.FromSeconds(60);

            using var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            _logger.LogInformation("Tavily search: query='{Query}', maxResults={Max}", query, requested);

            using var response = await client.PostAsync(cfg.Endpoint, content, cancellationToken).ConfigureAwait(false);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Tavily HTTP {Code}: {Body}", (int)response.StatusCode, raw);
                return JsonSerializer.Serialize(new { error = $"tavily http {(int)response.StatusCode}", body = Truncate(raw, 500) });
            }

            return NormalizeResponse(raw, query);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tavily search failed for query '{Query}'", query);
            return JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    private static string NormalizeResponse(string raw, string query)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            string? answer = null;
            if (root.TryGetProperty("answer", out var ansEl) && ansEl.ValueKind == JsonValueKind.String)
            {
                answer = ansEl.GetString();
            }

            var results = new List<ResearchSource>();
            if (root.TryGetProperty("results", out var resultsEl) && resultsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in resultsEl.EnumerateArray())
                {
                    var title = GetStringOrEmpty(item, "title");
                    var url = GetStringOrEmpty(item, "url");
                    var snippet = GetStringOrEmpty(item, "content");
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        results.Add(new ResearchSource(title, url, Truncate(snippet, 400)));
                    }
                }
            }

            return JsonSerializer.Serialize(new
            {
                query,
                answer,
                results
            }, ResponseOptions);
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { query, error = "tavily returned non-json", body = Truncate(raw, 500) });
        }
    }

    private static string GetStringOrEmpty(JsonElement el, string property)
        => el.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? string.Empty
            : string.Empty;

    private static string Truncate(string? s, int maxLength)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= maxLength ? s : s.Substring(0, maxLength) + "…";
    }
}
