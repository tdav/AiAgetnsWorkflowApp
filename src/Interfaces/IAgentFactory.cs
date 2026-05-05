using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MagenticWorkflowApp.Interfaces;

/// <summary>
/// Builds <see cref="AIAgent"/> instances for the DeepResearch pipeline using
/// Microsoft.Agents.AI primitives (IChatClient → AsAIAgent) with OpenTelemetry,
/// optional ChatReduction and an arbitrary tool set.
/// </summary>
public interface IAgentFactory
{
    /// <summary>
    /// Build an agent from the given configuration values.
    /// </summary>
    /// <param name="name">Display name. Used in spans and logs.</param>
    /// <param name="instructions">System instructions for the agent.</param>
    /// <param name="modelId">Ollama / OpenAI model id (e.g. "hadad/qwen3-4bd:Q8_0").</param>
    /// <param name="tools">Optional AI tools to expose to the model.</param>
    /// <param name="useChatReducer">Enable in-memory chat history with message-count reducer.</param>
    /// <param name="reducerWindow">When useChatReducer=true, target message-count window.</param>
    /// <param name="enableThinking">Enable native "think" mode for Ollama models.</param>
    AIAgent BuildAgent(
        string name,
        string instructions,
        string modelId,
        IReadOnlyList<AITool>? tools = null,
        bool useChatReducer = false,
        int reducerWindow = 10,
        bool enableThinking = false);
}
