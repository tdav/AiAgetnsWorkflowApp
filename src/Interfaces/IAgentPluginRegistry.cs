using System.Diagnostics.CodeAnalysis;

namespace MagenticWorkflowApp.Interfaces;

public interface IAgentPluginRegistry
{
    bool TryGet(string name, [NotNullWhen(true)] out IAgentPlugin? plugin);
    IEnumerable<string> RegisteredNames { get; }
}
