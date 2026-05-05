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
/// Web-search plugin backed by Serper.dev (Google Search API). Exposed to AIAgents
/// through <see cref="IAgentPlugin.AsAITools"/>. Tool name <c>google_search</c> to
/// distinguish it from Tavily's <c>web_search</c> when both are wired into the same agent.
/// </summary>
public sealed class SerperSearchPlugin : IAgentPlugin
{
    private static readonly JsonSerializerOptions ResponseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly IOptions<SerperConfiguration> _options;
    private readonly ILogger<SerperSearchPlugin> _logger;

    public SerperSearchPlugin(
        IHttpClientFactory httpFactory,
        IOptions<SerperConfiguration> options,
        ILogger<SerperSearchPlugin> logger)
    {
        _httpFactory = httpFactory;
        _options = options;
        _logger = logger;
    }

    public string Name => "SerperSearchPlugin";

    public IEnumerable<AITool> AsAITools()
    {
        yield return AIFunctionFactory.Create(
            GoogleSearchAsync,
            name: "google_search",
            description: "Search Google via Serper.dev. Returns JSON with 'answerBox' (optional), 'organic' (array of {title,link,snippet}) and 'knowledgeGraph' (optional).");
    }

    [Description("Search Google via Serper.dev. Returns JSON with answerBox (optional), organic results, and knowledgeGraph (optional).")]
    public async Task<string> GoogleSearchAsync(
        [Description("The natural-language query.")] string query,
        [Description("UI/language code, e.g. 'ru' or 'en'. Defaults to configuration.")] string? language = null,
        [Description("Number of organic results (1..20). Defaults to configuration.")] int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "{\"error\":\"query is empty\"}";
        }

        var cfg = _options.Value;
        if (string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            _logger.LogError("Serper:ApiKey is not configured. Set it via 'dotnet user-secrets set \"Serper:ApiKey\" \"...\"'");
            return "{\"error\":\"Serper:ApiKey is not configured\"}";
        }

        var requested = Math.Clamp(maxResults ?? cfg.MaxResults, 1, 20);
        var hl = string.IsNullOrWhiteSpace(language) ? cfg.Language : language;

        var payload = new Dictionary<string, object?>
        {
            ["q"] = query,
            ["hl"] = hl,
            ["num"] = requested,
        };
        if (!string.IsNullOrWhiteSpace(cfg.Country))
        {
            payload["gl"] = cfg.Country;
        }

        try
        {
            using var client = _httpFactory.CreateClient("serper");
            client.Timeout = TimeSpan.FromSeconds(30);

            using var request = new HttpRequestMessage(HttpMethod.Post, cfg.Endpoint);
            request.Headers.Add("X-API-KEY", cfg.ApiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            _logger.LogInformation("Serper search: query='{Query}', hl={Hl}, num={Num}", query, hl, requested);

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Serper HTTP {Code}: {Body}", (int)response.StatusCode, raw);
                return JsonSerializer.Serialize(new { error = $"serper http {(int)response.StatusCode}", body = Truncate(raw, 500) });
            }

            return NormalizeResponse(raw, query);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Serper search failed for query '{Query}'", query);
            return JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    private static string NormalizeResponse(string raw, string query)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            string? answerBox = null;
            if (root.TryGetProperty("answerBox", out var ab) && ab.ValueKind == JsonValueKind.Object)
            {
                answerBox = ab.TryGetProperty("answer", out var a) && a.ValueKind == JsonValueKind.String
                    ? a.GetString()
                    : (ab.TryGetProperty("snippet", out var sn) && sn.ValueKind == JsonValueKind.String ? sn.GetString() : null);
            }

            string? knowledgeGraph = null;
            if (root.TryGetProperty("knowledgeGraph", out var kg) && kg.ValueKind == JsonValueKind.Object)
            {
                knowledgeGraph = kg.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
                    ? d.GetString()
                    : null;
            }

            var organic = new List<ResearchSource>();
            if (root.TryGetProperty("organic", out var organicEl) && organicEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in organicEl.EnumerateArray())
                {
                    var title = GetStringOrEmpty(item, "title");
                    var url = GetStringOrEmpty(item, "link");
                    var snippet = GetStringOrEmpty(item, "snippet");
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        organic.Add(new ResearchSource(title, url, Truncate(snippet, 400)));
                    }
                }
            }

            return JsonSerializer.Serialize(new
            {
                query,
                answerBox,
                knowledgeGraph,
                organic
            }, ResponseOptions);
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { query, error = "serper returned non-json", body = Truncate(raw, 500) });
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
