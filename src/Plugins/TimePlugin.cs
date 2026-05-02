using System.ComponentModel;
using MagenticWorkflowApp.Interfaces;
using Microsoft.Extensions.AI;

namespace MagenticWorkflowApp.Plugins;

public sealed class TimePlugin : IAgentPlugin
{
    public string Name => "TimePlugin";

    public IEnumerable<AITool> AsAITools()
    {
        yield return AIFunctionFactory.Create(GetCurrentTime);
    }

    [Description("Returns current UTC time.")]
    public static string GetCurrentTime()
        => DateTime.UtcNow.ToString("O");
}
