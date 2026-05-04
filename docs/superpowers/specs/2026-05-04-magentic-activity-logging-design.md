# Magentic Activity Logging — Design Spec

**Date:** 2026-05-04
**Status:** Approved (brainstorming complete)
**Predecessor:** `2026-05-04-agent-activity-logging-design.md` (per-agent activity logger). This spec extends the same logger to the Magentic workflow path.

## Problem

In `workflow-sum.json` (type=Magentic, Ollama, 2 agents) the run takes ~4 minutes between `STARTING MAGENTIC WORKFLOW EXECUTION` and `FINAL RESULT`, and **no per-agent activity is visible** in console, Serilog file, or OpenTelemetry spans.

Root cause: `MagenticWorkflowOrchestrator.ExecuteMagenticWorkflowAsync` uses `Microsoft.SemanticKernel.Agents.Orchestration.MagenticOrchestration` whose `ResponseCallback` does not fire intermediately for the chosen orchestration model + Ollama path. Sequential / Concurrent / Conditional paths already emit through `HandleWorkflowEvent → IAgentActivityLogger`; only the Magentic path is silent.

The `Microsoft.Agents.AI.Workflows` 1.3.0 .NET package does **not** expose a `MagenticBuilder`/`MagenticManager` — those are Python-only at this version. So replacing `MagenticOrchestration` with a streaming builder is not possible on the current package version.

## Goal

Surface every Magentic agent turn and every manager decision through the existing `IAgentActivityLogger` (console + Serilog + OpenTelemetry), without changing the logger's contract or the non-Magentic execution paths.

## Non-Goals

- No replacement of `MagenticOrchestration` with a different orchestration engine.
- No tool-call bridging for Magentic agents (tools are deferred upstream and produce a warning today).
- No structural changes to Sequential/Concurrent/Conditional paths.
- No streaming token-by-token output if Semantic Kernel calls only `GetChatMessageContentsAsync` (non-streaming) under the hood. We log per-turn, not per-token, when streaming is unavailable.

## Approach

Two-step combination, abbreviated **A+C**:

**Step A — Serilog level overrides.** Lift `Microsoft.SemanticKernel` and `Microsoft.SemanticKernel.Agents` to `Information` in `appsettings.json`. Smoke test that SK itself produces some signal. Cheap, reversible, no code change.

**Step C — `IChatCompletionService` decorator.** Each Magentic kernel (manager + every participant agent) gets its `IChatCompletionService` wrapped with a `LoggingChatCompletionService` that calls `IAgentActivityLogger` before/after each model call. This is the deterministic source of activity events.

Step A is observability; Step C is the actual fix.

## Architecture

```
ExecuteMagenticWorkflowAsync
   │
   ├─ BuildKernel(participantModelId, ..., agentName="CounterAgent", activity)
   │      Kernel.CreateBuilder()
   │      ├ AddOpenAIChatCompletion / AddOllamaChatCompletion  → IChatCompletionService inner
   │      └ Replace IChatCompletionService with
   │           new LoggingChatCompletionService(inner, "CounterAgent", activity)
   │
   ├─ BuildKernel(managerModelId, ..., agentName="Manager", activity) ─ same wrap
   │
   └─ MagenticOrchestration(manager, agents)
         {
            ResponseCallback = no-op   ← removed body, decorator is sole source
         }
         .InvokeAsync(task)
```

Inside SK, every call to the underlying chat-completion provider goes through `LoggingChatCompletionService`, which emits `OnTurnStarted` → (optionally `OnChunk`s during streaming) → `OnTurnCompleted` (or `OnManagerDecision` for the manager kernel).

Failures emit `OnExecutorFailed` and rethrow. Cancellation rethrows without emission.

## Components

### `src/Services/LoggingChatCompletionService.cs` (new)

```csharp
internal sealed class LoggingChatCompletionService : IChatCompletionService
{
    private const int TextTruncationLimit = 1000;

    private readonly IChatCompletionService inner;
    private readonly string agentName;
    private readonly IAgentActivityLogger activity;
    private readonly bool isManager;

    public LoggingChatCompletionService(
        IChatCompletionService inner,
        string agentName,
        IAgentActivityLogger activity);

    public IReadOnlyDictionary<string, object?> Attributes => inner.Attributes;

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default);

    public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default);
}
```

`isManager` = `string.Equals(agentName, "Manager", StringComparison.Ordinal)`. Manager turns route to `OnManagerDecision`; agent turns route to `OnTurnCompleted`.

### `src/Services/MagenticWorkflowOrchestrator.cs` (modified)

`BuildKernel` signature gains two optional parameters:
```csharp
private Kernel BuildKernel(
    string modelId,
    string? openAiApiKey,
    string? ollamaEndpoint,
    bool enableThinking,
    string? agentName = null,
    IAgentActivityLogger? activity = null);
```

When both `agentName` and `activity` are non-null, **skip** `AddOpenAIChatCompletion` / `AddOllamaChatCompletion`/`AddSemanticKernelOpenAIChatCompletionService` calls and register the decorator directly with the inner constructed inside the factory closure:
```csharp
builder.Services.AddSingleton<IChatCompletionService>(_ =>
{
    IChatCompletionService inner = !string.IsNullOrWhiteSpace(ollamaEndpoint)
        ? BuildOllamaInner(modelId, ollamaEndpoint)
        : new OpenAIChatCompletionService(modelId, openAiApiKey!);
    return new LoggingChatCompletionService(inner, agentName, activity);
});
```
`BuildOllamaInner` is the same construction the existing `BuildKernel` uses today for the Ollama branch, factored into a helper. When `agentName` or `activity` is null (non-Magentic call sites, if any), `BuildKernel` falls back to its current behavior unchanged.

In `ExecuteMagenticWorkflowAsync`:
- agent loop: `BuildKernel(agentConfig.ModelId, ..., agentName: agentConfig.Name, activity: activity)`
- manager: `BuildKernel(config.Manager.ModelId, ..., agentName: "Manager", activity: activity)`
- `MagenticOrchestration.ResponseCallback` body replaced with `_ => ValueTask.CompletedTask` (the decorator is now the single source).

### `src/appsettings.json` (modified)

Under `Serilog.MinimumLevel.Override`:
```json
"Microsoft.SemanticKernel": "Information",
"Microsoft.SemanticKernel.Agents": "Information"
```

## Behavior Contract

### Non-streaming path (`GetChatMessageContentsAsync`)

1. Emit `activity.OnTurnStarted(agentName)`.
2. Try: `result = await inner.GetChatMessageContentsAsync(...)`.
3. Catch `OperationCanceledException` → rethrow without emission.
4. Catch any other `Exception ex` → `activity.OnExecutorFailed(agentName, ex)` then rethrow.
5. Concatenate `result` text contents into `fullText`.
6. Truncate `fullText` to `TextTruncationLimit` chars and append the literal suffix `… (truncated)` (Unicode U+2026 + space + parenthesized word) when the original length exceeds the limit; the truncated copy is `displayText`. Otherwise `displayText = fullText`.
7. If `isManager`: `activity.OnManagerDecision("Manager", displayText)`.
8. Else: `activity.OnTurnCompleted(agentName, displayText)`.
9. Return `result` (untouched, full text) to caller.

### Streaming path (`GetStreamingChatMessageContentsAsync`)

1. Emit `activity.OnTurnStarted(agentName)`.
2. Maintain a local `StringBuilder buffer` for the manager case only.
3. For each `delta` from inner:
   - if `isManager`: append `delta.Content` to local `buffer`; do **not** call `OnChunk` (avoids cyan "[Manager] tokens" noise — we emit a single `OnManagerDecision` at end). `yield return delta` to caller.
   - else: if `delta.Content` is non-empty, emit `activity.OnChunk(agentName, delta.Content)`; `yield return delta` to caller.
4. After stream completes normally:
   - if `isManager`: build `displayText` from `buffer` truncated per the truncation rule, emit `activity.OnManagerDecision("Manager", displayText)`.
   - else: `activity.OnTurnCompleted(agentName)` — `null` lets logger pick from its own per-agent buffer populated by `OnChunk`.
5. On `OperationCanceledException` from inner: rethrow without emission.
6. On any other `Exception ex`: `activity.OnExecutorFailed(agentName, ex)`; rethrow.

### Attributes passthrough

`Attributes` returns the same reference as `inner.Attributes`.

## Error Handling

| Scenario | Decorator action | Caller observes |
|---|---|---|
| Inner returns empty list | text=`""`, emit completion with empty text | Result returned |
| Inner throws non-OCE | `OnExecutorFailed`, rethrow | Original exception |
| Inner throws OCE | rethrow | OCE |
| Streaming partial then throws | OnChunks emitted, OnExecutorFailed, rethrow | Original exception, no OnTurnCompleted |
| Cancellation token fires mid-call | OCE rethrown, no emission | OCE |
| Manager returns >1000 char JSON | display truncated, full result returned to caller | Full text |

`OnExecutorFailed` already calls `FlushAllPendingTurns("aborted")`, which closes any other in-flight turn states with `aborted=true` and `Activity.SetStatus(Error)`. This is correct for Magentic where one agent failure usually halts the whole orchestration.

## Known Limitations

- Concurrent turns for the **same** `agentName` will conflict in the logger's `ConcurrentDictionary` keyed by `agentName`. Magentic serializes calls per agent so this does not occur in practice; if it does, one turn's state is overwritten and a single `OnTurnCompleted` is logged. Not addressed in this spec.
- Ollama via SK does not stream by default in this codebase, so token-level chunks will not appear; per-turn lines will. If SK stops calling `IChatCompletionService` directly (e.g., a future SK version uses a different inner abstraction), the decorator no longer fires. Smoke test catches this.
- The decorator emits `OnManagerDecision` for **every** manager LLM call (planning, progress ledger, replan, final answer synthesis). Logs become noisy for long Magentic runs. Mitigation: truncation to 1000 chars; consumers can grep on `Manager`.

## Tests

### Unit `tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs`

Eight tests, using `FakeChatCompletionService` stub and a real `AgentActivityLogger` with `RecordingLogger` + `RecordingConsoleWriter`:

1. **non-streaming success** — agent name "AgentX", inner returns one message "hello". Assert: TurnStarted then TurnCompleted("AgentX", "hello"); inner called once.
2. **non-streaming manager** — agent name "Manager", inner returns long JSON. Assert: TurnStarted, then OnManagerDecision("Manager", truncated to 1000+suffix); no OnTurnCompleted.
3. **non-streaming inner throws** — `InvalidOperationException`. Assert: OnExecutorFailed("AgentX", ex); exception rethrown.
4. **non-streaming OCE** — Assert: no OnExecutorFailed; OCE rethrown.
5. **streaming success** — inner yields three chunks "Hel", "lo ", "world". Assert: TurnStarted, three OnChunk calls in order, OnTurnCompleted("AgentX", null); caller receives all three deltas.
6. **streaming inner throws mid-stream** — yield two chunks then `InvalidOperationException`. Assert: two OnChunks, OnExecutorFailed, no OnTurnCompleted; exception rethrown.
7. **truncate** — inner returns 5000-char text for a non-manager agent. Assert: the `displayText` argument passed to `OnTurnCompleted` is exactly 1000 head chars + the literal suffix `… (truncated)`; the value returned to the caller still contains all 5000 chars.
8. **Attributes passthrough** — `service.Attributes` is the same reference as `inner.Attributes`.

### Test fixtures (new)

- `tests/MagenticWorkflowApp.Tests/TestDoubles/FakeChatCompletionService.cs` — stub `IChatCompletionService` configurable via ctor: returns/throws/streams. Records call count.
- Reuse existing `RecordingLogger`, `RecordingConsoleWriter`.

### Integration / regression

- `tests/AiAgetnsWorkflow.Tests/Integration/OrchestratorWiringTests.cs` — unchanged. The two existing tests still pass because Magentic path is not exercised in simulate mode.
- No new integration test for live Magentic (requires Ollama or OpenAI key). Manual smoke test:
  ```
  dotnet run --project src/
  ```
  Expected: between `STARTING MAGENTIC WORKFLOW EXECUTION` and `FINAL RESULT`, at least 4 lines of activity such as `[Manager] DECISION: ...` and `Agent CounterAgent completed turn: ...`.

## Files Touched

| File | Change | LOC |
|---|---|---|
| `src/appsettings.json` | add 2 Serilog override keys | +2 |
| `src/Services/LoggingChatCompletionService.cs` | new | ~120 |
| `src/Services/MagenticWorkflowOrchestrator.cs` | `BuildKernel` signature + 2 call sites + ResponseCallback body removed | ~+15 / -10 |
| `tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs` | new | ~250 |
| `tests/MagenticWorkflowApp.Tests/TestDoubles/FakeChatCompletionService.cs` | new | ~80 |

Total: 5 files, ~470 net new lines, 1-2 commits.

## Verification Checklist

- [ ] `dotnet build src/ tests/` — 0 errors, 0 warnings
- [ ] `dotnet test tests/MagenticWorkflowApp.Tests/` — all 13 + 8 = 21 pass
- [ ] `dotnet test tests/AiAgetnsWorkflow.Tests/` — all 28 pass (no regression)
- [ ] Manual: `dotnet run --project src/` against `workflow-sum.json` shows ≥4 lines of agent or manager activity between Start and FINAL RESULT
- [ ] Manual: `logs/` Serilog file contains structured `Agent ... completed turn: ...` and `Manager ... decision: ...` entries

## Rollback

- A: remove the two `Microsoft.SemanticKernel*` keys from `appsettings.json`.
- C: revert the implementation commits. `BuildKernel` returns to its original signature, `LoggingChatCompletionService` deleted, `ResponseCallback` body restored. Magentic returns to silent (pre-fix) behavior.
