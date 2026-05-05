using System.ClientModel;
using System.ClientModel.Primitives;
using System.IO;
using System.Text.Json;
using MagenticWorkflowApp.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace MagenticWorkflowApp.Services;

/// <summary>
/// Builds <see cref="AIAgent"/> instances for DeepResearch role agents.
/// Uses Ollama via the OpenAI-compatible endpoint, wires UseFunctionInvocation
/// on the IChatClient and UseOpenTelemetry on the agent itself.
/// ChatReduction is enforced by the orchestrator via tail-trimming the
/// conversation list before each <c>RunAsync</c>.
/// </summary>
public sealed class AgentFactory : IAgentFactory
{
    public const string TelemetrySourceName = "MagenticWorkflowApp.Agents";

    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _services;

    public AgentFactory(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        _services = services;
    }

    public AIAgent BuildAgent(
        string name,
        string instructions,
        string modelId,
        IReadOnlyList<AITool>? tools = null,
        bool useChatReducer = false,
        int reducerWindow = 10,
        bool enableThinking = false)
    {
        var ollamaEndpoint = _configuration["Ollama:Endpoint"];
        var openAiKey = _configuration["OpenAI:ApiKey"];

        IChatClient chatClient = BuildBaseChatClient(modelId, ollamaEndpoint, openAiKey, enableThinking)
            .AsBuilder()
            .UseFunctionInvocation(_loggerFactory)
            .Build();

        var options = new ChatClientAgentOptions
        {
            Name = name,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = tools is { Count: > 0 } ? tools.ToList() : null,
            },
        };

        var baseAgent = chatClient.AsAIAgent(options, _loggerFactory, _services);

        return baseAgent
            .AsBuilder()
            .UseOpenTelemetry(TelemetrySourceName, t => t.EnableSensitiveData = true)
            .Build();
    }

    private static IChatClient BuildBaseChatClient(
        string modelId,
        string? ollamaEndpoint,
        string? openAiKey,
        bool enableThinking)
    {
        if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
        {
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(ollamaEndpoint!.TrimEnd('/') + "/v1"),
                NetworkTimeout = TimeSpan.FromMinutes(5),
            };
            clientOptions.AddPolicy(new OllamaThinkPolicy(enableThinking), PipelinePosition.PerCall);

            var ollama = new OpenAIClient(new ApiKeyCredential("ollama"), clientOptions);
            return ollama.GetChatClient(modelId).AsIChatClient();
        }

        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            throw new InvalidOperationException(
                "DeepResearch requires either Ollama:Endpoint or OpenAI:ApiKey.");
        }

        return new OpenAIClient(openAiKey).GetChatClient(modelId).AsIChatClient();
    }

    /// <summary>
    /// Injects the Ollama-specific <c>"think"</c> field into outgoing chat-completion
    /// requests. Ollama's OpenAI-compatible endpoint accepts the extra field, while
    /// real OpenAI ignores it; we only register this policy for Ollama clients.
    /// </summary>
    private sealed class OllamaThinkPolicy : PipelinePolicy
    {
        private readonly bool _think;

        internal OllamaThinkPolicy(bool think) => _think = think;

        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            InjectThink(message);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            InjectThink(message);
            await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
        }

        private void InjectThink(PipelineMessage message)
        {
            if (message.Request?.Content is null) return;
            using var ms = new MemoryStream();
            message.Request.Content.WriteTo(ms, CancellationToken.None);
            ms.Position = 0;
            using var doc = JsonDocument.Parse(ms);
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                    prop.WriteTo(writer);
                writer.WriteBoolean("think", _think);
                writer.WriteEndObject();
            }
            message.Request.Content = BinaryContent.Create(new BinaryData(output.ToArray()));
        }
    }
}
