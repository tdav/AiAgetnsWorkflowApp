using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== Microsoft Agent Framework - Magentic Workflow ===\n");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        ConfigureServices(services, configuration);
        await using var serviceProvider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            var orchestrator = serviceProvider.GetRequiredService<IWorkflowOrchestrator>();
            var path = args.Length > 0 ? args[0] : "workflow-config.json";
            Console.WriteLine($"Loading workflow configuration from: {path}\n");
            await orchestrator.ExecuteWorkflowFromJsonAsync(path);
            Console.WriteLine("\n=== Workflow Execution Completed ===");
            return 0;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.WriteLine("\nCanceled by user.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n!!! Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
            return 1;
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(b => { b.AddConsole(); b.SetMinimumLevel(LogLevel.Information); });
        services.AddSingleton(configuration);
        services.AddSingleton<IWorkflowOrchestrator, MagenticWorkflowOrchestrator>();
        services.AddSingleton<IWorkflowJsonLoader, WorkflowJsonLoader>();
        services.AddSingleton<IWorkflowVisualizer, WorkflowVisualizer>();

        services.AddSingleton<IMcpClientPool, McpClientPool>();
        services.AddSingleton<IHostedToolFactory, HostedToolFactory>();
        services.AddSingleton<IAgentPluginRegistry, AgentPluginRegistry>();

        services.AddSingleton<IAgentPlugin, Plugins.WeatherPlugin>();
        services.AddSingleton<IAgentPlugin, Plugins.TimePlugin>();
    }
}
