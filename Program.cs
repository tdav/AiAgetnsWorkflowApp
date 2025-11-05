using System;
using System.IO;
using System.Threading.Tasks;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Microsoft Agent Framework - Magentic Workflow ===\n");

        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets<Program>()
            .AddEnvironmentVariables()
            .Build();

         

        // Setup DI
        var services = new ServiceCollection();
        ConfigureServices(services, configuration);
        var serviceProvider = services.BuildServiceProvider();

        // Get workflow orchestrator
        var orchestrator = serviceProvider.GetRequiredService<IWorkflowOrchestrator>();

        try
        {
            // Specify workflow configuration file
            string workflowConfigPath = args.Length > 0 
                ? args[0] 
                : "workflow-config.json";

            Console.WriteLine($"Loading workflow configuration from: {workflowConfigPath}\n");

            // Load and execute workflow
            await orchestrator.ExecuteWorkflowFromJsonAsync(workflowConfigPath);

            Console.WriteLine("\n=== Workflow Execution Completed ===");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n!!! Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Console.ResetColor();
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton(configuration);
        services.AddSingleton<IWorkflowOrchestrator, MagenticWorkflowOrchestrator>();
        services.AddSingleton<IWorkflowJsonLoader, WorkflowJsonLoader>();
        services.AddSingleton<IWorkflowVisualizer, WorkflowVisualizer>();
    }
}
