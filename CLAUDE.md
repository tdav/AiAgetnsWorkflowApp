# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Сборка
dotnet build src/

# Тесты (TUnit 1.58 на Microsoft.Testing.Platform; runner задан в global.json в корне репозитория)
dotnet test

# Запуск (файл по умолчанию — WorkflowSettings:DefaultConfigPath из appsettings.json,
# сейчас workflow-deep-research.json)
dotnet run --project src/

# Запуск с конкретным workflow
dotnet run --project src/ workflow-sequential.json
dotnet run --project src/ workflow-concurrent.json
dotnet run --project src/ workflow-conditional.json
dotnet run --project src/ workflow-simple.json

# Восстановление пакетов
dotnet restore src/

# Управление API-ключами через User Secrets
dotnet user-secrets set "OpenAI:ApiKey" "ваш_ключ" --project src/
```

## Архитектура

**MagenticWorkflowApp** — .NET 10.0 консольное приложение для оркестрации AI-агентов через Microsoft Agent Framework и Semantic Kernel.

### Точка входа

`src/Program.cs` — собирает DI-контейнер, читает `args[0]` (или `workflow-config.json` по умолчанию), вызывает `IWorkflowOrchestrator.ExecuteWorkflowFromJsonAsync()`.

### Слои

```
Interfaces/          — IWorkflowOrchestrator, IWorkflowJsonLoader, IWorkflowVisualizer,
                       IWorkflowExecutor, IChatClientProvider, IAgentFactory, ISelectionFunction
Models/              — WorkflowConfiguration (корень), AgentConfiguration,
                       OrchestrationConfiguration, ManagerConfiguration,
                       EdgeConfiguration, ConditionalEdgeConfiguration, ConcurrentConfiguration,
                       ContextBudgetConfiguration
Services/
  WorkflowJsonLoader.cs          — десериализация JSON + валидация
  WorkflowVisualizer.cs          — генерация Mermaid-диаграмм в консоль
  MagenticWorkflowOrchestrator.cs — тонкий фасад: load → visualize → dispatch к стратегии
  ChatClientProvider.cs          — выбор провайдера (OpenAI/Ollama; Azure → NotSupportedException),
                                   каждый IChatClient оборачивается в TokenTrimmingChatClient
  TokenTrimmingChatClient.cs     — middleware бюджета токенов (contextBudget)
  TokenEstimator.cs              — tiktoken (cl100k_base) или chars-эвристика
  AgentFactory.cs                — IAgentFactory.BuildAgent(config, tools?, nameOverride?, historyWindowOverride?)
  SelectionFunctionRegistry.cs   — резолв ISelectionFunction по имени (case-insensitive)
  KeywordSelectionFunction.cs    — встроенный fallback keywordMatch
  Executors/                     — стратегии IWorkflowExecutor (Name, CanExecute, ExecuteAsync):
    SequentialWorkflowExecutor.cs   — sequential И conditional
    ConcurrentWorkflowExecutor.cs
    MagenticWorkflowExecutor.cs
    DeepResearchWorkflowExecutor.cs
    SimulatedWorkflowExecutor.cs    — демо-режим без учётных данных
    AgentTeamBuilder.cs             — общий сборщик: hosted/MCP/plugin-инструменты + event stream
```

### Поток выполнения

1. `WorkflowJsonLoader` читает и валидирует JSON-файл → `WorkflowConfiguration`
2. `WorkflowVisualizer` рисует Mermaid-диаграмму в консоль
3. `MagenticWorkflowOrchestrator` (фасад): валидация плагинов → регистрация MCP → применение `contextBudget` → диспетчеризация в первый `IWorkflowExecutor`, чей `CanExecute(workflowType)` вернул true:
   - `sequential` / `conditional` → `SequentialWorkflowExecutor`; conditional маршрутизируется динамически через `selectionFunction` (fallback — `keywordMatch`)
   - `concurrent` → fan-out/fan-in через `concurrent.participantAgents`
   - `magentic` → менеджер SK координирует агентов; MCP/plugin-инструменты бриджатся в Kernel (`AsKernelFunction()`)
   - `deepResearch` → итеративный цикл Researcher → Critic → Synthesizer
   - без учётных данных → `SimulatedWorkflowExecutor`

Новый тип workflow = новая DI-регистрация `IWorkflowExecutor`, без правки switch.

### Конфигурация

- `appsettings.json` — логирование, OpenAI/Ollama endpoint, `WorkflowSettings:DefaultConfigPath`, fallback-секция `ContextBudget`
- `workflow-*.json` — декларативные описания workflow (копируются в output); опциональная секция `contextBudget` в корне
- User Secrets — хранение `OpenAI:ApiKey` без коммита в Git (UserSecretsId в csproj)

## Соглашения

**Именование:**
- Приватные поля: `_camelCase`
- Async-методы: суффикс `Async`
- `ConfigureAwait(false)` во всех `await` внутри сервисов

**Логирование:**
```csharp
// Структурированные параметры, НЕ string interpolation
_logger.LogInformation("Executing {WorkflowType} with {Count} agents", config.WorkflowType, config.Agents.Count);
```

**Запрещено:**
- `async void` (кроме event handlers)
- Пустые `catch { }`
- Хардкод API-ключей
- `.Wait()` / `.Result` на async-методах
- `async void`

**DI:** только конструктор-инъекция; регистрация через `AddSingleton<I, Impl>()` в `Program.ConfigureServices`.

**Обработка ошибок:** специфичные исключения первыми (`FileNotFoundException`, `JsonException`), затем `Exception`; использовать кастомные исключения (`WorkflowValidationException`).
