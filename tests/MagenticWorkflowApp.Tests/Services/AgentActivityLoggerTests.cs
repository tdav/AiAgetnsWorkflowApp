using System;
using System.Collections.Generic;
using System.Linq;
using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;
using MagenticWorkflowApp.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MagenticWorkflowApp.Tests.Services;

public class AgentActivityLoggerTests
{
    private static (AgentActivityLogger logger, RecordingConsoleWriter writer) Build()
    {
        var writer = new RecordingConsoleWriter();
        var logger = new AgentActivityLogger(NullLogger<AgentActivityLogger>.Instance, writer);
        return (logger, writer);
    }

    [Test]
    public void OnChunk_AccumulatesIntoBuffer_AndWritesToConsole()
    {
        var (logger, writer) = Build();
        logger.SetWorkflowMode(WorkflowDisplayMode.Sequential);

        logger.OnChunk("Alice", "Hello ");
        logger.OnChunk("Alice", "world");
        logger.OnTurnCompleted("Alice");

        writer.AllText.Should().Contain("Hello world");
    }

    [Test]
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
        completedEntries.Should().ContainSingle();
        completedEntries[0].FormattedMessage.Should().Contain("abcdef");
    }

    [Test]
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
        entries.Should().ContainSingle();
        entries[0].FormattedMessage.Should().Contain("explicit-text");
    }

    [Test]
    public void OnToolCall_LogsAndWritesToConsole()
    {
        var recorder = new RecordingLogger<AgentActivityLogger>();
        var writer = new RecordingConsoleWriter();
        var logger = new AgentActivityLogger(recorder, writer);

        logger.OnToolCall("Alice", "GetWeather", "{ \"city\":\"Paris\" }");

        recorder.Entries.Should().Contain(e =>
            e.FormattedMessage.Contains("Alice") &&
            e.FormattedMessage.Contains("GetWeather"));
        writer.AllText.Should().Contain("GetWeather");
    }

    [Test]
    public void OnManagerDecision_LogsAndWritesToConsole()
    {
        var recorder = new RecordingLogger<AgentActivityLogger>();
        var writer = new RecordingConsoleWriter();
        var logger = new AgentActivityLogger(recorder, writer);

        logger.OnManagerDecision("Manager-1", "Selecting agent Bob");

        recorder.Entries.Should().Contain(e =>
            e.FormattedMessage.Contains("Manager-1") &&
            e.FormattedMessage.Contains("Selecting agent Bob"));
        writer.AllText.Should().Contain("DECISION");
    }

    [Test]
    public void OnExecutorFailed_LogsError_AndFlushesPending()
    {
        var recorder = new RecordingLogger<AgentActivityLogger>();
        var writer = new RecordingConsoleWriter();
        var logger = new AgentActivityLogger(recorder, writer);
        logger.SetWorkflowMode(WorkflowDisplayMode.Sequential);

        logger.OnChunk("Alice", "in-progress text");
        logger.OnExecutorFailed("Alice", new InvalidOperationException("boom"));

        recorder.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error &&
            e.FormattedMessage.Contains("Alice"));
        recorder.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning &&
            e.FormattedMessage.Contains("Pending turn") &&
            e.FormattedMessage.Contains("aborted"));
    }

    [Test]
    public void OnWorkflowError_LogsError_AndFlushesAllPending()
    {
        var recorder = new RecordingLogger<AgentActivityLogger>();
        var writer = new RecordingConsoleWriter();
        var logger = new AgentActivityLogger(recorder, writer);
        logger.SetWorkflowMode(WorkflowDisplayMode.Concurrent);

        logger.OnChunk("Alice", "x");
        logger.OnChunk("Bob", "y");
        logger.OnWorkflowError(new InvalidOperationException("workflow boom"));

        var pending = recorder.Entries
            .Where(e => e.FormattedMessage.Contains("Pending turn"))
            .ToList();
        pending.Should().HaveCount(2);
    }

    [Test]
    public void OnWorkflowOutput_LogsAndWritesFinalResult()
    {
        var recorder = new RecordingLogger<AgentActivityLogger>();
        var writer = new RecordingConsoleWriter();
        var logger = new AgentActivityLogger(recorder, writer);

        logger.OnWorkflowOutput("final-answer");

        recorder.Entries.Should().Contain(e =>
            e.FormattedMessage.Contains("Workflow output") &&
            e.FormattedMessage.Contains("final-answer"));
        writer.AllText.Should().Contain("final-answer");
    }

    [Test]
    [NotInParallel] // global ActivityListener would capture spans from concurrently running tests
    public void OnTurnCompleted_StartsAndStopsActivity_WithTags()
    {
        var stopped = new List<System.Diagnostics.Activity>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = src => src.Name == "MagenticWorkflowApp.Agents",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStopped = a => stopped.Add(a),
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        var (logger, _) = Build();
        logger.SetWorkflowMode(WorkflowDisplayMode.Sequential);

        logger.OnChunk("Alice", "abc");
        logger.OnChunk("Alice", "def");
        logger.OnTurnCompleted("Alice");

        var span = stopped
            .Should().ContainSingle(a => a.OperationName.StartsWith("agent.turn."))
            .Which;
        span.OperationName.Should().Be("agent.turn.Alice");
        span.TagObjects.Any(t => t.Key == "chunks" && t.Value is int n && n == 2).Should().BeTrue();
        span.TagObjects.Any(t => t.Key == "text.length" && t.Value is int len && len == 6).Should().BeTrue();
    }

    [Test]
    [NotInParallel] // global MeterListener would capture measurements from concurrently running tests
    public void OnTurnCompleted_RecordsMetrics()
    {
        var counterValues = new List<long>();
        var histogramValues = new List<double>();
        using var listener = new System.Diagnostics.Metrics.MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "MagenticWorkflowApp.Agents")
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((inst, m, t, _) => counterValues.Add(m));
        listener.SetMeasurementEventCallback<double>((inst, m, t, _) => histogramValues.Add(m));
        listener.Start();

        var (logger, _) = Build();
        logger.OnChunk("Alice", "abc");
        logger.OnTurnCompleted("Alice");

        counterValues.Sum().Should().Be(1);
        histogramValues.Should().ContainSingle();
    }

    [Test]
    public void Concurrent_TwoAgents_IndependentBuffers_AndPrefixedConsole()
    {
        var recorder = new RecordingLogger<AgentActivityLogger>();
        var writer = new RecordingConsoleWriter();
        var logger = new AgentActivityLogger(recorder, writer);
        logger.SetWorkflowMode(WorkflowDisplayMode.Concurrent);

        logger.OnChunk("Alice", "AAA");
        logger.OnChunk("Bob", "BBB");
        logger.OnChunk("Alice", "AAA2");
        logger.OnTurnCompleted("Alice");
        logger.OnTurnCompleted("Bob");

        var completed = recorder.Entries
            .Where(e => e.FormattedMessage.Contains("completed turn"))
            .ToList();
        completed.Should().HaveCount(2);
        completed.Should().Contain(e => e.FormattedMessage.Contains("AAAAAA2"));
        completed.Should().Contain(e => e.FormattedMessage.Contains("BBB"));
        writer.AllText.Should().Contain("[Alice] AAA");
        writer.AllText.Should().Contain("[Bob] BBB");
    }
}
