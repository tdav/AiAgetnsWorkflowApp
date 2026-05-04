# Agent Activity Logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide runtime visibility into agent activity (which agent, what it says, what tools it calls, manager decisions) by routing all agent events through a single `IAgentActivityLogger` service that fans out to Console (live tokens), Serilog file (per-turn aggregated entries), and OpenTelemetry (spans + metrics).

**Architecture:** Singleton `AgentActivityLogger` buffers streaming chunks per agent in a `ConcurrentDictionary<string, AgentTurnState>`, writes live tokens to `IConsoleWriter`, and emits a single `ILogger.LogInformation` entry on `OnTurnCompleted`. `MagenticWorkflowOrchestrator` is refactored to call this service from `HandleWorkflowEvent` and Magentic `ResponseCallback`. `Program.cs` wires Serilog (Console + rolling file) and OpenTelemetry (`ActivitySource` + `Meter`) via DI.

**Tech Stack:** .NET 10.0, xUnit, Serilog (Sinks.Console + Sinks.File + Settings.Configuration), OpenTelemetry (Extensions.Hosting + Exporter.Console), Microsoft.Agents.AI.Workflows 1.3.0, Microsoft.SemanticKernel.Agents.Magentic.

**Spec:** [docs/superpowers/specs/2026-05-04-agent-activity-logging-design.md](../specs/2026-05-04-agent-activity-logging-design.md)

---

## File Structure

```
src/
  Interfaces/
    IAgentActivityLogger.cs          [CREATE] contract
    IConsoleWriter.cs                [CREATE] thin Console wrapper for testability
  Models/
    AgentActivityKind.cs             [CREATE] enum
    WorkflowDisplayMode.cs           [CREATE] enum (Sequential | Concurrent)
  Services/
    AgentActivityLogger.cs           [CREATE] main implementation
    DefaultConsoleWriter.cs          [CREATE] writes to System.Console
    MagenticWorkflowOrchestrator.cs  [MODIFY] replace LogEvent calls with IAgentActivityLogger
  Program.cs                         [MODIFY] DI: Serilog, OpenTelemetry, IAgentActivityLogger, IConsoleWriter
  appsettings.json                   [MODIFY] sections "Serilog", "OpenTelemetry"
  AiAgetnsWorkflow.csproj            [MODIFY] NuGet packages

tests/
  MagenticWorkflowApp.Tests/
    MagenticWorkflowApp.Tests.csproj         [CREATE]
    Services/
      AgentActivityLoggerTests.cs            [CREATE] all unit tests
    TestDoubles/
      RecordingConsoleWriter.cs              [CREATE] captures Console output
      RecordingLogger.cs                     [CREATE] captures ILogger entries
```

Each file has one focused responsibility. `AgentActivityLogger` is the only file that knows about buffering + OTEL + ILogger fan-out. Console output goes through `IConsoleWriter` so tests can capture it without `Console.SetOut`.

---

## Task 1: Create xUnit test project skeleton

**Files:**
- Create: `tests/MagenticWorkflowApp.Tests/MagenticWorkflowApp.Tests.csproj`
- Create: `tests/MagenticWorkflowApp.Tests/UsingsSmokeTest.cs`

- [ ] **Step 1: Create test project directory and csproj**

```bash
mkdir -p tests/MagenticWorkflowApp.Tests
```

Write `tests/MagenticWorkflowApp.Tests/MagenticWorkflowApp.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>MagenticWorkflowApp.Tests</RootNamespace>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.*" />
    <PackageReference Include="xunit" Version="2.9.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.*" />
    <PackageReference Include="Moq" Version="4.20.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\AiAgetnsWorkflow.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add a smoke test that fails first**

Write `tests/MagenticWorkflowApp.Tests/UsingsSmokeTest.cs`:

```csharp
namespace MagenticWorkflowApp.Tests;

public class UsingsSmokeTest
{
    [Fact]
    public void TestProjectCompilesAndDiscoversTests()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 3: Build and run test**

Run:
```bash
dotnet build tests/MagenticWorkflowApp.Tests/
dotnet test tests/MagenticWorkflowApp.Tests/
```

Expected: `Passed: 1`. If build fails because `src/AiAgetnsWorkflow.csproj` does not exist with that name, locate the actual src csproj and update `<ProjectReference>` accordingly.

- [ ] **Step 4: Commit**

```bash
git add tests/MagenticWorkflowApp.Tests/
git commit -m "test: scaffold MagenticWorkflowApp.Tests xUnit project"
```

---

## Task 2: Create enums (AgentActivityKind, WorkflowDisplayMode)

**Files:**
- Create: `src/Models/AgentActivityKind.cs`
- Create: `src/Models/WorkflowDisplayMode.cs`

- [ ] **Step 1: Write `WorkflowDisplayMode.cs`**

Write `src/Models/WorkflowDisplayMode.cs`:

```csharp
namespace MagenticWorkflowApp.Models;

public enum WorkflowDisplayMode
{
    Sequential,
    Concurrent,
}
```

- [ ] **Step 2: Write `AgentActivityKind.cs`**

Write `src/Models/AgentActivityKind.cs`:

```csharp
namespace MagenticWorkflowApp.Models;

public enum AgentActivityKind
{
    TurnStarted,
    Chunk,
    TurnCompleted,
    ToolCall,
    ManagerDecision,
    ExecutorFailed,
    WorkflowError,
    WorkflowOutput,
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/`
Expected: succeeds with no warnings.

- [ ] **Step 4: Commit**

```bash
git add src/Models/AgentActivityKind.cs src/Models/WorkflowDisplayMode.cs
git commit -m "feat: add AgentActivityKind and WorkflowDisplayMode enums"
```

---

## Task 3: Create IConsoleWriter contract + DefaultConsoleWriter

**Files:**
- Create: `src/Interfaces/IConsoleWriter.cs`
- Create: `src/Services/DefaultConsoleWriter.cs`

- [ ] **Step 1: Write `IConsoleWriter.cs`**

Write `src/Interfaces/IConsoleWriter.cs`:

```csharp
namespace MagenticWorkflowApp.Interfaces;

public interface IConsoleWriter
{
    void Write(string text);
    void WriteLine(string text);
    void WriteWithColor(string text, ConsoleColor color);
    void WriteLineWithColor(string text, ConsoleColor color);
}
```

- [ ] **Step 2: Write `DefaultConsoleWriter.cs`**

Write `src/Services/DefaultConsoleWriter.cs`:

```csharp
using MagenticWorkflowApp.Interfaces;

namespace MagenticWorkflowApp.Services;

public sealed class DefaultConsoleWriter : IConsoleWriter
{
    private readonly object syncRoot = new();

    public void Write(string text)
    {
        try { Console.Write(text); }
        catch (IOException) { /* closed stream — ignore */ }
    }

    public void WriteLine(string text)
    {
        try { Console.WriteLine(text); }
        catch (IOException) { /* closed stream — ignore */ }
    }

    public void WriteWithColor(string text, ConsoleColor color)
    {
        lock (syncRoot)
        {
            try
            {
                Console.ForegroundColor = color;
                Console.Write(text);
            }
            catch (IOException) { }
            finally { Console.ResetColor(); }
        }
    }

    public void WriteLineWithColor(string text, ConsoleColor color)
    {
        lock (syncRoot)
        {
            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine(text);
            }
            catch (IOException) { }
            finally { Console.ResetColor(); }
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/`
Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Interfaces/IConsoleWriter.cs src/Services/DefaultConsoleWriter.cs
git commit -m "feat: add IConsoleWriter abstraction and DefaultConsoleWriter"
```

---

## Task 4: Create RecordingConsoleWriter test double

**Files:**
- Create: `tests/MagenticWorkflowApp.Tests/TestDoubles/RecordingConsoleWriter.cs`

- [ ] **Step 1: Write the test double**

Write `tests/MagenticWorkflowApp.Tests/TestDoubles/RecordingConsoleWriter.cs`:

```csharp
using System.Text;
using MagenticWorkflowApp.Interfaces;

namespace MagenticWorkflowApp.Tests.TestDoubles;

public sealed class RecordingConsoleWriter : IConsoleWriter
{
    private readonly StringBuilder buffer = new();
    private readonly List<(string Text, ConsoleColor? Color)> entries = new();
    private readonly object syncRoot = new();

    public IReadOnlyList<(string Text, ConsoleColor? Color)> Entries
    {
        get { lock (syncRoot) { return entries.ToList(); } }
    }

    public string AllText
    {
        get { lock (syncRoot) { return buffer.ToString(); } }
    }

    public void Write(string text)
    {
        lock (syncRoot)
        {
            buffer.Append(text);
            entries.Add((text, null));
        }
    }

    public void WriteLine(string text)
    {
        lock (syncRoot)
        {
            buffer.AppendLine(text);
            entries.Add((text + Environment.NewLine, null));
        }
    }

    public void WriteWithColor(string text, ConsoleColor color)
    {
        lock (syncRoot)
        {
            buffer.Append(text);
            entries.Add((text, color));
        }
    }

    public void WriteLineWithColor(string text, ConsoleColor color)
    {
        lock (syncRoot)
        {
            buffer.AppendLine(text);
            entries.Add((text + Environment.NewLine, color));
        }
    }
}
```

- [ ] **Step 2: Smoke-test the recorder itself**

Add at the bottom of `UsingsSmokeTest.cs`:

```csharp
[Fact]
public void RecordingConsoleWriter_CapturesText()
{
    var w = new TestDoubles.RecordingConsoleWriter();
    w.Write("hello ");
    w.WriteWithColor("world", ConsoleColor.Cyan);
    Assert.Equal("hello world", w.AllText);
    Assert.Equal(2, w.Entries.Count);
    Assert.Equal(ConsoleColor.Cyan, w.Entries[1].Color);
}
```

- [ ] **Step 3: Run test**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/`
Expected: `Passed: 2`.

- [ ] **Step 4: Commit**

```bash
git add tests/MagenticWorkflowApp.Tests/
git commit -m "test: add RecordingConsoleWriter test double"
```

---

## Task 5: Define IAgentActivityLogger contract

**Files:**
- Create: `src/Interfaces/IAgentActivityLogger.cs`

- [ ] **Step 1: Write the interface**

Write `src/Interfaces/IAgentActivityLogger.cs`:

```csharp
using MagenticWorkflowApp.Models;

namespace MagenticWorkflowApp.Interfaces;

public interface IAgentActivityLogger
{
    void SetWorkflowMode(WorkflowDisplayMode mode);

    void OnTurnStarted(string agent, string? executorId = null);
    void OnChunk(string agent, string text);
    void OnTurnCompleted(string agent, string? fullText = null);

    void OnToolCall(string agent, string toolName, string? args = null);
    void OnManagerDecision(string managerName, string decision);

    void OnExecutorFailed(string executorId, Exception exception);
    void OnWorkflowError(Exception exception);
    void OnWorkflowOutput(string output);

    void FlushAllPendingTurns(string reason);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/`
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Interfaces/IAgentActivityLogger.cs
git commit -m "feat: add IAgentActivityLogger contract"
```

---

## Task 6: Skeleton AgentActivityLogger that throws NotImplementedException — TDD red phase

**Files:**
- Create: `src/Services/AgentActivityLogger.cs`
- Create: `tests/MagenticWorkflowApp.Tests/Services/AgentActivityLoggerTests.cs`

- [ ] **Step 1: Write failing test for chunk accumulation**

Write `tests/MagenticWorkflowApp.Tests/Services/AgentActivityLoggerTests.cs`:

```csharp
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;
using MagenticWorkflowApp.Tests.TestDoubles;
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
```

- [ ] **Step 2: Write skeleton class with NotImplementedException**

Write `src/Services/AgentActivityLogger.cs`:

```csharp
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services;

public sealed class AgentActivityLogger : IAgentActivityLogger
{
    private readonly ILogger<AgentActivityLogger> logger;
    private readonly IConsoleWriter console;

    public AgentActivityLogger(ILogger<AgentActivityLogger> logger, IConsoleWriter console)
    {
        this.logger = logger;
        this.console = console;
    }

    public void SetWorkflowMode(WorkflowDisplayMode mode) => throw new NotImplementedException();
    public void OnTurnStarted(string agent, string? executorId = null) => throw new NotImplementedException();
    public void OnChunk(string agent, string text) => throw new NotImplementedException();
    public void OnTurnCompleted(string agent, string? fullText = null) => throw new NotImplementedException();
    public void OnToolCall(string agent, string toolName, string? args = null) => throw new NotImplementedException();
    public void OnManagerDecision(string managerName, string decision) => throw new NotImplementedException();
    public void OnExecutorFailed(string executorId, Exception exception) => throw new NotImplementedException();
    public void OnWorkflowError(Exception exception) => throw new NotImplementedException();
    public void OnWorkflowOutput(string output) => throw new NotImplementedException();
    public void FlushAllPendingTurns(string reason) => throw new NotImplementedException();
}
```

- [ ] **Step 3: Run test, verify it fails on NotImplementedException**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/ --filter FullyQualifiedName~OnChunk_AccumulatesIntoBuffer`
Expected: FAIL with `System.NotImplementedException`.

- [ ] **Step 4: Commit (red phase)**

```bash
git add src/Services/AgentActivityLogger.cs tests/MagenticWorkflowApp.Tests/Services/AgentActivityLoggerTests.cs
git commit -m "test: add red-phase skeleton for AgentActivityLogger"
```

---

## Task 7: Implement chunk buffering + turn lifecycle (Sequential mode)

**Files:**
- Modify: `src/Services/AgentActivityLogger.cs`
- Modify: `tests/MagenticWorkflowApp.Tests/Services/AgentActivityLoggerTests.cs`

- [ ] **Step 1: Write second failing test — `OnTurnCompleted` logs once with full text**

Append to `AgentActivityLoggerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Create `RecordingLogger<T>` test double**

Write `tests/MagenticWorkflowApp.Tests/TestDoubles/RecordingLogger.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Tests.TestDoubles;

public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<RecordedLogEntry> entries = new();
    private readonly object syncRoot = new();

    public IReadOnlyList<RecordedLogEntry> Entries
    {
        get { lock (syncRoot) { return entries.ToList(); } }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var formatted = formatter(state, exception);
        var message = state?.ToString() ?? string.Empty;
        lock (syncRoot)
        {
            entries.Add(new RecordedLogEntry(logLevel, message, formatted, exception));
        }
    }
}

public sealed record RecordedLogEntry(
    LogLevel Level,
    string Message,
    string FormattedMessage,
    Exception? Exception);
```

- [ ] **Step 3: Implement Sequential-mode chunk + turn flow in `AgentActivityLogger.cs`**

Replace `src/Services/AgentActivityLogger.cs` entirely:

```csharp
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
```

- [ ] **Step 4: Run tests, verify all three pass**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/ --filter FullyQualifiedName~AgentActivityLoggerTests`
Expected: 3 pass (`OnChunk_AccumulatesIntoBuffer_AndWritesToConsole`, `OnTurnCompleted_LogsOnceWithFullText`, `OnTurnCompleted_WithExplicitText_PrefersExplicit`).

- [ ] **Step 5: Commit (green phase)**

```bash
git add src/Services/AgentActivityLogger.cs tests/MagenticWorkflowApp.Tests/
git commit -m "feat: implement chunk buffering and turn lifecycle in AgentActivityLogger"
```

---

## Task 8: Implement OnToolCall + OnManagerDecision

**Files:**
- Modify: `src/Services/AgentActivityLogger.cs`
- Modify: `tests/MagenticWorkflowApp.Tests/Services/AgentActivityLoggerTests.cs`

- [ ] **Step 1: Write failing tests**

Append to `AgentActivityLoggerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/ --filter FullyQualifiedName~OnToolCall_LogsAndWritesToConsole`
Expected: FAIL — current method body is empty stub.

- [ ] **Step 3: Replace `OnToolCall` and `OnManagerDecision` bodies in `AgentActivityLogger.cs`**

Replace the two stubs:

```csharp
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
```

- [ ] **Step 4: Run tests, verify pass**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/ --filter FullyQualifiedName~AgentActivityLoggerTests`
Expected: 5 pass.

- [ ] **Step 5: Commit**

```bash
git add src/Services/AgentActivityLogger.cs tests/MagenticWorkflowApp.Tests/Services/AgentActivityLoggerTests.cs
git commit -m "feat: implement OnToolCall and OnManagerDecision"
```

---

## Task 9: Implement error paths + FlushAllPendingTurns + WorkflowOutput

**Files:**
- Modify: `src/Services/AgentActivityLogger.cs`
- Modify: `tests/MagenticWorkflowApp.Tests/Services/AgentActivityLoggerTests.cs`

- [ ] **Step 1: Write failing tests**

Append to `AgentActivityLoggerTests.cs`:

```csharp
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
        e.Level == Microsoft.Extensions.Logging.LogLevel.Error &&
        e.FormattedMessage.Contains("Alice"));
    Assert.Contains(recorder.Entries, e =>
        e.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
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
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/ --filter FullyQualifiedName~OnExecutorFailed_LogsError`
Expected: FAIL — stubs empty.

- [ ] **Step 3: Replace stubs in `AgentActivityLogger.cs`**

Replace the four stubs (`OnExecutorFailed`, `OnWorkflowError`, `OnWorkflowOutput`, `FlushAllPendingTurns`):

```csharp
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
                logger.LogWarning(
                    "Pending turn for {Agent} flushed: reason={Reason}, chunks={Chunks}",
                    key, reason, state.ChunkCount);
            }
        }
    });
}
```

- [ ] **Step 4: Run tests, verify pass**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/`
Expected: 8 pass.

- [ ] **Step 5: Commit**

```bash
git add src/Services/AgentActivityLogger.cs tests/MagenticWorkflowApp.Tests/Services/AgentActivityLoggerTests.cs
git commit -m "feat: implement error paths and FlushAllPendingTurns"
```

---

## Task 10: Add ActivitySource (OTEL traces)

**Files:**
- Modify: `src/Services/AgentActivityLogger.cs`
- Modify: `tests/MagenticWorkflowApp.Tests/Services/AgentActivityLoggerTests.cs`

- [ ] **Step 1: Write failing test that listens to `ActivitySource`**

Append to `AgentActivityLoggerTests.cs`:

```csharp
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
    Assert.Contains(span.Tags, t => t.Key == "chunks" && t.Value == "2");
    Assert.Contains(span.Tags, t => t.Key == "text.length" && t.Value == "6");
}
```

- [ ] **Step 2: Run test, verify it fails**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/ --filter FullyQualifiedName~OnTurnCompleted_StartsAndStopsActivity`
Expected: FAIL — no activity created yet.

- [ ] **Step 3: Add `ActivitySource` and wire spans in `AgentActivityLogger.cs`**

In `AgentActivityLogger.cs`:

3a. Add `using System.Diagnostics;` at top.

3b. Add static field after class opening:

```csharp
private static readonly ActivitySource ActivitySource = new("MagenticWorkflowApp.Agents");
```

3c. Add `Activity?` property to `AgentTurnState`:

```csharp
public Activity? Activity { get; set; }
```

3d. In `OnChunk` (and `OnTurnStarted`), modify the `GetOrAdd` to start activity:

```csharp
var state = turns.GetOrAdd(agent, _ =>
{
    var s = new AgentTurnState(DateTime.UtcNow)
    {
        Activity = ActivitySource.StartActivity($"agent.turn.{agent}"),
    };
    logger.LogInformation("Agent {Agent} turn started", agent);
    if (mode == WorkflowDisplayMode.Sequential)
        console.WriteLineWithColor($"\n┌── {agent} ──", ConsoleColor.Cyan);
    else
        console.WriteLineWithColor($"\n[{agent}] (started)", ConsoleColor.Cyan);
    return s;
});
```

3e. In `OnTurnCompleted`, set tags and dispose activity before returning:

After the `text`/`chunks`/`durationMs` are computed, before the `LogInformation`, add:

```csharp
state?.Activity?.SetTag("chunks", chunks);
state?.Activity?.SetTag("text.length", text.Length);
state?.Activity?.SetTag("durationMs", (long)durationMs);
state?.Activity?.Dispose();
```

3f. In `FlushAllPendingTurns`, dispose activity with aborted tag:

```csharp
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
```

- [ ] **Step 4: Run tests, verify pass**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/`
Expected: 9 pass.

- [ ] **Step 5: Commit**

```bash
git add src/Services/AgentActivityLogger.cs tests/MagenticWorkflowApp.Tests/Services/AgentActivityLoggerTests.cs
git commit -m "feat: emit OpenTelemetry spans per agent turn"
```

---

## Task 11: Add Meter (OTEL metrics)

**Files:**
- Modify: `src/Services/AgentActivityLogger.cs`
- Modify: `tests/MagenticWorkflowApp.Tests/Services/AgentActivityLoggerTests.cs`

- [ ] **Step 1: Write failing metrics test**

Append to `AgentActivityLoggerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test, verify it fails**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/ --filter FullyQualifiedName~OnTurnCompleted_RecordsMetrics`
Expected: FAIL.

- [ ] **Step 3: Add `Meter` to `AgentActivityLogger.cs`**

3a. Add `using System.Diagnostics.Metrics;` at top.

3b. Add static fields beside `ActivitySource`:

```csharp
private static readonly Meter Meter = new("MagenticWorkflowApp.Agents");
private static readonly Counter<long> TurnsCompleted = Meter.CreateCounter<long>("agent.turns.completed");
private static readonly Histogram<double> TurnDurationMs = Meter.CreateHistogram<double>("agent.turn.duration.ms");
```

3c. In `OnTurnCompleted`, after the activity disposal block, before `LogInformation`, add:

```csharp
TurnsCompleted.Add(1, new KeyValuePair<string, object?>("agent", agent));
TurnDurationMs.Record(durationMs, new KeyValuePair<string, object?>("agent", agent));
```

- [ ] **Step 4: Run tests, verify pass**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/`
Expected: 10 pass.

- [ ] **Step 5: Commit**

```bash
git add src/Services/AgentActivityLogger.cs tests/MagenticWorkflowApp.Tests/Services/AgentActivityLoggerTests.cs
git commit -m "feat: emit OpenTelemetry metrics for agent turns"
```

---

## Task 12: Concurrent-mode test (two agents, independent buffers)

**Files:**
- Modify: `tests/MagenticWorkflowApp.Tests/Services/AgentActivityLoggerTests.cs`

- [ ] **Step 1: Write the test**

Append to `AgentActivityLoggerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test, verify pass (no implementation change needed — Concurrent mode already implemented in Task 7)**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/`
Expected: 11 pass.

- [ ] **Step 3: Commit**

```bash
git add tests/MagenticWorkflowApp.Tests/Services/AgentActivityLoggerTests.cs
git commit -m "test: verify concurrent two-agent independent buffers and prefixed console"
```

---

## Task 13: Add NuGet packages — Serilog + OpenTelemetry

**Files:**
- Modify: `src/AiAgetnsWorkflow.csproj` (or actual src csproj)

- [ ] **Step 1: Locate src csproj**

Run: `ls src/*.csproj`
Note the file name. The plan assumes `src/AiAgetnsWorkflow.csproj`. Use whatever exists.

- [ ] **Step 2: Add package references**

Edit src csproj `<ItemGroup>` block (the one containing existing PackageReferences). Append:

```xml
<PackageReference Include="Serilog.Extensions.Hosting" Version="8.0.0" />
<PackageReference Include="Serilog.Settings.Configuration" Version="8.0.4" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />
<PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.10.0" />
<PackageReference Include="OpenTelemetry.Exporter.Console" Version="1.10.0" />
```

If any of these versions yield compatibility errors against .NET 10 preview, run `dotnet list package` and bump to nearest matching available version.

- [ ] **Step 3: Restore + build**

Run:
```bash
dotnet restore src/
dotnet build src/
```
Expected: build succeeds, no version conflicts.

- [ ] **Step 4: Commit**

```bash
git add src/*.csproj
git commit -m "build: add Serilog and OpenTelemetry NuGet packages"
```

---

## Task 14: Configure Serilog + OpenTelemetry in appsettings.json

**Files:**
- Modify: `src/appsettings.json`

- [ ] **Step 1: Add Serilog and OpenTelemetry sections**

Edit `src/appsettings.json` — replace whole content with:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "System": "Warning"
    }
  },

  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/agents-.log",
          "rollingInterval": "Day",
          "outputTemplate": "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}"
        }
      }
    ],
    "Enrich": [ "FromLogContext" ]
  },

  "OpenTelemetry": {
    "ServiceName": "MagenticWorkflowApp"
  },

  "Ollama": {
    "Endpoint": "http://localhost:11434",
    "DefaultModelId": "bjoernb/gemma4-e2b-fast"
  },

  "WorkflowSettings": {
    "DefaultConfigPath": "workflow-sum.json",
    "EnableDetailedLogging": true,
    "SaveVisualizationToFile": false
  }
}
```

- [ ] **Step 2: Build to ensure JSON valid**

Run: `dotnet build src/`
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/appsettings.json
git commit -m "config: add Serilog and OpenTelemetry settings"
```

---

## Task 15: Wire DI in Program.cs (Serilog, OTEL, IAgentActivityLogger, IConsoleWriter)

**Files:**
- Modify: `src/Program.cs`

- [ ] **Step 1: Replace Program.cs contents**

Write `src/Program.cs`:

```csharp
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace MagenticWorkflowApp;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== Microsoft Agent Framework - Magentic Workflow ===\n");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services, configuration);
            await using var serviceProvider = services.BuildServiceProvider();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            try
            {
                var orchestrator = serviceProvider.GetRequiredService<IWorkflowOrchestrator>();
                var path = args.Length > 0 ? args[0] : "workflow-sum.json";
                Console.WriteLine($"Loading workflow configuration from: {path}\n");
                await orchestrator.ExecuteWorkflowFromJsonAsync(path, cts.Token);
                Console.WriteLine("\n=== Workflow Execution Completed ===");
                return 0;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                Console.WriteLine("\nCanceled by user.");
                return 130;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n!!! Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
                return 1;
            }
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(b =>
        {
            b.ClearProviders();
            b.AddSerilog(Log.Logger, dispose: true);
        });

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(
                configuration["OpenTelemetry:ServiceName"] ?? "MagenticWorkflowApp"))
            .WithTracing(t => t
                .AddSource("MagenticWorkflowApp.Agents")
                .AddConsoleExporter())
            .WithMetrics(m => m
                .AddMeter("MagenticWorkflowApp.Agents")
                .AddConsoleExporter());

        services.AddSingleton(configuration);
        services.AddSingleton<IConsoleWriter, DefaultConsoleWriter>();
        services.AddSingleton<IAgentActivityLogger, AgentActivityLogger>();
        services.AddSingleton<IWorkflowOrchestrator, MagenticWorkflowOrchestrator>();
        services.AddSingleton<IWorkflowJsonLoader, WorkflowJsonLoader>();
        services.AddSingleton<IWorkflowVisualizer, WorkflowVisualizer>();

        services.AddSingleton<IMcpClientPool, McpClientPool>();
        services.AddSingleton<IHostedToolFactory, HostedToolFactory>();
        services.AddSingleton<IAgentPluginRegistry, AgentPluginRegistry>();

        services.AddSingleton<IAgentPlugin, Plugins.WeatherPlugin>();
        services.AddSingleton<IAgentPlugin, Plugins.TimePlugin>();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/`
Expected: succeeds. If `MagenticWorkflowOrchestrator` ctor signature mismatch (next task adds `IAgentActivityLogger` param), we expect an error here — proceed to Task 16.

- [ ] **Step 3: Commit (intermediate, expected red — will go green after Task 16)**

If the build fails only because of orchestrator ctor signature, do not commit yet — proceed to Task 16 and commit them together. If build succeeds, commit:

```bash
git add src/Program.cs
git commit -m "feat: wire Serilog and OpenTelemetry in DI container"
```

---

## Task 16: Inject IAgentActivityLogger into MagenticWorkflowOrchestrator

**Files:**
- Modify: `src/Services/MagenticWorkflowOrchestrator.cs`

- [ ] **Step 1: Add field, ctor parameter, and assignment**

In `src/Services/MagenticWorkflowOrchestrator.cs`:

1a. Add new field after `private readonly IAgentPluginRegistry pluginRegistry;`:

```csharp
private readonly IAgentActivityLogger activity;
```

1b. Modify constructor — add `IAgentActivityLogger activity` parameter at the end and assign:

```csharp
public MagenticWorkflowOrchestrator(
    ILogger<MagenticWorkflowOrchestrator> logger,
    ILoggerFactory loggerFactory,
    IWorkflowJsonLoader jsonLoader,
    IWorkflowVisualizer visualizer,
    IConfiguration configuration,
    IMcpClientPool mcpPool,
    IHostedToolFactory hostedFactory,
    IAgentPluginRegistry pluginRegistry,
    IAgentActivityLogger activity)
{
    this.logger = logger;
    this.loggerFactory = loggerFactory;
    this.jsonLoader = jsonLoader;
    this.visualizer = visualizer;
    this.configuration = configuration;
    this.mcpPool = mcpPool;
    this.hostedFactory = hostedFactory;
    this.pluginRegistry = pluginRegistry;
    this.activity = activity;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/`
Expected: succeeds.

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/`
Expected: 11 pass (no regression).

- [ ] **Step 4: Commit**

```bash
git add src/Services/MagenticWorkflowOrchestrator.cs src/Program.cs
git commit -m "feat: inject IAgentActivityLogger into MagenticWorkflowOrchestrator"
```

---

## Task 17: Replace HandleWorkflowEvent body with IAgentActivityLogger calls

**Files:**
- Modify: `src/Services/MagenticWorkflowOrchestrator.cs`

- [ ] **Step 1: Replace `HandleWorkflowEvent` method body**

In `src/Services/MagenticWorkflowOrchestrator.cs`, replace the entire `HandleWorkflowEvent` method:

```csharp
private Exception? HandleWorkflowEvent(WorkflowEvent evt)
{
    switch (evt)
    {
        case AgentResponseUpdateEvent a:
            activity.OnChunk(
                a.Update?.AuthorName ?? a.ExecutorId ?? "?",
                a.Update?.Text ?? string.Empty);
            return null;

        case AgentResponseEvent r:
            var agentName = r.ExecutorId ?? "?";
            activity.OnTurnCompleted(agentName, r.Response?.Text);
            if (r.Response?.Messages is { } msgs)
            {
                foreach (var m in msgs)
                {
                    foreach (var c in m.Contents.OfType<Microsoft.Extensions.AI.FunctionCallContent>())
                    {
                        activity.OnToolCall(
                            agentName,
                            c.Name,
                            c.Arguments is null ? null : System.Text.Json.JsonSerializer.Serialize(c.Arguments));
                    }
                }
            }
            return null;

        case ExecutorFailedEvent ef:
            var execEx = ef.Data as Exception
                ?? new InvalidOperationException($"Executor '{ef.ExecutorId}' failed");
            activity.OnExecutorFailed(ef.ExecutorId ?? "?", execEx);
            return execEx;

        case WorkflowErrorEvent e:
            var workflowEx = e.Exception ?? new InvalidOperationException("Unknown workflow error");
            activity.OnWorkflowError(workflowEx);
            return workflowEx;

        case WorkflowOutputEvent o:
            activity.OnWorkflowOutput(o.Data?.ToString() ?? "(no result)");
            return null;

        default:
            logger.LogDebug("Unhandled workflow event: {Type}", evt.GetType().Name);
            return null;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/`
Expected: succeeds. If `FunctionCallContent` cannot be found, verify `using Microsoft.Extensions.AI;` is at top of file (already present per spec) — adjust as needed.

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/`
Expected: 11 pass.

- [ ] **Step 4: Commit**

```bash
git add src/Services/MagenticWorkflowOrchestrator.cs
git commit -m "refactor: route workflow events through IAgentActivityLogger"
```

---

## Task 18: SetWorkflowMode before each Execute*WorkflowAsync

**Files:**
- Modify: `src/Services/MagenticWorkflowOrchestrator.cs`

- [ ] **Step 1: Add SetWorkflowMode calls**

In `src/Services/MagenticWorkflowOrchestrator.cs`:

1a. At the start of `ExecuteSequentialWorkflowAsync` (right after the method opening brace), add:

```csharp
activity.SetWorkflowMode(WorkflowDisplayMode.Sequential);
```

1b. At the start of `ExecuteConcurrentWorkflowAsync`, add:

```csharp
activity.SetWorkflowMode(WorkflowDisplayMode.Concurrent);
```

1c. At the start of `ExecuteConditionalWorkflowAsync`, add:

```csharp
activity.SetWorkflowMode(WorkflowDisplayMode.Sequential);
```

1d. At the start of `ExecuteMagenticWorkflowAsync`, add:

```csharp
activity.SetWorkflowMode(WorkflowDisplayMode.Sequential);
```

1e. Add `using MagenticWorkflowApp.Models;` at the top of the file if not already present.

- [ ] **Step 2: Build**

Run: `dotnet build src/`
Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Services/MagenticWorkflowOrchestrator.cs
git commit -m "feat: set IAgentActivityLogger workflow mode per execution path"
```

---

## Task 19: Replace Magentic ResponseCallback with IAgentActivityLogger

**Files:**
- Modify: `src/Services/MagenticWorkflowOrchestrator.cs`

- [ ] **Step 1: Replace ResponseCallback in `ExecuteMagenticWorkflowAsync`**

Find the `ResponseCallback = response =>` lambda and replace with:

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

- [ ] **Step 2: Replace `ShowFinalResult(output ?? "(no output)")` call**

Replace `ShowFinalResult(output ?? "(no output)");` with:

```csharp
activity.OnWorkflowOutput(output ?? "(no output)");
```

- [ ] **Step 3: Build**

Run: `dotnet build src/`
Expected: succeeds.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/`
Expected: 11 pass.

- [ ] **Step 5: Commit**

```bash
git add src/Services/MagenticWorkflowOrchestrator.cs
git commit -m "refactor: route Magentic ResponseCallback through IAgentActivityLogger"
```

---

## Task 20: Refactor Simulate methods to use IAgentActivityLogger

**Files:**
- Modify: `src/Services/MagenticWorkflowOrchestrator.cs`

- [ ] **Step 1: Replace `SimulateSequentialWorkflowAsync` body**

Replace existing method body. Inside, replace each `LogEvent($"AGENT:{agent.Name}", ...)` with `IAgentActivityLogger` calls. Keep `LogEvent("WORKFLOW", ...)` for orchestration messages (these are not agent activity).

```csharp
private async Task SimulateSequentialWorkflowAsync(WorkflowConfiguration config)
{
    activity.SetWorkflowMode(WorkflowDisplayMode.Sequential);

    LogEvent("WORKFLOW", $"Starting Sequential execution with {config.Agents.Count} agents", ConsoleColor.Cyan);
    if (config.Orchestration?.StartAgent != null)
        LogEvent("WORKFLOW", $"Start agent: {config.Orchestration.StartAgent}", ConsoleColor.Cyan);

    await Task.Delay(300);

    var processedAgents = new HashSet<string>();
    var currentAgent = config.Orchestration?.StartAgent ?? config.Agents.First().Name;

    while (currentAgent != null && !processedAgents.Contains(currentAgent))
    {
        var agent = config.Agents.FirstOrDefault(a => a.Name == currentAgent);
        if (agent == null) break;
        processedAgents.Add(currentAgent);

        await Task.Delay(400);
        activity.OnChunk(agent.Name, $"Processing using {agent.ModelId}...");
        await Task.Delay(600);
        activity.OnTurnCompleted(agent.Name, $"✓ Completed task: {agent.Description}");

        var edge = config.Orchestration?.Edges.FirstOrDefault(e => e.From == currentAgent);
        currentAgent = edge?.To;
        if (currentAgent != null)
        {
            await Task.Delay(200);
            LogEvent("WORKFLOW", $"→ Passing result to {currentAgent}", ConsoleColor.Cyan);
        }
    }

    await Task.Delay(300);
    activity.OnWorkflowOutput("Sequential pipeline completed successfully!");
}
```

- [ ] **Step 2: Replace `SimulateConcurrentWorkflowAsync` body**

```csharp
private async Task SimulateConcurrentWorkflowAsync(WorkflowConfiguration config)
{
    activity.SetWorkflowMode(WorkflowDisplayMode.Concurrent);

    LogEvent("WORKFLOW", $"Starting Concurrent execution with {config.Agents.Count} agents", ConsoleColor.Cyan);

    var participants = config.Orchestration?.Concurrent?.ParticipantAgents
        ?? config.Agents.Select(a => a.Name).ToList();
    LogEvent("WORKFLOW", $"Participants: {string.Join(", ", participants)}", ConsoleColor.Cyan);
    await Task.Delay(300);

    LogEvent("WORKFLOW", "⚡ Fan-out: Distributing task to all agents simultaneously", ConsoleColor.Magenta);
    await Task.Delay(400);

    var tasks = new List<Task>();
    foreach (var agentName in participants)
    {
        var agent = config.Agents.FirstOrDefault(a => a.Name == agentName);
        if (agent != null) tasks.Add(SimulateAgentWorkAsync(agent));
    }
    await Task.WhenAll(tasks);

    await Task.Delay(300);
    var strategy = config.Orchestration?.Concurrent?.AggregationStrategy ?? "Collect";
    LogEvent("WORKFLOW", $"⚡ Fan-in: Aggregating results using '{strategy}' strategy", ConsoleColor.Magenta);
    await Task.Delay(400);

    activity.OnWorkflowOutput($"Concurrent execution completed! All {participants.Count} agents finished.");
}
```

- [ ] **Step 3: Replace `SimulateConditionalWorkflowAsync` body**

```csharp
private async Task SimulateConditionalWorkflowAsync(WorkflowConfiguration config)
{
    activity.SetWorkflowMode(WorkflowDisplayMode.Sequential);

    LogEvent("WORKFLOW", "Starting Conditional execution with dynamic routing", ConsoleColor.Cyan);
    if (config.Orchestration?.StartAgent != null)
        LogEvent("WORKFLOW", $"Start agent: {config.Orchestration.StartAgent}", ConsoleColor.Cyan);

    await Task.Delay(300);

    var processedAgents = new HashSet<string>();
    var currentAgent = config.Orchestration?.StartAgent ?? config.Agents.First().Name;

    while (currentAgent != null && !processedAgents.Contains(currentAgent))
    {
        var agent = config.Agents.FirstOrDefault(a => a.Name == currentAgent);
        if (agent == null) break;
        processedAgents.Add(currentAgent);

        await Task.Delay(400);
        activity.OnChunk(agent.Name, $"Processing using {agent.ModelId}...");
        await Task.Delay(600);
        activity.OnTurnCompleted(agent.Name, $"✓ Completed: {agent.Description}");

        var conditionalEdge = config.Orchestration?.ConditionalEdges
            .FirstOrDefault(ce => ce.From == currentAgent);
        if (conditionalEdge != null)
        {
            await Task.Delay(300);
            activity.OnManagerDecision("DECISION", $"Evaluating condition: {conditionalEdge.SelectionFunction}");
            var selectedTargets = conditionalEdge.ToOptions.Take(1).ToList();
            await Task.Delay(200);
            activity.OnManagerDecision("DECISION", $"✓ Selected target(s): {string.Join(", ", selectedTargets)}");
            currentAgent = selectedTargets.FirstOrDefault();
        }
        else
        {
            var edge = config.Orchestration?.Edges.FirstOrDefault(e => e.From == currentAgent);
            currentAgent = edge?.To;
            if (currentAgent != null)
            {
                await Task.Delay(200);
                LogEvent("WORKFLOW", $"→ Moving to {currentAgent}", ConsoleColor.Cyan);
            }
        }
    }

    await Task.Delay(300);
    activity.OnWorkflowOutput("Conditional workflow completed with dynamic routing!");
}
```

- [ ] **Step 4: Replace `SimulateMagenticWorkflowAsync` body**

```csharp
private async Task SimulateMagenticWorkflowAsync(WorkflowConfiguration config)
{
    activity.SetWorkflowMode(WorkflowDisplayMode.Sequential);

    await Task.Delay(500);
    LogEvent("ORCHESTRATOR", "Initializing Magentic Manager...", ConsoleColor.Cyan);
    await Task.Delay(300);
    LogEvent("ORCHESTRATOR",
        $"Creating execution plan for task: {config.Task.Substring(0, Math.Min(80, config.Task.Length))}...",
        ConsoleColor.Cyan);

    for (int round = 1; round <= 3; round++)
    {
        Console.WriteLine($"\n--- Round {round} ---");
        foreach (var agent in config.Agents)
        {
            await Task.Delay(400);
            activity.OnChunk(agent.Name, $"Executing task using {agent.ModelId}...");
            await Task.Delay(600);
            activity.OnTurnCompleted(agent.Name, $"[{agent.Description}] Completed subtask.");
        }
        await Task.Delay(300);
        LogEvent("ORCHESTRATOR", $"Reviewing progress from round {round}...", ConsoleColor.Cyan);
    }

    await Task.Delay(500);
    activity.OnWorkflowOutput($"Magentic orchestration completed! All {config.Agents.Count} agents collaborated successfully.");
}
```

- [ ] **Step 5: Replace `SimulateAgentWorkAsync`**

```csharp
private async Task SimulateAgentWorkAsync(AgentConfiguration agent)
{
    await Task.Delay(500);
    activity.OnChunk(agent.Name, $"[Concurrent] Processing using {agent.ModelId}...");
    await Task.Delay(Random.Shared.Next(800, 1500));
    activity.OnTurnCompleted(agent.Name, $"[Concurrent] ✓ Completed: {agent.Description}");
}
```

- [ ] **Step 6: Build**

Run: `dotnet build src/`
Expected: succeeds.

- [ ] **Step 7: Run tests**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/`
Expected: 11 pass.

- [ ] **Step 8: Commit**

```bash
git add src/Services/MagenticWorkflowOrchestrator.cs
git commit -m "refactor: route simulate-mode agent activity through IAgentActivityLogger"
```

---

## Task 21: End-to-end smoke run (verify logs/agents-*.log gets written)

**Files:** none (manual verification step)

- [ ] **Step 1: Build**

Run: `dotnet build src/`
Expected: succeeds.

- [ ] **Step 2: Delete previous logs (if any) and run app in demo mode**

```bash
rm -rf src/bin/Debug/net10.0/logs
dotnet run --project src/ workflow-sequential.json
```

Expected behavior:
- Console shows live agent activity with `┌── Agent ──` framing.
- A directory `src/bin/Debug/net10.0/logs/` is created with at least one file matching `agents-YYYYMMDD.log`.

- [ ] **Step 3: Inspect log file**

```bash
ls src/bin/Debug/net10.0/logs/
cat src/bin/Debug/net10.0/logs/agents-*.log | head -40
```

Expected: lines like `[INF] Agent X completed turn: chunks=N, durationMs=..., text=...`.

- [ ] **Step 4: Run tests one more time**

Run: `dotnet test tests/MagenticWorkflowApp.Tests/`
Expected: all 11 pass.

- [ ] **Step 5: Commit only if logs/ pattern needs gitignore**

If `logs/` ended up tracked, add to `.gitignore`:

```bash
echo "logs/" >> .gitignore
git add .gitignore
git commit -m "chore: ignore logs directory"
```

---

## Acceptance Criteria (recap from spec)

1. ✅ Per-turn `Information` log entries in `logs/agents-{date}.log` for every agent in any workflow type.
2. ✅ Console live-stream of tokens (Sequential — clean; Concurrent — `[agent]` prefix).
3. ✅ OpenTelemetry Console exporter prints `agent.turn.{name}` spans with `chunks`, `text.length`, `durationMs` tags.
4. ✅ `agent.turns.completed` counter increments per turn.
5. ✅ Magentic `ResponseCallback` uses `IAgentActivityLogger`.
6. ✅ Tool calls logged separately via `OnToolCall`.
7. ✅ Aborted turns flushed with warning on `OnExecutorFailed` / `OnWorkflowError`.
8. ✅ All xUnit tests pass.
9. ✅ `dotnet build src/` clean.

---

## Self-Review Notes

- **Spec coverage:** All 9 acceptance criteria mapped to tasks. Manager decisions (spec §"Magentic manager decisions") covered by Task 8 + Magentic ResponseCallback in Task 19. Tool calls (spec §"OnToolCall") covered by Task 8 + Task 17. ActivitySource/Meter (spec §"OTEL") covered by Tasks 10–11. Serilog config (spec §"appsettings.json") covered by Task 14. DI wiring (spec §"Program.ConfigureServices") covered by Task 15. ✅
- **Type consistency:** `IAgentActivityLogger` method signatures appear identically in Tasks 5, 7, 8, 9, 10, 11. `AgentTurnState` introduced in Task 7 with `Activity` added in Task 10 — consistent. `RecordingLogger<T>` and `RecordingConsoleWriter` defined and reused. ✅
- **Placeholder scan:** No "TBD"/"TODO"/"implement later" remain. All code blocks complete. ✅
- **Order:** Tests precede impl (TDD red→green) for Tasks 6→7, 8, 9, 10, 11. Tasks 13→16 are configuration-then-code (mechanical). Task 21 is integration smoke. ✅
