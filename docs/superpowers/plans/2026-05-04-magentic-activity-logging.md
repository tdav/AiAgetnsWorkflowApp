# Magentic Activity Logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every Magentic agent turn and every manager LLM call visible through the existing `IAgentActivityLogger` (console + Serilog + OTEL) by decorating each Semantic Kernel `IChatCompletionService` instance.

**Architecture:** Introduce `LoggingChatCompletionService` — a sealed decorator implementing `Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService`. It wraps an inner SK chat-completion service and emits `OnTurnStarted` / `OnChunk` / `OnTurnCompleted` / `OnManagerDecision` / `OnExecutorFailed` to `IAgentActivityLogger` around each model call. `BuildKernel` registers the decorator instead of the raw `OpenAIChatCompletionService`. Each Magentic participant kernel is built with `agentName=<participant name>`; the manager kernel with `agentName="Manager"`. The existing `MagenticOrchestration.ResponseCallback` is reduced to a no-op so the decorator is the single source of activity events.

**Tech Stack:** .NET 10.0, Microsoft.SemanticKernel.Connectors.OpenAI 1.75.0, Microsoft.SemanticKernel.Agents.Orchestration 1.75.0-preview, xUnit, NSubstitute, Serilog 8.0.4, OpenTelemetry 1.10.0.

---

## File Structure

| Path | Action | Purpose |
|---|---|---|
| `src/Services/LoggingChatCompletionService.cs` | Create | The decorator. ~120 LOC. |
| `src/Services/MagenticWorkflowOrchestrator.cs` | Modify | `BuildKernel` signature + 2 call sites + ResponseCallback body. |
| `src/appsettings.json` | Modify | Add 2 Serilog `MinimumLevel.Override` keys (Step A). |
| `tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs` | Create | Unit tests, 8 cases. |
| `tests/MagenticWorkflowApp.Tests/TestDoubles/FakeChatCompletionService.cs` | Create | Stub `IChatCompletionService` configurable per test. |

Existing files reused unchanged: `RecordingLogger`, `RecordingConsoleWriter`, `AgentActivityLogger`.

---

## Task 1: Step A — Serilog overrides for Semantic Kernel

**Files:**
- Modify: `src/appsettings.json`

- [ ] **Step 1: Read current Serilog block**

Run: `grep -A 15 '"Serilog"' src/appsettings.json`
Goal: locate the `"MinimumLevel"."Override"` object.

- [ ] **Step 2: Add SK override keys**

In the `"Override"` object inside `"Serilog":{"MinimumLevel":{...}}`, add two keys (do not duplicate existing keys):

```json
"Microsoft.SemanticKernel": "Information",
"Microsoft.SemanticKernel.Agents": "Information"
```

The final `"Override"` object should contain at least these two keys plus whatever existed.

- [ ] **Step 3: Build to make sure config still parses**

Run: `dotnet build src/AiAgetnsWorkflow.csproj -nologo -v q`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/appsettings.json
git commit -m "chore: raise Serilog level for Microsoft.SemanticKernel namespaces"
```

---

## Task 2: `FakeChatCompletionService` test double

**Files:**
- Create: `tests/MagenticWorkflowApp.Tests/TestDoubles/FakeChatCompletionService.cs`

- [ ] **Step 1: Write the file**

```csharp
using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace MagenticWorkflowApp.Tests.TestDoubles;

internal sealed class FakeChatCompletionService : IChatCompletionService
{
    public IReadOnlyDictionary<string, object?> Attributes { get; } =
        new Dictionary<string, object?> { ["fake"] = true };

    public List<ChatMessageContent>? NonStreamingResult { get; set; }
    public Exception? ThrowOnNonStreaming { get; set; }

    public List<StreamingChatMessageContent>? StreamingChunks { get; set; }
    public int? ThrowAfterStreamingChunkCount { get; set; }
    public Exception? StreamingException { get; set; }

    public int NonStreamingCallCount { get; private set; }
    public int StreamingCallCount { get; private set; }

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        NonStreamingCallCount++;
        if (ThrowOnNonStreaming is not null)
        {
            throw ThrowOnNonStreaming;
        }
        IReadOnlyList<ChatMessageContent> result = NonStreamingResult ?? new List<ChatMessageContent>();
        return Task.FromResult(result);
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StreamingCallCount++;
        var chunks = StreamingChunks ?? new List<StreamingChatMessageContent>();
        for (int i = 0; i < chunks.Count; i++)
        {
            if (ThrowAfterStreamingChunkCount is int n && i == n && StreamingException is not null)
            {
                throw StreamingException;
            }
            yield return chunks[i];
            await Task.Yield();
        }
        if (ThrowAfterStreamingChunkCount is int m && m >= chunks.Count && StreamingException is not null)
        {
            throw StreamingException;
        }
    }
}
```

- [ ] **Step 2: Build tests project**

Run: `dotnet build tests/MagenticWorkflowApp.Tests -nologo -v q`
Expected: `0 Error(s)`. Warnings about unused fields are OK at this point.

- [ ] **Step 3: Commit**

```bash
git add tests/MagenticWorkflowApp.Tests/TestDoubles/FakeChatCompletionService.cs
git commit -m "test: add FakeChatCompletionService stub for decorator tests"
```

---

## Task 3: `LoggingChatCompletionService` skeleton + first failing test (non-streaming success)

**Files:**
- Create: `src/Services/LoggingChatCompletionService.cs`
- Create: `tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs`

- [ ] **Step 1: Write skeleton class (compiles, throws NotImplementedException)**

Create `src/Services/LoggingChatCompletionService.cs`:

```csharp
using System.Runtime.CompilerServices;
using MagenticWorkflowApp.Interfaces;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MagenticWorkflowApp.Services;

internal sealed class LoggingChatCompletionService : IChatCompletionService
{
    internal const int TextTruncationLimit = 1000;
    internal const string TruncationSuffix = "… (truncated)";
    internal const string ManagerAgentName = "Manager";

    private readonly IChatCompletionService inner;
    private readonly string agentName;
    private readonly IAgentActivityLogger activity;
    private readonly bool isManager;

    public LoggingChatCompletionService(
        IChatCompletionService inner,
        string agentName,
        IAgentActivityLogger activity)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.agentName = agentName ?? throw new ArgumentNullException(nameof(agentName));
        this.activity = activity ?? throw new ArgumentNullException(nameof(activity));
        this.isManager = string.Equals(agentName, ManagerAgentName, StringComparison.Ordinal);
    }

    public IReadOnlyDictionary<string, object?> Attributes => inner.Attributes;

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    private static string Truncate(string text)
        => text.Length <= TextTruncationLimit ? text : text.Substring(0, TextTruncationLimit) + TruncationSuffix;
}
```

- [ ] **Step 2: Write the first failing test**

Create `tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;
using MagenticWorkflowApp.Tests.TestDoubles;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using NSubstitute;
using Xunit;

namespace MagenticWorkflowApp.Tests.Services;

public class LoggingChatCompletionServiceTests
{
    private static (LoggingChatCompletionService sut, FakeChatCompletionService inner, IAgentActivityLogger activity)
        CreateSut(string agentName = "AgentX")
    {
        var inner = new FakeChatCompletionService();
        var activity = Substitute.For<IAgentActivityLogger>();
        var sut = new LoggingChatCompletionService(inner, agentName, activity);
        return (sut, inner, activity);
    }

    [Fact]
    public async Task NonStreaming_Success_EmitsTurnStartedAndCompleted()
    {
        var (sut, inner, activity) = CreateSut("AgentX");
        inner.NonStreamingResult = new List<ChatMessageContent>
        {
            new(AuthorRole.Assistant, "hello")
        };

        var result = await sut.GetChatMessageContentsAsync(new ChatHistory());

        result.Should().HaveCount(1);
        result[0].Content.Should().Be("hello");
        inner.NonStreamingCallCount.Should().Be(1);
        Received.InOrder(() =>
        {
            activity.OnTurnStarted("AgentX", null);
            activity.OnTurnCompleted("AgentX", "hello");
        });
        activity.DidNotReceiveWithAnyArgs().OnManagerDecision(default!, default!);
        activity.DidNotReceiveWithAnyArgs().OnExecutorFailed(default!, default!);
    }
}
```

- [ ] **Step 3: Run the test, verify it fails**

Run: `dotnet test tests/MagenticWorkflowApp.Tests --filter "FullyQualifiedName~LoggingChatCompletionServiceTests" -nologo -v q`
Expected: 1 failed. Failure message contains `NotImplementedException`.

- [ ] **Step 4: Implement non-streaming success path**

Replace the body of `GetChatMessageContentsAsync` in `src/Services/LoggingChatCompletionService.cs`:

```csharp
public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
    ChatHistory chatHistory,
    PromptExecutionSettings? executionSettings = null,
    Kernel? kernel = null,
    CancellationToken cancellationToken = default)
{
    activity.OnTurnStarted(agentName);

    IReadOnlyList<ChatMessageContent> result;
    try
    {
        result = await inner.GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex)
    {
        activity.OnExecutorFailed(agentName, ex);
        throw;
    }

    var fullText = string.Concat(result.Select(c => c.Content ?? string.Empty));
    var displayText = Truncate(fullText);

    if (isManager)
    {
        activity.OnManagerDecision(ManagerAgentName, displayText);
    }
    else
    {
        activity.OnTurnCompleted(agentName, displayText);
    }

    return result;
}
```

Mark the method `async`. Remove `=> throw new NotImplementedException();`.

- [ ] **Step 5: Run test, verify pass**

Run: `dotnet test tests/MagenticWorkflowApp.Tests --filter "FullyQualifiedName~LoggingChatCompletionServiceTests.NonStreaming_Success" -nologo -v q`
Expected: 1 passed.

- [ ] **Step 6: Commit**

```bash
git add src/Services/LoggingChatCompletionService.cs tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs
git commit -m "feat: add LoggingChatCompletionService non-streaming success path"
```

---

## Task 4: Manager non-streaming → `OnManagerDecision`

**Files:**
- Modify: `tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs`

- [ ] **Step 1: Add failing test**

Append inside the test class:

```csharp
[Fact]
public async Task NonStreaming_ManagerRole_EmitsOnManagerDecisionInsteadOfTurnCompleted()
{
    var (sut, inner, activity) = CreateSut("Manager");
    inner.NonStreamingResult = new List<ChatMessageContent>
    {
        new(AuthorRole.Assistant, "{ \"plan\": \"do X\" }")
    };

    await sut.GetChatMessageContentsAsync(new ChatHistory());

    Received.InOrder(() =>
    {
        activity.OnTurnStarted("Manager", null);
        activity.OnManagerDecision("Manager", "{ \"plan\": \"do X\" }");
    });
    activity.DidNotReceiveWithAnyArgs().OnTurnCompleted(default!, default!);
}
```

- [ ] **Step 2: Run, verify pass (already implemented in Task 3)**

Run: `dotnet test tests/MagenticWorkflowApp.Tests --filter "FullyQualifiedName~NonStreaming_ManagerRole" -nologo -v q`
Expected: 1 passed.

- [ ] **Step 3: Commit**

```bash
git add tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs
git commit -m "test: cover Manager-role non-streaming routing to OnManagerDecision"
```

---

## Task 5: Non-streaming exception → `OnExecutorFailed`, OCE → no emission

**Files:**
- Modify: `tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs`

- [ ] **Step 1: Add tests**

Append:

```csharp
[Fact]
public async Task NonStreaming_InnerThrows_EmitsOnExecutorFailedAndRethrows()
{
    var (sut, inner, activity) = CreateSut("AgentX");
    var ex = new InvalidOperationException("boom");
    inner.ThrowOnNonStreaming = ex;

    Func<Task> act = () => sut.GetChatMessageContentsAsync(new ChatHistory());

    var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
    thrown.Which.Should().BeSameAs(ex);

    Received.InOrder(() =>
    {
        activity.OnTurnStarted("AgentX", null);
        activity.OnExecutorFailed("AgentX", ex);
    });
    activity.DidNotReceiveWithAnyArgs().OnTurnCompleted(default!, default!);
    activity.DidNotReceiveWithAnyArgs().OnManagerDecision(default!, default!);
}

[Fact]
public async Task NonStreaming_OperationCanceled_NoExecutorFailedEmission()
{
    var (sut, inner, activity) = CreateSut("AgentX");
    inner.ThrowOnNonStreaming = new OperationCanceledException();

    Func<Task> act = () => sut.GetChatMessageContentsAsync(new ChatHistory());

    await act.Should().ThrowAsync<OperationCanceledException>();
    activity.Received(1).OnTurnStarted("AgentX", null);
    activity.DidNotReceiveWithAnyArgs().OnExecutorFailed(default!, default!);
    activity.DidNotReceiveWithAnyArgs().OnTurnCompleted(default!, default!);
}
```

- [ ] **Step 2: Run, verify both pass**

Run: `dotnet test tests/MagenticWorkflowApp.Tests --filter "FullyQualifiedName~LoggingChatCompletionServiceTests" -nologo -v q`
Expected: 4 passed (cumulative).

- [ ] **Step 3: Commit**

```bash
git add tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs
git commit -m "test: cover non-streaming exception and cancellation paths"
```

---

## Task 6: Streaming success path

**Files:**
- Modify: `src/Services/LoggingChatCompletionService.cs`
- Modify: `tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs`

- [ ] **Step 1: Add failing streaming test**

Append:

```csharp
[Fact]
public async Task Streaming_Success_EmitsChunkPerDeltaAndTurnCompletedAtEnd()
{
    var (sut, inner, activity) = CreateSut("AgentX");
    inner.StreamingChunks = new List<StreamingChatMessageContent>
    {
        new(AuthorRole.Assistant, "Hel"),
        new(AuthorRole.Assistant, "lo "),
        new(AuthorRole.Assistant, "world"),
    };

    var collected = new List<string?>();
    await foreach (var d in sut.GetStreamingChatMessageContentsAsync(new ChatHistory()))
    {
        collected.Add(d.Content);
    }

    collected.Should().Equal("Hel", "lo ", "world");
    inner.StreamingCallCount.Should().Be(1);

    Received.InOrder(() =>
    {
        activity.OnTurnStarted("AgentX", null);
        activity.OnChunk("AgentX", "Hel");
        activity.OnChunk("AgentX", "lo ");
        activity.OnChunk("AgentX", "world");
        activity.OnTurnCompleted("AgentX", null);
    });
}
```

- [ ] **Step 2: Run, verify it fails (NotImplementedException)**

Run: `dotnet test tests/MagenticWorkflowApp.Tests --filter "FullyQualifiedName~Streaming_Success" -nologo -v q`
Expected: 1 failed.

- [ ] **Step 3: Implement streaming method**

Replace `GetStreamingChatMessageContentsAsync` body in `src/Services/LoggingChatCompletionService.cs`:

```csharp
public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
    ChatHistory chatHistory,
    PromptExecutionSettings? executionSettings = null,
    Kernel? kernel = null,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    activity.OnTurnStarted(agentName);

    var managerBuffer = isManager ? new System.Text.StringBuilder() : null;
    IAsyncEnumerator<StreamingChatMessageContent> enumerator;
    try
    {
        enumerator = inner.GetStreamingChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken).GetAsyncEnumerator(cancellationToken);
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception ex)
    {
        activity.OnExecutorFailed(agentName, ex);
        throw;
    }

    try
    {
        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                activity.OnExecutorFailed(agentName, ex);
                throw;
            }

            if (!hasNext) break;
            var delta = enumerator.Current;
            if (managerBuffer is not null)
            {
                if (!string.IsNullOrEmpty(delta.Content)) managerBuffer.Append(delta.Content);
            }
            else if (!string.IsNullOrEmpty(delta.Content))
            {
                activity.OnChunk(agentName, delta.Content!);
            }
            yield return delta;
        }
    }
    finally
    {
        await enumerator.DisposeAsync().ConfigureAwait(false);
    }

    if (isManager)
    {
        activity.OnManagerDecision(ManagerAgentName, Truncate(managerBuffer!.ToString()));
    }
    else
    {
        activity.OnTurnCompleted(agentName);
    }
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test tests/MagenticWorkflowApp.Tests --filter "FullyQualifiedName~Streaming_Success" -nologo -v q`
Expected: 1 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Services/LoggingChatCompletionService.cs tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs
git commit -m "feat: add LoggingChatCompletionService streaming success path"
```

---

## Task 7: Streaming manager-role buffers and emits `OnManagerDecision`

**Files:**
- Modify: `tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs`

- [ ] **Step 1: Add test**

Append:

```csharp
[Fact]
public async Task Streaming_ManagerRole_BuffersChunksAndEmitsOnManagerDecisionAtEnd()
{
    var (sut, inner, activity) = CreateSut("Manager");
    inner.StreamingChunks = new List<StreamingChatMessageContent>
    {
        new(AuthorRole.Assistant, "{\"step\":"),
        new(AuthorRole.Assistant, "1}"),
    };

    var collected = new List<string?>();
    await foreach (var d in sut.GetStreamingChatMessageContentsAsync(new ChatHistory()))
    {
        collected.Add(d.Content);
    }

    collected.Should().Equal("{\"step\":", "1}");
    activity.DidNotReceiveWithAnyArgs().OnChunk(default!, default!);
    activity.DidNotReceiveWithAnyArgs().OnTurnCompleted(default!, default!);
    Received.InOrder(() =>
    {
        activity.OnTurnStarted("Manager", null);
        activity.OnManagerDecision("Manager", "{\"step\":1}");
    });
}
```

- [ ] **Step 2: Run, verify pass**

Run: `dotnet test tests/MagenticWorkflowApp.Tests --filter "FullyQualifiedName~Streaming_ManagerRole" -nologo -v q`
Expected: 1 passed.

- [ ] **Step 3: Commit**

```bash
git add tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs
git commit -m "test: cover streaming manager-role buffering"
```

---

## Task 8: Streaming exception mid-stream

**Files:**
- Modify: `tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs`

- [ ] **Step 1: Add test**

Append:

```csharp
[Fact]
public async Task Streaming_InnerThrowsMidStream_EmitsTwoChunksThenExecutorFailed()
{
    var (sut, inner, activity) = CreateSut("AgentX");
    inner.StreamingChunks = new List<StreamingChatMessageContent>
    {
        new(AuthorRole.Assistant, "first"),
        new(AuthorRole.Assistant, "second"),
        new(AuthorRole.Assistant, "third"),
    };
    var ex = new InvalidOperationException("mid-stream boom");
    inner.ThrowAfterStreamingChunkCount = 2;
    inner.StreamingException = ex;

    var collected = new List<string?>();
    Func<Task> act = async () =>
    {
        await foreach (var d in sut.GetStreamingChatMessageContentsAsync(new ChatHistory()))
        {
            collected.Add(d.Content);
        }
    };

    var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
    thrown.Which.Should().BeSameAs(ex);
    collected.Should().Equal("first", "second");

    Received.InOrder(() =>
    {
        activity.OnTurnStarted("AgentX", null);
        activity.OnChunk("AgentX", "first");
        activity.OnChunk("AgentX", "second");
        activity.OnExecutorFailed("AgentX", ex);
    });
    activity.DidNotReceiveWithAnyArgs().OnTurnCompleted(default!, default!);
}
```

- [ ] **Step 2: Run, verify pass**

Run: `dotnet test tests/MagenticWorkflowApp.Tests --filter "FullyQualifiedName~Streaming_InnerThrowsMidStream" -nologo -v q`
Expected: 1 passed.

- [ ] **Step 3: Commit**

```bash
git add tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs
git commit -m "test: cover streaming mid-stream failure path"
```

---

## Task 9: Truncation

**Files:**
- Modify: `tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs`

- [ ] **Step 1: Add test**

Append:

```csharp
[Fact]
public async Task NonStreaming_LongText_TruncatesEmittedDisplayButReturnsFullToCaller()
{
    var (sut, inner, activity) = CreateSut("AgentX");
    var longText = new string('x', 5000);
    inner.NonStreamingResult = new List<ChatMessageContent>
    {
        new(AuthorRole.Assistant, longText)
    };

    var result = await sut.GetChatMessageContentsAsync(new ChatHistory());

    result[0].Content.Should().Be(longText);
    result[0].Content!.Length.Should().Be(5000);

    var expectedDisplay = new string('x', LoggingChatCompletionService.TextTruncationLimit)
        + LoggingChatCompletionService.TruncationSuffix;
    activity.Received(1).OnTurnCompleted("AgentX", expectedDisplay);
}
```

- [ ] **Step 2: Run, verify pass**

Run: `dotnet test tests/MagenticWorkflowApp.Tests --filter "FullyQualifiedName~LongText_Truncates" -nologo -v q`
Expected: 1 passed.

- [ ] **Step 3: Commit**

```bash
git add tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs
git commit -m "test: cover non-streaming text truncation"
```

---

## Task 10: `Attributes` passthrough

**Files:**
- Modify: `tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs`

- [ ] **Step 1: Add test**

Append:

```csharp
[Fact]
public void Attributes_ReturnsInnerReference()
{
    var (sut, inner, _) = CreateSut("AgentX");
    sut.Attributes.Should().BeSameAs(inner.Attributes);
}
```

- [ ] **Step 2: Run, verify pass**

Run: `dotnet test tests/MagenticWorkflowApp.Tests --filter "FullyQualifiedName~Attributes_ReturnsInnerReference" -nologo -v q`
Expected: 1 passed.

- [ ] **Step 3: Commit**

```bash
git add tests/MagenticWorkflowApp.Tests/Services/LoggingChatCompletionServiceTests.cs
git commit -m "test: cover Attributes passthrough"
```

---

## Task 11: Verify all decorator tests pass together

**Files:** none (verification only)

- [ ] **Step 1: Run full test class**

Run: `dotnet test tests/MagenticWorkflowApp.Tests --filter "FullyQualifiedName~LoggingChatCompletionServiceTests" -nologo -v q`
Expected: 8 passed, 0 failed.

- [ ] **Step 2: Run full test project**

Run: `dotnet test tests/MagenticWorkflowApp.Tests -nologo -v q`
Expected: 21 passed (13 existing + 8 new), 0 failed.

- [ ] **Step 3: No commit (verification only)**

If anything fails: read the output, fix the failing test, then re-run. Do not advance to Task 12 until all 21 pass.

---

## Task 12: Refactor `BuildKernel` to register the decorator

**Files:**
- Modify: `src/Services/MagenticWorkflowOrchestrator.cs`

The current `BuildKernel` (line 762) is `private static`. It will no longer be static (we need access to instance fields, but we'll inject through parameters to keep it static). It will accept new optional parameters `agentName` and `activity`.

- [ ] **Step 1: Read current BuildKernel context**

Run: `grep -n "BuildKernel\|AddOpenAIChatCompletion\|AsIChatClient\|AddPolicy" src/Services/MagenticWorkflowOrchestrator.cs`
Note the line numbers for the call sites at the agent loop and the manager kernel.

- [ ] **Step 2: Replace `BuildKernel` body**

Locate the existing method:

```csharp
private static Kernel BuildKernel(string modelId, string? openAiApiKey, string? ollamaEndpoint, bool enableThinking = false)
{
    if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
    {
        var options = new OpenAI.OpenAIClientOptions { Endpoint = new Uri(ollamaEndpoint + "/v1"), NetworkTimeout = TimeSpan.FromMinutes(5) };
        options.AddPolicy(new OllamaThinkingPolicy(enableThinking), PipelinePosition.PerCall);
        var ollamaClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential("ollama"), options);
        return Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelId, ollamaClient)
            .Build();
    }
    return Kernel.CreateBuilder()
        .AddOpenAIChatCompletion(modelId, openAiApiKey!)
        .Build();
}
```

Replace it with:

```csharp
private static Kernel BuildKernel(
    string modelId,
    string? openAiApiKey,
    string? ollamaEndpoint,
    bool enableThinking = false,
    string? agentName = null,
    IAgentActivityLogger? activity = null)
{
    OpenAIClient client;
    if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
    {
        var options = new OpenAI.OpenAIClientOptions
        {
            Endpoint = new Uri(ollamaEndpoint + "/v1"),
            NetworkTimeout = TimeSpan.FromMinutes(5),
        };
        options.AddPolicy(new OllamaThinkingPolicy(enableThinking), PipelinePosition.PerCall);
        client = new OpenAIClient(new System.ClientModel.ApiKeyCredential("ollama"), options);
    }
    else
    {
        client = new OpenAIClient(openAiApiKey!);
    }

    var builder = Kernel.CreateBuilder();
    builder.AddOpenAIChatCompletion(modelId, client);

    if (agentName is not null && activity is not null)
    {
        var descriptor = builder.Services.LastOrDefault(d =>
            d.ServiceType == typeof(Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService));
        if (descriptor is not null)
        {
            builder.Services.Remove(descriptor);
            builder.Services.AddSingleton<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>(sp =>
            {
                Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService inner = descriptor.ImplementationFactory is not null
                    ? (Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService)descriptor.ImplementationFactory(sp)
                    : descriptor.ImplementationInstance is not null
                        ? (Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService)descriptor.ImplementationInstance
                        : (Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
                return new LoggingChatCompletionService(inner, agentName, activity);
            });
        }
    }

    return builder.Build();
}
```

Add these usings at the top of the file if missing:

```csharp
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
```

- [ ] **Step 3: Build, verify it compiles**

Run: `dotnet build src/AiAgetnsWorkflow.csproj -nologo -v q`
Expected: `0 Error(s)`. If you see `'IChatCompletionService' is ambiguous`, fully-qualify both occurrences with `Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService` (already done above).

- [ ] **Step 4: Run all tests, no regression**

Run: `dotnet test -nologo -v q`
Expected: 21 + 28 = 49 passed total, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/Services/MagenticWorkflowOrchestrator.cs
git commit -m "feat: BuildKernel registers LoggingChatCompletionService when agent context supplied"
```

---

## Task 13: Wire `ExecuteMagenticWorkflowAsync` to pass agent context

**Files:**
- Modify: `src/Services/MagenticWorkflowOrchestrator.cs` (around lines 350-405)

- [ ] **Step 1: Update agent loop call site**

Find the line:
```csharp
var kernel = BuildKernel(agentConfig.ModelId, openAiApiKey, ollamaEndpoint, agentConfig.EnableThinking);
```
Replace with:
```csharp
var kernel = BuildKernel(
    agentConfig.ModelId,
    openAiApiKey,
    ollamaEndpoint,
    agentConfig.EnableThinking,
    agentName: agentConfig.Name,
    activity: activity);
```

- [ ] **Step 2: Update manager kernel call site**

Find the line:
```csharp
var managerKernel = BuildKernel(config.Manager.ModelId, openAiApiKey, ollamaEndpoint, config.Manager.EnableThinking);
```
Replace with:
```csharp
var managerKernel = BuildKernel(
    config.Manager.ModelId,
    openAiApiKey,
    ollamaEndpoint,
    config.Manager.EnableThinking,
    agentName: "Manager",
    activity: activity);
```

- [ ] **Step 3: Reduce `ResponseCallback` to no-op**

Find the assignment:
```csharp
ResponseCallback = response =>
{
    var name = response.AuthorName ?? "?";
    var text = response.Content ?? string.Empty;

    if (string.Equals(name, config.Manager.ModelId, StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Manager", StringComparison.OrdinalIgnoreCase))
    {
        activity.OnManagerDecision(name, text);
    }
    else
    {
        activity.OnTurnCompleted(name, text);
    }
    return ValueTask.CompletedTask;
},
```

Replace with:
```csharp
ResponseCallback = _ => ValueTask.CompletedTask,
```

- [ ] **Step 4: Build**

Run: `dotnet build src/AiAgetnsWorkflow.csproj -nologo -v q`
Expected: `0 Error(s) 0 Warning(s)`.

- [ ] **Step 5: Run all tests**

Run: `dotnet test -nologo -v q`
Expected: 49 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/Services/MagenticWorkflowOrchestrator.cs
git commit -m "feat: wire Magentic agent and manager kernels through LoggingChatCompletionService"
```

---

## Task 14: Manual smoke test

**Files:** none (run-only)

- [ ] **Step 1: Ensure Ollama is running locally on `http://localhost:11434`**

Run: `curl -s http://localhost:11434/api/version`
Expected: JSON response with a `version` key. If it fails, start Ollama, then retry. (If you cannot run Ollama, skip this task and report status.)

- [ ] **Step 2: Run the default workflow**

Run: `dotnet run --project src/`
Expected: between the line `STARTING MAGENTIC WORKFLOW EXECUTION` and the line `FINAL RESULT:`, you observe at least four lines of activity matching one of these patterns:
- `Agent <Name> turn started` (Serilog file)
- `[<Name>] (started)` or `┌── <Name> ──` (console)
- `Agent <Name> completed turn: chunks=...` (Serilog file)
- `Manager <Name> decision: ...` (Serilog file)
- `[Manager] DECISION: ...` (console, cyan)

- [ ] **Step 3: Inspect Serilog file**

Locate today's log file under `src/bin/Debug/net10.0/logs/` (the path is determined by the existing Serilog config). Confirm it contains entries with both `CounterAgent` and `Manager` source names.

- [ ] **Step 4: Record observations**

If activity is visible: report success.

If activity is **not** visible:
- Check that `LoggingChatCompletionService` is actually being instantiated by adding a one-time `Console.WriteLine($"[DECORATOR] wrapping {agentName}")` in the constructor. Re-run. If you see two `[DECORATOR]` lines (one per kernel) but still no activity, SK is bypassing `IChatCompletionService` (probable: SK 1.75 might use a newer abstraction). In that case, fall back to **plan B**: revert Tasks 12-13, switch to raising Serilog levels only (Task 1 stays). File a follow-up issue.

- [ ] **Step 5: No commit (verification only)**

Remove any temporary `Console.WriteLine` you added.

---

## Task 15: Final regression sweep

**Files:** none (verification only)

- [ ] **Step 1: Build everything**

Run: `dotnet build -nologo -v q`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Run all tests across both test projects**

Run: `dotnet test -nologo -v q`
Expected: 49 passed (21 in `MagenticWorkflowApp.Tests`, 28 in `AiAgetnsWorkflow.Tests`), 0 failed, 0 skipped.

- [ ] **Step 3: Sanity-check git state**

Run: `git status` and `git log --oneline origin/main..HEAD`
Expected: clean working tree; commits from Tasks 1-13 listed (no commits from Tasks 11, 14, 15 — those are verification-only).

- [ ] **Step 4: No commit (verification only)**

If verification fails, return to the failing task and fix.
