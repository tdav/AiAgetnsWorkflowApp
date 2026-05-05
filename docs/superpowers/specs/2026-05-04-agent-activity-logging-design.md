# Agent Activity Logging — Design Spec

**Дата:** 2026-05-04
**Статус:** Утверждено к реализации
**Автор:** brainstorming session

## Цель

Обеспечить runtime-видимость работы агентов в `MagenticWorkflowApp`: какой агент сейчас активен, что говорит, какой tool вызывает, какие решения принимает менеджер. Лог должен попадать в три приёмника: `Console` (live), Serilog file (структурированно), OpenTelemetry (трейсинг + метрики).

## Контекст и проблема

Текущий `MagenticWorkflowOrchestrator`:

- `HandleWorkflowEvent` пишет события агентов через `Console.Write/WriteLine` в обёртке `LogEvent(source, message, color)`.
- `MagenticOrchestration.ResponseCallback` — то же самое, дублирует логику.
- `ILogger` почти не используется для активности агентов (только для warning-ов и operational-сообщений).
- Streaming-токены (`AgentResponseUpdateEvent`) выводятся в Console, но не персистируются в логи.
- Нет файлового логгера. Нет OpenTelemetry. Нет метрик по агентам.

При выполнении нескольких агентов параллельно (concurrent workflow) Console-вывод перемешивается без префиксов. После завершения ничего не остаётся для разбора.

## Решения по итогам брейншторма

| № | Решение |
|---|---------|
| 1 | Стриминг — гибрид: Console показывает токены live, `ILogger` пишет одну агрегированную запись на ход агента (`AgentResponseEvent`). |
| 2 | Приёмники: Console (стандартный), Serilog файл (`logs/agents-{date}.log` с rolling), OpenTelemetry (`ActivitySource` + `Meter`, console exporter сейчас, OTLP — позже). |
| 3 | Подход — выделенный сервис `IAgentActivityLogger` (не inline в orchestrator, не pipeline-handlers). |
| 4 | DI scope — `Singleton`, состояние буферов живёт через workflow run. |

## Архитектура

### Новые файлы

```
src/
  Interfaces/
    IAgentActivityLogger.cs
    IConsoleWriter.cs
  Models/
    AgentActivityKind.cs           — enum для категоризации
    WorkflowDisplayMode.cs         — enum: Sequential | Concurrent
  Services/
    AgentActivityLogger.cs         — основная реализация
    DefaultConsoleWriter.cs        — обёртка над System.Console
```

### Изменённые файлы

```
src/
  Services/MagenticWorkflowOrchestrator.cs   — все agent-events идут через IAgentActivityLogger
  Program.cs                                  — DI: Serilog, OpenTelemetry, IAgentActivityLogger, IConsoleWriter
  appsettings.json                            — секции "Serilog", "OpenTelemetry"
  AiAgetnsWorkflow.csproj                     — пакеты Serilog, OpenTelemetry
tests/MagenticWorkflowApp.Tests/              — новый xUnit проект
```

## Контракт `IAgentActivityLogger`

```csharp
public interface IAgentActivityLogger
{
    void SetWorkflowMode(WorkflowDisplayMode mode);

    void OnTurnStarted(string agent, string? executorId = null);
    void OnChunk(string agent, string text);
    void OnTurnCompleted(string agent, string? fullText = null);

    void OnToolCall(string agent, string toolName, string? args = null);
    void OnManagerDecision(string managerName, string decision);

    void OnExecutorFailed(string executorId, Exception ex);
    void OnWorkflowError(Exception ex);
    void OnWorkflowOutput(string output);

    void FlushAllPendingTurns(string reason);
}
```

`OnTurnStarted` обычно зовётся неявно из `OnChunk` (lazy: первый chunk → создание буфера → log).

## Внутреннее состояние

```csharp
private sealed class AgentTurnState
{
    public StringBuilder Buffer { get; } = new();
    public DateTime StartedUtc { get; init; }
    public int ChunkCount { get; set; }
    public Activity? Activity { get; set; }
    public object Lock { get; } = new();
}

private readonly ConcurrentDictionary<string, AgentTurnState> turns = new();
private WorkflowDisplayMode mode = WorkflowDisplayMode.Sequential;
private static readonly ActivitySource activitySource = new("MagenticWorkflowApp.Agents");
private static readonly Meter meter = new("MagenticWorkflowApp.Agents");
private readonly Counter<long> turnsCompleted;
private readonly Histogram<double> turnDurationMs;
```

## Поведение

### `OnChunk(agent, text)`

1. `turns.GetOrAdd(agent, ...)` — если новый ход, неявно зовётся `OnTurnStarted`:
   - `ILogger.LogInformation("Agent {Agent} turn started", agent)`
   - Console: `\n┌── {agent} ──\n` (Sequential) или `\n[{agent}] (started)\n` (Concurrent)
   - `activity = activitySource.StartActivity($"agent.turn.{agent}")`
2. `lock(state.Lock)`:
   - `state.Buffer.Append(text)`
   - `state.ChunkCount++`
3. **Console:**
   - Sequential mode: `consoleWriter.Write(text)` — без префикса, "печатание"
   - Concurrent mode: `consoleWriter.Write($"[{agent}] {text}")` (либо буферизуем до `\n`, дописываем префикс на новой строке)
4. **ILogger:** не пишем (chunk-уровень шумный).

### `OnTurnCompleted(agent, fullText?)`

1. `turns.TryRemove(agent, out var state)` — забрать накопленное.
2. Текст итогового сообщения:
   - Если `fullText != null` (Magentic путь без chunks) — используем его.
   - Иначе — `state?.Buffer.ToString()`.
3. **Console:** `\n└── end {agent} ──\n` (Sequential) или `\n[{agent}] (completed)\n` (Concurrent).
4. **ILogger:**
   ```csharp
   logger.LogInformation(
     "Agent {Agent} completed turn: chunks={Chunks}, durationMs={Duration}, text={Text}",
     agent, state?.ChunkCount ?? 0, elapsedMs, text);
   ```
5. **OTEL:**
   - `state?.Activity?.SetTag("chunks", state.ChunkCount)`
   - `state?.Activity?.SetTag("text.length", text.Length)`
   - `state?.Activity?.Dispose()`
   - `turnsCompleted.Add(1, new("agent", agent))`
   - `turnDurationMs.Record(elapsedMs, new("agent", agent))`

### `OnToolCall(agent, toolName, args?)`

- Console: `[{agent}] → tool: {toolName}({args})` magenta.
- ILogger: `LogInformation("Agent {Agent} called tool {Tool} with args {Args}", agent, toolName, args)`.
- OTEL: `Activity.AddEvent(new ActivityEvent("tool.call", tags: { tool, args }))` на текущем span агента.

### `OnManagerDecision(managerName, decision)`

- Console: `[{managerName}] DECISION: {decision}` cyan.
- ILogger: `LogInformation("Manager {Manager} decision: {Decision}", managerName, decision)`.
- OTEL: отдельный span `manager.decision`.

### `OnExecutorFailed`, `OnWorkflowError`

- Console red.
- `ILogger.LogError(ex, "...")`.
- `FlushAllPendingTurns("aborted")` — закрыть незавершённые ходы.
- OTEL: `Activity.SetStatus(ActivityStatusCode.Error)`.

### `OnWorkflowOutput(output)`

- Делегируется в `ShowFinalResult` (визуальная рамка).
- ILogger: `LogInformation("Workflow output: {Output}", output)`.

### `FlushAllPendingTurns(reason)`

Для каждого `state` в `turns`:
- `LogWarning("Pending turn for {Agent} flushed: reason={Reason}, chunks={Chunks}", ...)`.
- `Activity?.SetTag("aborted", true).Dispose()`.
- Удалить из словаря.

## Маппинг событий SDK → методы интерфейса

| Источник | Метод |
|----------|-------|
| `AgentResponseUpdateEvent` | `OnChunk(authorName ?? executorId, text)` |
| `AgentResponseEvent` | `OnTurnCompleted(executorId, response.Text)` + извлечение `FunctionCallContent` → `OnToolCall` |
| `ExecutorFailedEvent` | `OnExecutorFailed(executorId, exception)` |
| `WorkflowErrorEvent` | `OnWorkflowError(exception)` |
| `WorkflowOutputEvent` | `OnWorkflowOutput(data.ToString())` |
| Magentic `ResponseCallback` | `authorName == manager` → `OnManagerDecision`, иначе `OnTurnCompleted(authorName, content)` |

## Концерн: thread-safety

- `ConcurrentDictionary<string, AgentTurnState>` — atomic add/remove ходов.
- `lock(state.Lock)` для `Buffer.Append` и `ChunkCount++` (StringBuilder не thread-safe).
- `Counter<long>.Add` и `Histogram<double>.Record` — thread-safe by design.
- `Console.Write` без lock в Concurrent mode → используется префикс `[agent]` + перевод строки между ходами для читаемости.

## Конфигурация

### `appsettings.json` — добавляется

```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": { "Microsoft": "Warning", "System": "Warning" }
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
  "ServiceName": "MagenticWorkflowApp",
  "ConsoleExporter": true
}
```

### `Program.ConfigureServices` — изменения

```csharp
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .CreateLogger();

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

services.AddSingleton<IConsoleWriter, DefaultConsoleWriter>();
services.AddSingleton<IAgentActivityLogger, AgentActivityLogger>();
```

### `Program.Main` — graceful shutdown

```csharp
finally { Log.CloseAndFlush(); }
```

### NuGet пакеты (csproj)

```xml
<PackageReference Include="Serilog.Extensions.Hosting" Version="8.*" />
<PackageReference Include="Serilog.Settings.Configuration" Version="8.*" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.*" />
<PackageReference Include="Serilog.Sinks.File" Version="6.*" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.10.*" />
<PackageReference Include="OpenTelemetry.Exporter.Console" Version="1.10.*" />
```

## Точки интеграции в Orchestrator

### Конструктор — добавить параметр

```csharp
private readonly IAgentActivityLogger activity;
// + ctor parameter
```

### `HandleWorkflowEvent` — упрощённая версия

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
                foreach (var m in msgs)
                    foreach (var c in m.Contents.OfType<FunctionCallContent>())
                        activity.OnToolCall(agentName, c.Name, c.Arguments?.ToString());
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

### Перед каждым `Execute*WorkflowAsync`

```csharp
activity.SetWorkflowMode(WorkflowDisplayMode.Sequential);   // или Concurrent
```

| Метод | Mode |
|-------|------|
| `ExecuteSequentialWorkflowAsync` | Sequential |
| `ExecuteConditionalWorkflowAsync` | Sequential |
| `ExecuteConcurrentWorkflowAsync` | Concurrent |
| `ExecuteMagenticWorkflowAsync` | Sequential |

### Magentic `ResponseCallback`

```csharp
ResponseCallback = response =>
{
    var name = response.AuthorName ?? "?";
    var text = response.Content ?? string.Empty;

    if (string.Equals(name, config.Manager.ModelId, StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Manager", StringComparison.OrdinalIgnoreCase))
        activity.OnManagerDecision(name, text);
    else
        activity.OnTurnCompleted(name, text);

    return ValueTask.CompletedTask;
}
```

### Симуляция (Demo mode)

`Simulate*WorkflowAsync` методы тоже используют `IAgentActivityLogger` для консистентного стиля вывода. Прямые `LogEvent("AGENT:...", ...)` для агентских событий удаляются. `LogEvent("WORKFLOW", ...)` для оркестрационных строк остаётся (это не agent activity).

## Обработка ошибок

| Сценарий | Поведение |
|----------|-----------|
| Исключение внутри метода логгера | `try/catch` → `LogWarning(ex, "Activity logger failure")`, дальше silent. Никогда не ломаем workflow. |
| Console.Write на закрытом stream | `IOException` → silent. |
| Serilog file lock | Serilog ретраит через `Sinks.File`. `Log.CloseAndFlush()` в `finally` Main. |
| OTEL без listener | `ActivitySource.StartActivity` возвращает `null`. Использование через `?.`. |
| Concurrent fault, незакрытый turn | `OnExecutorFailed` / `OnWorkflowError` → `FlushAllPendingTurns("aborted")`. |
| Pending buffers на shutdown | `Dispose` логгера → `LogWarning("Pending turn buffer dropped")`. |

## Тесты

Новый xUnit-проект `tests/MagenticWorkflowApp.Tests/`:

| Тест | Проверяет |
|------|-----------|
| `OnChunk_AccumulatesBuffer` | Несколько chunks → один `OnTurnCompleted` собирает весь текст. |
| `OnTurnCompleted_LogsOnceWithFullText` | Mock `ILogger` → 1 запись Information с `{Text}` равным конкатенации. |
| `OnTurnCompleted_WithExplicitText_PrefersExplicit` | Magentic путь: `fullText` приоритетнее буфера. |
| `Concurrent_TwoAgents_BuffersIndependent` | Параллельные `OnChunk` для A и B → 2 отдельные записи. |
| `OnExecutorFailed_FlushesPending` | Незакрытый turn → запись с `status=aborted`. |
| `OnTurnCompleted_StartsAndStopsActivity` | `ActivityListener` ловит span с tags. |
| `OnTurnCompleted_RecordsMetrics` | `MeterListener` ловит `agent.turn.completed +1`. |
| `Sequential_WritesNoPrefixToConsole` | `IConsoleWriter` mock → chunk без префикса. |
| `Concurrent_WritesAgentPrefix` | `IConsoleWriter` mock → chunk с `[agent]`. |

`IConsoleWriter` — тонкая обёртка над `Console`, чтобы тесты могли подменять вывод без `Console.SetOut`.

## YAGNI / отложено

- OTLP exporter (Jaeger/Tempo) — сейчас только Console exporter.
- Дополнительные приёмники Serilog (Elastic, Seq) — конфигурируется через `appsettings.json` без правок кода.
- UI dashboard для realtime агентских событий.
- Trace correlation между workflow run и индивидуальными turns — TODO в следующей итерации (нужен `WorkflowRunId` propagation).

## Acceptance Criteria

1. При запуске любого workflow в `logs/agents-{date}.log` появляются записи Information для каждого хода каждого агента с полным текстом.
2. В Console продолжает идти live-стриминг токенов (Sequential — без префиксов, Concurrent — с префиксом агента).
3. `OpenTelemetry.Exporter.Console` выводит spans `agent.turn.{agent}` в stdout с tags `chunks`, `text.length`, `durationMs`.
4. `Counter agent.turns.completed` инкрементируется на каждый завершённый ход.
5. Magentic `ResponseCallback` работает через тот же `IAgentActivityLogger`.
6. Tool-вызовы агентов попадают в лог отдельной записью.
7. Незавершённые ходы при ошибке flush-ятся в лог как `aborted`.
8. Все тесты xUnit проходят.
9. `dotnet build src/` без warnings.
