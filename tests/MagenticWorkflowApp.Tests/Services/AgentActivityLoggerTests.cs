using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;
using MagenticWorkflowApp.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public void OnToolCall_LogsAndWritesToConsole()
    {
        var recorder = new RecordingLogger<AgentActivityLogger>();
        var writer = new RecordingConsoleWriter();
        var logger = new AgentActivityLogger(recorder, writer);

        logger.OnToolCall("Alice", "GetWeather", "{ \"city\":\"Paris\" }");

        Assert.Contains(recorder.Entries, e =>
            e.FormattedMessage.Contains("Alice") &&
            e.FormattedMessage.Contains("GetWeather"));
        Assert.Contains("GetWeather", writer.AllText);
    }

    [Fact]
    public void OnManagerDecision_LogsAndWritesToConsole()
    {
        var recorder = new RecordingLogger<AgentActivityLogger>();
        var writer = new RecordingConsoleWriter();
        var logger = new AgentActivityLogger(recorder, writer);

        logger.OnManagerDecision("Manager-1", "Selecting agent Bob");

        Assert.Contains(recorder.Entries, e =>
            e.FormattedMessage.Contains("Manager-1") &&
            e.FormattedMessage.Contains("Selecting agent Bob"));
        Assert.Contains("DECISION", writer.AllText);
    }

    [Fact]
    public void OnExecutorFailed_LogsError_AndFlushesPending()
    {
        var recorder = new RecordingLogger<AgentActivityLogger>();
        var writer = new RecordingConsoleWriter();
        var logger = new AgentActivityLogger(recorder, writer);
        logger.SetWorkflowMode(WorkflowDisplayMode.Sequential);

        logger.OnChunk("Alice", "in-progress text");
        logger.OnExecutorFailed("Alice", new InvalidOperationException("boom"));

        Assert.Contains(recorder.Entries, e =>
            e.Level == LogLevel.Error &&
            e.FormattedMessage.Contains("Alice"));
        Assert.Contains(recorder.Entries, e =>
            e.Level == LogLevel.Warning &&
            e.FormattedMessage.Contains("Pending turn") &&
            e.FormattedMessage.Contains("aborted"));
    }

    [Fact]
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
        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public void OnWorkflowOutput_LogsAndWritesFinalResult()
    {
        var recorder = new RecordingLogger<AgentActivityLogger>();
        var writer = new RecordingConsoleWriter();
        var logger = new AgentActivityLogger(recorder, writer);

        logger.OnWorkflowOutput("final-answer");

        Assert.Contains(recorder.Entries, e =>
            e.FormattedMessage.Contains("Workflow output") &&
            e.FormattedMessage.Contains("final-answer"));
        Assert.Contains("final-answer", writer.AllText);
    }

    [Fact]
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

        var span = Assert.Single(stopped, a => a.OperationName.StartsWith("agent.turn."));
        Assert.Equal("agent.turn.Alice", span.OperationName);
        Assert.Contains(span.TagObjects, t => t.Key == "chunks" && t.Value is int n && n == 2);
        Assert.Contains(span.TagObjects, t => t.Key == "text.length" && t.Value is int len && len == 6);
    }

    [Fact]
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

        Assert.Equal(1, counterValues.Sum());
        Assert.Single(histogramValues);
    }

    [Fact]
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
        Assert.Equal(2, completed.Count);
        Assert.Contains(completed, e => e.FormattedMessage.Contains("AAAAAA2"));
        Assert.Contains(completed, e => e.FormattedMessage.Contains("BBB"));
        Assert.Contains("[Alice] AAA", writer.AllText);
        Assert.Contains("[Bob] BBB", writer.AllText);
    }
}
