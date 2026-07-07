using MagenticWorkflowApp.Models;

namespace MagenticWorkflowApp.Interfaces;

/// <summary>
/// Routing strategy for conditional edges: picks one of
/// <see cref="ConditionalEdgeConfiguration.ToOptions"/> based on the routing
/// agent's last output. Implementations registered in DI are resolvable by
/// name from workflow JSON ("selectionFunction").
/// </summary>
public interface ISelectionFunction
{
    /// <summary>Name referenced by workflow JSON (case-insensitive).</summary>
    string Name { get; }

    /// <summary>
    /// Returns the chosen target agent name (one of edge.ToOptions) or null when the
    /// output gives no basis for a decision — the executor then reuses its last
    /// decision for this edge (or the first option if none was made yet).
    /// </summary>
    string? SelectTarget(ConditionalEdgeConfiguration edge, string lastAgentOutput);
}

/// <summary>Resolves selection functions by name with a keyword-matching default.</summary>
public interface ISelectionFunctionRegistry
{
    /// <summary>Resolve by name; unknown or empty names fall back to the default keyword matcher.</summary>
    ISelectionFunction Resolve(string? name);
}
