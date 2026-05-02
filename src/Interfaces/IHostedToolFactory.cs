using Microsoft.Extensions.AI;

namespace MagenticWorkflowApp.Interfaces;

public interface IHostedToolFactory
{
    IReadOnlyList<AITool> Create(IReadOnlyList<string> toolNames);
}
