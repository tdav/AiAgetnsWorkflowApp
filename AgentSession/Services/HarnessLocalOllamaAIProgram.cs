#pragma warning disable MAAI001

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;

namespace AgentSession.Services;

/// <summary>
/// Демо SubAgents через локальный Ollama (OpenAI-совместимый endpoint).
/// Настройки: Ollama:Endpoint, Ollama:Model (appsettings.json).
/// </summary>
internal class HarnessOllamaProgram(IConfiguration config, ILogger<HarnessOllamaProgram> logger) : IHarness
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = new Uri(config["Ollama:Endpoint"] ?? "http://localhost:11434/v1");
        var modelName = config["Ollama:Model"] ?? "bjoernb/gemma4-e2b-fast:latest";

        logger.LogInformation("Ollama (local) harness запущен. Endpoint={Endpoint} Model={Model}", endpoint, modelName);

        var clientOptions = new OpenAIClientOptions { Endpoint = endpoint };

        AIAgent webSearchAgent =
            new OpenAIClient(new ApiKeyCredential("ollama"), clientOptions)
            .GetChatClient(modelName)
            .AsIChatClient()
            .AsAIAgent(
                new ChatClientAgentOptions
                {
                    Name = "WebSearchAgent",
                    Description = "An agent that can search the web to find information.",
                    ChatOptions = new ChatOptions
                    {
                        Instructions = "You are a web search assistant. When asked to find information, return a concise, factual answer based on your knowledge.",
                    },
                });

        var parentInstructions =
            """
    Ты — помощник по исследованию цен на акции. У тебя есть доступ к вспомогательному агенту WebSearchAgent.

    Когда тебе передают список тикеров акций, твоя задача — найти цену закрытия для каждого тикера на 31 декабря 2025 года.

    ## Рабочий процесс

    1. Для каждого тикера запусти подзадачу на агенте WebSearchAgent с запросом цены закрытия на 31 декабря 2025 года.
       - Запускай все подзадачи до того, как ждать завершения какой-либо из них, чтобы они выполнялись параллельно.
    2. Дождись завершения всех подзадач.
    3. Получи результаты каждой подзадачи.
    4. Представь сводную таблицу с тикером и ценой закрытия для каждой акции.
    5. Очисти все завершённые задачи для освобождения памяти.

    ## Важно

    - Всегда делегируй веб-поиск вспомогательному агенту WebSearchAgent. Не отвечай по памяти.
    - Если подзадача завершилась с ошибкой или вернула неясный результат, продолжи задачу с более конкретным запросом.
    - Представляй результаты в виде аккуратной таблицы в формате Markdown.
    """;

        AIAgent parentAgent =
            new OpenAIClient(new ApiKeyCredential("ollama"), clientOptions)
            .GetChatClient(modelName)
            .AsIChatClient()
            .AsAIAgent(
                new ChatClientAgentOptions
                {
                    Name = "StockPriceResearcher",
                    Description = "An agent that researches stock prices.",
                    ChatOptions = new ChatOptions
                    {
                        Instructions = parentInstructions,
                        MaxOutputTokens = 16_000,
                        Tools = [webSearchAgent.AsAIFunction()],
                    },
                });

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Stock Price Researcher (SubAgents Demo — Ollama Local) ===");
        Console.ResetColor();
        Console.Write("Enter a list of stock tickers (e.g., BAC, MSFT, BA): ");
        var userInput = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(userInput))
            return;

        Console.WriteLine();
        var session = await parentAgent.CreateSessionAsync(cancellationToken);
        var messages = new List<ChatMessage> { new(ChatRole.User, userInput) };

        await foreach (var update in parentAgent.RunStreamingAsync(messages, session).WithCancellation(cancellationToken))
        {
            Console.Write(update.Text);
        }
        Console.WriteLine();
    }
}
