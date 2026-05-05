using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AgentSession;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("=== Microsoft Agent Framework - Magentic Workflow ===\n");




       await HarnessGithubAIProgram.RunAsync(args);

         




        //var configuration = new ConfigurationBuilder()
        //    .SetBasePath(Directory.GetCurrentDirectory())
        //    .AddJsonFile("appsettings.json", optional: false)
        //    .AddEnvironmentVariables()
        //    .Build();

        //Log.Logger = new LoggerConfiguration()
        //    .ReadFrom.Configuration(configuration)
        //    .WriteTo.Console()
        //    .WriteTo.File(
        //        path: "logs/log-.txt",
        //        rollingInterval: RollingInterval.Day,
        //        retainedFileCountLimit: 7)
        //    .CreateLogger();

        //var services = new ServiceCollection();

        //services.AddLogging(b =>        
        //{
        //    b.ClearProviders();
        //    b.AddSerilog(Log.Logger, dispose: true);
        //});
        //services.AddSingleton(configuration);


        //await using var serviceProvider = services.BuildServiceProvider();

        //using var cts = new CancellationTokenSource();
        //Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        //try
        //{



        //    Log.Information("=== Workflow Execution Completed ===");
        //    return 0;
        //}
        //catch (Exception ex)
        //{
        //    Log.Fatal(ex, "Критическая ошибка при выполнении workflow");
        //    return 1;
        //}
        //finally
        //{
        //    await Log.CloseAndFlushAsync();
        //}
    }

}
