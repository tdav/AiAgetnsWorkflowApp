#pragma warning disable MAAI001

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenTelemetry;
using OpenTelemetry.Trace;
using System.ClientModel;
using System.ComponentModel;
using System.Text.Json;

/// <summary>
/// Демо SubAgents через GitHub Copilot API (https://api.githubcopilot.com).
/// Требуется переменная окружения GITHUB_TOKEN (GitHub PAT с доступом к Copilot).
/// Опционально: GITHUB_COPILOT_MODEL (по умолчанию gpt-4.1).
/// Документация по моделям: https://docs.github.com/ru/copilot/reference/ai-models/supported-models
/// </summary>
internal class HarnessGithubAIProgram
{
    public static async Task RunAsync(string[] args)
    {
        var githubCopilotEndpoint = new Uri("https://models.github.ai/inference");
        var apiKey = "***REMOVED***";
        var modelName = "openai/gpt-5-mini";


        using var traceProvider = Sdk.CreateTracerProviderBuilder()
                    .AddSource("agent-telemetry-source")
                    .AddConsoleExporter()
                    .Build();


        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = githubCopilotEndpoint,
        };

        // --- Sub-agent: Web Search Agent ---
        // Использует реальный инструмент GetStockClosingPrice для получения цен закрытия через Yahoo Finance.
        var stockTool = AIFunctionFactory.Create(StockPriceTools.GetStockClosingPriceAsync);

        AIAgent webSearchAgent =
            new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions)
            .GetChatClient(modelName)
            .AsIChatClient()
            .AsAIAgent(
                new ChatClientAgentOptions
                {
                    Name = "WebSearchAgent",
                    Description = "An agent that can retrieve stock closing prices.",
                    ChatOptions = new ChatOptions
                    {
                        Instructions =
                            "You are a stock price assistant. " +
                            "Use the GetStockClosingPrice tool to get the closing price for a given ticker and date. " +
                            "Return the result as-is without additional commentary.",
                        Tools = [stockTool],
                    },
                })
              .AsBuilder()
                    .UseOpenTelemetry(sourceName: "agent-telemetry-source")
                    .Build();


         


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
    - Если подзадача завершилась с ошибкой или вернула неясный результат, повтори с более конкретным запросом.
    - Представляй результаты в виде аккуратной таблицы в формате Markdown.
    """;

        AIAgent parentAgent =
            new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions)
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
                })
              .AsBuilder()
                    .UseOpenTelemetry(sourceName: "agent-telemetry-source")
                    .Build();

        // Интерактивный консольный цикл
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Stock Price Researcher (SubAgents Demo — GitHub Copilot API) ===");
        Console.ResetColor();
        Console.Write("Enter a list of stock tickers (e.g., BAC, MSFT, BA): ");
        //var userInput = Console.ReadLine();
        var userInput = "MSFT";

        if (!string.IsNullOrWhiteSpace(userInput))
        {
            Console.WriteLine();
            var session = await parentAgent.CreateSessionAsync();
            var messages = new List<Microsoft.Extensions.AI.ChatMessage> { new(ChatRole.User, userInput) };

            AgentRunOptions runOptions = new AgentRunOptions
            {
                // Включаем потоковую передачу для получения промежуточных результатов по мере их готовности
               AllowBackgroundResponses= true,
                
            };

            await foreach (var update in parentAgent.RunStreamingAsync(messages, session, runOptions))
            {
                Console.Write(update.Text);
            }
            Console.WriteLine();
        }
    }
}

/// <summary>
/// Инструменты для получения реальных биржевых данных через Yahoo Finance API.
/// </summary>
internal static class StockPriceTools
{
    private static readonly HttpClient _http = new();

    /// <summary>
    /// Возвращает цену закрытия акции для заданного тикера и даты (формат YYYY-MM-DD).
    /// </summary>
    [Description("Get the closing price of a stock for a specific date. Date must be in YYYY-MM-DD format.")]
    public static async Task<string> GetStockClosingPriceAsync(
        [Description("Stock ticker symbol, e.g. MSFT, AAPL")] string ticker,
        [Description("Date in YYYY-MM-DD format, e.g. 2025-12-31")] string date)
    {
        try
        {
            if (!DateTimeOffset.TryParse(date, out var targetDate))
                return $"Неверный формат даты: {date}. Используйте YYYY-MM-DD.";

            // Yahoo Finance: period1/period2 — unix timestamps диапазона ±3 дня для учёта выходных
            var from = targetDate.AddDays(-5).ToUnixTimeSeconds();
            var to   = targetDate.AddDays(1).ToUnixTimeSeconds();

            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(ticker)}" +
                      $"?period1={from}&period2={to}&interval=1d";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // Yahoo Finance требует User-Agent
            request.Headers.Add("User-Agent", "Mozilla/5.0");

            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return $"Ошибка запроса к Yahoo Finance для {ticker}: HTTP {(int)response.StatusCode}";

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

            var timestamps = doc.RootElement
                .GetProperty("chart").GetProperty("result")[0]
                .GetProperty("timestamp")
                .EnumerateArray()
                .Select(t => DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()))
                .ToList();

            var closes = doc.RootElement
                .GetProperty("chart").GetProperty("result")[0]
                .GetProperty("indicators").GetProperty("quote")[0]
                .GetProperty("close")
                .EnumerateArray()
                .Select(v => v.ValueKind == JsonValueKind.Null ? (double?)null : v.GetDouble())
                .ToList();

            // Ищем ближайшую дату ≤ targetDate
            int bestIdx = -1;
            for (int i = timestamps.Count - 1; i >= 0; i--)
            {
                if (timestamps[i].Date <= targetDate.Date && closes[i].HasValue)
                {
                    bestIdx = i;
                    break;
                }
            }

            if (bestIdx < 0)
                return $"Данные для {ticker} на {date} не найдены (биржа могла быть закрыта).";

            return $"{ticker}: цена закрытия на {timestamps[bestIdx]:yyyy-MM-dd} = {closes[bestIdx]:F2} USD";
        }
        catch (Exception ex)
        {
            return $"Ошибка при получении данных для {ticker}: {ex.Message}";
        }
    }
}
