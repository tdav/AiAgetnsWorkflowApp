using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgetnsWorkflow.Tests.Integration;

public class OrchestratorWiringTests
{
    [Fact]
    public async Task LoadAndValidate_WithUnknownPlugin_Throws()
    {
        var loader = Substitute.For<IWorkflowJsonLoader>();
        loader.LoadConfigurationAsync(Arg.Any<string>()).Returns(new WorkflowConfiguration
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

        var visualizer = Substitute.For<IWorkflowVisualizer>();
        var pool = Substitute.For<IMcpClientPool>();
        var hosted = Substitute.For<IHostedToolFactory>();
        var registry = new AgentPluginRegistry(Array.Empty<IAgentPlugin>());
        var cfg = new ConfigurationBuilder().Build();

        var sut = new MagenticWorkflowOrchestrator(
            NullLogger<MagenticWorkflowOrchestrator>.Instance,
            loader, visualizer, cfg, pool, hosted, registry);

        var act = () => sut.ExecuteWorkflowFromJsonAsync("any.json");
        await act.Should().ThrowAsync<WorkflowValidationException>().WithMessage("*missing*");
    }
}
