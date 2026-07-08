# Microsoft Agent Framework — Magentic Workflow

Консольное приложение для оркестрации AI-агентов через **Microsoft Agent Framework** и **Semantic Kernel** на C# **.NET 10.0**.

> 🎯 **5 типов оркестрации:** Sequential, Concurrent, Conditional, Magentic, DeepResearch
> 🔌 **Расширения:** MCP (Model Context Protocol) серверы и C# плагины как инструменты агентов
> 🧮 **Контроль контекста:** бюджет токенов (`contextBudget`) с обрезкой/суммаризацией истории на каждом вызове модели

## gemma 4 practical guide for developers
https://dev.to/arshtechpro/gemma-4-a-practical-guide-for-developers-2co5

## Модели
https://docs.lm-kit.com/lm-kit-net/guides/model-catalog/model-catalog.html

## 🌟 Что это

Универсальная платформа для создания и выполнения workflow с AI-агентами через **декларативную JSON-конфигурацию**. Агенты могут пользоваться инструментами:

- **MCP-серверы** (stdio/http) — внешние инструменты по протоколу Model Context Protocol
- **C# плагины** (`IAgentPlugin`) — встроенные .NET-функции, доступные агентам через `AIFunctionFactory`
- **Hosted tools** — встроенные инструменты OpenAI (например, `CodeInterpreter`)

## 🎯 Возможности

- ✅ 5 типов оркестрации (каждый — отдельная стратегия `IWorkflowExecutor`):
  - **Sequential** — pipeline A→B→C
  - **Concurrent** — fan-out/fan-in
  - **Conditional** — динамическая маршрутизация через `selectionFunction` (`ISelectionFunction`)
  - **Magentic** — динамическая координация менеджером (Semantic Kernel), с пробросом MCP/plugin-инструментов в Kernel
  - **DeepResearch** — итеративное исследование (Researcher → Critic → Synthesizer)
- ✅ Бюджет токенов `contextBudget`: обрезка tool-результатов, скользящее окно истории, суммаризация вытесненных сообщений (`TokenTrimmingChatClient`)
- ✅ MCP-инструменты через `IMcpClientPool` (stdio + http)
- ✅ C# плагины через `IAgentPlugin` + `AgentPluginRegistry`
- ✅ Провайдеры моделей через `IChatClientProvider`: OpenAI и Ollama (OpenAI-совместимый endpoint)
- ✅ Mermaid-визуализация workflow в консоль
- ✅ Graceful shutdown (Ctrl+C → `CancellationToken`)
- ✅ Демо-режим без API-ключа (для проверки конфигурации)
- ✅ DI через `Microsoft.Extensions.DependencyInjection`
- ✅ User Secrets для безопасного хранения ключей

## 📋 Требования

- .NET 10.0 SDK
- OpenAI API key или Ollama endpoint (для реального запуска; без учётных данных работает демо-режим; Azure OpenAI не поддерживается)
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
dotnet run --project src/ workflow-mixed.json           # комбо MCP + plugin + hosted tool
```

## 📁 Структура проекта

```
AiAgetnsWorkflowApp/
├── src/
│   ├── AiAgetnsWorkflow.csproj          # Главный проект (.NET 10)
│   ├── Program.cs                        # Точка входа + DI + Ctrl+C
│   ├── appsettings.json                  # Логирование, OpenAI endpoint
│   │
│   ├── Interfaces/                       # IWorkflowOrchestrator, IWorkflowExecutor,
│   │                                     # IChatClientProvider, IAgentFactory,
│   │                                     # ISelectionFunction, IMcpClientPool, IAgentPlugin, ...
│   ├── Models/                           # WorkflowConfiguration + AgentConfiguration,
│   │                                     # McpServerConfiguration, ManagerConfiguration, ...
│   ├── Services/
│   │   ├── WorkflowJsonLoader.cs         # JSON → конфигурация + валидация
│   │   ├── WorkflowVisualizer.cs         # Mermaid-диаграммы в консоль
│   │   ├── MagenticWorkflowOrchestrator.cs  # Тонкий фасад: load → visualize → dispatch
│   │   ├── Executors/                    # Стратегии IWorkflowExecutor
│   │   │   ├── SequentialWorkflowExecutor.cs   # sequential + conditional
│   │   │   ├── ConcurrentWorkflowExecutor.cs
│   │   │   ├── MagenticWorkflowExecutor.cs
│   │   │   ├── DeepResearchWorkflowExecutor.cs
│   │   │   ├── SimulatedWorkflowExecutor.cs    # демо-режим без ключей
│   │   │   └── AgentTeamBuilder.cs             # общий сборщик агентов/инструментов
│   │   ├── ChatClientProvider.cs         # Выбор провайдера (OpenAI/Ollama) + TokenTrimming-обёртка
│   │   ├── TokenTrimmingChatClient.cs    # Middleware бюджета токенов
│   │   ├── TokenEstimator.cs             # tiktoken (cl100k_base) или chars-эвристика
│   │   ├── SelectionFunctionRegistry.cs  # Резолв selectionFunction по имени
│   │   ├── KeywordSelectionFunction.cs   # Встроенная keywordMatch-маршрутизация
│   │   ├── AgentFactory.cs               # IAgentFactory.BuildAgent(config, tools, overrides)
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
│   ├── AiAgetnsWorkflow.Tests/           # TUnit + NSubstitute + FluentAssertions
│   │   ├── Mcp/                          # тесты McpClientPool, EnvVarSubstitution
│   │   ├── Plugins/                      # тесты AgentPluginRegistry
│   │   ├── Json/                         # тесты WorkflowJsonLoader
│   │   ├── Integration/                  # OrchestratorWiringTests
│   │   └── Fakes/                        # FakeChatClient, FakeMcpClient, ...
│   ├── MagenticWorkflowApp.Tests/        # TUnit-тесты (executors, token trimming, selection functions)
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

# Все тесты (TUnit на Microsoft.Testing.Platform; runner задан в global.json в корне репозитория)
dotnet test

# Запуск (файл по умолчанию — из WorkflowSettings:DefaultConfigPath в appsettings.json,
# сейчас workflow-deep-research.json)
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

> Поддерживаемые значения `aggregationStrategy`:
> - `Collect` (по умолчанию) — стандартная агрегация SDK: последние сообщения всех участников;
> - `Merge` — ответы всех участников склеиваются в одно сообщение с метками `ИмяАгента: текст`;
> - `Vote` — побеждает самый частый ответ (сравнение текста без учёта регистра/пробелов, при ничьей — порядок участников).
>
> Неизвестное значение → `WorkflowValidationException`.

### 3. Conditional

Динамическая маршрутизация через `conditionalEdges`: выход агента-источника передаётся в selection-функцию, которая выбирает следующего агента из `toOptions`.

```json
{
  "workflowType": "Conditional",
  "orchestration": {
    "startAgent": "Classifier",
    "conditionalEdges": [
      {
        "from": "Classifier",
        "toOptions": ["TechnicalSupportAgent", "BillingSupportAgent", "GeneralInquiryAgent"],
        "selectionFunction": "classify_ticket_type"
      }
    ],
    "edges": [
      { "from": "TechnicalSupportAgent", "to": "ResponseGeneratorAgent" },
      { "from": "BillingSupportAgent", "to": "ResponseGeneratorAgent" },
      { "from": "GeneralInquiryAgent", "to": "ResponseGeneratorAgent" }
    ]
  }
}
```

Как резолвится `selectionFunction`:

- Реализации `ISelectionFunction` регистрируются в DI и находятся по имени (без учёта регистра) через `SelectionFunctionRegistry`.
- Неизвестное имя → fallback на встроенную `keywordMatch` (`KeywordSelectionFunction`): либо явная карта через параметр ребра `"keywords"` (`{keyword: targetAgent}`), либо ключевое слово выводится из имени каждого целевого агента (первый CamelCase-токен: `TechnicalSupportAgent` → `"technical"`).
- Нет совпадения → первый агент из `toOptions`.

> **Важно:** граф должен сходиться к одному терминальному агенту (в примере все ветки ведут в `ResponseGeneratorAgent`).

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

> **Замечание:** MCP- и plugin-инструменты (`AIFunction`) пробрасываются в Kernel каждого SK-агента через `AsKernelFunction()` как плагин `AgentTools` с `FunctionChoiceBehavior.Auto`. Hosted tools (`CodeInterpreter`) в SK не пробрасываются — пишется warning. SK/magentic-путь не проходит через токен-тримминг (ограничен `maxRoundCount` менеджера).
>
> ⚠️ **Требование к модели менеджера:** `StandardMagenticManager` использует `response_format: json_object` — модель менеджера обязана надёжно возвращать структурированный JSON. Это работает с OpenAI, но **не** с рядом Ollama-моделей (напр. `gpt-oss:120b-cloud` возвращает markdown → `JsonException` в `EvaluateTaskProgressAsync` → менеджер исчерпывает `maxResetCount` и завершается без результата). Проброс инструментов при этом работает корректно (в логах `bridged N tool(s) into SemanticKernel`) — блокирует именно управляющий цикл менеджера. Для Magentic на Ollama выбирайте модель с надёжной поддержкой JSON-формата; проброс инструментов проверен end-to-end на Sequential/Conditional-путях.

## 📊 Формат JSON конфигурации

### Корневые поля

| Поле | Тип | Описание |
|------|-----|----------|
| `workflowType` | string | `"Sequential"`, `"Concurrent"`, `"Conditional"`, `"Magentic"`, `"DeepResearch"` |
| `task` | string | Описание задачи |
| `manager` | object | Конфигурация менеджера (Magentic) |
| `orchestration` | object | Конфигурация оркестрации (Sequential/Concurrent/Conditional) |
| `agents` | array | Список агентов |
| `mcpServers` | array | Описание MCP-серверов |
| `contextBudget` | object | Бюджет токенов (см. раздел «Оптимизация токенов»); опционально |
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
| `maxOutputTokens` | int? | Лимит выходных токенов для агента (опционально) |
| `temperature` | float? | Температура модели для агента (опционально) |
| `metadata` | object | Произвольные метаданные |

## 🧮 Оптимизация токенов (`contextBudget`)

Опциональная секция `contextBudget` в корне workflow-JSON (fallback — секция `ContextBudget` в `appsettings.json`). Каждый `IChatClient` оборачивается в middleware `TokenTrimmingChatClient`, который на **каждом** вызове модели:

1. **Обрезает** слишком большие tool-результаты до `maxToolResultTokens` (маркер `…[truncated ~N tokens]`).
2. **Ограничивает историю** скользящим окном: system prompt + первое сообщение задачи + свежий хвост; пары tool call/tool result никогда не разрываются.
3. **Вытесненную середину** либо суммаризирует отдельным LLM-вызовом (результат кэшируется), либо просто отбрасывает (`strategy: "truncate"`).

```json
{
  "workflowType": "Sequential",
  "contextBudget": {
    "maxInputTokens": 32000,
    "maxToolResultTokens": 4000,
    "historyWindowMessages": 40,
    "tokenizer": "tiktoken",
    "strategy": "summarize",
    "summaryMaxTokens": 1024
  }
}
```

### Поля `contextBudget`

| Поле | Тип | По умолчанию | Описание |
|------|-----|--------------|----------|
| `maxInputTokens` | int | `32000` | Бюджет входных токенов на вызов модели |
| `maxToolResultTokens` | int | `4000` | Максимум токенов на один tool-результат |
| `historyWindowMessages` | int | `40` | Размер скользящего окна истории (сообщений) |
| `tokenizer` | string | `"tiktoken"` | `"tiktoken"` (cl100k_base, Microsoft.ML.Tokenizers) или `"chars"` (эвристика chars/`charsPerToken`) |
| `charsPerToken` | double | `4.0` | Делитель для `"chars"`-эвристики |
| `strategy` | string | `"summarize"` | `"summarize"` (вытесненное суммаризируется LLM) или `"truncate"` (отбрасывается) |
| `summaryMaxTokens` | int | `1024` | Лимит токенов на суммаризацию |

Дополнительно в DeepResearch:

- Critic получает только находки текущей итерации + однострочный дайджест старых.
- Вход Synthesizer ограничен ~60% от `maxInputTokens`; невместившиеся находки включаются дайджестом.
- `chatReducerWindow` реально применяется к сессии каждой роли.

> **Ограничение:** SK/magentic-путь через токен-тримминг не проходит (ограничен `maxRoundCount` менеджера).

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

Если учётные данные провайдера не заданы — выполнение уходит в `SimulatedWorkflowExecutor`: симулирует выполнение, печатает события агентов, не делает реальных вызовов к LLM. Удобно для проверки JSON-конфигурации.

## 🧪 Тесты

Оба тестовых проекта — на **TUnit 1.58** поверх Microsoft.Testing.Platform (миграция с xUnit); FluentAssertions и NSubstitute сохранены. `dotnet test` работает через `global.json` в корне репозитория (`{"test":{"runner":"Microsoft.Testing.Platform"}}`) и `dotnet.config`. Всего 92 + 27 тестов.

```bash
dotnet test
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
| Selection-функции в Conditional | ✅ Реализовано (`ISelectionFunction` + fallback `keywordMatch`) |
| Tool bridging в Magentic | ✅ Реализовано (`AIFunction` → Kernel через `AsKernelFunction()`; hosted tools — нет) |
| Кастомные `aggregationStrategy` в Concurrent | ✅ Реализовано (`Collect` / `Merge` / `Vote`) |
| Azure OpenAI | ❌ Не поддерживается (`NotSupportedException`); только OpenAI и Ollama |
| Токен-тримминг в SK/magentic-пути | ❌ Не применяется (ограничен `maxRoundCount` менеджера) |
| Многократный `RegisterServersAsync` | ❌ Pool single-use per app lifetime |

## 🔗 Полезные ссылки

- [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp)
- [Magentic Orchestration Guide](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/orchestrations/magentic)
- [Semantic Kernel Agents](https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/)
- [Model Context Protocol](https://modelcontextprotocol.io/)
- [OpenAI API](https://platform.openai.com/docs)

## 📄 Лицензия

MIT License
