# AgentSession

Консольное приложение на **.NET 10 / C# 14**, демонстрирующее паттерн **SubAgents** (вложенные агенты) с помощью [Microsoft.Agents.AI](https://www.nuget.org/packages/Microsoft.Agents.AI).

Поддерживаются четыре провайдера (harness-ы), переключаемые через конфигурацию:

| Ключ (`ActiveHarness`) | Класс | Провайдер |
|---|---|---|
| `GitHub` | `HarnessGithubAIProgram` | GitHub Models API |
| `Anthropic` | `HarnessClaudeProgram` | Anthropic Claude (OpenAI-совместимый endpoint) |
| `Ollama` | `HarnessOllamaProgram` | Локальный Ollama (OpenAI-совместимый endpoint) |
| `OllamaCloud` | `HarnessCloudOllamaProgram` | Ollama Cloud (OllamaSharp) |

---

## Архитектура

```
AgentSession/
├── Program.cs                          # Точка входа + DI + Serilog
├── IHarness.cs                         # Интерфейс Task RunAsync(CancellationToken)
├── appsettings.json                    # Конфигурация (endpoint-ы, модели, ActiveHarness)
└── Services/
    ├── HarnessGithubAIProgram.cs       # SubAgents через GitHub Models
    ├── HarnessClaudeAIProgram.cs       # SubAgents через Anthropic Claude
    ├── HarnessLocalOllamaAIProgram.cs  # SubAgents через локальный Ollama
    └── HarnessCloudOllamaAIProgram.cs  # SubAgents через Ollama Cloud
```

### Паттерн SubAgents

Каждый harness создаёт двухуровневую иерархию агентов:

```
StockPriceResearcher (родительский агент)
└── WebSearchAgent (дочерний агент, инструменты: web_search, web_fetch)
```

1. **WebSearchAgent** — специализированный агент с инструментами поиска и загрузки URL.  
2. **StockPriceResearcher (parentAgent)** — оркестратор, который делегирует веб-поиск дочернему агенту, запускает подзадачи параллельно и агрегирует результаты в Markdown-таблицу.

---

## Требования

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Доступ к одному из поддерживаемых AI-провайдеров

---

## Быстрый старт

### 1. Клонирование

```bash
git clone https://github.com/tdav/MagenticWorkflowApp.git
cd MagenticWorkflowApp/AgentSession
```

### 2. Настройка секретов

Секреты хранятся через **dotnet user-secrets** и **никогда** не попадают в репозиторий.

**GitHub Models:**
```bash
dotnet user-secrets set "GitHub:Token" "<ваш_токен>"
```

**Anthropic Claude:**
```bash
dotnet user-secrets set "Anthropic:ApiKey" "<ваш_ключ>"
```

**Ollama Cloud:**
```bash
dotnet user-secrets set "OllamaCloud:ApiKey" "<ваш_ключ>"
```

Локальный Ollama API-ключ не требует.

### 3. Настройка `appsettings.json`

```json
{
  "ActiveHarness": "OllamaCloud",

  "GitHub": {
    "Endpoint": "https://models.github.ai/inference",
    "Model": "openai/gpt-4.1-mini"
  },
  "Anthropic": {
    "Endpoint": "https://api.anthropic.com/v1",
    "Model": "claude-opus-4-5"
  },
  "Ollama": {
    "Endpoint": "http://localhost:11434/v1",
    "Model": "bjoernb/gemma4-e2b-fast:latest"
  },
  "OllamaCloud": {
    "Endpoint": "https://ollama.com",
    "Model": "gpt-oss:120b"
  }
}
```

Измените `ActiveHarness` на нужный ключ из таблицы выше.

### 4. Запуск

```bash
dotnet run
```

Приложение запросит список тикеров акций:

```
=== Stock Price Researcher (SubAgents Demo — Ollama Cloud) ===
Enter a list of stock tickers (e.g., BAC, MSFT, BA):
```

Введите тикеры через запятую, например: `AAPL, MSFT, NVDA`.  
Агент параллельно найдёт цены закрытия на **31 декабря 2025** и выведет Markdown-таблицу.

---

## NuGet-пакеты

| Пакет | Версия | Назначение |
|---|---|---|
| `Microsoft.Agents.AI` | 1.3.0 | Базовый агентский фреймворк |
| `Microsoft.Agents.AI.OpenAI` | 1.3.0 | OpenAI-адаптер |
| `Microsoft.Agents.AI.Workflows` | 1.3.0 | Оркестрация воркфлоу |
| `OllamaSharp` | 5.4.23 | Клиент Ollama Cloud |
| `Serilog` | 4.x | Структурированное логирование |
| `OpenTelemetry` | 1.15.3 | Трассировка (GitHub harness) |

---

## Логирование

Логи пишутся одновременно в консоль и в файл `logs/log-<дата>.txt` (ротация по дням, хранится 7 файлов).  
Конфигурация Serilog задаётся в `appsettings.json` в секции `Serilog`.

---

## Отмена выполнения

Нажмите **Ctrl+C** — приложение корректно отменит текущий запрос через `CancellationToken` и завершится с кодом `0`.

---

## Лицензия

Проект распространяется под лицензией MIT. См. файл [LICENSE](../LICENSE) в корне репозитория.
