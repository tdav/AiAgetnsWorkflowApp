using MagenticWorkflowApp.Interfaces;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services;

/// <summary>
/// Collects DI-registered <see cref="ISelectionFunction"/> implementations by name
/// (case-insensitive). Unknown or empty names resolve to the default keyword matcher,
/// so workflow JSONs with descriptive function names keep working out of the box.
/// </summary>
public sealed class SelectionFunctionRegistry : ISelectionFunctionRegistry
{
    private readonly Dictionary<string, ISelectionFunction> functions;
    private readonly ISelectionFunction fallback;
    private readonly ILogger<SelectionFunctionRegistry> logger;

    public SelectionFunctionRegistry(
        IEnumerable<ISelectionFunction> registered,
        ILogger<SelectionFunctionRegistry> logger)
    {
        this.logger = logger;
        this.functions = new Dictionary<string, ISelectionFunction>(StringComparer.OrdinalIgnoreCase);
        foreach (var fn in registered)
        {
            if (!this.functions.TryAdd(fn.Name, fn))
            {
                throw new InvalidOperationException($"Duplicate selection function name '{fn.Name}'");
            }
        }
        if (!this.functions.TryGetValue(KeywordSelectionFunction.DefaultName, out var def))
        {
            throw new InvalidOperationException(
                $"Default selection function '{KeywordSelectionFunction.DefaultName}' is not registered");
        }
        this.fallback = def;
    }

    public ISelectionFunction Resolve(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && functions.TryGetValue(name, out var fn))
        {
            return fn;
        }
        logger.LogInformation(
            "Selection function '{Name}' is not registered — using default '{Default}'",
            name, fallback.Name);
        return fallback;
    }
}
