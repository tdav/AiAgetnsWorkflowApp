using MagenticWorkflowApp.Models;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;

namespace MagenticWorkflowApp.Interfaces;

/// <summary>
/// Single source of chat clients and Semantic Kernel instances for all workflow
/// types. Encapsulates provider selection (Ollama / OpenAI / Azure) and wraps
/// every <see cref="IChatClient"/> with context-budget trimming middleware.
/// </summary>
public interface IChatClientProvider
{
    /// <summary>True when at least one LLM credential/endpoint is configured.</summary>
    bool HasCredentials { get; }

    /// <summary>Apply the per-workflow context budget (null keeps appsettings defaults).</summary>
    void SetWorkflowBudget(ContextBudgetConfiguration? budget);

    /// <summary>Build a budget-trimmed chat client for the Microsoft.Agents.AI paths.</summary>
    IChatClient GetChatClient(string modelId, bool enableThinking = false, int? historyWindowOverride = null);

    /// <summary>Build a Semantic Kernel (magentic path) with optional activity-logging decoration.</summary>
    Kernel BuildKernel(string modelId, bool enableThinking = false, string? agentName = null);
}
