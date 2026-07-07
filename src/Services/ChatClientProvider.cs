using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenAI;

namespace MagenticWorkflowApp.Services;

/// <summary>
/// Single place that turns configuration (Ollama endpoint / OpenAI key / Azure) into
/// chat clients and Semantic Kernel instances. Every <see cref="IChatClient"/> is
/// wrapped with <see cref="TokenTrimmingChatClient"/>, so the context budget applies
/// uniformly to all Microsoft.Agents.AI workflow paths and their tool results.
/// </summary>
public sealed class ChatClientProvider : IChatClientProvider
{
    private readonly IConfiguration configuration;
    private readonly ILoggerFactory loggerFactory;
    private readonly IAgentActivityLogger activity;
    private ContextBudgetConfiguration budget;

    public ChatClientProvider(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        IAgentActivityLogger activity)
    {
        this.configuration = configuration;
        this.loggerFactory = loggerFactory;
        this.activity = activity;
        this.budget = configuration.GetSection("ContextBudget").Get<ContextBudgetConfiguration>()
            ?? new ContextBudgetConfiguration();
    }

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(configuration["OpenAI:ApiKey"])
        || !string.IsNullOrWhiteSpace(configuration["AzureOpenAI:Endpoint"])
        || !string.IsNullOrWhiteSpace(configuration["Ollama:Endpoint"]);

    public void SetWorkflowBudget(ContextBudgetConfiguration? workflowBudget)
    {
        // ponytail: mutable singleton state — the console app executes exactly one workflow per process.
        if (workflowBudget is not null)
        {
            this.budget = workflowBudget;
        }
    }

    public IChatClient GetChatClient(string modelId, bool enableThinking = false, int? historyWindowOverride = null)
        => new TokenTrimmingChatClient(
            BuildRawChatClient(modelId, enableThinking),
            () => this.budget,
            loggerFactory.CreateLogger<TokenTrimmingChatClient>(),
            historyWindowOverride);

    public Kernel BuildKernel(string modelId, bool enableThinking = false, string? agentName = null)
    {
        var builder = Kernel.CreateBuilder();
        var ollamaEndpoint = configuration["Ollama:Endpoint"];
        if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
        {
            builder.AddOpenAIChatCompletion(modelId, CreateOllamaClient(ollamaEndpoint, enableThinking));
        }
        else if (!string.IsNullOrWhiteSpace(configuration["AzureOpenAI:Endpoint"]))
        {
            throw new NotSupportedException(
                "Azure OpenAI endpoint is configured, but Azure.AI.OpenAI package is not yet referenced.");
        }
        else if (configuration["OpenAI:ApiKey"] is { Length: > 0 } apiKey)
        {
            builder.AddOpenAIChatCompletion(modelId, apiKey);
        }
        else
        {
            throw new InvalidOperationException(
                "No LLM credentials configured: set Ollama:Endpoint or OpenAI:ApiKey.");
        }

        if (agentName is not null)
        {
            var descriptor = builder.Services.LastOrDefault(d =>
                d.ServiceType == typeof(IChatCompletionService));
            if (descriptor is not null)
            {
                builder.Services.Remove(descriptor);
                builder.Services.AddSingleton<IChatCompletionService>(sp =>
                {
                    IChatCompletionService inner = descriptor.ImplementationFactory is not null
                        ? (IChatCompletionService)descriptor.ImplementationFactory(sp)
                        : descriptor.ImplementationInstance is not null
                            ? (IChatCompletionService)descriptor.ImplementationInstance
                            : (IChatCompletionService)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
                    return new LoggingChatCompletionService(inner, agentName, activity, stripReasoning: !enableThinking);
                });
            }
        }

        // ponytail: SK/magentic path is not token-trimmed — rounds are bounded by
        // ManagerConfiguration.MaxRoundCount; unify via IChatClient.AsChatCompletionService() if needed.
        return builder.Build();
    }

    private IChatClient BuildRawChatClient(string modelId, bool enableThinking)
    {
        var ollamaEndpoint = configuration["Ollama:Endpoint"];
        if (!string.IsNullOrWhiteSpace(ollamaEndpoint))
        {
            return CreateOllamaClient(ollamaEndpoint, enableThinking).GetChatClient(modelId).AsIChatClient();
        }
        if (!string.IsNullOrWhiteSpace(configuration["AzureOpenAI:Endpoint"]))
        {
            throw new NotSupportedException(
                "Azure OpenAI endpoint is configured, but Azure.AI.OpenAI package is not yet referenced.");
        }
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "No LLM credentials configured: set Ollama:Endpoint or OpenAI:ApiKey.");
        }
        return new OpenAIClient(apiKey).GetChatClient(modelId).AsIChatClient();
    }

    private static OpenAIClient CreateOllamaClient(string endpoint, bool enableThinking)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint.TrimEnd('/') + "/v1"),
            NetworkTimeout = TimeSpan.FromMinutes(5),
        };
        options.AddPolicy(new OllamaThinkPolicy(enableThinking), PipelinePosition.PerCall);
        return new OpenAIClient(new ApiKeyCredential("ollama"), options);
    }

    /// <summary>
    /// Injects the Ollama-specific <c>"think"</c> field into outgoing chat-completion
    /// requests. Ollama's OpenAI-compatible endpoint accepts the extra field; the
    /// policy is only registered for Ollama clients.
    /// </summary>
    private sealed class OllamaThinkPolicy : PipelinePolicy
    {
        private readonly bool think;

        internal OllamaThinkPolicy(bool think) => this.think = think;

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
            if (message.Request?.Content is null)
            {
                return;
            }
            using var ms = new MemoryStream();
            message.Request.Content.WriteTo(ms, CancellationToken.None);
            ms.Position = 0;
            using var doc = JsonDocument.Parse(ms);
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    prop.WriteTo(writer);
                }
                writer.WriteBoolean("think", think);
                writer.WriteEndObject();
            }
            message.Request.Content = System.ClientModel.BinaryContent.Create(new BinaryData(output.ToArray()));
        }
    }
}
