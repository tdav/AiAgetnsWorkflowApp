using MagenticWorkflowApp.Interfaces;

namespace MagenticWorkflowApp.Services;

public sealed class AgentPluginRegistry : IAgentPluginRegistry
{
    private readonly Dictionary<string, IAgentPlugin> byName;

    public AgentPluginRegistry(IEnumerable<IAgentPlugin> plugins)
    {
        byName = new Dictionary<string, IAgentPlugin>(StringComparer.Ordinal);
        foreach (var p in plugins)
        {
            if (!byName.TryAdd(p.Name, p))
                throw new InvalidOperationException($"Found duplicate plugin name '{p.Name}'");
        }
    }

    public bool TryGet(string name, out IAgentPlugin? plugin)
    {
        if (byName.TryGetValue(name, out var found))
        {
            plugin = found;
            return true;
        }
        plugin = null;
        return false;
    }

    public IEnumerable<string> RegisteredNames => byName.Keys;
}
