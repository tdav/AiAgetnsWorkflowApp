# Microsoft Agent Framework — Magentic Workflow

Консольное приложение для оркестрации AI-агентов через **Microsoft Agent Framework** и **Semantic Kernel** на C# **.NET 10.0**.

> 🎯 **4 типа оркестрации:** Sequential, Concurrent, Conditional, Magentic
> 🔌 **Расширения:** MCP (Model Context Protocol) серверы и C# плагины как инструменты агентов

## 🌟 Что это

Универсальная платформа для создания и выполнения workflow с AI-агентами через **декларативную JSON-конфигурацию**. Агенты могут пользоваться инструментами:

- **MCP-серверы** (stdio/http) — внешние инструменты по протоколу Model Context Protocol
- **C# плагины** (`IAgentPlugin`) — встроенные .NET-функции, доступные агентам через `AIFunctionFactory`
- **Hosted tools** — встроенные инструменты OpenAI (например, `CodeInterpreter`)

## 🎯 Возможности

- ✅ 4 типа оркестрации:
  - **Sequential** — pipeline A→B→C
  - **Concurrent** — fan-out/fan-in
  - **Conditional** — статический DAG (selection-функции на roadmap)
  - **Magentic** — динамическая координация менеджером (Semantic Kernel)
- ✅ MCP-инструменты через `IMcpClientPool` (stdio + http)
- ✅ C# плагины через `IAgentPlugin` + `AgentPluginRegistry`
- ✅ Mermaid-визуализация workflow в консоль
- ✅ Graceful shutdown (Ctrl+C → `CancellationToken`)
- ✅ Демо-режим без API-ключа (для проверки конфигурации)
- ✅ DI через `Microsoft.Extensions.DependencyInjection`
- ✅ User Secrets для безопасного хранения ключей

## 📋 Требования

- .NET 10.0 SDK
- OpenAI API key или Azure OpenAI endpoint (для реального запуска; без ключа работает демо-режим)
- (Опционально) Node.js + npx — если используются NPM-based MCP-серверы

## 🚀 Быстрый старт

```bash
# Восстановление пакетов
dotnet restore src/

# Запуск demo (без API-ключа — пойдёт по DEMO branch)
dotnet run --project src/ workflow-simple.json

# Установка API-ключа (User Secrets, без коммита)
dotnet user-secrets set "OpenAI:ApiKey" "sk-..." --project src/

# Запуск разных типов workflow
dotnet run --project src/ workflow-sequential.json
dotnet run --project src/ workflow-concurrent.json
dotnet run --project src/ workflow-conditional.json
dotnet run --project src/ workflow-config.json          # Magentic
dotnet run --project src/ workflow-with-plugins.json    # с C# плагинами
dotnet run --project src/ workflow-with-mcp.json        # с MCP-сервером
```

## 📁 Структура проекта

```
AiAgetnsWorkflowApp/
├── src/
│   ├── AiAgetnsWorkflow.csproj          # Главный проект (.NET 10)
│   ├── Program.cs                        # Точка входа + DI + Ctrl+C
│   ├── appsettings.json                  # Логирование, OpenAI endpoint
│   │
│   ├── Interfaces/                       # IWorkflowOrchestrator, IMcpClientPool,
│   │                                     # IAgentPlugin, IHostedToolFactory, ...
│   ├── Models/                           # WorkflowConfiguration + AgentConfiguration,
│   │                                     # McpServerConfiguration, ManagerConfiguration, ...
│   ├── Services/
│   │   ├── WorkflowJsonLoader.cs         # JSON → конфигурация + валидация
│   │   ├── WorkflowVisualizer.cs         # Mermaid-диаграммы в консоль
│   │   ├── MagenticWorkflowOrchestrator.cs  # Главный оркестратор (все 4 типа)
│   │   ├── McpClientPool.cs              # Lazy-инициализация MCP-клиентов
│   │   ├── HostedToolFactory.cs          # Создание hosted-инструментов
│   │   ├── AgentPluginRegistry.cs        # Реестр C#-плагинов
│   │   └── EnvVarSubstitution.cs         # ${VAR} в JSON-конфигах
│   ├── Plugins/
│   │   ├── WeatherPlugin.cs              # Пример: GetWeather(city)
│   │   └── TimePlugin.cs                 # Пример: GetCurrentTime()
│   ├── Exceptions/
│   │   ├── WorkflowValidationException.cs
│   │   ├── McpServerStartupException.cs
│   │   └── McpServerCommunicationException.cs
│   │
│   ├── workflow-*.json                   # Примеры конфигураций workflow
│   └── ...
│
├── tests/
│   ├── AiAgetnsWorkflow.Tests/           # xUnit + NSubstitute + FluentAssertions
│   │   ├── Mcp/                          # тесты McpClientPool, EnvVarSubstitution
│   │   ├── Plugins/                      # тесты AgentPluginRegistry
│   │   ├── Json/                         # тесты WorkflowJsonLoader
│   │   ├── Integration/                  # OrchestratorWiringTests
│   │   └── Fakes/                        # FakeChatClient, FakeMcpClient, ...
│   └── FakeMcpServer/                    # Тестовый MCP-сервер для интеграционных тестов
│
└── docs/
    └── superpowers/
        ├── specs/                        # Спецификация дизайна
        └── plans/                        # 20-task implementation plan
```

## 🔧 Команды разработки

```bash
# Сборка
dotnet build src/

# Все тесты
dotnet test tests/AiAgetnsWorkflow.Tests/

# Запуск (workflow-config.json по умолчанию)
dotnet run --project src/

# Запуск с конкретным workflow
dotnet run --project src/ workflow-sequential.json

# Управление API-ключами через User Secrets
dotnet user-secrets set "OpenAI:ApiKey" "ваш_ключ" --project src/
dotnet user-secrets list --project src/

# Восстановление пакетов
dotnet restore src/
```

## 🔌 MCP-серверы (Model Context Protocol)

Подключение внешних инструментов через MCP-протокол. Транспорты: `stdio`, `http`.

### Конфигурация

```json
{
  "workflowType": "Sequential",
  "task": "List files in the data directory.",
  "agents": [
    {
      "name": "FsAgent",
      "instructions": "Use the filesystem MCP tools to answer.",
      "modelId": "gpt-4",
      "mcpServers": ["filesystem"]
    }
  ],
  "orchestration": { "startAgent": "FsAgent", "edges": [] },
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

### Поля `mcpServers[]`

| Поле | Тип | Описание |
|------|-----|----------|
| `name` | string | Уникальное имя (используется в `agents[].mcpServers`) |
| `transport` | string | `"stdio"` или `"http"` |
| `command` | string | (stdio) Исполняемый файл |
| `args` | string[] | (stdio) Аргументы; поддерживается `${ENV_VAR}` |
| `env` | object | (stdio) Переменные окружения процесса |
| `url` | string | (http) URL endpoint |
| `headers` | object | (http) HTTP-заголовки |
| `startupTimeoutSeconds` | int | Таймаут старта (по умолчанию 30) |

### Жизненный цикл

- Регистрация серверов происходит однократно в начале workflow.
- Клиенты создаются **лениво** при первом обращении (`GetToolsAsync`).
- При завершении приложения `IMcpClientPool.DisposeAsync` корректно закрывает все клиенты.

## 🧩 C# плагины

Плагин = класс, реализующий `IAgentPlugin`. Возвращает набор `AITool` через `AIFunctionFactory.Create(method)`.

### Пример

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
    public static string GetWeather([Description("City name.")] string city)
        => $"It is sunny and 22°C in {city} (stub).";
}
```

### Регистрация в DI (Program.cs)

```csharp
services.AddSingleton<IAgentPlugin, Plugins.WeatherPlugin>();
services.AddSingleton<IAgentPlugin, Plugins.TimePlugin>();
services.AddSingleton<IAgentPluginRegistry, AgentPluginRegistry>();
```

DI автоматически собирает все `IAgentPlugin` в `IEnumerable<IAgentPlugin>` и передаёт в `AgentPluginRegistry`.

### Использование в workflow JSON

```json
{
  "agents": [
    {
      "name": "ClockAgent",
      "instructions": "Use TimePlugin tools.",
      "modelId": "gpt-4",
      "plugins": ["TimePlugin"]
    }
  ]
}
```

## 🔀 Типы Workflow Orchestration

### 1. Sequential

Pipeline A→B→C через `edges`. Выход одного агента → вход следующего.

```json
{
  "workflowType": "Sequential",
  "orchestration": {
    "startAgent": "DataCollector",
    "edges": [
      { "from": "DataCollector", "to": "Analyst" },
      { "from": "Analyst", "to": "ReportWriter" }
    ]
  }
}
```

### 2. Concurrent

Все участники выполняются параллельно с одним входом, результаты агрегируются.

```json
{
  "workflowType": "Concurrent",
  "orchestration": {
    "concurrent": {
      "participantAgents": ["Healthcare", "Finance", "Education"],
      "aggregationStrategy": "Collect"
    }
  }
}
```

> **Замечание:** в текущей версии `aggregationStrategy` логируется, но фреймворк применяет агрегацию по умолчанию (`Collect`-эквивалент). Кастомные стратегии — в roadmap.

### 3. Conditional

Статический DAG через `edges`. Selection-функции (`conditionalEdges` с `selectionFunction`) принимаются в JSON, но **не выполняются** в текущей версии — выводится warning, выполняется только статическая часть.

```json
{
  "workflowType": "Conditional",
  "orchestration": {
    "startAgent": "Classifier",
    "edges": [{ "from": "Classifier", "to": "Responder" }],
    "conditionalEdges": [
      {
        "from": "Classifier",
        "toOptions": ["Tech", "Billing", "General"],
        "selectionFunction": "classify_issue_type"
      }
    ]
  }
}
```

### 4. Magentic

Менеджер на базе **Semantic Kernel** (`StandardMagenticManager`) динамически выбирает агентов из списка участников.

```json
{
  "workflowType": "Magentic",
  "manager": {
    "modelId": "gpt-4",
    "maxRoundCount": 10,
    "maxStallCount": 3,
    "maxResetCount": 2
  },
  "agents": [...]
}
```

> **Замечание:** Magentic в текущей итерации использует SemanticKernel `ChatCompletionAgent` без передачи `AITool` в SK Kernel. Tool bridging (M.E.AI → KernelPlugin) — в roadmap. При наличии инструментов в конфиге пишется warning.

## 📊 Формат JSON конфигурации

### Корневые поля

| Поле | Тип | Описание |
|------|-----|----------|
| `workflowType` | string | `"Sequential"`, `"Concurrent"`, `"Conditional"`, `"Magentic"` |
| `task` | string | Описание задачи |
| `manager` | object | Конфигурация менеджера (Magentic) |
| `orchestration` | object | Конфигурация оркестрации (Sequential/Concurrent/Conditional) |
| `agents` | array | Список агентов |
| `mcpServers` | array | Описание MCP-серверов |
| `settings` | object | Дополнительные настройки |

### Конфигурация агента

| Поле | Тип | Описание |
|------|-----|----------|
| `name` | string | Уникальное имя |
| `description` | string | Описание |
| `instructions` | string | Системные инструкции |
| `modelId` | string | Например, `"gpt-4"` |
| `tools` | string[] | Hosted tools (например, `["CodeInterpreter"]`) |
| `mcpServers` | string[] | Имена MCP-серверов из корневого `mcpServers[]` |
| `plugins` | string[] | Имена C#-плагинов из `IAgentPluginRegistry` |
| `metadata` | object | Произвольные метаданные |

## 🔐 Безопасность

```bash
# User Secrets (не коммитятся в Git)
dotnet user-secrets set "OpenAI:ApiKey" "sk-..." --project src/

# Или переменные окружения
# Windows: set OpenAI__ApiKey=ваш_ключ
# Linux/Mac: export OpenAI__ApiKey=ваш_ключ
```

`UserSecretsId` уже зарегистрирован в `src/AiAgetnsWorkflow.csproj`.

## 🐛 Демо-режим

Если ни `OpenAI:ApiKey`, ни `AzureOpenAI:Endpoint` не заданы — приложение запускается в **DEMO branch**: симулирует выполнение, печатает события агентов, не делает реальных вызовов к LLM. Удобно для проверки JSON-конфигурации.

## 🧪 Тесты

```bash
dotnet test tests/AiAgetnsWorkflow.Tests/
```

Покрытие:
- `WorkflowJsonLoader` — валидация JSON, ссылок на агентов/плагины/MCP-серверы
- `McpClientPool` — lazy-инициализация, dispose, idempotency
- `AgentPluginRegistry` — duplicate detection, lookup
- `HostedToolFactory` — создание `CodeInterpreter`
- `EnvVarSubstitution` — подстановка `${VAR}` с указанием отсутствующих
- `Integration/OrchestratorWiringTests` — полный путь от JSON до DEMO-выполнения

## 🔧 Расширенные возможности

### Подстановка переменных окружения в JSON

В строковых значениях `args`, `command`, `url` и т.п. поддерживается синтаксис `${VAR_NAME}`:

```json
"args": ["-y", "@modelcontextprotocol/server-filesystem", "${MCP_FS_ROOT}"]
```

Если `${MCP_FS_ROOT}` не задана — `WorkflowValidationException` с указанием отсутствующих переменных.

### Graceful shutdown

`Ctrl+C` → `CancellationToken` → exit code `130`. Внутри workflow `OperationCanceledException` пробрасывается до `Program.Main`.

## ⚠️ Известные ограничения текущей версии

| Область | Состояние |
|--------|-----------|
| Selection-функции в Conditional | ❌ Не реализовано (только статические `edges`) |
| Tool bridging в Magentic | ❌ Не реализовано (`AITool` → `KernelPlugin`) |
| Кастомные `aggregationStrategy` в Concurrent | ❌ Не реализовано (используется default) |
| Azure OpenAI в Magentic | ❌ Только OpenAI |
| Многократный `RegisterServersAsync` | ❌ Pool single-use per app lifetime |

## 🔗 Полезные ссылки

- [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp)
- [Magentic Orchestration Guide](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/orchestrations/magentic)
- [Semantic Kernel Agents](https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/)
- [Model Context Protocol](https://modelcontextprotocol.io/)
- [OpenAI API](https://platform.openai.com/docs)

## 📄 Лицензия

MIT License
