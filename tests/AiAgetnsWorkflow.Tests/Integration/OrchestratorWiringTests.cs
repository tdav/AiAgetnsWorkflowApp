using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;
using MagenticWorkflowApp.Services.Executors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgetnsWorkflow.Tests.Integration;

public class OrchestratorWiringTests
{
    private static MagenticWorkflowOrchestrator CreateSut(
        IWorkflowJsonLoader loader,
        IMcpClientPool pool,
        IChatClientProvider? clientProvider = null,
        IEnumerable<IWorkflowExecutor>? executors = null)
    {
        var activity = Substitute.For<IAgentActivityLogger>();
        var teamBuilder = new AgentTeamBuilder(
            Substitute.For<IAgentFactory>(),
            pool,
            Substitute.For<IHostedToolFactory>(),
            new AgentPluginRegistry(Array.Empty<IAgentPlugin>()),
            activity,
            NullLogger<AgentTeamBuilder>.Instance);

        return new MagenticWorkflowOrchestrator(
            NullLogger<MagenticWorkflowOrchestrator>.Instance,
            loader,
            Substitute.For<IWorkflowVisualizer>(),
            new ConfigurationBuilder().Build(),
            pool,
            clientProvider ?? Substitute.For<IChatClientProvider>(), // HasCredentials == false => demo mode
            teamBuilder,
            new SimulatedWorkflowExecutor(activity),
            executors ?? Array.Empty<IWorkflowExecutor>());
    }

    private static IWorkflowJsonLoader LoaderReturning(WorkflowConfiguration config)
    {
        var loader = Substitute.For<IWorkflowJsonLoader>();
        loader.LoadConfigurationAsync(Arg.Any<string>()).Returns(config);
        return loader;
    }

    [Test]
    public async Task LoadAndValidate_WithUnknownPlugin_Throws()
    {
        var loader = LoaderReturning(new WorkflowConfiguration
        {
            WorkflowType = "Sequential",
            Task = "demo",
            Agents = new()
            {
                new()
                {
                    Name = "A",
                    Description = "x",
                    Instructions = "x",
                    ModelId = "gpt-4",
                    Plugins = new() { "missing" }
                }
            },
            Orchestration = new() { StartAgent = "A" }
        });

        var sut = CreateSut(loader, Substitute.For<IMcpClientPool>());

        var act = () => sut.ExecuteWorkflowFromJsonAsync("any.json");
        await act.Should().ThrowAsync<WorkflowValidationException>().WithMessage("*missing*");
    }

    [Test]
    public async Task ExecuteDemoMode_NoApiKey_NoMcpNoPlugins_CompletesWithoutThrowing()
    {
        var loader = LoaderReturning(new WorkflowConfiguration
        {
            WorkflowType = "Sequential",
            Task = "demo",
            Agents = new() { new() { Name = "A", Description = "x", Instructions = "x", ModelId = "gpt-4" } },
            Orchestration = new() { StartAgent = "A", Edges = new() }
        });

        var pool = Substitute.For<IMcpClientPool>();
        var sut = CreateSut(loader, pool);

        Func<Task> act = () => sut.ExecuteWorkflowFromJsonAsync("any.json");
        await act.Should().NotThrowAsync();

        await pool.Received(1).RegisterServersAsync(
            Arg.Is<IReadOnlyList<McpServerConfiguration>>(list => list.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_WithCredentials_UnknownWorkflowType_ThrowsNotSupported()
    {
        var loader = LoaderReturning(new WorkflowConfiguration
        {
            WorkflowType = "bogus",
            Task = "demo",
            Agents = new() { new() { Name = "A", Description = "x", Instructions = "x", ModelId = "gpt-4" } }
        });

        var clientProvider = Substitute.For<IChatClientProvider>();
        clientProvider.HasCredentials.Returns(true);

        var sut = CreateSut(loader, Substitute.For<IMcpClientPool>(), clientProvider);

        var act = () => sut.ExecuteWorkflowFromJsonAsync("any.json");
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*bogus*");
    }
}
