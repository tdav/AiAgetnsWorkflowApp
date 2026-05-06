#pragma warning disable MAAI001

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using System.ComponentModel;
using System.Text.Json;

namespace AgentSession.Services;

/// <summary>
/// Демо SubAgents через Ollama Cloud (OllamaSharp).
/// Секреты: OllamaCloud:ApiKey (dotnet user-secrets set "OllamaCloud:ApiKey" "ваш_ключ").
/// Настройки: OllamaCloud:Endpoint, OllamaCloud:Model (appsettings.json).
/// </summary>
internal class HarnessCloudOllamaProgram(IConfiguration config, ILogger<HarnessCloudOllamaProgram> logger) : IHarness
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = config["OllamaCloud:ApiKey"]
            ?? throw new InvalidOperationException("OllamaCloud:ApiKey не задан. Выполните: dotnet user-secrets set \"OllamaCloud:ApiKey\" \"<ваш_ключ>\"");
        var cloudEndpoint = config["OllamaCloud:Endpoint"] ?? "https://ollama.com";
        var modelName = config["OllamaCloud:Model"] ?? "gpt-oss:120b";

        logger.LogInformation("Ollama Cloud harness запущен. Endpoint={Endpoint} Model={Model}", cloudEndpoint, modelName);

        using var http = new HttpClient { BaseAddress = new Uri(cloudEndpoint) };
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        OllamaApiClient CreateClient() => new(http, modelName);

        // --- Инструмент: веб-поиск через Ollama Cloud /api/web_search ---
        var webSearchTool = AIFunctionFactory.Create(
            async (
                [Description("Search query to find information on the web")] string query,
                [Description("Maximum number of results to return (1-10)")] int maxResults = 5) =>
            {
                var body = JsonSerializer.Serialize(new { query, max_results = maxResults });
                using var req = new HttpRequestMessage(HttpMethod.Post, "/api/web_search")
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
                };
                using var resp = await http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return $"web_search failed: HTTP {(int)resp.StatusCode}";

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                var results = doc.RootElement.GetProperty("results").EnumerateArray()
                    .Select(r =>
                        $"**{r.GetProperty("title").GetString()}**\n" +
                        $"{r.GetProperty("url").GetString()}\n" +
                        r.GetProperty("content").GetString())
                    .ToList();

                return results.Count == 0
                    ? "Результаты не найдены."
                    : string.Join("\n\n---\n\n", results);
            },
            name: "web_search",
            description: "Search the web for current information using Ollama Cloud web search API.");

        // --- Инструмент: загрузка содержимого URL через Ollama Cloud /api/web_fetch ---
        var webFetchTool = AIFunctionFactory.Create(
            async ([Description("URL to fetch content from")] string url) =>
            {
                var body = JsonSerializer.Serialize(new { url });
                using var req = new HttpRequestMessage(HttpMethod.Post, "/api/web_fetch")
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
                };
                using var resp = await http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return $"web_fetch failed: HTTP {(int)resp.StatusCode}";

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                return doc.RootElement.GetProperty("content").GetString() ?? "Пустой ответ.";
            },
            name: "web_fetch",
            description: "Fetch and read the content of a specific URL.");

        AIAgent webSearchAgent =
            ((IChatClient)CreateClient())
            .AsAIAgent(
                new ChatClientAgentOptions
                {
                    Name = "WebSearchAgent",
                    Description = "An agent that searches the web and fetches URLs using Ollama Cloud tools.",
                    ChatOptions = new ChatOptions
                    {
                        Instructions =
                            "You are a web research assistant. " +
                            "Use the web_search tool to find information and web_fetch to read specific pages. " +
                            "Return concise, factual answers with sources.",
                        Tools = [webSearchTool, webFetchTool],
                        MaxOutputTokens = 32_000,
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
            ((IChatClient)CreateClient())
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
        Console.WriteLine("=== Stock Price Researcher (SubAgents Demo — Ollama Cloud) ===");
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
