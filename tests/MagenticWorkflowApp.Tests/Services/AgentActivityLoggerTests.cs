using System.Linq;
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

    [Fact]
    public void OnTurnCompleted_LogsOnceWithFullText()
    {
        var recorder = new RecordingLogger<AgentActivityLogger>();
        var writer = new RecordingConsoleWriter();
        var logger = new AgentActivityLogger(recorder, writer);
        logger.SetWorkflowMode(WorkflowDisplayMode.Sequential);

        logger.OnChunk("Bob", "abc");
        logger.OnChunk("Bob", "def");
        logger.OnTurnCompleted("Bob");

        var completedEntries = recorder.Entries
            .Where(e => e.Message.Contains("completed turn"))
            .ToList();
        Assert.Single(completedEntries);
        Assert.Contains("abcdef", completedEntries[0].FormattedMessage);
    }

    [Fact]
    public void OnTurnCompleted_WithExplicitText_PrefersExplicit()
    {
        var recorder = new RecordingLogger<AgentActivityLogger>();
        var writer = new RecordingConsoleWriter();
        var logger = new AgentActivityLogger(recorder, writer);
        logger.SetWorkflowMode(WorkflowDisplayMode.Sequential);

        logger.OnTurnCompleted("Carol", "explicit-text");

        var entries = recorder.Entries
            .Where(e => e.Message.Contains("completed turn"))
            .ToList();
        Assert.Single(entries);
        Assert.Contains("explicit-text", entries[0].FormattedMessage);
    }
}
