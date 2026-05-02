# Дизайн: вызов MCP-инструментов и C# плагинов из AI-агентов

**Дата:** 2026-05-02
**Статус:** Draft (ожидает review пользователя)
**Проект:** `AiAgetnsWorkflowApp` (MagenticWorkflowApp)

## 1. Цель

Дать AI-агентам в проекте возможность вызывать инструменты, поставляемые через **Model Context Protocol** (MCP) серверы (stdio + HTTP/SSE), а также инструменты, реализованные локально как C# **плагины**. Расширение касается всех четырёх типов оркестрации (Sequential, Concurrent, Conditional, Magentic).

В рамках этой работы также раскомментируется и реализуется реальное выполнение workflow через `Microsoft.Agents.AI.Workflows` — сейчас оркестратор содержит только заглушки `Simulate*`.

## 2. Контекст и текущее состояние

- Проект — .NET 10.0 консольное приложение, DI-based, конфигурируется JSON-файлами `workflow-*.json`.
- `AgentConfiguration.Tools` сейчас представлен `List<string>`; обработчик умеет лишь имя `"CodeInterpreter"` и существует в виде закомментированного кода.
- Реальные пути выполнения (`ExecuteSequentialWorkflowAsync` и т.д.) — заглушки, печатают сообщение и вызывают `SimulateWorkflowExecutionAsync`.
- В `csproj` уже подключены: `Microsoft.Agents.AI` 1.3.0, `Microsoft.Agents.AI.OpenAI`, `Microsoft.Agents.AI.Workflows`, `Microsoft.Extensions.AI.OpenAI`, набор `Microsoft.SemanticKernel.Agents.*` (preview).
- Зависимостей MCP пока нет.

## 3. Не-цели (out of scope)

- Auto-restart MCP-серверов после падения.
- UI-approval flow для tool calls (Foundry-feature; мы используем OpenAI напрямую).
- Rate-limiting вызовов tools.
- HTTPS-pinning или нестандартная валидация сертификатов.
- Реализация selection-функций для Conditional workflow за пределами того, что уже было заложено в JSON (selection-функции остаются как future work и сейчас валидируются только на наличие).

## 4. Решения, принятые при brainstorming

| Вопрос | Решение |
|---|---|
| Объём | (C) полная end-to-end реализация: MCP + plugins + раскомментирование real-workflow |
| Транспорты MCP | Оба: stdio и HTTP/SSE |
| Расположение MCP-серверов в JSON | Глобальный пул `mcpServers` + ссылки из агентов по `name` |
| Выбор tools из MCP-сервера | Все автоматически (без allowlist) |
| Секреты | Подстановка `${VAR}` из environment variables |
| Lifecycle MCP | Singleton `IMcpClientPool` в DI, ленивая инициализация при первом использовании |
| Hosted vs MCP | Раздельные поля в JSON: `tools` (hosted) и `mcpServers` (MCP) |
| Plugins | Третий источник tools, регистрируется в DI как `IAgentPlugin`, ссылается из JSON по `name` |
| Архитектурный подход | (A) прямой `IMcpClientPool` + inline резолвинг tools в оркестраторе (без реестра `IAgentToolSource`) |

## 5. JSON-схема

### 5.1 Корневой уровень

Добавляется новая секция `mcpServers` (опциональная):

```jsonc
{
  "workflowType": "Sequential",
  "task": "...",
  "agents": [...],
  "orchestration": {...},
  "mcpServers": [
    {
      "name": "filesystem",
      "transport": "stdio",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "/data"],
      "env": { "LOG_LEVEL": "info" },
      "url": null,
      "headers": {},
      "startupTimeoutSeconds": 30
    },
    {
      "name": "github",
      "transport": "http",
      "url": "https://api.githubcopilot.com/mcp/",
      "headers": { "Authorization": "Bearer ${GITHUB_TOKEN}" },
      "startupTimeoutSeconds": 30
    }
  ]
}
```

### 5.2 Внутри агента

Добавляются два опциональных поля. Существующее поле `tools` сохраняет назначение для hosted built-ins.

```jsonc
{
  "name": "ResearcherAgent",
  "description": "...",
  "instructions": "...",
  "modelId": "gpt-4",
  "tools": ["CodeInterpreter"],
  "mcpServers": ["filesystem", "github"],
  "plugins": ["WeatherPlugin", "TimePlugin"],
  "metadata": {}
}
```

### 5.3 Подстановка переменных окружения

- Применяется ко всем строковым значениям внутри `mcpServers[*]`: `command`, элементы `args`, значения `env`, `url`, значения `headers`.
- Паттерн: `${UPPER_SNAKE}` — регулярка `\$\{([A-Z_][A-Z0-9_]*)\}`.
- Подстановка выполняется в `WorkflowJsonLoader` после десериализации, до валидации.
- Отсутствие переменной → `WorkflowValidationException` с её именем.

### 5.4 Правила валидации

Структурная валидация — в `WorkflowJsonLoader` (не требует доступа к DI):

1. Имена в `mcpServers[*].name` уникальны.
2. Каждое имя в `agent.mcpServers` встречается в корневом `mcpServers[*].name`.
3. `transport == "stdio"` → `command` обязателен.
4. `transport == "http"` → `url` обязателен.
5. `transport` ∈ {`"stdio"`, `"http"`}; иное значение → ошибка.

Семантическая валидация против DI — в `MagenticWorkflowOrchestrator` после построения контейнера, до выполнения:

6. Каждое имя в `agent.plugins` зарегистрировано в `IAgentPluginRegistry`. Ошибка → `WorkflowValidationException`.

## 6. Модели C# (DTO)

### 6.1 Новый файл `src/Models/McpServerConfiguration.cs`

```csharp
public class McpServerConfiguration
{
    public string Name { get; set; } = string.Empty;
    public string Transport { get; set; } = "stdio";
    public string? Command { get; set; }
    public List<string> Args { get; set; } = new();
    public Dictionary<string, string> Env { get; set; } = new();
    public string? Url { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public int StartupTimeoutSeconds { get; set; } = 30;
}
```

### 6.2 Изменения в существующих моделях

`src/Models/WorkflowConfiguration.cs` — добавить:

```csharp
public List<McpServerConfiguration> McpServers { get; set; } = new();
```

`src/Models/AgentConfiguration.cs` — добавить:

```csharp
public List<string> McpServers { get; set; } = new();
public List<string> Plugins { get; set; } = new();
```

## 7. Контракты и компоненты

### 7.1 Интерфейсы (новые)

```csharp
// src/Interfaces/IMcpClientPool.cs
public interface IMcpClientPool : IAsyncDisposable
{
    Task RegisterServersAsync(
        IReadOnlyList<McpServerConfiguration> servers,
        CancellationToken ct = default);

    Task<IReadOnlyList<AITool>> GetToolsAsync(
        IReadOnlyList<string> serverNames,
        CancellationToken ct = default);
}

// src/Interfaces/IAgentPlugin.cs
public interface IAgentPlugin
{
    string Name { get; }
    IEnumerable<AITool> AsAITools();
}

// src/Interfaces/IAgentPluginRegistry.cs
public interface IAgentPluginRegistry
{
    bool TryGet(string name, out IAgentPlugin? plugin);
    IEnumerable<string> RegisteredNames { get; }
}

// src/Interfaces/IHostedToolFactory.cs
public interface IHostedToolFactory
{
    IReadOnlyList<AITool> Create(IReadOnlyList<string> toolNames);
}
```

### 7.2 Реализации (skeleton)

**`src/Services/McpClientPool.cs`** (singleton)

- `Dictionary<string, McpServerConfiguration> _configs` — конфиги.
- `Dictionary<string, IMcpClient> _clients` — кэш клиентов.
- `SemaphoreSlim _initLock` — потокобезопасная ленивая инициализация.
- `RegisterServersAsync` сохраняет конфиги, не делает I/O.
- `GetToolsAsync(names)`:
  - Для каждого `name` — `GetOrCreateClient(name, ct)`.
  - `client.ListToolsAsync(ct)` → `IList<McpClientTool>`. `McpClientTool` наследует `AIFunction : AITool`, поэтому возвращается прямо как `AITool`.
- `GetOrCreateClient`:
  - `transport == "stdio"` → `new StdioClientTransport(new StdioClientTransportOptions { Command, Arguments = Args, EnvironmentVariables = Env })`.
  - `transport == "http"` → `new SseClientTransport(new SseClientTransportOptions { Endpoint = new Uri(Url), AdditionalHeaders = Headers })`.
  - `await McpClient.CreateAsync(transport, options: null, ct)` с timeout = `StartupTimeoutSeconds`.
- `DisposeAsync` — для каждого клиента в `_clients` вызывает `await client.DisposeAsync()`.

**`src/Services/AgentPluginRegistry.cs`** (singleton)

- Конструктор: `AgentPluginRegistry(IEnumerable<IAgentPlugin> plugins)` — собирает в `Dictionary<string, IAgentPlugin>` по `Name`. Дубликат имени → throws при старте.

**`src/Services/HostedToolFactory.cs`** (singleton)

- `Create(["CodeInterpreter"])` → `[new HostedCodeInterpreterTool()]`.
- Незнакомое имя → `NotSupportedException`.

**`src/Services/EnvVarSubstitution.cs`** (internal helper)

```csharp
internal static class EnvVarSubstitution
{
    private static readonly Regex Pattern = new(@"\$\{([A-Z_][A-Z0-9_]*)\}", RegexOptions.Compiled);

    public static string Apply(string input)
    {
        return Pattern.Replace(input, m =>
        {
            var name = m.Groups[1].Value;
            return Environment.GetEnvironmentVariable(name)
                ?? throw new WorkflowValidationException(
                       $"Environment variable '{name}' is not set");
        });
    }
}
```

### 7.3 Новые исключения

```csharp
// src/Exceptions/McpServerStartupException.cs
public sealed class McpServerStartupException : Exception
{
    public string ServerName { get; }
    public McpServerStartupException(string serverName, string message, Exception? inner = null)
        : base($"MCP server '{serverName}' failed to start: {message}", inner)
        => ServerName = serverName;
}

// src/Exceptions/McpServerCommunicationException.cs
public sealed class McpServerCommunicationException : Exception
{
    public string ServerName { get; }
    public McpServerCommunicationException(string serverName, string message, Exception? inner = null)
        : base($"MCP server '{serverName}' communication failure: {message}", inner)
        => ServerName = serverName;
}

// WorkflowValidationException уже планировался в copilot-instructions; используем единый класс.
```

## 8. Изменения в `MagenticWorkflowOrchestrator`

### 8.1 Конструктор

Добавляются три зависимости:

```csharp
public MagenticWorkflowOrchestrator(
    ILogger<MagenticWorkflowOrchestrator> logger,
    IWorkflowJsonLoader jsonLoader,
    IWorkflowVisualizer visualizer,
    IConfiguration configuration,
    IMcpClientPool mcpPool,
    IHostedToolFactory hostedToolFactory,
    IAgentPluginRegistry pluginRegistry)
```

### 8.2 Раскомментирование real-workflow и новый метод

`CreateAgentsFromConfigurationAsync` (новый, заменяет закомментированный):

```csharp
private async Task<Dictionary<string, AIAgent>> CreateAgentsFromConfigurationAsync(
    WorkflowConfiguration config,
    string? openAiApiKey,
    string? azureEndpoint,
    CancellationToken ct)
{
    var agents = new Dictionary<string, AIAgent>(StringComparer.Ordinal);

    foreach (var agentConfig in config.Agents)
    {
        var hostedTools = _hostedFactory.Create(agentConfig.Tools);
        var mcpTools    = await _mcpPool.GetToolsAsync(agentConfig.McpServers, ct).ConfigureAwait(false);
        var pluginTools = ResolvePluginTools(agentConfig.Plugins);
        var allTools    = hostedTools.Concat(mcpTools).Concat(pluginTools).ToArray();

        var chatClient = BuildChatClient(agentConfig.ModelId, openAiApiKey, azureEndpoint);
        var agent = chatClient.CreateAIAgent(
            name:         agentConfig.Name,
            description:  agentConfig.Description,
            instructions: agentConfig.Instructions,
            tools:        allTools);

        agents[agentConfig.Name] = agent;
    }

    return agents;
}
```

`Execute*WorkflowAsync` — раскомментировать предзаготовленный код, убрать `Console.WriteLine("📝 ... would occur here")` и вызов `SimulateWorkflowExecutionAsync`. DEMO-режим остаётся на верхнем уровне в `ExecuteWorkflowFromJsonAsync` (когда нет ApiKey).

`HandleWorkflowEvent(WorkflowEvent evt)` — единый метод обработки событий с цветным выводом по типу:

| Event              | Source label    | Color    |
|--------------------|-----------------|----------|
| AgentRunUpdate     | `[AGENT:name]`  | Yellow   |
| FunctionCallEvent  | `[TOOL:name]`   | Magenta  |
| FunctionResultEvent| `[TOOL:name]`   | DarkGray |
| WorkflowErrorEvent | `[ERROR]`       | Red      |
| Magentic events    | `[ORCHESTRATOR]`| Cyan     |

### 8.3 Регистрация в DI (`Program.ConfigureServices`)

```csharp
services.AddSingleton<IMcpClientPool, McpClientPool>();
services.AddSingleton<IHostedToolFactory, HostedToolFactory>();
services.AddSingleton<IAgentPluginRegistry, AgentPluginRegistry>();

// Plugins пользователя — пример:
services.AddSingleton<IAgentPlugin, WeatherPlugin>();
services.AddSingleton<IAgentPlugin, TimePlugin>();
```

`Program.Main` оборачивает `serviceProvider` в `await using` (через каст к `IAsyncDisposable`), чтобы при выходе вызывался `IMcpClientPool.DisposeAsync` и stdio-процессы завершились корректно. Также добавляется `Console.CancelKeyPress` → `cts.Cancel()` для graceful shutdown.

## 9. Поток выполнения (data flow)

1. `Program.Main`: build DI, создаёт `CancellationTokenSource` с обработчиком Ctrl+C, вызывает `orchestrator.ExecuteWorkflowFromJsonAsync(path, ct)`.
2. `WorkflowJsonLoader.LoadConfigurationAsync`:
   1. Десериализация JSON.
   2. `EnvVarSubstitution.Apply` ко всем строкам в `config.McpServers[*]`.
   3. `ValidateConfiguration` (см. §5.4 + существующие проверки).
3. `MagenticWorkflowOrchestrator.ExecuteWorkflowFromJsonAsync`:
   1. Семантическая валидация против DI (правило 6 из §5.4): `agent.plugins[i] ∈ pluginRegistry.RegisteredNames`.
   2. `Visualizer.VisualizeWorkflow(config)`.
   3. `await _mcpPool.RegisterServersAsync(config.McpServers, ct)`.
   4. Если ApiKey отсутствует — DEMO-ветка `SimulateWorkflowExecutionAsync` (без MCP I/O).
   5. Иначе — `ExecuteActualWorkflowAsync`.
4. `ExecuteActualWorkflowAsync`:
   1. `agents = await CreateAgentsFromConfigurationAsync(...)` — на этом шаге MCP-серверы реально стартуют (lazy через `_mcpPool.GetToolsAsync`).
   2. `switch(workflowType)` → выбор `WorkflowBuilder`/`ConcurrentBuilder`/`MagenticBuilder`.
   3. `await foreach (var evt in workflow.RunStreamAsync(config.Task, ct)) HandleWorkflowEvent(evt)`.
5. Cleanup на выходе из `Main`: `serviceProvider.DisposeAsync()` → `IMcpClientPool.DisposeAsync` → каждый `IMcpClient.DisposeAsync` → stdio-процессы завершаются, HTTP-клиенты закрываются.

## 10. Error handling

| Точка отказа | Исключение | Обработка |
|---|---|---|
| Env var отсутствует | `WorkflowValidationException` | Fail fast при загрузке |
| Несуществующая ссылка `agent.mcpServers[i]` | `WorkflowValidationException` | Fail fast при валидации |
| Несуществующая ссылка `agent.plugins[i]` | `WorkflowValidationException` | Fail fast при валидации |
| stdio process не запустился (timeout/exit) | `McpServerStartupException` | Прервать workflow, логировать stderr |
| HTTP сервер недоступен (timeout/5xx) | `McpServerStartupException` | Прервать workflow |
| Сервер упал mid-workflow | `McpServerCommunicationException` | Прокинуть наверх (auto-restart — future) |
| Tool invocation бросил | Прокидывается через `AITool` | Логируется как `FunctionCallError`; recovery — на стороне агента |
| Auth fail OpenAI/Azure | Из SDK | Прокинуть с подсказкой `dotnet user-secrets` |
| Schema mismatch JSON | `JsonException` | Существующая обработка |

Структурированный лог обязателен в ключевых точках:

```
"Registering {Count} MCP servers"
"Starting MCP server {Name} via {Transport}"
"MCP server {Name} ready: {ToolCount} tools"
"Agent {Agent} resolved tools: hosted={H}, mcp={M}, plugins={P}"
"MCP server {Name} stderr: {Line}"   // stdio diagnostics
"MCP server {Name} startup failed"   // Error level + ex
```

## 11. Зависимости (csproj)

Добавить:

```xml
<PackageReference Include="ModelContextProtocol" Version="1.2.0" />
```

`Microsoft.Extensions.AI` уже подтягивается транзитивно через `Microsoft.Agents.AI`; явно добавлять не требуется. Если возникнут конфликты версий — закрепить явно.

Целевой framework — `net10.0` (без изменений).

## 12. Тестирование

### 12.1 Новый тестовый проект

```
tests/AiAgetnsWorkflow.Tests/
  AiAgetnsWorkflow.Tests.csproj   # xUnit + FluentAssertions + NSubstitute
  Loader/
    WorkflowJsonLoaderTests.cs
    EnvVarSubstitutionTests.cs
  Mcp/
    McpClientPoolTests.cs
  Plugins/
    AgentPluginRegistryTests.cs
  Tools/
    HostedToolFactoryTests.cs
  Integration/
    OrchestratorMcpIntegrationTests.cs
  Fakes/
    FakeAgentPlugin.cs
  TestData/
    workflow-with-mcp.json
    workflow-with-plugins.json
    workflow-invalid-missing-mcp-ref.json
    workflow-invalid-missing-env-var.json

tests/FakeMcpServer/                # отдельный stdio-сервер для integration
  FakeMcpServer.csproj
  Program.cs                        # экспортирует tools: echo, add
```

### 12.2 Unit-тесты

- `EnvVarSubstitutionTests`: одна/несколько подстановок; `${var}` lower-case не подставляется; отсутствующая переменная → exception с её именем.
- `WorkflowJsonLoaderTests`: позитивные кейсы, все правила валидации §5.4, обратная совместимость со старыми `workflow-*.json`.
- `AgentPluginRegistryTests`: lookup по имени, дубликат при старте → throws.
- `HostedToolFactoryTests`: известное/неизвестное имя, пустой список.
- `McpClientPoolTests` (с моком транспорта): ленивая инициализация, кэширование, потокобезопасность, dispose.

### 12.3 Integration-тесты

- `FakeMcpServer` стартуется как stdio process; пул соединяется, запрашивает tools, инвокирует их.
- HTTP-кейс: in-process сервер через `WebApplicationFactory` + `ModelContextProtocol.AspNetCore`.
- Smoke-кейс полного workflow: загрузка JSON с MCP-сервером + плагинами, создание агентов, проверка что `AIAgent` получил правильное число tools (без реального LLM — `IChatClient` подменяется фейком).

Тесты с процессами помечаются `[Trait("Category","Integration")]` для отдельного запуска.

## 13. Совместимость

- Существующие `workflow-*.json` без полей `mcpServers` и `plugins` — продолжают работать (поля опциональны).
- Существующие тесты (когда появятся — сейчас отсутствуют) не должны страдать от добавления новых зависимостей в DI.
- Smoke-кейс совместимости: `dotnet run workflow-simple.json` после изменений показывает тот же результат, что и до них (только теперь — через реальный `Microsoft.Agents.AI.Workflows`, а не через `Simulate*`).

## 14. Future work (намеренно отложено)

- Selection-функции для Conditional workflow (через отдельный `ISelectionFunctionRegistry`).
- Allowlist/denylist tools при ссылке на MCP-сервер.
- Auto-restart упавших MCP-серверов с backoff.
- Approval-flow для tool invocations (UI-prompt перед выполнением).
- Унификация `tools`/`mcpServers`/`plugins` в один типизированный массив, если появится 4-й источник.
- Реестр `IAgentToolSource` (подход B из brainstorming).

## 15. Open questions

Нет открытых вопросов на момент написания. Все развилки закрыты в §4.
