using System;
using System.Collections.Generic;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;

namespace MagenticWorkflowApp.Tests.TestDoubles;

public sealed class RecordingActivityLogger : IAgentActivityLogger
{
    public sealed record Call(string Method, string? Arg1 = null, string? Arg2 = null, Exception? Ex = null);

    private readonly List<Call> calls = new();
    public IReadOnlyList<Call> Calls => calls;

    public void SetWorkflowMode(WorkflowDisplayMode mode)
        => calls.Add(new Call("SetWorkflowMode", mode.ToString()));

    public void OnTurnStarted(string agent, string? executorId = null)
        => calls.Add(new Call("OnTurnStarted", agent, executorId));

    public void OnChunk(string agent, string text)
        => calls.Add(new Call("OnChunk", agent, text));

    public void OnTurnCompleted(string agent, string? fullText = null)
        => calls.Add(new Call("OnTurnCompleted", agent, fullText));

    public void OnToolCall(string agent, string toolName, string? args = null)
        => calls.Add(new Call("OnToolCall", agent, toolName + (args is null ? "" : "(" + args + ")")));

    public void OnManagerDecision(string managerName, string decision)
        => calls.Add(new Call("OnManagerDecision", managerName, decision));

    public void OnExecutorFailed(string executorId, Exception exception)
        => calls.Add(new Call("OnExecutorFailed", executorId, null, exception));

    public void OnWorkflowError(Exception exception)
        => calls.Add(new Call("OnWorkflowError", null, null, exception));

    public void OnWorkflowOutput(string output)
        => calls.Add(new Call("OnWorkflowOutput", output));

    public void FlushAllPendingTurns(string reason)
        => calls.Add(new Call("FlushAllPendingTurns", reason));
}
