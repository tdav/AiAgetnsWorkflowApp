using AgentSession;
using AgentSession.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets<HarnessGithubAIProgram>()
            .AddEnvironmentVariables()
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .WriteTo.Console()
            .WriteTo.File(
                path: "logs/log-.txt",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        var services = new ServiceCollection();

        services.AddLogging(b => { b.ClearProviders(); b.AddSerilog(Log.Logger, dispose: true); });

        services.AddSingleton<IConfiguration>(configuration);

        //services.AddKeyedTransient<IHarness, HarnessGithubAIProgram>("GitHub");
        //services.AddKeyedTransient<IHarness, HarnessClaudeProgram>("Anthropic");
        //services.AddKeyedTransient<IHarness, HarnessOllamaProgram>("Ollama");
        services.AddKeyedTransient<IHarness, HarnessCloudOllamaProgram>("OllamaCloud");

        await using var serviceProvider = services.BuildServiceProvider();

        var activeHarness = configuration["ActiveHarness"]
            ?? throw new InvalidOperationException("ActiveHarness not configured in appsettings.json.");

        Log.Information("Starting harness: {Harness}", activeHarness);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            var harness = serviceProvider.GetRequiredKeyedService<IHarness>(activeHarness);
            await harness.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Information("Cancelled by user.");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled error in harness {Harness}", activeHarness);
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

}