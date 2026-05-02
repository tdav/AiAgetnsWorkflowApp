using MagenticWorkflowApp.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MagenticWorkflowApp.Services;

public sealed class HostedToolFactory : IHostedToolFactory
{
    public IReadOnlyList<AITool> Create(IReadOnlyList<string> toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);
        if (toolNames.Count == 0) return Array.Empty<AITool>();

        var result = new List<AITool>(toolNames.Count);
        foreach (var name in toolNames)
        {
            AITool tool = name switch
            {
                "CodeInterpreter" => new HostedCodeInterpreterTool(),
                _ => throw new NotSupportedException($"Hosted tool '{name}' is not supported")
            };
            result.Add(tool);
        }
        return result;
    }
}
