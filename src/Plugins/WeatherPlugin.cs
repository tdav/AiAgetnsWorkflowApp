using System.ComponentModel;
using MagenticWorkflowApp.Interfaces;
using Microsoft.Extensions.AI;

namespace MagenticWorkflowApp.Plugins;

public sealed class WeatherPlugin : IAgentPlugin
{
    public string Name => "WeatherPlugin";

    public IEnumerable<AITool> AsAITools()
    {
        yield return AIFunctionFactory.Create(GetWeather);
    }

    [Description("Returns current weather for a city.")]
    public static string GetWeather(
        [Description("City name.")] string city)
        => $"It is sunny and 22°C in {city} (stub).";
}
