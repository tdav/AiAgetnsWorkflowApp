using Microsoft.Extensions.AI;

namespace MagenticWorkflowApp.Interfaces;

public interface IAgentPlugin
{
    string Name { get; }
    IEnumerable<AITool> AsAITools();
}
