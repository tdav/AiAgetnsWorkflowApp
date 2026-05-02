using MagenticWorkflowApp.Interfaces;
using Microsoft.Extensions.AI;

namespace AiAgetnsWorkflow.Tests.Fakes;

public sealed class FakeAgentPlugin(string name, params AITool[] tools) : IAgentPlugin
{
    public string Name { get; } = name;
    public IEnumerable<AITool> AsAITools() => tools;
}
