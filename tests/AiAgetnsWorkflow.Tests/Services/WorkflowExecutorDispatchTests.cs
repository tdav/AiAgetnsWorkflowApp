using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Services;
using MagenticWorkflowApp.Services.Executors;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgetnsWorkflow.Tests.Services;

public class WorkflowExecutorDispatchTests
{
    private static AgentTeamBuilder TeamBuilder() => new(
        Substitute.For<IAgentFactory>(),
        Substitute.For<IMcpClientPool>(),
        Substitute.For<IHostedToolFactory>(),
        new AgentPluginRegistry(Array.Empty<IAgentPlugin>()),
        Substitute.For<IAgentActivityLogger>(),
        NullLogger<AgentTeamBuilder>.Instance);

    private static SequentialWorkflowExecutor Sequential() => new(
        TeamBuilder(), Substitute.For<IAgentActivityLogger>(), NullLogger<SequentialWorkflowExecutor>.Instance);

    private static ConcurrentWorkflowExecutor Concurrent() => new(
        TeamBuilder(), Substitute.For<IAgentActivityLogger>(), NullLogger<ConcurrentWorkflowExecutor>.Instance);

    private static MagenticWorkflowExecutor Magentic() => new(
        Substitute.For<IChatClientProvider>(),
        Substitute.For<IAgentActivityLogger>(),
        NullLoggerFactory.Instance,
        NullLogger<MagenticWorkflowExecutor>.Instance);

    private static DeepResearchWorkflowExecutor DeepResearch() => new(
        Substitute.For<IDeepResearchOrchestrator>());

    [Test]
    [Arguments("sequential", true)]
    [Arguments("SEQUENTIAL", true)]
    [Arguments("conditional", true)]
    [Arguments("Conditional", true)]
    [Arguments("concurrent", false)]
    [Arguments("magentic", false)]
    [Arguments("deepresearch", false)]
    public void SequentialExecutor_CanExecute(string workflowType, bool expected)
    {
        Sequential().CanExecute(workflowType).Should().Be(expected);
    }

    [Test]
    [Arguments("concurrent", true)]
    [Arguments("Concurrent", true)]
    [Arguments("sequential", false)]
    [Arguments("conditional", false)]
    [Arguments("magentic", false)]
    public void ConcurrentExecutor_CanExecute(string workflowType, bool expected)
    {
        Concurrent().CanExecute(workflowType).Should().Be(expected);
    }

    [Test]
    [Arguments("magentic", true)]
    [Arguments("Magentic", true)]
    [Arguments("sequential", false)]
    [Arguments("deepresearch", false)]
    public void MagenticExecutor_CanExecute(string workflowType, bool expected)
    {
        Magentic().CanExecute(workflowType).Should().Be(expected);
    }

    [Test]
    [Arguments("deepresearch", true)]
    [Arguments("DeepResearch", true)]
    [Arguments("sequential", false)]
    [Arguments("magentic", false)]
    public void DeepResearchExecutor_CanExecute(string workflowType, bool expected)
    {
        DeepResearch().CanExecute(workflowType).Should().Be(expected);
    }
}
