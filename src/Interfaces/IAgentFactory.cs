using MagenticWorkflowApp.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MagenticWorkflowApp.Interfaces;

/// <summary>
/// Builds <see cref="AIAgent"/> instances from an <see cref="AgentConfiguration"/>
/// using Microsoft.Agents.AI primitives (IChatClient → AsAIAgent) with OpenTelemetry
/// and function invocation. Context-budget trimming is applied by the underlying
/// <see cref="IChatClientProvider"/>.
/// </summary>
public interface IAgentFactory
{
    /// <param name="config">Agent configuration (name, instructions, model, thinking, sampling).</param>
    /// <param name="tools">Optional AI tools to expose to the model.</param>
    /// <param name="nameOverride">Optional display-name override (e.g. per-sub-question researchers).</param>
    /// <param name="historyWindowOverride">Optional per-agent history window (messages) for context trimming.</param>
    AIAgent BuildAgent(
        AgentConfiguration config,
        IReadOnlyList<AITool>? tools = null,
        string? nameOverride = null,
        int? historyWindowOverride = null);
}
