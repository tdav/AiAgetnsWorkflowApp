using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services;

public sealed class AgentActivityLogger : IAgentActivityLogger
{
    private readonly ILogger<AgentActivityLogger> logger;
    private readonly IConsoleWriter console;

    private readonly ConcurrentDictionary<string, AgentTurnState> turns = new(StringComparer.Ordinal);
    private WorkflowDisplayMode mode = WorkflowDisplayMode.Sequential;

    private static readonly ActivitySource ActivitySource = new("MagenticWorkflowApp.Agents");
    private static readonly Meter Meter = new("MagenticWorkflowApp.Agents");
    private static readonly Counter<long> TurnsCompleted = Meter.CreateCounter<long>("agent.turns.completed");
    private static readonly Histogram<double> TurnDurationMs = Meter.CreateHistogram<double>("agent.turn.duration.ms");

    public AgentActivityLogger(ILogger<AgentActivityLogger> logger, IConsoleWriter console)
    {
        this.logger = logger;
        this.console = console;
    }

    public void SetWorkflowMode(WorkflowDisplayMode m) => mode = m;

    public void OnTurnStarted(string agent, string? executorId = null)
    {
        SafeRun(() =>
        {
            AgentTurnState newState = new(DateTime.UtcNow);
            var state = turns.GetOrAdd(agent, newState);
            if (ReferenceEquals(state, newState))
            {
                state.Activity = ActivitySource.StartActivity($"agent.turn.{agent}");
                logger.LogInformation("Agent {Agent} turn started", agent);
                if (mode == WorkflowDisplayMode.Sequential)
                    console.WriteLineWithColor($"\n┌── {agent} ──", ConsoleColor.Cyan);
                else
                    console.WriteLineWithColor($"\n[{agent}] (started)", ConsoleColor.Cyan);
            }
        });
    }

    public void OnChunk(string agent, string text)
    {
        SafeRun(() =>
        {
            AgentTurnState newState = new(DateTime.UtcNow);
            var state = turns.GetOrAdd(agent, newState);
            if (ReferenceEquals(state, newState))
            {
                state.Activity = ActivitySource.StartActivity($"agent.turn.{agent}");
                logger.LogInformation("Agent {Agent} turn started", agent);
                if (mode == WorkflowDisplayMode.Sequential)
                    console.WriteLineWithColor($"\n┌── {agent} ──", ConsoleColor.Cyan);
                else
                    console.WriteLineWithColor($"\n[{agent}] (started)", ConsoleColor.Cyan);
            }

            lock (state.Lock)
            {
                state.Buffer.Append(text);
                state.ChunkCount++;
            }

            if (mode == WorkflowDisplayMode.Sequential)
                console.Write(text);
            else
                console.Write($"[{agent}] {text}");
        });
    }

    public void OnTurnCompleted(string agent, string? fullText = null)
    {
        SafeRun(() =>
        {
            turns.TryRemove(agent, out var state);

            var chunks = state?.ChunkCount ?? 0;
            var durationMs = state is null ? 0 : (DateTime.UtcNow - state.StartedUtc).TotalMilliseconds;
            var text = fullText ?? state?.Buffer.ToString() ?? string.Empty;

            state?.Activity?.SetTag("chunks", chunks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            state?.Activity?.SetTag("text.length", text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            state?.Activity?.SetTag("durationMs", ((long)durationMs).ToString(System.Globalization.CultureInfo.InvariantCulture));
            state?.Activity?.Dispose();

            TurnsCompleted.Add(1, new KeyValuePair<string, object?>("agent", agent));
            TurnDurationMs.Record(durationMs, new KeyValuePair<string, object?>("agent", agent));

            if (mode == WorkflowDisplayMode.Sequential)
                console.WriteLineWithColor($"\n└── end {agent} ──", ConsoleColor.Cyan);
            else
                console.WriteLineWithColor($"\n[{agent}] (completed)", ConsoleColor.Cyan);

            logger.LogInformation(
                "Agent {Agent} completed turn: chunks={Chunks}, durationMs={Duration:F0}, text={Text}",
                agent, chunks, durationMs, text);
        });
    }

    public void OnToolCall(string agent, string toolName, string? args = null)
    {
        SafeRun(() =>
        {
            console.WriteLineWithColor(
                $"[{agent}] → tool: {toolName}({args ?? string.Empty})",
                ConsoleColor.Magenta);
            logger.LogInformation(
                "Agent {Agent} called tool {Tool} with args {Args}",
                agent, toolName, args ?? string.Empty);
        });
    }

    public void OnManagerDecision(string managerName, string decision)
    {
        SafeRun(() =>
        {
            console.WriteLineWithColor(
                $"[{managerName}] DECISION: {decision}",
                ConsoleColor.Cyan);
            logger.LogInformation(
                "Manager {Manager} decision: {Decision}",
                managerName, decision);
        });
    }

    public void OnExecutorFailed(string executorId, Exception exception)
    {
        SafeRun(() =>
        {
            console.WriteLineWithColor($"[EXECUTOR:{executorId}] FAILED: {exception.Message}", ConsoleColor.Red);
            logger.LogError(exception, "Executor {Executor} failed", executorId);
            FlushAllPendingTurns("aborted");
        });
    }

    public void OnWorkflowError(Exception exception)
    {
        SafeRun(() =>
        {
            console.WriteLineWithColor($"[WORKFLOW] ERROR: {exception.Message}", ConsoleColor.Red);
            logger.LogError(exception, "Workflow error");
            FlushAllPendingTurns("aborted");
        });
    }

    public void OnWorkflowOutput(string output)
    {
        SafeRun(() =>
        {
            console.WriteLine(string.Empty);
            console.WriteLineWithColor(new string('=', 60), ConsoleColor.Green);
            console.WriteLineWithColor("FINAL RESULT:", ConsoleColor.Green);
            console.WriteLineWithColor(new string('=', 60), ConsoleColor.Green);
            console.WriteLine($"✅ {output}");
            console.WriteLineWithColor(new string('=', 60), ConsoleColor.Green);
            logger.LogInformation("Workflow output: {Output}", output);
        });
    }

    public void FlushAllPendingTurns(string reason)
    {
        SafeRun(() =>
        {
            foreach (var key in turns.Keys.ToList())
            {
                if (turns.TryRemove(key, out var state))
                {
                    state.Activity?.SetTag("aborted", true);
                    state.Activity?.SetTag("abort.reason", reason);
                    state.Activity?.SetStatus(ActivityStatusCode.Error, reason);
                    state.Activity?.Dispose();
                    logger.LogWarning(
                        "Pending turn for {Agent} flushed: reason={Reason}, chunks={Chunks}",
                        key, reason, state.ChunkCount);
                }
            }
        });
    }

    private void SafeRun(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Activity logger failure");
        }
    }

    private sealed class AgentTurnState
    {
        public AgentTurnState(DateTime startedUtc) { StartedUtc = startedUtc; }
        public DateTime StartedUtc { get; }
        public StringBuilder Buffer { get; } = new();
        public int ChunkCount { get; set; }
        public object Lock { get; } = new();
        public Activity? Activity { get; set; }
    }
}
