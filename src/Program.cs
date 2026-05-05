using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

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

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)            
            .CreateLogger();

        //var teeFileLogger = new LoggerConfiguration()
        //    .MinimumLevel.Verbose()
        //    .Enrich.FromLogContext()
        //    .WriteTo.File(
        //        path: "logs/agents-.log",
        //        rollingInterval: Serilog.RollingInterval.Day,
        //        outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] [CON] {Message:lj}{NewLine}",
        //        shared: true)
        //    .CreateLogger();

        //Console.SetOut(new SerilogTeeTextWriter(Console.Out, teeFileLogger));
        //Console.SetError(new SerilogTeeTextWriter(Console.Error, teeFileLogger, Serilog.Events.LogEventLevel.Warning));

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services, configuration);
            await using var serviceProvider = services.BuildServiceProvider();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            try
            {
                var orchestrator = serviceProvider.GetRequiredService<IWorkflowOrchestrator>();
                var path = args.Length > 0 ? args[0] : "workflow-sequential.json";
                Console.WriteLine($"Loading workflow configuration from: {path}\n");
                await orchestrator.ExecuteWorkflowFromJsonAsync(path, cts.Token);
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
        finally
        {
            //(teeFileLogger as IDisposable)?.Dispose();
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(b =>
        {
            b.ClearProviders();
            b.AddSerilog(Log.Logger, dispose: true);
        });

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(
                configuration["OpenTelemetry:ServiceName"] ?? "MagenticWorkflowApp"))
            .WithTracing(t => t
                .AddSource("MagenticWorkflowApp.Agents")
                .AddConsoleExporter())
            .WithMetrics(m => m
                .AddMeter("MagenticWorkflowApp.Agents")
                .AddConsoleExporter());

        services.AddSingleton(configuration);
        services.AddSingleton<IConsoleWriter, DefaultConsoleWriter>();
        services.AddSingleton<IAgentActivityLogger, AgentActivityLogger>();
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
