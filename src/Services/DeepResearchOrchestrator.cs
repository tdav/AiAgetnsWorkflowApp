using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services;

/// <summary>
/// Five-role research pipeline backed by Microsoft.Agents.AI agents:
/// Clarifier (interactive) → Planner (structured plan) → N×Researcher
/// (parallel web search via Tavily) → Critic (gap analysis) → Synthesizer
/// (final markdown report).
/// </summary>
public sealed class DeepResearchOrchestrator : IDeepResearchOrchestrator
{
    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex JsonArrayRegex = new(@"\[\s*[\s\S]*\]", RegexOptions.Compiled);
    private static readonly Regex JsonObjectRegex = new(@"\{\s*[\s\S]*\}", RegexOptions.Compiled);

    private readonly IAgentFactory _factory;
    private readonly IAgentPluginRegistry _pluginRegistry;
    private readonly IAgentActivityLogger _activity;
    private readonly ILogger<DeepResearchOrchestrator> _logger;

    public DeepResearchOrchestrator(
        IAgentFactory factory,
        IAgentPluginRegistry pluginRegistry,
        IAgentActivityLogger activity,
        ILogger<DeepResearchOrchestrator> logger)
    {
        _factory = factory;
        _pluginRegistry = pluginRegistry;
        _activity = activity;
        _logger = logger;
    }

    public async Task ExecuteAsync(WorkflowConfiguration config, CancellationToken cancellationToken = default)
    {
        if (config.DeepResearch is null)
        {
            throw new WorkflowValidationException(
                "DeepResearch workflow requires the 'deepResearch' configuration section.");
        }

        var dr = config.DeepResearch;
        _activity.SetWorkflowMode(WorkflowDisplayMode.Sequential);

        Directory.CreateDirectory(dr.SessionsDir);
        Directory.CreateDirectory(dr.ReportsDir);

        var sessionId = string.IsNullOrWhiteSpace(dr.ResumeSessionId)
            ? Guid.NewGuid().ToString("N")
            : dr.ResumeSessionId!;
        var sessionPath = Path.Combine(dr.SessionsDir, $"research-{sessionId}.json");

        // Build the four persistent role agents up-front. Researcher agents
        // are spawned ad-hoc per sub-question and don't share a session.
        var clarifier = _factory.BuildAgent(
            dr.Clarifier.Name, dr.Clarifier.Instructions, dr.Clarifier.ModelId,
            tools: null, enableThinking: dr.Clarifier.EnableThinking);
        var planner = _factory.BuildAgent(
            dr.Planner.Name, dr.Planner.Instructions, dr.Planner.ModelId,
            tools: null, enableThinking: dr.Planner.EnableThinking);
        var critic = _factory.BuildAgent(
            dr.Critic.Name, dr.Critic.Instructions, dr.Critic.ModelId,
            tools: null, enableThinking: dr.Critic.EnableThinking);
        var synthesizer = _factory.BuildAgent(
            dr.Synthesizer.Name, dr.Synthesizer.Instructions, dr.Synthesizer.ModelId,
            tools: null, enableThinking: dr.Synthesizer.EnableThinking);

        // Resolve Researcher tools (Tavily plugin, optionally extra ones from agent.Plugins).
        var researcherTools = ResolvePluginTools(dr.Researcher);

        var envelope = await LoadEnvelopeAsync(sessionPath, cancellationToken).ConfigureAwait(false);

        var clarifierSession = await ResumeOrCreateSessionAsync(clarifier, envelope?.Clarifier, cancellationToken).ConfigureAwait(false);
        var plannerSession = await ResumeOrCreateSessionAsync(planner, envelope?.Planner, cancellationToken).ConfigureAwait(false);
        var criticSession = await ResumeOrCreateSessionAsync(critic, envelope?.Critic, cancellationToken).ConfigureAwait(false);
        var synthSession = await ResumeOrCreateSessionAsync(synthesizer, envelope?.Synthesizer, cancellationToken).ConfigureAwait(false);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"🔎 DeepResearch session: {sessionId}{(envelope is null ? " (new)" : " (resumed)")}");
        Console.ResetColor();

        // 1. Clarifier — interactive dialog
        var refinedTopic = await RunClarifierLoopAsync(
            clarifier, clarifierSession, config.Task, dr.MaxClarifierTurns, cancellationToken).ConfigureAwait(false);

        // 2. Planner — produce a list of sub-questions
        var planItems = await RunPlannerAsync(planner, plannerSession, refinedTopic, cancellationToken).ConfigureAwait(false);

        // 3-4. Research → Critic loop (max iterations)
        var allFindings = new List<ResearchFinding>();
        var lastCriticReport = string.Empty;

        var currentItems = planItems;
        for (int iter = 0; iter < dr.MaxResearchIterations; iter++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"── Research iteration {iter + 1}/{dr.MaxResearchIterations} ──");
            Console.ResetColor();

            var iterFindings = await RunResearchersAsync(
                dr.Researcher, researcherTools, currentItems,
                dr.MaxParallelResearchers, cancellationToken).ConfigureAwait(false);

            allFindings.AddRange(iterFindings);

            lastCriticReport = await RunCriticAsync(
                critic, criticSession, allFindings, cancellationToken).ConfigureAwait(false);

            if (CriticIsDone(lastCriticReport))
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Critic accepts coverage — proceeding to synthesis.");
                Console.ResetColor();
                break;
            }

            // Otherwise extract gap items and continue if iterations remain.
            var gapItems = ExtractGapItems(lastCriticReport);
            if (gapItems.Count == 0 || iter == dr.MaxResearchIterations - 1)
            {
                break;
            }

            currentItems = gapItems;
        }

        // 5. Synthesizer
        var report = await RunSynthesizerAsync(
            synthesizer, synthSession, refinedTopic, allFindings, lastCriticReport, cancellationToken).ConfigureAwait(false);

        // Persist outputs.
        var reportPath = Path.Combine(dr.ReportsDir, $"research-{sessionId}.md");
        await File.WriteAllTextAsync(reportPath, report, cancellationToken).ConfigureAwait(false);

        envelope = new SessionEnvelope
        {
            SessionId = sessionId,
            RefinedTopic = refinedTopic,
            Plan = planItems,
            Findings = allFindings,
            CriticReport = lastCriticReport,
            Clarifier = await clarifier.SerializeSessionAsync(clarifierSession, cancellationToken: cancellationToken).ConfigureAwait(false),
            Planner = await planner.SerializeSessionAsync(plannerSession, cancellationToken: cancellationToken).ConfigureAwait(false),
            Critic = await critic.SerializeSessionAsync(criticSession, cancellationToken: cancellationToken).ConfigureAwait(false),
            Synthesizer = await synthesizer.SerializeSessionAsync(synthSession, cancellationToken: cancellationToken).ConfigureAwait(false),
        };

        await File.WriteAllTextAsync(sessionPath,
            JsonSerializer.Serialize(envelope, EnvelopeJsonOptions),
            cancellationToken).ConfigureAwait(false);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"📝 Report:  {Path.GetFullPath(reportPath)}");
        Console.WriteLine($"💾 Session: {Path.GetFullPath(sessionPath)}");
        Console.ResetColor();

        _activity.OnWorkflowOutput($"DeepResearch complete. Report: {reportPath}");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Stages
    // ──────────────────────────────────────────────────────────────────────

    private async Task<string> RunClarifierLoopAsync(
        AIAgent clarifier, AgentSession session, string initialTask, int maxTurns, CancellationToken ct)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("── Stage 1/5: Clarifier ──");
        Console.ResetColor();

        var prompt = "INITIAL_TOPIC: " + initialTask;
        for (int turn = 0; turn < maxTurns; turn++)
        {
            ct.ThrowIfCancellationRequested();
            _activity.OnTurnStarted(clarifier.Name ?? "Clarifier");
            var response = await clarifier.RunAsync(prompt, session, cancellationToken: ct).ConfigureAwait(false);
            var text = response.Text ?? string.Empty;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[Clarifier] {text}");
            Console.ResetColor();
            _activity.OnTurnCompleted(clarifier.Name ?? "Clarifier", text);

            var readyIdx = text.IndexOf("READY:", StringComparison.OrdinalIgnoreCase);
            if (readyIdx >= 0)
            {
                var refined = text.Substring(readyIdx + "READY:".Length).Trim();
                if (string.IsNullOrWhiteSpace(refined)) refined = initialTask;
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Topic refined: {refined}");
                Console.ResetColor();
                return refined;
            }

            Console.Write("you> ");
            var userInput = await ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(userInput))
            {
                // Empty answer signals "use the topic as-is".
                return initialTask;
            }
            prompt = userInput;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("⚠ Reached MaxClarifierTurns without READY — using last user input as topic.");
        Console.ResetColor();
        return prompt;
    }

    private async Task<List<ResearchPlanItem>> RunPlannerAsync(
        AIAgent planner, AgentSession session, string refinedTopic, CancellationToken ct)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("── Stage 2/5: Planner ──");
        Console.ResetColor();

        var prompt =
            "Decompose the topic into 3-6 specific sub-questions for web research. " +
            "Return ONLY a strict JSON array of objects with shape " +
            "{\"subQuestion\": string, \"searchHints\": [string]}." +
            "\n\nTOPIC:\n" + refinedTopic;

        _activity.OnTurnStarted(planner.Name ?? "Planner");
        var response = await planner.RunAsync(prompt, session, cancellationToken: ct).ConfigureAwait(false);
        var text = response.Text ?? string.Empty;
        _activity.OnTurnCompleted(planner.Name ?? "Planner", text);

        var items = TolerantParseList<ResearchPlanItem>(text);
        // Normalize: LLMs sometimes omit fields, leaving SearchHints null even after deserialization.
        items = items.Select(i => i with { SearchHints = i.SearchHints ?? new List<string>() }).ToList();

        if (items.Count == 0)
        {
            // Fallback: treat the refined topic as a single sub-question.
            items = new List<ResearchPlanItem>
            {
                new ResearchPlanItem { SubQuestion = refinedTopic }
            };
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Planner produced {items.Count} sub-question(s):");
        for (int i = 0; i < items.Count; i++)
        {
            Console.WriteLine($"   {i + 1}. {items[i].SubQuestion}");
        }
        Console.ResetColor();

        return items;
    }

    private async Task<List<ResearchFinding>> RunResearchersAsync(
        AgentConfiguration template,
        IReadOnlyList<AITool> tools,
        IReadOnlyList<ResearchPlanItem> items,
        int maxParallelism,
        CancellationToken ct)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"── Stage 3/5: Researchers ({items.Count} parallel) ──");
        Console.ResetColor();

        var bag = new ConcurrentBag<ResearchFinding>();
        var degree = Math.Max(1, maxParallelism);

        await Parallel.ForEachAsync(
            items,
            new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = ct },
            async (item, token) =>
            {
                var slug = Slug(item.SubQuestion);
                var researcher = _factory.BuildAgent(
                    name: $"{template.Name}-{slug}",
                    instructions: template.Instructions,
                    modelId: template.ModelId,
                    tools: tools,
                    enableThinking: template.EnableThinking);

                _activity.OnTurnStarted(researcher.Name ?? "Researcher");
                var session = await researcher.CreateSessionAsync(cancellationToken: token).ConfigureAwait(false);

                var hintList = item.SearchHints ?? new List<string>();
                var hints = hintList.Count > 0
                    ? "\nHINTS: " + string.Join("; ", hintList)
                    : string.Empty;

                var prompt =
                    "SUB_QUESTION: " + item.SubQuestion + hints +
                    "\n\nUse the web_search tool one or more times to gather sources, then return " +
                    "ONLY a strict JSON object {\"summary\": string, \"sources\": [{\"title\": string, \"url\": string, \"snippet\": string}]}.";

                try
                {
                    var resp = await researcher.RunAsync(prompt, session, cancellationToken: token).ConfigureAwait(false);
                    var text = resp.Text ?? string.Empty;
                    _activity.OnTurnCompleted(researcher.Name ?? "Researcher", text);

                    var finding = ParseFinding(text, item);
                    bag.Add(finding);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Researcher failed for sub-question '{Q}'", item.SubQuestion);
                    bag.Add(new ResearchFinding(item.SubQuestion, new List<ResearchSource>(),
                        $"(researcher failed: {ex.Message})"));
                }
            }).ConfigureAwait(false);

        var findings = bag.ToList();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Collected {findings.Count} finding(s).");
        Console.ResetColor();
        return findings;
    }

    private async Task<string> RunCriticAsync(
        AIAgent critic, AgentSession session, IReadOnlyList<ResearchFinding> findings, CancellationToken ct)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("── Stage 4/5: Critic ──");
        Console.ResetColor();

        var prompt =
            "Evaluate the research coverage for the topic. Score 0-10. " +
            "If the score is >= 8, reply ONLY with the literal string \"OK\". " +
            "Otherwise reply ONLY with a JSON array of {subQuestion, searchHints} objects " +
            "describing the gaps that should be researched in the next iteration." +
            "\n\nFINDINGS:\n" + JsonSerializer.Serialize(findings, EnvelopeJsonOptions);

        _activity.OnTurnStarted(critic.Name ?? "Critic");
        var resp = await critic.RunAsync(prompt, session, cancellationToken: ct).ConfigureAwait(false);
        var text = resp.Text ?? string.Empty;
        _activity.OnTurnCompleted(critic.Name ?? "Critic", text);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[Critic] {Truncate(text, 600)}");
        Console.ResetColor();
        return text;
    }

    private async Task<string> RunSynthesizerAsync(
        AIAgent synthesizer, AgentSession session, string refinedTopic,
        IReadOnlyList<ResearchFinding> findings, string criticReport, CancellationToken ct)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("── Stage 5/5: Synthesizer ──");
        Console.ResetColor();

        var sb = new StringBuilder();
        sb.AppendLine("TOPIC: " + refinedTopic);
        sb.AppendLine();
        sb.AppendLine("FINDINGS (JSON):");
        sb.AppendLine(JsonSerializer.Serialize(findings, EnvelopeJsonOptions));
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(criticReport))
        {
            sb.AppendLine("CRITIC_REPORT:");
            sb.AppendLine(criticReport);
            sb.AppendLine();
        }
        sb.AppendLine(
            "Produce a final markdown report with: " +
            "an introduction, one section per sub-question, inline citations [n] referring " +
            "to a Sources list at the end (n = 1..N over unique URLs).");

        _activity.OnTurnStarted(synthesizer.Name ?? "Synthesizer");
        var resp = await synthesizer.RunAsync(sb.ToString(), session, cancellationToken: ct).ConfigureAwait(false);
        var text = resp.Text ?? "(no synthesis produced)";
        _activity.OnTurnCompleted(synthesizer.Name ?? "Synthesizer", text);
        return text;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Persistence
    // ──────────────────────────────────────────────────────────────────────

    private static async Task<SessionEnvelope?> LoadEnvelopeAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var raw = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<SessionEnvelope>(raw, EnvelopeJsonOptions);
        }
        catch (JsonException)
        {
            // Corrupt envelope — start fresh.
            return null;
        }
    }

    private static async Task<AgentSession> ResumeOrCreateSessionAsync(
        AIAgent agent, JsonElement? state, CancellationToken ct)
    {
        if (state is JsonElement element && element.ValueKind != JsonValueKind.Undefined && element.ValueKind != JsonValueKind.Null)
        {
            try
            {
                return await agent.DeserializeSessionAsync(element, cancellationToken: ct).ConfigureAwait(false);
            }
            catch
            {
                // Fall through to fresh session if old state is incompatible.
            }
        }
        return await agent.CreateSessionAsync(cancellationToken: ct).ConfigureAwait(false);
    }

    private sealed class SessionEnvelope
    {
        public string SessionId { get; set; } = string.Empty;
        public string RefinedTopic { get; set; } = string.Empty;
        public List<ResearchPlanItem> Plan { get; set; } = new();
        public List<ResearchFinding> Findings { get; set; } = new();
        public string CriticReport { get; set; } = string.Empty;
        public JsonElement? Clarifier { get; set; }
        public JsonElement? Planner { get; set; }
        public JsonElement? Critic { get; set; }
        public JsonElement? Synthesizer { get; set; }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private IReadOnlyList<AITool> ResolvePluginTools(AgentConfiguration agentConfig)
    {
        var pluginNames = agentConfig.Plugins.Count > 0
            ? agentConfig.Plugins
            : agentConfig.Tools; // fallback: tools entries can hold plugin names too

        var tools = new List<AITool>();
        foreach (var name in pluginNames)
        {
            if (_pluginRegistry.TryGet(name, out var plugin))
            {
                tools.AddRange(plugin!.AsAITools());
            }
            else
            {
                _logger.LogWarning("Plugin '{Plugin}' referenced by Researcher is not registered — skipping.", name);
            }
        }

        // Fallback: if no search plugin was resolved from config, try Serper first, then Tavily,
        // so the Researcher always has at least one web-search tool.
        if (!tools.Any())
        {
            if (_pluginRegistry.TryGet("SerperSearchPlugin", out var serper))
            {
                tools.AddRange(serper!.AsAITools());
            }
            else if (_pluginRegistry.TryGet("TavilySearchPlugin", out var tavily))
            {
                tools.AddRange(tavily!.AsAITools());
            }
        }
        return tools;
    }

    private static List<T> TolerantParseList<T>(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<T>();
        try
        {
            return JsonSerializer.Deserialize<List<T>>(text, EnvelopeJsonOptions) ?? new List<T>();
        }
        catch (JsonException) { /* fall through */ }

        var match = JsonArrayRegex.Match(text);
        if (match.Success)
        {
            try
            {
                return JsonSerializer.Deserialize<List<T>>(match.Value, EnvelopeJsonOptions) ?? new List<T>();
            }
            catch (JsonException) { /* ignore */ }
        }
        return new List<T>();
    }

    private static ResearchFinding ParseFinding(string text, ResearchPlanItem item)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ResearchFinding(item.SubQuestion, new List<ResearchSource>(), "(empty response)");
        }

        // Try parsing as a strict object first.
        var candidate = ExtractObjectJson(text);
        if (candidate is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                var root = doc.RootElement;
                var summary = root.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String
                    ? s.GetString() ?? string.Empty : string.Empty;
                var sources = new List<ResearchSource>();
                if (root.TryGetProperty("sources", out var srcEl) && srcEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var src in srcEl.EnumerateArray())
                    {
                        var title = src.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? string.Empty : string.Empty;
                        var url = src.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? string.Empty : string.Empty;
                        var snippet = src.TryGetProperty("snippet", out var sn) && sn.ValueKind == JsonValueKind.String ? sn.GetString() ?? string.Empty : string.Empty;
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            sources.Add(new ResearchSource(title, url, snippet));
                        }
                    }
                }
                return new ResearchFinding(item.SubQuestion, sources, summary);
            }
            catch (JsonException) { /* ignore */ }
        }

        // Fallback: keep raw text as the summary, no sources.
        return new ResearchFinding(item.SubQuestion, new List<ResearchSource>(), text);
    }

    private static string? ExtractObjectJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
        {
            return trimmed;
        }
        var match = JsonObjectRegex.Match(text);
        return match.Success ? match.Value : null;
    }

    private static bool CriticIsDone(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim().Trim('\'', '"');
        return trimmed.Equals("OK", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("OK", StringComparison.OrdinalIgnoreCase) && trimmed.Length <= 4;
    }

    private static List<ResearchPlanItem> ExtractGapItems(string text)
    {
        var items = TolerantParseList<ResearchPlanItem>(text);
        return items
            .Where(i => !string.IsNullOrWhiteSpace(i.SubQuestion))
            .Select(i => i with { SearchHints = i.SearchHints ?? new List<string>() })
            .ToList();
    }

    private static string Slug(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "q";
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (ch == ' ' || ch == '-' || ch == '_')
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            if (sb.Length >= 24) break;
        }
        return sb.Length == 0 ? "q" : sb.ToString().Trim('-');
    }

    private static string Truncate(string s, int maxLength)
        => string.IsNullOrEmpty(s) || s.Length <= maxLength ? s ?? string.Empty : s.Substring(0, maxLength) + "…";

    private static Task<string?> ReadLineAsync(CancellationToken ct)
    {
        // Console.ReadLine is synchronous; offload so cancellation can interrupt outer awaits.
        return Task.Run(() => Console.ReadLine(), ct);
    }
}
