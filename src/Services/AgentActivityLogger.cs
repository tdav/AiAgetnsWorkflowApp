using System.Collections.Concurrent;
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
            turns.GetOrAdd(agent, _ => new AgentTurnState(DateTime.UtcNow));
            logger.LogInformation("Agent {Agent} turn started", agent);
            if (mode == WorkflowDisplayMode.Sequential)
                console.WriteLineWithColor($"\n┌── {agent} ──", ConsoleColor.Cyan);
            else
                console.WriteLineWithColor($"\n[{agent}] (started)", ConsoleColor.Cyan);
        });
    }

    public void OnChunk(string agent, string text)
    {
        SafeRun(() =>
        {
            var state = turns.GetOrAdd(agent, _ =>
            {
                var s = new AgentTurnState(DateTime.UtcNow);
                logger.LogInformation("Agent {Agent} turn started", agent);
                if (mode == WorkflowDisplayMode.Sequential)
                    console.WriteLineWithColor($"\n┌── {agent} ──", ConsoleColor.Cyan);
                else
                    console.WriteLineWithColor($"\n[{agent}] (started)", ConsoleColor.Cyan);
                return s;
            });

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

            string text;
            int chunks;
            double durationMs;
            if (fullText is not null)
            {
                text = fullText;
                chunks = state?.ChunkCount ?? 0;
                durationMs = state is null ? 0 : (DateTime.UtcNow - state.StartedUtc).TotalMilliseconds;
            }
            else
            {
                text = state?.Buffer.ToString() ?? string.Empty;
                chunks = state?.ChunkCount ?? 0;
                durationMs = state is null ? 0 : (DateTime.UtcNow - state.StartedUtc).TotalMilliseconds;
            }

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
        => SafeRun(() => { /* implemented in Task 8 */ });

    public void OnManagerDecision(string managerName, string decision)
        => SafeRun(() => { /* implemented in Task 8 */ });

    public void OnExecutorFailed(string executorId, Exception exception)
        => SafeRun(() => { /* implemented in Task 9 */ });

    public void OnWorkflowError(Exception exception)
        => SafeRun(() => { /* implemented in Task 9 */ });

    public void OnWorkflowOutput(string output)
        => SafeRun(() => { /* implemented in Task 9 */ });

    public void FlushAllPendingTurns(string reason)
        => SafeRun(() => { /* implemented in Task 9 */ });

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
    }
}
