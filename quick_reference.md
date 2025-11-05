# 🚀 Краткая справка - Quick Reference

Быстрый доступ ко всем важным командам и концепциям.

---

## ⚡ Быстрые команды

```bash
# Установка и настройка
./setup.sh                              # Linux/Mac
setup.bat                               # Windows

# Запуск примеров
dotnet run                              # Default (workflow-config.json)
dotnet run workflow-simple.json         # Simple example
dotnet run workflow-sequential.json     # Sequential pipeline
dotnet run workflow-concurrent.json     # Parallel execution
dotnet run workflow-conditional.json    # Conditional routing

# Запуск всех примеров
./run-examples.sh                       # Linux/Mac

# Build и очистка
dotnet build                            # Build project
dotnet clean                            # Clean build
dotnet restore                          # Restore packages
```

---

## 📋 Типы Workflow (краткая сводка)

| Тип | Использование | Команда |
|-----|---------------|---------|
| **Sequential** | Этапы друг за другом | `dotnet run workflow-sequential.json` |
| **Concurrent** | Параллельное выполнение | `dotnet run workflow-concurrent.json` |
| **Conditional** | Условная маршрутизация | `dotnet run workflow-conditional.json` |
| **Magentic** | Динамическая координация | `dotnet run workflow-config.json` |

---

## 📝 Шаблоны JSON конфигурации

### Sequential
```json
{
  "workflowType": "Sequential",
  "task": "Ваша задача",
  "orchestration": {
    "startAgent": "FirstAgent",
    "edges": [
      { "from": "FirstAgent", "to": "SecondAgent" }
    ]
  },
  "agents": [...]
}
```

### Concurrent
```json
{
  "workflowType": "Concurrent",
  "task": "Ваша задача",
  "orchestration": {
    "concurrent": {
      "participantAgents": ["Agent1", "Agent2"],
      "aggregationStrategy": "Merge"
    }
  },
  "agents": [...]
}
```

### Conditional
```json
{
  "workflowType": "Conditional",
  "task": "Ваша задача",
  "orchestration": {
    "startAgent": "InitialAgent",
    "conditionalEdges": [
      {
        "from": "DecisionAgent",
        "toOptions": ["Option1", "Option2"],
        "selectionFunction": "decision_func"
      }
    ]
  },
  "agents": [...]
}
```

### Magentic
```json
{
  "workflowType": "Magentic",
  "task": "Ваша задача",
  "manager": {
    "modelId": "gpt-4",
    "maxRoundCount": 10,
    "maxStallCount": 3,
    "maxResetCount": 2
  },
  "agents": [...]
}
```

---

## 🤖 Шаблон агента

```json
{
  "name": "AgentName",
  "description": "Краткое описание специализации",
  "instructions": "Детальные инструкции для агента",
  "modelId": "gpt-4",
  "tools": ["CodeInterpreter"],  // Опционально
  "metadata": {}                  // Опционально
}
```

### Популярные модели
- `gpt-4` - лучшее качество, дороже
- `gpt-4o-search-preview` - с поиском в интернете
- `gpt-3.5-turbo` - быстрее и дешевле

---

## 🔧 Настройка API ключей

### Вариант 1: appsettings.json
```json
{
  "OpenAI": {
    "ApiKey": "sk-your-key-here"
  }
}
```

### Вариант 2: Environment variables
```bash
# Linux/Mac
export OpenAI__ApiKey="sk-your-key-here"

# Windows CMD
set OpenAI__ApiKey=sk-your-key-here

# Windows PowerShell
$env:OpenAI__ApiKey="sk-your-key-here"
```

### Вариант 3: User Secrets
```bash
dotnet user-secrets init
dotnet user-secrets set "OpenAI:ApiKey" "sk-your-key-here"
```

---

## 📊 Aggregation Strategies (Concurrent)

| Strategy | Описание | Когда использовать |
|----------|----------|-------------------|
| `Collect` | Все результаты отдельно | Нужны все ответы |
| `Merge` | Объединить в один документ | Результаты дополняют друг друга |
| `Vote` | Выбрать лучший | Нужен консенсус |

---

## 🎯 Параметры Manager (Magentic)

```json
{
  "manager": {
    "modelId": "gpt-4",           // Модель для менеджера
    "maxRoundCount": 10,          // Максимум раундов координации
    "maxStallCount": 3,           // Раундов без прогресса до reset
    "maxResetCount": 2,           // Максимум сбросов плана
    "enablePlanReview": false     // Human-in-the-loop
  }
}
```

**Рекомендации:**
- Простые задачи: `maxRoundCount: 5-7`
- Средние задачи: `maxRoundCount: 8-12`
- Сложные задачи: `maxRoundCount: 15-20`

---

## 🐛 Отладка

```bash
# Увеличить детализацию логов
# В appsettings.json:
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"  // Вместо "Information"
    }
  }
}

# Запуск в DEMO режиме (без API ключей)
# Просто не настраивайте API ключи

# Проверка конфигурации
dotnet run your-config.json
# Смотрите на визуализацию перед выполнением
```

---

## 💰 Оценка стоимости

### По типу workflow:
- Sequential: **$** (1x стоимость агентов)
- Conditional: **$$** (только нужные агенты)
- Concurrent: **$$$** (все агенты × N)
- Magentic: **$$$$** (множество раундов)

### По модели (приблизительно):
- gpt-3.5-turbo: $0.0015 / 1K tokens (вход)
- gpt-4: $0.03 / 1K tokens (вход)
- gpt-4o: $0.005 / 1K tokens (вход)

---

## 📁 Структура файлов

```
MagenticWorkflowApp/
├── Program.cs                    # Точка входа
├── appsettings.json             # Конфигурация API
├── workflow-simple.json         # Простой пример
├── workflow-sequential.json     # Sequential
├── workflow-concurrent.json     # Concurrent
├── workflow-conditional.json    # Conditional
├── workflow-config.json         # Magentic (default)
├── workflow-advanced.json       # Advanced Magentic
├── Models/
│   └── WorkflowConfiguration.cs
├── Services/
│   ├── Interfaces.cs
│   ├── WorkflowJsonLoader.cs
│   ├── WorkflowVisualizer.cs
│   └── MagenticWorkflowOrchestrator.cs
└── README.md
```

---

## 🔗 Полезные документы

- **README.md** - Основная документация и начало работы
- **USAGE-GUIDE.md** - Подробное руководство пользователя
- **WORKFLOW-SELECTION-GUIDE.md** - Как выбрать тип workflow
- **EXAMPLES-OVERVIEW.md** - Обзор всех примеров
- **QUICK-REFERENCE.md** - Этот документ

---

## 🆘 Частые проблемы

### API key not found
```bash
# Решение: настройте в appsettings.json или environment variable
export OpenAI__ApiKey="your-key"
```

### Package restore failed
```bash
dotnet clean
dotnet restore --force
```

### Workflow stalls (зависает)
```json
// Увеличьте maxStallCount или упростите задачу
{
  "manager": {
    "maxStallCount": 5  // Вместо 3
  }
}
```

### Высокая стоимость
```json
// Уменьшите maxRoundCount или используйте gpt-3.5-turbo
{
  "manager": {
    "maxRoundCount": 5
  },
  "agents": [{
    "modelId": "gpt-3.5-turbo"  // Вместо gpt-4
  }]
}
```

---

## 🎓 Обучение - С чего начать

1. **День 1**: Установка + `workflow-simple.json`
2. **День 2**: `workflow-sequential.json` + модификация
3. **День 3**: `workflow-concurrent.json`
4. **День 4**: `workflow-conditional.json`
5. **День 5+**: `workflow-config.json` и эксперименты

---

## 💡 Быстрые советы

- ✅ Начинайте с простых примеров
- ✅ Используйте DEMO режим для тестирования
- ✅ Смотрите на визуализацию перед запуском
- ✅ Начинайте с Sequential, не с Magentic
- ✅ Тестируйте на простых задачах сначала
- ❌ Не используйте gpt-4 для всех агентов (дорого)
- ❌ Не делайте maxRoundCount > 20 без мониторинга
- ❌ Не пропускайте визуализацию workflow

---

## 🌐 NuGet пакеты (требуются для production)

```bash
# Preview пакеты Microsoft Agent Framework
dotnet add package Microsoft.Agents.AI --prerelease
dotnet add package Microsoft.Agents.AI.Workflows --prerelease
dotnet add package Microsoft.Agents.OpenAI --prerelease
```

**Примечание:** Эти пакеты могут быть в preview. Проверьте актуальные версии на NuGet.org

---

## 📞 Поддержка

- Документация: Смотрите файлы в корне проекта
- Примеры: 6 готовых workflow конфигураций
- Microsoft Docs: https://learn.microsoft.com/en-us/agent-framework/

---

**Сохраните эту страницу для быстрого доступа! 🔖**
