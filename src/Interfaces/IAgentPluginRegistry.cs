namespace MagenticWorkflowApp.Interfaces;

public interface IAgentPluginRegistry
{
    bool TryGet(string name, out IAgentPlugin? plugin);
    IEnumerable<string> RegisteredNames { get; }
}
