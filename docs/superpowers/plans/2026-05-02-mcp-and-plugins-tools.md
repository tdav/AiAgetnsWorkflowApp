# MCP-инструменты и C#-плагины — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Реализовать вызов инструментов от MCP-серверов (stdio + HTTP/SSE) и локальных C#-плагинов в AI-агентах MagenticWorkflowApp; раскомментировать и запустить реальное выполнение workflow через `Microsoft.Agents.AI.Workflows`.

**Architecture:** Три источника tools — `IHostedToolFactory` (built-ins), `IMcpClientPool` (singleton, ленивая инициализация MCP-клиентов), `IAgentPluginRegistry` (DI-реестр C#-плагинов). Оркестратор собирает `AITool[]` из трёх источников и передаёт в `IChatClient.CreateAIAgent`. JSON получает корневой `mcpServers` и поля `mcpServers`/`plugins` внутри агента. Подстановка `${VAR}` в строках MCP-конфига при загрузке.

**Tech Stack:** .NET 10.0, C# 12, `Microsoft.Agents.AI` 1.3.0, `Microsoft.Agents.AI.Workflows`, `ModelContextProtocol` 1.2.0, xUnit + FluentAssertions + NSubstitute.

**Spec:** [docs/superpowers/specs/2026-05-02-mcp-and-plugins-tools-design.md](../specs/2026-05-02-mcp-and-plugins-tools-design.md)

---

## File Structure

### Создаются (new)

| Path | Назначение |
|---|---|
| `AiAgetnsWorkflow.sln` | solution-файл |
| `src/Exceptions/WorkflowValidationException.cs` | Доменное исключение валидации |
| `src/Exceptions/McpServerStartupException.cs` | Ошибка старта MCP-сервера |
| `src/Exceptions/McpServerCommunicationException.cs` | Ошибка связи с MCP-сервером |
| `src/Models/McpServerConfiguration.cs` | DTO MCP-сервера |
| `src/Interfaces/IMcpClientPool.cs` | Контракт пула MCP-клиентов |
| `src/Interfaces/IAgentPlugin.cs` | Контракт C#-плагина |
| `src/Interfaces/IAgentPluginRegistry.cs` | Контракт реестра плагинов |
| `src/Interfaces/IHostedToolFactory.cs` | Контракт фабрики hosted-tools |
| `src/Services/EnvVarSubstitution.cs` | Internal helper подстановки `${VAR}` |
| `src/Services/HostedToolFactory.cs` | Реализация |
| `src/Services/AgentPluginRegistry.cs` | Реализация |
| `src/Services/McpClientPool.cs` | Реализация |
| `src/Plugins/WeatherPlugin.cs` | Пример плагина |
| `src/Plugins/TimePlugin.cs` | Пример плагина |
| `src/workflow-with-mcp.json` | Пример workflow с MCP |
| `src/workflow-with-plugins.json` | Пример workflow с плагинами |
| `tests/AiAgetnsWorkflow.Tests/AiAgetnsWorkflow.Tests.csproj` | xUnit-проект |
| `tests/AiAgetnsWorkflow.Tests/Loader/EnvVarSubstitutionTests.cs` | unit |
| `tests/AiAgetnsWorkflow.Tests/Loader/WorkflowJsonLoaderTests.cs` | unit |
| `tests/AiAgetnsWorkflow.Tests/Tools/HostedToolFactoryTests.cs` | unit |
| `tests/AiAgetnsWorkflow.Tests/Plugins/AgentPluginRegistryTests.cs` | unit |
| `tests/AiAgetnsWorkflow.Tests/Mcp/McpClientPoolTests.cs` | unit (моки) |
| `tests/AiAgetnsWorkflow.Tests/Integration/McpClientPoolStdioTests.cs` | integration |
| `tests/AiAgetnsWorkflow.Tests/Integration/OrchestratorWiringTests.cs` | smoke |
| `tests/AiAgetnsWorkflow.Tests/Fakes/FakeAgentPlugin.cs` | helper |
| `tests/AiAgetnsWorkflow.Tests/Fakes/FakeChatClient.cs` | helper |
| `tests/AiAgetnsWorkflow.Tests/TestData/*.json` | фикстуры |
| `tests/FakeMcpServer/FakeMcpServer.csproj` | console MCP-сервер для integration |
| `tests/FakeMcpServer/Program.cs` | echo + add tools |

### Изменяются (modify)

| Path | Что меняется |
|---|---|
| `src/AiAgetnsWorkflow.csproj` | + `ModelContextProtocol` 1.2.0 |
| `src/Models/WorkflowConfiguration.cs` | + `McpServers` |
| `src/Models/AgentConfiguration.cs` | + `McpServers`, `Plugins` |
| `src/Services/WorkflowJsonLoader.cs` | env-substitution + новые правила валидации |
| `src/Services/MagenticWorkflowOrchestrator.cs` | новый конструктор, реальное выполнение workflow, `CreateAgentsFromConfigurationAsync`, расширенный `HandleWorkflowEvent` |
| `src/Program.cs` | DI-регистрация новых сервисов, `Console.CancelKeyPress`, `await using` для DI |

---

## Конвенции для всех задач

- Один `dotnet build` после каждого редактирования кода — sanity check.
- Тесты пишутся xUnit + FluentAssertions + NSubstitute.
- Целевой framework везде `net10.0`.
- Stage только перечисленные в задаче файлы (`git add path1 path2`); никогда `git add .` / `-A`.
- Структурные коммиты: `feat:` / `test:` / `refactor:` / `chore:`.
- Все async-методы в коде проекта — суффикс `Async` + `ConfigureAwait(false)` внутри сервисов.

---

## Phase 0 — Infrastructure

### Task 1: Solution + test project + WorkflowValidationException

**Files:**
- Create: `AiAgetnsWorkflow.sln`
- Create: `tests/AiAgetnsWorkflow.Tests/AiAgetnsWorkflow.Tests.csproj`
- Create: `tests/AiAgetnsWorkflow.Tests/UsingsGlobal.cs`
- Create: `src/Exceptions/WorkflowValidationException.cs`

- [ ] **Step 1: Создать solution и подключить существующий проект**

```bash
dotnet new sln -n AiAgetnsWorkflow
dotnet sln add src/AiAgetnsWorkflow.csproj
```

- [ ] **Step 2: Создать тестовый проект**

```bash
dotnet new xunit -n AiAgetnsWorkflow.Tests -o tests/AiAgetnsWorkflow.Tests --framework net10.0
dotnet sln add tests/AiAgetnsWorkflow.Tests/AiAgetnsWorkflow.Tests.csproj
dotnet add tests/AiAgetnsWorkflow.Tests reference src/AiAgetnsWorkflow.csproj
dotnet add tests/AiAgetnsWorkflow.Tests package FluentAssertions
dotnet add tests/AiAgetnsWorkflow.Tests package NSubstitute
dotnet add tests/AiAgetnsWorkflow.Tests package Microsoft.Extensions.Logging.Abstractions
```

- [ ] **Step 3: Глобальные usings для тестов**

`tests/AiAgetnsWorkflow.Tests/UsingsGlobal.cs`:
```csharp
global using FluentAssertions;
global using NSubstitute;
global using Xunit;
```

- [ ] **Step 4: Доменное исключение валидации**

`src/Exceptions/WorkflowValidationException.cs`:
```csharp
namespace MagenticWorkflowApp.Exceptions;

public sealed class WorkflowValidationException : Exception
{
    public WorkflowValidationException(string message) : base(message) { }
    public WorkflowValidationException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 5: Build всего solution**

Run: `dotnet build`
Expected: `Build succeeded.` без ошибок.

- [ ] **Step 6: Commit**

```bash
git add AiAgetnsWorkflow.sln tests/AiAgetnsWorkflow.Tests/AiAgetnsWorkflow.Tests.csproj tests/AiAgetnsWorkflow.Tests/UsingsGlobal.cs src/Exceptions/WorkflowValidationException.cs
git commit -m "chore: добавить solution, xunit-проект и WorkflowValidationException"
```

---

### Task 2: Подключить ModelContextProtocol NuGet

**Files:**
- Modify: `src/AiAgetnsWorkflow.csproj`

- [ ] **Step 1: Добавить пакет**

```bash
dotnet add src/AiAgetnsWorkflow.csproj package ModelContextProtocol --version 1.2.0
```

- [ ] **Step 2: Sanity-build**

Run: `dotnet build src/AiAgetnsWorkflow.csproj`
Expected: успех, в логе виден ModelContextProtocol.

- [ ] **Step 3: Commit**

```bash
git add src/AiAgetnsWorkflow.csproj
git commit -m "chore: подключить ModelContextProtocol 1.2.0"
```

---

## Phase 1 — Configuration models + env substitution

### Task 3: McpServerConfiguration + расширение Workflow/AgentConfiguration

**Files:**
- Create: `src/Models/McpServerConfiguration.cs`
- Modify: `src/Models/WorkflowConfiguration.cs`
- Modify: `src/Models/AgentConfiguration.cs`

- [ ] **Step 1: DTO MCP-сервера**

`src/Models/McpServerConfiguration.cs`:
```csharp
namespace MagenticWorkflowApp.Models;

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

- [ ] **Step 2: Поле в WorkflowConfiguration**

В `src/Models/WorkflowConfiguration.cs` — добавить свойство в существующий класс:
```csharp
public List<McpServerConfiguration> McpServers { get; set; } = new();
```

- [ ] **Step 3: Поля в AgentConfiguration**

`src/Models/AgentConfiguration.cs` — добавить два свойства:
```csharp
public List<string> McpServers { get; set; } = new();
public List<string> Plugins { get; set; } = new();
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: успех.

- [ ] **Step 5: Commit**

```bash
git add src/Models/McpServerConfiguration.cs src/Models/WorkflowConfiguration.cs src/Models/AgentConfiguration.cs
git commit -m "feat(models): добавить McpServerConfiguration и поля McpServers/Plugins"
```

---

### Task 4: EnvVarSubstitution helper (TDD)

**Files:**
- Create: `tests/AiAgetnsWorkflow.Tests/Loader/EnvVarSubstitutionTests.cs`
- Create: `src/Services/EnvVarSubstitution.cs`

- [ ] **Step 1: Failing tests**

`tests/AiAgetnsWorkflow.Tests/Loader/EnvVarSubstitutionTests.cs`:
```csharp
using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Services;

namespace AiAgetnsWorkflow.Tests.Loader;

public class EnvVarSubstitutionTests : IDisposable
{
    private const string TestVar = "AIAGENTS_TEST_VAR_X";

    public void Dispose() => Environment.SetEnvironmentVariable(TestVar, null);

    [Fact]
    public void Apply_NoPlaceholders_ReturnsInputUnchanged()
    {
        EnvVarSubstitution.Apply("plain string").Should().Be("plain string");
    }

    [Fact]
    public void Apply_SinglePlaceholder_SubstitutesValue()
    {
        Environment.SetEnvironmentVariable(TestVar, "secret");
        EnvVarSubstitution.Apply($"Bearer ${{{TestVar}}}").Should().Be("Bearer secret");
    }

    [Fact]
    public void Apply_MultiplePlaceholders_SubstitutesAll()
    {
        Environment.SetEnvironmentVariable(TestVar, "X");
        EnvVarSubstitution.Apply($"${{{TestVar}}}-${{{TestVar}}}").Should().Be("X-X");
    }

    [Fact]
    public void Apply_LowerCaseVariable_DoesNotSubstitute()
    {
        EnvVarSubstitution.Apply("${lower_case}").Should().Be("${lower_case}");
    }

    [Fact]
    public void Apply_MissingVariable_ThrowsWithVarName()
    {
        var act = () => EnvVarSubstitution.Apply("${THIS_VAR_DOES_NOT_EXIST_XYZ_42}");
        act.Should().Throw<WorkflowValidationException>()
           .WithMessage("*THIS_VAR_DOES_NOT_EXIST_XYZ_42*");
    }
}
```

- [ ] **Step 2: Run — fail**

Run: `dotnet test tests/AiAgetnsWorkflow.Tests --filter "FullyQualifiedName~EnvVarSubstitution"`
Expected: компиляция падает (`EnvVarSubstitution` не существует).

- [ ] **Step 3: Implementation**

`src/Services/EnvVarSubstitution.cs`:
```csharp
using System.Text.RegularExpressions;
using MagenticWorkflowApp.Exceptions;

namespace MagenticWorkflowApp.Services;

internal static class EnvVarSubstitution
{
    private static readonly Regex Pattern = new(@"\$\{([A-Z_][A-Z0-9_]*)\}", RegexOptions.Compiled);

    public static string Apply(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

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

`EnvVarSubstitution` — `internal`. Сделать его видимым тесту: добавить в `src/AiAgetnsWorkflow.csproj`:
```xml
<ItemGroup>
  <InternalsVisibleTo Include="AiAgetnsWorkflow.Tests" />
</ItemGroup>
```

- [ ] **Step 4: Run — pass**

Run: `dotnet test tests/AiAgetnsWorkflow.Tests --filter "FullyQualifiedName~EnvVarSubstitution"`
Expected: 5 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Services/EnvVarSubstitution.cs src/AiAgetnsWorkflow.csproj tests/AiAgetnsWorkflow.Tests/Loader/EnvVarSubstitutionTests.cs
git commit -m "feat(loader): EnvVarSubstitution helper с подстановкой \${VAR}"
```

---

### Task 5: WorkflowJsonLoader — env-substitution + новые правила (TDD)

**Files:**
- Create: `tests/AiAgetnsWorkflow.Tests/Loader/WorkflowJsonLoaderTests.cs`
- Create: `tests/AiAgetnsWorkflow.Tests/TestData/workflow-with-mcp.json`
- Create: `tests/AiAgetnsWorkflow.Tests/TestData/workflow-invalid-missing-mcp-ref.json`
- Create: `tests/AiAgetnsWorkflow.Tests/TestData/workflow-invalid-stdio-no-command.json`
- Create: `tests/AiAgetnsWorkflow.Tests/TestData/workflow-invalid-http-no-url.json`
- Create: `tests/AiAgetnsWorkflow.Tests/TestData/workflow-invalid-duplicate-mcp.json`
- Create: `tests/AiAgetnsWorkflow.Tests/TestData/workflow-invalid-bad-transport.json`
- Modify: `src/Services/WorkflowJsonLoader.cs`
- Modify: `tests/AiAgetnsWorkflow.Tests/AiAgetnsWorkflow.Tests.csproj` (CopyToOutputDirectory testdata)

- [ ] **Step 1: TestData JSON-файлы**

`tests/AiAgetnsWorkflow.Tests/TestData/workflow-with-mcp.json`:
```json
{
  "workflowType": "Sequential",
  "task": "demo",
  "agents": [
    {
      "name": "AgentA",
      "description": "first",
      "instructions": "do",
      "modelId": "gpt-4",
      "mcpServers": ["filesystem"]
    },
    {
      "name": "AgentB",
      "description": "second",
      "instructions": "do",
      "modelId": "gpt-4"
    }
  ],
  "orchestration": {
    "startAgent": "AgentA",
    "edges": [{ "from": "AgentA", "to": "AgentB" }]
  },
  "mcpServers": [
    {
      "name": "filesystem",
      "transport": "stdio",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "${TEST_FS_ROOT}"]
    }
  ]
}
```

`tests/AiAgetnsWorkflow.Tests/TestData/workflow-invalid-missing-mcp-ref.json`:
```json
{
  "workflowType": "Sequential",
  "task": "demo",
  "agents": [
    { "name": "A", "description": "x", "instructions": "x", "modelId": "gpt-4", "mcpServers": ["nope"] }
  ],
  "orchestration": { "startAgent": "A", "edges": [] },
  "mcpServers": []
}
```

`tests/AiAgetnsWorkflow.Tests/TestData/workflow-invalid-stdio-no-command.json`:
```json
{
  "workflowType": "Sequential",
  "task": "demo",
  "agents": [{ "name": "A", "description": "x", "instructions": "x", "modelId": "gpt-4" }],
  "orchestration": { "startAgent": "A", "edges": [] },
  "mcpServers": [{ "name": "broken", "transport": "stdio" }]
}
```

`tests/AiAgetnsWorkflow.Tests/TestData/workflow-invalid-http-no-url.json`:
```json
{
  "workflowType": "Sequential",
  "task": "demo",
  "agents": [{ "name": "A", "description": "x", "instructions": "x", "modelId": "gpt-4" }],
  "orchestration": { "startAgent": "A", "edges": [] },
  "mcpServers": [{ "name": "broken", "transport": "http" }]
}
```

`tests/AiAgetnsWorkflow.Tests/TestData/workflow-invalid-duplicate-mcp.json`:
```json
{
  "workflowType": "Sequential",
  "task": "demo",
  "agents": [{ "name": "A", "description": "x", "instructions": "x", "modelId": "gpt-4" }],
  "orchestration": { "startAgent": "A", "edges": [] },
  "mcpServers": [
    { "name": "fs", "transport": "stdio", "command": "echo" },
    { "name": "fs", "transport": "stdio", "command": "echo" }
  ]
}
```

`tests/AiAgetnsWorkflow.Tests/TestData/workflow-invalid-bad-transport.json`:
```json
{
  "workflowType": "Sequential",
  "task": "demo",
  "agents": [{ "name": "A", "description": "x", "instructions": "x", "modelId": "gpt-4" }],
  "orchestration": { "startAgent": "A", "edges": [] },
  "mcpServers": [{ "name": "x", "transport": "smtp", "command": "x" }]
}
```

- [ ] **Step 2: Включить копирование TestData в out**

В `tests/AiAgetnsWorkflow.Tests/AiAgetnsWorkflow.Tests.csproj` добавить:
```xml
<ItemGroup>
  <None Include="TestData\**\*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

- [ ] **Step 3: Failing tests**

`tests/AiAgetnsWorkflow.Tests/Loader/WorkflowJsonLoaderTests.cs`:
```csharp
using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgetnsWorkflow.Tests.Loader;

public class WorkflowJsonLoaderTests : IDisposable
{
    private const string FsRootVar = "TEST_FS_ROOT";

    public WorkflowJsonLoaderTests()
        => Environment.SetEnvironmentVariable(FsRootVar, "/tmp/data");

    public void Dispose()
        => Environment.SetEnvironmentVariable(FsRootVar, null);

    private static WorkflowJsonLoader CreateLoader()
        => new(NullLogger<WorkflowJsonLoader>.Instance);

    private static string Path(string fileName)
        => System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    [Fact]
    public async Task LoadAsync_ValidMcpConfig_ParsesAndSubstitutesEnv()
    {
        var cfg = await CreateLoader().LoadConfigurationAsync(Path("workflow-with-mcp.json"));
        cfg.McpServers.Should().HaveCount(1);
        cfg.McpServers[0].Args[2].Should().Be("/tmp/data");
        cfg.Agents[0].McpServers.Should().BeEquivalentTo(new[] { "filesystem" });
    }

    [Fact]
    public async Task LoadAsync_MissingMcpRef_ThrowsValidation()
    {
        var act = () => CreateLoader().LoadConfigurationAsync(Path("workflow-invalid-missing-mcp-ref.json"));
        await act.Should().ThrowAsync<WorkflowValidationException>().WithMessage("*nope*");
    }

    [Fact]
    public async Task LoadAsync_StdioWithoutCommand_ThrowsValidation()
    {
        var act = () => CreateLoader().LoadConfigurationAsync(Path("workflow-invalid-stdio-no-command.json"));
        await act.Should().ThrowAsync<WorkflowValidationException>().WithMessage("*command*");
    }

    [Fact]
    public async Task LoadAsync_HttpWithoutUrl_ThrowsValidation()
    {
        var act = () => CreateLoader().LoadConfigurationAsync(Path("workflow-invalid-http-no-url.json"));
        await act.Should().ThrowAsync<WorkflowValidationException>().WithMessage("*url*");
    }

    [Fact]
    public async Task LoadAsync_DuplicateMcpName_ThrowsValidation()
    {
        var act = () => CreateLoader().LoadConfigurationAsync(Path("workflow-invalid-duplicate-mcp.json"));
        await act.Should().ThrowAsync<WorkflowValidationException>().WithMessage("*duplicate*");
    }

    [Fact]
    public async Task LoadAsync_UnknownTransport_ThrowsValidation()
    {
        var act = () => CreateLoader().LoadConfigurationAsync(Path("workflow-invalid-bad-transport.json"));
        await act.Should().ThrowAsync<WorkflowValidationException>().WithMessage("*transport*smtp*");
    }
}
```

- [ ] **Step 4: Run — fail**

Run: `dotnet test --filter "FullyQualifiedName~WorkflowJsonLoaderTests"`
Expected: тесты падают (новой логики нет).

- [ ] **Step 5: Implementation**

В `src/Services/WorkflowJsonLoader.cs` (модификация existing класса) — после десериализации перед прежней валидацией прогоняем env-substitution для всех строк в `config.McpServers[*]`, затем добавляем 5 новых правил. Псевдокод нового метода:

```csharp
private static void ApplyEnvSubstitution(WorkflowConfiguration cfg)
{
    foreach (var s in cfg.McpServers)
    {
        if (s.Command is not null) s.Command = EnvVarSubstitution.Apply(s.Command);
        for (int i = 0; i < s.Args.Count; i++) s.Args[i] = EnvVarSubstitution.Apply(s.Args[i]);
        foreach (var k in s.Env.Keys.ToList()) s.Env[k] = EnvVarSubstitution.Apply(s.Env[k]);
        if (s.Url is not null) s.Url = EnvVarSubstitution.Apply(s.Url);
        foreach (var k in s.Headers.Keys.ToList()) s.Headers[k] = EnvVarSubstitution.Apply(s.Headers[k]);
    }
}

private static void ValidateMcpServers(WorkflowConfiguration cfg)
{
    var names = new HashSet<string>(StringComparer.Ordinal);
    foreach (var s in cfg.McpServers)
    {
        if (string.IsNullOrWhiteSpace(s.Name))
            throw new WorkflowValidationException("MCP server name cannot be empty");
        if (!names.Add(s.Name))
            throw new WorkflowValidationException($"Duplicate MCP server name: {s.Name}");
        var t = s.Transport?.ToLowerInvariant();
        if (t != "stdio" && t != "http")
            throw new WorkflowValidationException($"Unknown MCP transport '{s.Transport}' for server '{s.Name}'");
        if (t == "stdio" && string.IsNullOrWhiteSpace(s.Command))
            throw new WorkflowValidationException($"MCP server '{s.Name}' (stdio) requires 'command'");
        if (t == "http" && string.IsNullOrWhiteSpace(s.Url))
            throw new WorkflowValidationException($"MCP server '{s.Name}' (http) requires 'url'");
    }

    foreach (var agent in cfg.Agents)
        foreach (var refName in agent.McpServers)
            if (!names.Contains(refName))
                throw new WorkflowValidationException(
                    $"Agent '{agent.Name}' references unknown MCP server '{refName}'");
}
```

Подключить вызовы в `LoadConfigurationAsync`:
```csharp
ApplyEnvSubstitution(config);
ValidateConfiguration(config);   // существующий
ValidateMcpServers(config);      // новый
return config;
```

- [ ] **Step 6: Run — pass**

Run: `dotnet test --filter "FullyQualifiedName~WorkflowJsonLoaderTests"`
Expected: 6 passed.

- [ ] **Step 7: Commit**

```bash
git add src/Services/WorkflowJsonLoader.cs tests/AiAgetnsWorkflow.Tests
git commit -m "feat(loader): env-substitution и валидация MCP-конфигов"
```

---

## Phase 2 — Tool factories (3 источника)

### Task 6: HostedToolFactory + IHostedToolFactory (TDD)

**Files:**
- Create: `src/Interfaces/IHostedToolFactory.cs`
- Create: `src/Services/HostedToolFactory.cs`
- Create: `tests/AiAgetnsWorkflow.Tests/Tools/HostedToolFactoryTests.cs`

- [ ] **Step 1: Failing tests**

`tests/AiAgetnsWorkflow.Tests/Tools/HostedToolFactoryTests.cs`:
```csharp
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.AI;

namespace AiAgetnsWorkflow.Tests.Tools;

public class HostedToolFactoryTests
{
    [Fact]
    public void Create_EmptyList_ReturnsEmpty()
    {
        new HostedToolFactory().Create(Array.Empty<string>()).Should().BeEmpty();
    }

    [Fact]
    public void Create_KnownName_ReturnsCodeInterpreterTool()
    {
        var tools = new HostedToolFactory().Create(new[] { "CodeInterpreter" });
        tools.Should().ContainSingle();
        tools[0].Should().BeAssignableTo<AITool>();
    }

    [Fact]
    public void Create_UnknownName_Throws()
    {
        var act = () => new HostedToolFactory().Create(new[] { "Bogus" });
        act.Should().Throw<NotSupportedException>().WithMessage("*Bogus*");
    }
}
```

- [ ] **Step 2: Run — fail**

Run: `dotnet test --filter "FullyQualifiedName~HostedToolFactoryTests"`
Expected: компиляция падает.

- [ ] **Step 3: Interface + implementation**

`src/Interfaces/IHostedToolFactory.cs`:
```csharp
using Microsoft.Extensions.AI;

namespace MagenticWorkflowApp.Interfaces;

public interface IHostedToolFactory
{
    IReadOnlyList<AITool> Create(IReadOnlyList<string> toolNames);
}
```

`src/Services/HostedToolFactory.cs`:
```csharp
using MagenticWorkflowApp.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MagenticWorkflowApp.Services;

public sealed class HostedToolFactory : IHostedToolFactory
{
    public IReadOnlyList<AITool> Create(IReadOnlyList<string> toolNames)
    {
        if (toolNames.Count == 0) return Array.Empty<AITool>();

        var result = new List<AITool>(toolNames.Count);
        foreach (var name in toolNames)
        {
            AITool tool = name switch
            {
                "CodeInterpreter" => new HostedCodeInterpreterTool(),
                _ => throw new NotSupportedException($"Hosted tool '{name}' is not supported")
            };
            result.Add(tool);
        }
        return result;
    }
}
```

> Если в `Microsoft.Agents.AI` 1.3.0 имя класса отличается (`HostedCodeInterpreterTool` или вариант) — заменить на актуальный, остальные тесты не затронуты.

- [ ] **Step 4: Run — pass**

Run: `dotnet test --filter "FullyQualifiedName~HostedToolFactoryTests"`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Interfaces/IHostedToolFactory.cs src/Services/HostedToolFactory.cs tests/AiAgetnsWorkflow.Tests/Tools/HostedToolFactoryTests.cs
git commit -m "feat(tools): HostedToolFactory + IHostedToolFactory"
```

---

### Task 7: IAgentPlugin + AgentPluginRegistry (TDD)

**Files:**
- Create: `src/Interfaces/IAgentPlugin.cs`
- Create: `src/Interfaces/IAgentPluginRegistry.cs`
- Create: `src/Services/AgentPluginRegistry.cs`
- Create: `tests/AiAgetnsWorkflow.Tests/Fakes/FakeAgentPlugin.cs`
- Create: `tests/AiAgetnsWorkflow.Tests/Plugins/AgentPluginRegistryTests.cs`

- [ ] **Step 1: Fake plugin для тестов**

`tests/AiAgetnsWorkflow.Tests/Fakes/FakeAgentPlugin.cs`:
```csharp
using MagenticWorkflowApp.Interfaces;
using Microsoft.Extensions.AI;

namespace AiAgetnsWorkflow.Tests.Fakes;

public sealed class FakeAgentPlugin(string name, params AITool[] tools) : IAgentPlugin
{
    public string Name { get; } = name;
    public IEnumerable<AITool> AsAITools() => tools;
}
```

- [ ] **Step 2: Failing tests**

`tests/AiAgetnsWorkflow.Tests/Plugins/AgentPluginRegistryTests.cs`:
```csharp
using AiAgetnsWorkflow.Tests.Fakes;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Services;

namespace AiAgetnsWorkflow.Tests.Plugins;

public class AgentPluginRegistryTests
{
    [Fact]
    public void TryGet_KnownName_ReturnsTrueAndPlugin()
    {
        IAgentPlugin a = new FakeAgentPlugin("A");
        var reg = new AgentPluginRegistry(new[] { a });
        reg.TryGet("A", out var found).Should().BeTrue();
        found.Should().BeSameAs(a);
    }

    [Fact]
    public void TryGet_UnknownName_ReturnsFalse()
    {
        var reg = new AgentPluginRegistry(Array.Empty<IAgentPlugin>());
        reg.TryGet("missing", out var found).Should().BeFalse();
        found.Should().BeNull();
    }

    [Fact]
    public void RegisteredNames_ReturnsAll()
    {
        var reg = new AgentPluginRegistry(new IAgentPlugin[] { new FakeAgentPlugin("A"), new FakeAgentPlugin("B") });
        reg.RegisteredNames.Should().BeEquivalentTo(new[] { "A", "B" });
    }

    [Fact]
    public void Constructor_DuplicateName_Throws()
    {
        var act = () => new AgentPluginRegistry(new IAgentPlugin[] { new FakeAgentPlugin("A"), new FakeAgentPlugin("A") });
        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate*A*");
    }
}
```

- [ ] **Step 3: Run — fail**

Run: `dotnet test --filter "FullyQualifiedName~AgentPluginRegistryTests"`
Expected: компиляция падает.

- [ ] **Step 4: Interfaces + implementation**

`src/Interfaces/IAgentPlugin.cs`:
```csharp
using Microsoft.Extensions.AI;

namespace MagenticWorkflowApp.Interfaces;

public interface IAgentPlugin
{
    string Name { get; }
    IEnumerable<AITool> AsAITools();
}
```

`src/Interfaces/IAgentPluginRegistry.cs`:
```csharp
namespace MagenticWorkflowApp.Interfaces;

public interface IAgentPluginRegistry
{
    bool TryGet(string name, out IAgentPlugin? plugin);
    IEnumerable<string> RegisteredNames { get; }
}
```

`src/Services/AgentPluginRegistry.cs`:
```csharp
using MagenticWorkflowApp.Interfaces;

namespace MagenticWorkflowApp.Services;

public sealed class AgentPluginRegistry : IAgentPluginRegistry
{
    private readonly Dictionary<string, IAgentPlugin> _byName;

    public AgentPluginRegistry(IEnumerable<IAgentPlugin> plugins)
    {
        _byName = new Dictionary<string, IAgentPlugin>(StringComparer.Ordinal);
        foreach (var p in plugins)
        {
            if (!_byName.TryAdd(p.Name, p))
                throw new InvalidOperationException($"Found duplicate plugin name '{p.Name}'");
        }
    }

    public bool TryGet(string name, out IAgentPlugin? plugin)
    {
        if (_byName.TryGetValue(name, out var found))
        {
            plugin = found;
            return true;
        }
        plugin = null;
        return false;
    }

    public IEnumerable<string> RegisteredNames => _byName.Keys;
}
```

- [ ] **Step 5: Run — pass**

Run: `dotnet test --filter "FullyQualifiedName~AgentPluginRegistryTests"`
Expected: 4 passed.

- [ ] **Step 6: Commit**

```bash
git add src/Interfaces/IAgentPlugin.cs src/Interfaces/IAgentPluginRegistry.cs src/Services/AgentPluginRegistry.cs tests/AiAgetnsWorkflow.Tests/Fakes/FakeAgentPlugin.cs tests/AiAgetnsWorkflow.Tests/Plugins/AgentPluginRegistryTests.cs
git commit -m "feat(plugins): IAgentPlugin + AgentPluginRegistry"
```

---

### Task 8: MCP-исключения

**Files:**
- Create: `src/Exceptions/McpServerStartupException.cs`
- Create: `src/Exceptions/McpServerCommunicationException.cs`

- [ ] **Step 1: Startup exception**

`src/Exceptions/McpServerStartupException.cs`:
```csharp
namespace MagenticWorkflowApp.Exceptions;

public sealed class McpServerStartupException : Exception
{
    public string ServerName { get; }

    public McpServerStartupException(string serverName, string message, Exception? inner = null)
        : base($"MCP server '{serverName}' failed to start: {message}", inner)
    {
        ServerName = serverName;
    }
}
```

- [ ] **Step 2: Communication exception**

`src/Exceptions/McpServerCommunicationException.cs`:
```csharp
namespace MagenticWorkflowApp.Exceptions;

public sealed class McpServerCommunicationException : Exception
{
    public string ServerName { get; }

    public McpServerCommunicationException(string serverName, string message, Exception? inner = null)
        : base($"MCP server '{serverName}' communication failure: {message}", inner)
    {
        ServerName = serverName;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: успех.

- [ ] **Step 4: Commit**

```bash
git add src/Exceptions/McpServerStartupException.cs src/Exceptions/McpServerCommunicationException.cs
git commit -m "feat(mcp): добавить исключения старта и связи MCP-сервера"
```

---

### Task 9: McpClientPool unit-уровня (TDD с моками транспорта)

**Files:**
- Create: `src/Interfaces/IMcpClientPool.cs`
- Create: `src/Services/McpClientPool.cs`
- Create: `tests/AiAgetnsWorkflow.Tests/Mcp/McpClientPoolTests.cs`

`McpClientPool` имеет внутреннюю seam: factory-делегат `Func<McpServerConfiguration, CancellationToken, Task<IMcpClient>>` — позволяет подменить реальное создание клиента в тестах. В production используется default-factory, опирающаяся на `ModelContextProtocol`.

- [ ] **Step 1: Interface**

`src/Interfaces/IMcpClientPool.cs`:
```csharp
using MagenticWorkflowApp.Models;
using Microsoft.Extensions.AI;

namespace MagenticWorkflowApp.Interfaces;

public interface IMcpClientPool : IAsyncDisposable
{
    Task RegisterServersAsync(IReadOnlyList<McpServerConfiguration> servers, CancellationToken ct = default);
    Task<IReadOnlyList<AITool>> GetToolsAsync(IReadOnlyList<string> serverNames, CancellationToken ct = default);
}
```

- [ ] **Step 2: Failing tests**

`tests/AiAgetnsWorkflow.Tests/Mcp/McpClientPoolTests.cs`:
```csharp
using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace AiAgetnsWorkflow.Tests.Mcp;

public class McpClientPoolTests
{
    private static McpServerConfiguration Cfg(string name) => new()
    {
        Name = name, Transport = "stdio", Command = "noop"
    };

    [Fact]
    public async Task Register_DoesNotStartClients()
    {
        var calls = 0;
        var pool = new McpClientPool(NullLogger<McpClientPool>.Instance,
            (_, _) => { calls++; return Task.FromResult(StubClient(Array.Empty<string>())); });

        await pool.RegisterServersAsync(new[] { Cfg("A") });
        calls.Should().Be(0);
    }

    [Fact]
    public async Task GetTools_FirstAccess_StartsClientOnce()
    {
        var calls = 0;
        var pool = new McpClientPool(NullLogger<McpClientPool>.Instance,
            (_, _) => { calls++; return Task.FromResult(StubClient(new[] { "echo" })); });

        await pool.RegisterServersAsync(new[] { Cfg("A") });
        await pool.GetToolsAsync(new[] { "A" });
        await pool.GetToolsAsync(new[] { "A" });
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetTools_UnknownName_Throws()
    {
        var pool = new McpClientPool(NullLogger<McpClientPool>.Instance,
            (_, _) => Task.FromResult(StubClient(Array.Empty<string>())));
        await pool.RegisterServersAsync(new[] { Cfg("A") });

        var act = () => pool.GetToolsAsync(new[] { "B" });
        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*B*");
    }

    [Fact]
    public async Task Dispose_DisposesAllStartedClients()
    {
        var disposed = false;
        var pool = new McpClientPool(NullLogger<McpClientPool>.Instance,
            (_, _) => Task.FromResult(StubClient(Array.Empty<string>(), () => disposed = true)));

        await pool.RegisterServersAsync(new[] { Cfg("A") });
        await pool.GetToolsAsync(new[] { "A" });
        await pool.DisposeAsync();
        disposed.Should().BeTrue();
    }

    private static IMcpClient StubClient(IReadOnlyList<string> toolNames, Action? onDispose = null)
        => new StubMcpClient(toolNames, onDispose);
}

internal sealed class StubMcpClient(IReadOnlyList<string> toolNames, Action? onDispose) : IMcpClient
{
    public ValueTask DisposeAsync() { onDispose?.Invoke(); return ValueTask.CompletedTask; }
    // ...нужные члены IMcpClient заглушены через NotImplementedException, кроме ListToolsAsync
    // (точную сигнатуру и заглушки уточнить под версию ModelContextProtocol 1.2.0 — namespace `ModelContextProtocol.Client`).
}
```

> Реальные сигнатуры `IMcpClient` и `McpClientTool` сверяются на месте; стаб реализует только `ListToolsAsync` и `DisposeAsync`. Остальные члены — `=> throw new NotImplementedException();`.

- [ ] **Step 3: Run — fail**

Run: `dotnet test --filter "FullyQualifiedName~McpClientPoolTests"`
Expected: компиляция падает.

- [ ] **Step 4: Implementation**

`src/Services/McpClientPool.cs`:
```csharp
using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace MagenticWorkflowApp.Services;

public sealed class McpClientPool : IMcpClientPool
{
    private readonly ILogger<McpClientPool> _logger;
    private readonly Func<McpServerConfiguration, CancellationToken, Task<IMcpClient>> _clientFactory;
    private readonly Dictionary<string, McpServerConfiguration> _configs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IMcpClient> _clients = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _disposed;

    public McpClientPool(
        ILogger<McpClientPool> logger,
        Func<McpServerConfiguration, CancellationToken, Task<IMcpClient>>? clientFactory = null)
    {
        _logger = logger;
        _clientFactory = clientFactory ?? DefaultClientFactory;
    }

    public Task RegisterServersAsync(IReadOnlyList<McpServerConfiguration> servers, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _configs.Clear();
        foreach (var s in servers) _configs[s.Name] = s;
        _logger.LogInformation("Registering {Count} MCP servers", servers.Count);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<AITool>> GetToolsAsync(IReadOnlyList<string> serverNames, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (serverNames.Count == 0) return Array.Empty<AITool>();

        var tools = new List<AITool>();
        foreach (var name in serverNames)
        {
            var client = await GetOrCreateClientAsync(name, ct).ConfigureAwait(false);
            var mcpTools = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
            // McpClientTool : AIFunction : AITool — приводимы к AITool напрямую.
            foreach (var t in mcpTools) tools.Add(t);
        }
        return tools;
    }

    private async Task<IMcpClient> GetOrCreateClientAsync(string name, CancellationToken ct)
    {
        if (_clients.TryGetValue(name, out var existing)) return existing;
        if (!_configs.TryGetValue(name, out var cfg))
            throw new KeyNotFoundException($"MCP server '{name}' is not registered");

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_clients.TryGetValue(name, out existing)) return existing;
            _logger.LogInformation("Starting MCP server {Name} via {Transport}", cfg.Name, cfg.Transport);

            try
            {
                var client = await _clientFactory(cfg, ct).ConfigureAwait(false);
                _clients[name] = client;
                _logger.LogInformation("MCP server {Name} ready", cfg.Name);
                return client;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCP server {Name} startup failed", cfg.Name);
                throw new McpServerStartupException(cfg.Name, ex.Message, ex);
            }
        }
        finally { _initLock.Release(); }
    }

    private static async Task<IMcpClient> DefaultClientFactory(McpServerConfiguration cfg, CancellationToken ct)
    {
        var transport = cfg.Transport.ToLowerInvariant() switch
        {
            "stdio" => (IClientTransport)new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = cfg.Command!,
                Arguments = cfg.Args,
                EnvironmentVariables = cfg.Env.Count > 0 ? cfg.Env : null
            }),
            "http" => new SseClientTransport(new SseClientTransportOptions
            {
                Endpoint = new Uri(cfg.Url!),
                AdditionalHeaders = cfg.Headers.Count > 0 ? cfg.Headers : null
            }),
            _ => throw new InvalidOperationException($"Unknown transport '{cfg.Transport}'")
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(cfg.StartupTimeoutSeconds));
        return await McpClient.CreateAsync(transport, options: null, cancellationToken: timeoutCts.Token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var c in _clients.Values)
        {
            try { await c.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Disposing MCP client failed"); }
        }
        _clients.Clear();
        _initLock.Dispose();
    }
}
```

> Имена опций (`Command`, `Arguments`, `EnvironmentVariables`, `Endpoint`, `AdditionalHeaders`) — соответствуют `ModelContextProtocol` 1.2.0. Если в новой версии API отличается — править здесь точечно.

- [ ] **Step 5: Run — pass**

Run: `dotnet test --filter "FullyQualifiedName~McpClientPoolTests"`
Expected: 4 passed.

- [ ] **Step 6: Commit**

```bash
git add src/Interfaces/IMcpClientPool.cs src/Services/McpClientPool.cs tests/AiAgetnsWorkflow.Tests/Mcp/McpClientPoolTests.cs
git commit -m "feat(mcp): McpClientPool с ленивой инициализацией клиентов"
```

---

### Task 10: FakeMcpServer (stdio) для integration

**Files:**
- Create: `tests/FakeMcpServer/FakeMcpServer.csproj`
- Create: `tests/FakeMcpServer/Program.cs`

- [ ] **Step 1: Console-проект**

```bash
dotnet new console -n FakeMcpServer -o tests/FakeMcpServer --framework net10.0
dotnet sln add tests/FakeMcpServer/FakeMcpServer.csproj
dotnet add tests/FakeMcpServer package ModelContextProtocol --version 1.2.0
```

- [ ] **Step 2: Сервер с двумя tools**

`tests/FakeMcpServer/Program.cs`:
```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

await new McpServerBuilder()
    .WithStdioServerTransport()
    .WithTools<EchoTools>()
    .RunAsync();

[McpServerToolType]
public static class EchoTools
{
    [McpServerTool, Description("Echoes the provided message back.")]
    public static string Echo([Description("Message to echo.")] string message) => message;

    [McpServerTool, Description("Adds two integers.")]
    public static int Add(int a, int b) => a + b;
}
```

> Если builder-API отличается в 1.2.0 — использовать вариант через `Microsoft.Extensions.Hosting`-host (см. документацию SDK). Цель — две tools `echo`, `add`.

- [ ] **Step 3: Тестовый проект ссылается на FakeMcpServer как build-зависимость**

В `tests/AiAgetnsWorkflow.Tests/AiAgetnsWorkflow.Tests.csproj` добавить:
```xml
<ItemGroup>
  <ProjectReference Include="..\FakeMcpServer\FakeMcpServer.csproj">
    <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
    <OutputItemType>Content</OutputItemType>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </ProjectReference>
</ItemGroup>
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: успех; в `tests/AiAgetnsWorkflow.Tests/bin/Debug/net10.0` лежит `FakeMcpServer.dll`.

- [ ] **Step 5: Commit**

```bash
git add tests/FakeMcpServer tests/AiAgetnsWorkflow.Tests/AiAgetnsWorkflow.Tests.csproj AiAgetnsWorkflow.sln
git commit -m "test: FakeMcpServer для integration-тестов"
```

---

### Task 11: Integration-тест McpClientPool через FakeMcpServer (stdio)

**Files:**
- Create: `tests/AiAgetnsWorkflow.Tests/Integration/McpClientPoolStdioTests.cs`

- [ ] **Step 1: Failing test**

`tests/AiAgetnsWorkflow.Tests/Integration/McpClientPoolStdioTests.cs`:
```csharp
using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgetnsWorkflow.Tests.Integration;

[Trait("Category", "Integration")]
public class McpClientPoolStdioTests
{
    private static McpServerConfiguration FakeServer() => new()
    {
        Name = "fake",
        Transport = "stdio",
        Command = "dotnet",
        Args = new() { Path.Combine(AppContext.BaseDirectory, "FakeMcpServer.dll") },
        StartupTimeoutSeconds = 30
    };

    [Fact]
    public async Task GetTools_StartsServerAndExposesEchoAndAdd()
    {
        await using var pool = new McpClientPool(NullLogger<McpClientPool>.Instance);
        await pool.RegisterServersAsync(new[] { FakeServer() });
        var tools = await pool.GetToolsAsync(new[] { "fake" });

        tools.Should().HaveCount(2);
        tools.Select(t => t.Name).Should().Contain(new[] { "Echo", "Add" });
    }

    [Fact]
    public async Task EchoTool_InvokeReturnsInput()
    {
        await using var pool = new McpClientPool(NullLogger<McpClientPool>.Instance);
        await pool.RegisterServersAsync(new[] { FakeServer() });
        var tools = await pool.GetToolsAsync(new[] { "fake" });

        var echo = (AIFunction)tools.First(t => t.Name == "Echo");
        var result = await echo.InvokeAsync(new() { ["message"] = "hi" });
        result.ToString().Should().Contain("hi");
    }
}
```

- [ ] **Step 2: Run — pass**

Run: `dotnet test --filter "Category=Integration"`
Expected: 2 passed (если SDK actual signatures совпали — иначе сначала корректируем `McpClientPool.DefaultClientFactory`).

- [ ] **Step 3: Commit**

```bash
git add tests/AiAgetnsWorkflow.Tests/Integration/McpClientPoolStdioTests.cs
git commit -m "test(mcp): integration-тест stdio через FakeMcpServer"
```

---

## Phase 3 — Orchestrator real implementation

### Task 12: Orchestrator constructor + CreateAgentsFromConfigurationAsync

**Files:**
- Modify: `src/Services/MagenticWorkflowOrchestrator.cs`
- Create: `tests/AiAgetnsWorkflow.Tests/Fakes/FakeChatClient.cs`
- Create: `tests/AiAgetnsWorkflow.Tests/Integration/OrchestratorWiringTests.cs`

- [ ] **Step 1: FakeChatClient — обходит реальный LLM**

`tests/AiAgetnsWorkflow.Tests/Fakes/FakeChatClient.cs`:
```csharp
using Microsoft.Extensions.AI;

namespace AiAgetnsWorkflow.Tests.Fakes;

public sealed class FakeChatClient : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new("fake");
    public IList<ChatMessage>? LastMessages { get; private set; }
    public ChatOptions? LastOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(IList<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        LastMessages = messages;
        LastOptions = options;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IList<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => AsyncEnumerable.Empty<ChatResponseUpdate>();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
```

- [ ] **Step 2: Изменить конструктор оркестратора + добавить новый метод**

В `src/Services/MagenticWorkflowOrchestrator.cs` (модификация):

1. Добавить поля и параметры конструктора:
```csharp
private readonly IMcpClientPool _mcpPool;
private readonly IHostedToolFactory _hostedFactory;
private readonly IAgentPluginRegistry _pluginRegistry;

public MagenticWorkflowOrchestrator(
    ILogger<MagenticWorkflowOrchestrator> logger,
    IWorkflowJsonLoader jsonLoader,
    IWorkflowVisualizer visualizer,
    IConfiguration configuration,
    IMcpClientPool mcpPool,
    IHostedToolFactory hostedFactory,
    IAgentPluginRegistry pluginRegistry)
{
    _logger = logger;
    _jsonLoader = jsonLoader;
    _visualizer = visualizer;
    _configuration = configuration;
    _mcpPool = mcpPool;
    _hostedFactory = hostedFactory;
    _pluginRegistry = pluginRegistry;
}
```

2. Реализация `CreateAgentsFromConfigurationAsync` (новый private-метод). Заменяет полностью закомментированный блок:
```csharp
private async Task<Dictionary<string, AIAgent>> CreateAgentsFromConfigurationAsync(
    WorkflowConfiguration config, string? openAiApiKey, string? azureEndpoint, CancellationToken ct)
{
    var agents = new Dictionary<string, AIAgent>(StringComparer.Ordinal);

    foreach (var agentConfig in config.Agents)
    {
        var hostedTools = _hostedFactory.Create(agentConfig.Tools);
        var mcpTools = await _mcpPool.GetToolsAsync(agentConfig.McpServers, ct).ConfigureAwait(false);
        var pluginTools = ResolvePluginTools(agentConfig);

        var allTools = hostedTools.Concat(mcpTools).Concat(pluginTools).ToArray();

        _logger.LogInformation(
            "Agent {Agent} resolved tools: hosted={H}, mcp={M}, plugins={P}",
            agentConfig.Name, hostedTools.Count, mcpTools.Count, pluginTools.Count);

        var chatClient = BuildChatClient(agentConfig.ModelId, openAiApiKey, azureEndpoint);
        agents[agentConfig.Name] = chatClient.CreateAIAgent(
            name: agentConfig.Name,
            description: agentConfig.Description,
            instructions: agentConfig.Instructions,
            tools: allTools);
    }
    return agents;
}

private IReadOnlyList<AITool> ResolvePluginTools(AgentConfiguration agentConfig)
{
    if (agentConfig.Plugins.Count == 0) return Array.Empty<AITool>();
    var tools = new List<AITool>();
    foreach (var name in agentConfig.Plugins)
    {
        if (!_pluginRegistry.TryGet(name, out var plugin))
            throw new WorkflowValidationException(
                $"Agent '{agentConfig.Name}' references unknown plugin '{name}'");
        tools.AddRange(plugin!.AsAITools());
    }
    return tools;
}

private static IChatClient BuildChatClient(string modelId, string? openAiApiKey, string? azureEndpoint)
{
    if (!string.IsNullOrWhiteSpace(azureEndpoint))
    {
        // Azure OpenAI:
        var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(
            new Uri(azureEndpoint),
            new Azure.AI.OpenAI.ApiKeyCredential(openAiApiKey ?? ""));
        return azureClient.GetChatClient(modelId).AsIChatClient();
    }
    var openAi = new OpenAI.OpenAIClient(openAiApiKey!);
    return openAi.GetChatClient(modelId).AsIChatClient();
}
```

> При расхождении API в installed-версиях — править точечно вызовы `AsIChatClient`/`CreateAIAgent`.

3. В `ExecuteWorkflowFromJsonAsync` после визуализации добавить:
```csharp
await _mcpPool.RegisterServersAsync(config.McpServers, default).ConfigureAwait(false);
ValidatePluginReferences(config);
```

4. Метод `ValidatePluginReferences`:
```csharp
private void ValidatePluginReferences(WorkflowConfiguration config)
{
    foreach (var agent in config.Agents)
        foreach (var name in agent.Plugins)
            if (!_pluginRegistry.TryGet(name, out _))
                throw new WorkflowValidationException(
                    $"Agent '{agent.Name}' references unknown plugin '{name}'");
}
```

- [ ] **Step 3: Smoke-тест wiring**

`tests/AiAgetnsWorkflow.Tests/Integration/OrchestratorWiringTests.cs`:
```csharp
using AiAgetnsWorkflow.Tests.Fakes;
using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgetnsWorkflow.Tests.Integration;

public class OrchestratorWiringTests
{
    [Fact]
    public async Task LoadAndValidate_WithUnknownPlugin_Throws()
    {
        var loader = Substitute.For<IWorkflowJsonLoader>();
        loader.LoadConfigurationAsync(Arg.Any<string>()).Returns(new WorkflowConfiguration
        {
            WorkflowType = "Sequential",
            Task = "demo",
            Agents = new() { new() { Name = "A", Description = "x", Instructions = "x", ModelId = "gpt-4", Plugins = new() { "missing" } } },
            Orchestration = new() { StartAgent = "A" }
        });

        var visualizer = Substitute.For<IWorkflowVisualizer>();
        var pool = Substitute.For<IMcpClientPool>();
        var hosted = Substitute.For<IHostedToolFactory>();
        var registry = new AgentPluginRegistry(Array.Empty<IAgentPlugin>());
        var cfg = new ConfigurationBuilder().Build();

        var sut = new MagenticWorkflowOrchestrator(
            NullLogger<MagenticWorkflowOrchestrator>.Instance,
            loader, visualizer, cfg, pool, hosted, registry);

        var act = () => sut.ExecuteWorkflowFromJsonAsync("any.json");
        await act.Should().ThrowAsync<WorkflowValidationException>().WithMessage("*missing*");
    }
}
```

- [ ] **Step 4: Build + run unit-тестов**

Run: `dotnet test --filter "Category!=Integration"`
Expected: все unit-тесты passed (включая новый wiring-тест).

- [ ] **Step 5: Commit**

```bash
git add src/Services/MagenticWorkflowOrchestrator.cs tests/AiAgetnsWorkflow.Tests/Fakes/FakeChatClient.cs tests/AiAgetnsWorkflow.Tests/Integration/OrchestratorWiringTests.cs
git commit -m "feat(orchestrator): новый конструктор и CreateAgentsFromConfigurationAsync"
```

---

### Task 13: HandleWorkflowEvent + расширения событий

**Files:**
- Modify: `src/Services/MagenticWorkflowOrchestrator.cs`

- [ ] **Step 1: Метод `HandleWorkflowEvent`**

В `MagenticWorkflowOrchestrator` добавить:
```csharp
private void HandleWorkflowEvent(WorkflowEvent evt)
{
    switch (evt)
    {
        case AgentRunUpdateEvent a:
            LogEvent($"AGENT:{a.AgentName ?? "?"}", a.Update?.Text ?? string.Empty, ConsoleColor.Yellow);
            break;
        case FunctionCallEvent f:
            LogEvent($"TOOL:{f.FunctionName}", $"called with {f.Arguments?.Count ?? 0} args", ConsoleColor.Magenta);
            break;
        case FunctionResultEvent r:
            LogEvent($"TOOL:{r.FunctionName}", $"result: {r.Result}", ConsoleColor.DarkGray);
            break;
        case WorkflowErrorEvent e:
            LogEvent("ERROR", e.Exception?.Message ?? "unknown", ConsoleColor.Red);
            break;
        case WorkflowCompletedEvent c:
            ShowFinalResult(c.Result?.ToString() ?? "(no result)");
            break;
        default:
            LogEvent("WORKFLOW", evt.GetType().Name, ConsoleColor.Cyan);
            break;
    }
}
```

> Точные имена типов сверять с `Microsoft.Agents.AI.Workflows` 1.3.0. Если каких-то событий нет — убрать соответствующий case.

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: успех.

- [ ] **Step 3: Commit**

```bash
git add src/Services/MagenticWorkflowOrchestrator.cs
git commit -m "feat(orchestrator): HandleWorkflowEvent с поддержкой Function/Error/Completed"
```

---

### Task 14: Sequential реальное выполнение

**Files:**
- Modify: `src/Services/MagenticWorkflowOrchestrator.cs`

- [ ] **Step 1: Заменить заглушку**

Заменить тело `ExecuteSequentialWorkflowAsync` на:
```csharp
private async Task ExecuteSequentialWorkflowAsync(
    WorkflowConfiguration config, string? openAiApiKey, string? azureEndpoint)
{
    var agents = await CreateAgentsFromConfigurationAsync(config, openAiApiKey, azureEndpoint, default).ConfigureAwait(false);

    var builder = new WorkflowBuilder();
    builder.SetStartExecutor(agents[config.Orchestration!.StartAgent!]);
    foreach (var edge in config.Orchestration.Edges)
        builder.AddEdge(agents[edge.From], agents[edge.To]);

    var workflow = builder.Build();
    await foreach (var evt in workflow.RunStreamAsync(config.Task).ConfigureAwait(false))
        HandleWorkflowEvent(evt);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: успех (методы `WorkflowBuilder`, `RunStreamAsync` — проверить актуальные сигнатуры в 1.3.0; править при расхождении).

- [ ] **Step 3: Commit**

```bash
git add src/Services/MagenticWorkflowOrchestrator.cs
git commit -m "feat(orchestrator): реальное Sequential выполнение через WorkflowBuilder"
```

---

### Task 15: Concurrent реальное выполнение

**Files:**
- Modify: `src/Services/MagenticWorkflowOrchestrator.cs`

- [ ] **Step 1: Заменить заглушку**

```csharp
private async Task ExecuteConcurrentWorkflowAsync(
    WorkflowConfiguration config, string? openAiApiKey, string? azureEndpoint)
{
    var agents = await CreateAgentsFromConfigurationAsync(config, openAiApiKey, azureEndpoint, default).ConfigureAwait(false);
    var participants = config.Orchestration!.Concurrent!.ParticipantAgents
        .Select(n => agents[n]).ToArray();

    var workflow = new ConcurrentBuilder()
        .Participants(participants)
        .Build();

    await foreach (var evt in workflow.RunStreamAsync(config.Task).ConfigureAwait(false))
        HandleWorkflowEvent(evt);
}
```

- [ ] **Step 2: Build + commit**

```bash
dotnet build
git add src/Services/MagenticWorkflowOrchestrator.cs
git commit -m "feat(orchestrator): реальное Concurrent выполнение через ConcurrentBuilder"
```

---

### Task 16: Conditional реальное выполнение (без selection-функций)

**Files:**
- Modify: `src/Services/MagenticWorkflowOrchestrator.cs`

Selection-функции в этой итерации не реализуются (см. Future work спеки). Если в JSON есть `conditionalEdges` с `selectionFunction`, выводим warning и идём только по статическим `edges`.

- [ ] **Step 1: Заменить заглушку**

```csharp
private async Task ExecuteConditionalWorkflowAsync(
    WorkflowConfiguration config, string? openAiApiKey, string? azureEndpoint)
{
    var agents = await CreateAgentsFromConfigurationAsync(config, openAiApiKey, azureEndpoint, default).ConfigureAwait(false);

    var builder = new WorkflowBuilder();
    builder.SetStartExecutor(agents[config.Orchestration!.StartAgent!]);
    foreach (var edge in config.Orchestration.Edges)
        builder.AddEdge(agents[edge.From], agents[edge.To]);

    if (config.Orchestration.ConditionalEdges.Count > 0)
        _logger.LogWarning("Conditional edges present but selection-function support is deferred — статическая часть workflow выполняется как есть.");

    var workflow = builder.Build();
    await foreach (var evt in workflow.RunStreamAsync(config.Task).ConfigureAwait(false))
        HandleWorkflowEvent(evt);
}
```

- [ ] **Step 2: Build + commit**

```bash
dotnet build
git add src/Services/MagenticWorkflowOrchestrator.cs
git commit -m "feat(orchestrator): реальное Conditional (статические edges)"
```

---

### Task 17: Magentic реальное выполнение

**Files:**
- Modify: `src/Services/MagenticWorkflowOrchestrator.cs`

- [ ] **Step 1: Заменить заглушку**

```csharp
private async Task ExecuteMagenticWorkflowAsync(
    WorkflowConfiguration config, string? openAiApiKey, string? azureEndpoint)
{
    var agents = await CreateAgentsFromConfigurationAsync(config, openAiApiKey, azureEndpoint, default).ConfigureAwait(false);
    var managerClient = BuildChatClient(config.Manager!.ModelId, openAiApiKey, azureEndpoint);

    var builder = new MagenticBuilder()
        .Participants(agents.Values.ToArray())
        .WithStandardManager(
            chatClient: managerClient,
            maxRoundCount: config.Manager.MaxRoundCount,
            maxStallCount: config.Manager.MaxStallCount,
            maxResetCount: config.Manager.MaxResetCount);

    if (config.Manager.EnablePlanReview) builder.WithPlanReview();

    var workflow = builder.Build();
    await foreach (var evt in workflow.RunStreamAsync(config.Task).ConfigureAwait(false))
        HandleWorkflowEvent(evt);
}
```

- [ ] **Step 2: Build + commit**

```bash
dotnet build
git add src/Services/MagenticWorkflowOrchestrator.cs
git commit -m "feat(orchestrator): реальное Magentic выполнение через MagenticBuilder"
```

---

## Phase 4 — Wiring + samples

### Task 18: Program.cs DI + Ctrl+C + IAsyncDisposable lifecycle

**Files:**
- Modify: `src/Program.cs`

- [ ] **Step 1: Регистрация новых сервисов и graceful shutdown**

Полная замена `Program.cs`:
```csharp
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== Microsoft Agent Framework - Magentic Workflow ===\n");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        ConfigureServices(services, configuration);
        await using var serviceProvider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            var orchestrator = serviceProvider.GetRequiredService<IWorkflowOrchestrator>();
            var path = args.Length > 0 ? args[0] : "workflow-config.json";
            Console.WriteLine($"Loading workflow configuration from: {path}\n");
            await orchestrator.ExecuteWorkflowFromJsonAsync(path);
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

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(b => { b.AddConsole(); b.SetMinimumLevel(LogLevel.Information); });
        services.AddSingleton(configuration);
        services.AddSingleton<IWorkflowOrchestrator, MagenticWorkflowOrchestrator>();
        services.AddSingleton<IWorkflowJsonLoader, WorkflowJsonLoader>();
        services.AddSingleton<IWorkflowVisualizer, WorkflowVisualizer>();

        services.AddSingleton<IMcpClientPool, McpClientPool>();
        services.AddSingleton<IHostedToolFactory, HostedToolFactory>();
        services.AddSingleton<IAgentPluginRegistry, AgentPluginRegistry>();

        // Plugins пользователя:
        services.AddSingleton<IAgentPlugin, Plugins.WeatherPlugin>();
        services.AddSingleton<IAgentPlugin, Plugins.TimePlugin>();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/AiAgetnsWorkflow.csproj`
Expected: ошибка — `Plugins.WeatherPlugin`/`TimePlugin` ещё не существуют. Нормально: они в следующей задаче.

- [ ] **Step 3: Временно закомментировать регистрации plugins до Task 19**

В `ConfigureServices` закомментировать `AddSingleton<IAgentPlugin, ...>()` строки. Build снова — должен пройти.

- [ ] **Step 4: Commit**

```bash
git add src/Program.cs
git commit -m "feat(program): регистрация MCP/plugins сервисов и graceful shutdown"
```

---

### Task 19: Примеры плагинов WeatherPlugin + TimePlugin

**Files:**
- Create: `src/Plugins/WeatherPlugin.cs`
- Create: `src/Plugins/TimePlugin.cs`
- Modify: `src/Program.cs` (раскомментировать регистрации)

- [ ] **Step 1: WeatherPlugin**

`src/Plugins/WeatherPlugin.cs`:
```csharp
using System.ComponentModel;
using MagenticWorkflowApp.Interfaces;
using Microsoft.Extensions.AI;

namespace MagenticWorkflowApp.Plugins;

public sealed class WeatherPlugin : IAgentPlugin
{
    public string Name => "WeatherPlugin";

    public IEnumerable<AITool> AsAITools()
    {
        yield return AIFunctionFactory.Create(GetWeather);
    }

    [Description("Returns current weather for a city.")]
    public string GetWeather(
        [Description("City name.")] string city)
        => $"It is sunny and 22°C in {city} (stub).";
}
```

- [ ] **Step 2: TimePlugin**

`src/Plugins/TimePlugin.cs`:
```csharp
using System.ComponentModel;
using MagenticWorkflowApp.Interfaces;
using Microsoft.Extensions.AI;

namespace MagenticWorkflowApp.Plugins;

public sealed class TimePlugin : IAgentPlugin
{
    public string Name => "TimePlugin";

    public IEnumerable<AITool> AsAITools()
    {
        yield return AIFunctionFactory.Create(GetCurrentTime);
    }

    [Description("Returns current UTC time.")]
    public string GetCurrentTime()
        => DateTime.UtcNow.ToString("O");
}
```

- [ ] **Step 3: Раскомментировать регистрации в Program.cs**

В `ConfigureServices`:
```csharp
services.AddSingleton<IAgentPlugin, Plugins.WeatherPlugin>();
services.AddSingleton<IAgentPlugin, Plugins.TimePlugin>();
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: успех.

- [ ] **Step 5: Commit**

```bash
git add src/Plugins/WeatherPlugin.cs src/Plugins/TimePlugin.cs src/Program.cs
git commit -m "feat(plugins): пример WeatherPlugin и TimePlugin"
```

---

### Task 20: Примеры workflow JSON и smoke-test обратной совместимости

**Files:**
- Create: `src/workflow-with-mcp.json`
- Create: `src/workflow-with-plugins.json`
- Modify: `src/AiAgetnsWorkflow.csproj` (добавить копирование новых JSON в output)

- [ ] **Step 1: workflow-with-plugins.json**

`src/workflow-with-plugins.json`:
```json
{
  "workflowType": "Sequential",
  "task": "Tell me what time it is and the weather in Moscow.",
  "agents": [
    {
      "name": "ClockAgent",
      "description": "Knows current time",
      "instructions": "Use TimePlugin tools.",
      "modelId": "gpt-4",
      "plugins": ["TimePlugin"]
    },
    {
      "name": "WeatherAgent",
      "description": "Reports weather",
      "instructions": "Use WeatherPlugin tools.",
      "modelId": "gpt-4",
      "plugins": ["WeatherPlugin"]
    }
  ],
  "orchestration": {
    "startAgent": "ClockAgent",
    "edges": [{ "from": "ClockAgent", "to": "WeatherAgent" }]
  }
}
```

- [ ] **Step 2: workflow-with-mcp.json**

`src/workflow-with-mcp.json`:
```json
{
  "workflowType": "Sequential",
  "task": "List files in the data directory.",
  "agents": [
    {
      "name": "FsAgent",
      "description": "Reads filesystem",
      "instructions": "Use the filesystem MCP tools to answer.",
      "modelId": "gpt-4",
      "mcpServers": ["filesystem"]
    }
  ],
  "orchestration": {
    "startAgent": "FsAgent",
    "edges": []
  },
  "mcpServers": [
    {
      "name": "filesystem",
      "transport": "stdio",
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "${MCP_FS_ROOT}"]
    }
  ]
}
```

- [ ] **Step 3: Копирование в output**

В `src/AiAgetnsWorkflow.csproj`, существующий `<ItemGroup>` с `<None Update="workflow-*.json">` — добавить два новых:
```xml
<None Update="workflow-with-mcp.json">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
<None Update="workflow-with-plugins.json">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

- [ ] **Step 4: Smoke-test обратной совместимости**

Запустить существующий пример (без OpenAI key — попадает в DEMO branch):

```bash
dotnet run --project src/ workflow-simple.json
```

Expected: вывод не содержит `"📝 Magentic workflow execution would occur here"` (старая заглушка убрана). Должен пойти в DEMO branch (`SimulateWorkflowExecutionAsync`) или в реальный workflow при наличии API key.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test`
Expected: все unit-тесты passed; integration-тесты passed (если установлен `dotnet` runtime для FakeMcpServer).

- [ ] **Step 6: Commit**

```bash
git add src/workflow-with-mcp.json src/workflow-with-plugins.json src/AiAgetnsWorkflow.csproj
git commit -m "feat(samples): workflow-with-mcp.json и workflow-with-plugins.json"
```

---

## Self-Review (выполнено перед финализацией)

**Spec coverage check:**

| Spec section | Реализующая задача |
|---|---|
| §5.1 Корневой `mcpServers` | Tasks 3, 5 |
| §5.2 Поля agent.mcpServers / plugins | Task 3 |
| §5.3 Env-substitution `${VAR}` | Tasks 4, 5 |
| §5.4 Структурная валидация | Task 5 |
| §5.4 Семантическая валидация plugins | Task 12 |
| §6 Models | Task 3 |
| §7.1 IMcpClientPool | Task 9 |
| §7.1 IAgentPlugin/Registry | Task 7 |
| §7.1 IHostedToolFactory | Task 6 |
| §7.2 McpClientPool, AgentPluginRegistry, HostedToolFactory, EnvVarSubstitution | Tasks 4, 6, 7, 9 |
| §7.3 Исключения (Validation, McpStartup, McpComm) | Tasks 1, 8 |
| §8.1 Constructor с тремя новыми зависимостями | Task 12 |
| §8.2 CreateAgentsFromConfigurationAsync | Task 12 |
| §8.2 ExecuteSequential/Concurrent/Conditional/Magentic real | Tasks 14-17 |
| §8.2 BuildChatClient | Task 12 |
| §8.2 HandleWorkflowEvent с FunctionCall/Error | Task 13 |
| §8.3 DI-регистрация + IAsyncDisposable + Ctrl+C | Task 18 |
| §9 Data flow | Tasks 12, 14-18 |
| §10 Error handling | Tasks 1, 5, 8, 9, 12 |
| §11 Зависимость ModelContextProtocol | Task 2 |
| §12 Тестирование (unit + integration + FakeMcpServer) | Tasks 4, 5, 6, 7, 9, 10, 11, 12 |
| §13 Совместимость smoke | Task 20 |

Все пункты spec покрыты. Future work (selection functions, allowlist/denylist, auto-restart, approval-flow) намеренно вне scope — отмечено в Task 16.

**Placeholder scan:** нет TBD/TODO/«similar to»/«implement later». Все блоки кода полные.

**Type consistency:**
- `IMcpClientPool.GetToolsAsync(IReadOnlyList<string>, CancellationToken)` — единая сигнатура в Tasks 9, 12.
- `IAgentPlugin.AsAITools()` — везде `IEnumerable<AITool>` (Task 7, использован в 12, 19).
- `IHostedToolFactory.Create(IReadOnlyList<string>)` — Tasks 6, 12.
- `IAgentPluginRegistry.TryGet(string, out IAgentPlugin?)` — Tasks 7, 12.
- `WorkflowValidationException` — единственный класс по всему плану (Tasks 1, 4, 5, 12).
- `BuildChatClient(modelId, apiKey, azureEndpoint)` — одна сигнатура в Tasks 12, 17.
- Имя файла плагина-примера `WeatherPlugin` совпадает между регистрацией DI (Task 18) и реализацией (Task 19). Аналогично `TimePlugin`.
