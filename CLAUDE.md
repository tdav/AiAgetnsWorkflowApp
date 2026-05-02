# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Сборка
dotnet build src/

# Запуск (файл workflow по умолчанию: workflow-config.json)
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
Interfaces/          — IWorkflowOrchestrator, IWorkflowJsonLoader, IWorkflowVisualizer
Models/              — WorkflowConfiguration (корень), AgentConfiguration,
                       OrchestrationConfiguration, ManagerConfiguration,
                       EdgeConfiguration, ConditionalEdgeConfiguration, ConcurrentConfiguration
Services/
  WorkflowJsonLoader.cs          — десериализация JSON + валидация
  WorkflowVisualizer.cs          — генерация Mermaid-диаграмм в консоль
  MagenticWorkflowOrchestrator.cs — главный оркестратор, switch по workflowType
```

### Поток выполнения

1. `WorkflowJsonLoader` читает и валидирует JSON-файл → `WorkflowConfiguration`
2. `WorkflowVisualizer` рисует Mermaid-диаграмму в консоль
3. `MagenticWorkflowOrchestrator` выбирает стратегию по `config.WorkflowType`:
   - `sequential` → pipeline A→B→C через `edges`
   - `concurrent` → fan-out/fan-in через `concurrent.participantAgents`
   - `conditional` → маршрутизация через `conditionalEdges`
   - `magentic` → менеджер динамически координирует агентов

### Конфигурация

- `appsettings.json` — настройки логирования, OpenAI endpoint
- `workflow-*.json` — декларативные описания workflow (копируются в output)
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
