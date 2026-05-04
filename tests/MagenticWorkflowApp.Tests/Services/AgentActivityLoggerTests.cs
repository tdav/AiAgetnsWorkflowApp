using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;
using MagenticWorkflowApp.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MagenticWorkflowApp.Tests.Services;

public class AgentActivityLoggerTests
{
    private static (AgentActivityLogger logger, RecordingConsoleWriter writer) Build()
    {
        var writer = new RecordingConsoleWriter();
        var logger = new AgentActivityLogger(NullLogger<AgentActivityLogger>.Instance, writer);
        return (logger, writer);
    }

    [Fact]
    public void OnChunk_AccumulatesIntoBuffer_AndWritesToConsole()
    {
        var (logger, writer) = Build();
        logger.SetWorkflowMode(WorkflowDisplayMode.Sequential);

        logger.OnChunk("Alice", "Hello ");
        logger.OnChunk("Alice", "world");
        logger.OnTurnCompleted("Alice");

        Assert.Contains("Hello world", writer.AllText);
    }
}
