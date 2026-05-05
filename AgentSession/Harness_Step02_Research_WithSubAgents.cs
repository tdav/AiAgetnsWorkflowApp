#pragma warning disable MAAI001  

using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

internal class HarnessOllamaProgram
{
    public static async Task RunAsync(string[] args)
    {
        var ollamaEndpoint = new Uri(Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT") ?? "http://localhost:11434/v1");
        var modelName = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "bjoernb/gemma4-e2b-fast:latest";

        // --- Sub-agent: Web Search Agent ---
        // Ollama не поддерживает встроенный web search — агент отвечает на основе своих знаний.
        AIAgent webSearchAgent =
            new OpenAIClient(
                new ApiKeyCredential("ollama"),
                new OpenAIClientOptions { Endpoint = ollamaEndpoint })
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
    Ты — помощник по исследованию цен на акции. У тебя есть доступ к вспомогательному агенту веб-поиска, который умеет искать информацию в интернете.

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
            new OpenAIClient(
                new ApiKeyCredential("ollama"),
                new OpenAIClientOptions { Endpoint = ollamaEndpoint })
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
                        // Sub-agent регистрируется как инструмент через AsAIFunction
                        Tools = [webSearchAgent.AsAIFunction()],
                    },
                });

        // Интерактивный консольный цикл
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Stock Price Researcher (SubAgents Demo — Ollama) ===");
        Console.ResetColor();
        Console.Write("Enter a list of stock tickers (e.g., BAC, MSFT, BA): ");
        var userInput = Console.ReadLine();

        if (!string.IsNullOrWhiteSpace(userInput))
        {
            Console.WriteLine();
            var session = await parentAgent.CreateSessionAsync();
            var messages = new List<ChatMessage> { new(ChatRole.User, userInput) };

            await foreach (var update in parentAgent.RunStreamingAsync(messages, session))
            {
                Console.Write(update.Text);
            }
            Console.WriteLine();
        }
    }
}