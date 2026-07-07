using System.Text.Json;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services;

/// <summary>
/// Default selection function: routes by keyword occurrence in the agent output.
/// Keywords come from the edge's "keywords" parameter (map keyword → target agent),
/// or are derived from each target's name (first CamelCase token, e.g.
/// "TechnicalSupportAgent" → "technical"). No match → first option.
/// </summary>
public sealed class KeywordSelectionFunction : ISelectionFunction
{
    public const string DefaultName = "keywordMatch";

    private readonly ILogger<KeywordSelectionFunction> logger;

    public KeywordSelectionFunction(ILogger<KeywordSelectionFunction> logger)
    {
        this.logger = logger;
    }

    public string Name => DefaultName;

    public string? SelectTarget(ConditionalEdgeConfiguration edge, string lastAgentOutput)
    {
        var output = lastAgentOutput ?? string.Empty;

        // 1. Explicit mapping: "parameters": { "keywords": { "billing": "BillingSupportAgent", ... } }
        if (edge.Parameters.TryGetValue("keywords", out var raw)
            && raw is JsonElement { ValueKind: JsonValueKind.Object } map)
        {
            foreach (var prop in map.EnumerateObject())
            {
                var target = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
                if (target is not null
                    && edge.ToOptions.Contains(target, StringComparer.Ordinal)
                    && output.Contains(prop.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return target;
                }
            }
        }

        // 2. Derived stems from target names.
        foreach (var option in edge.ToOptions)
        {
            if (output.Contains(Stem(option), StringComparison.OrdinalIgnoreCase))
            {
                return option;
            }
        }

        logger.LogDebug(
            "Selection function '{Function}': no keyword match in output from '{From}' — no decision",
            edge.SelectionFunction, edge.From);
        return null;
    }

    /// <summary>First CamelCase token of an agent name, lowercased ("BillingSupportAgent" → "billing").</summary>
    internal static string Stem(string agentName)
    {
        if (string.IsNullOrEmpty(agentName))
        {
            return agentName ?? string.Empty;
        }
        var end = 1;
        while (end < agentName.Length && !char.IsUpper(agentName[end]))
        {
            end++;
        }
        var stem = agentName[..end].ToLowerInvariant();
        return stem == "agent" ? agentName.ToLowerInvariant() : stem;
    }
}
