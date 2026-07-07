namespace MagenticWorkflowApp.Models;

/// <summary>
/// Token/context budget applied to every model call. Defaults are protective:
/// trimming only kicks in when a limit is exceeded, so small workflows behave
/// exactly as before. Configure per-workflow via the optional "contextBudget"
/// JSON section or globally via the "ContextBudget" appsettings section.
/// </summary>
public class ContextBudgetConfiguration
{
    public const string TruncateStrategy = "truncate";
    public const string SummarizeStrategy = "summarize";

    /// <summary>Upper bound (estimated tokens) for the message list sent to the model.</summary>
    public int MaxInputTokens { get; set; } = 32000;

    /// <summary>Upper bound (estimated tokens) for a single tool/function result.</summary>
    public int MaxToolResultTokens { get; set; } = 4000;

    /// <summary>Sliding-window size in messages (system prompt + first task message + tail).</summary>
    public int HistoryWindowMessages { get; set; } = 40;

    /// <summary>Calibration knob for the chars-based token estimator.</summary>
    public double CharsPerToken { get; set; } = 4.0;

    /// <summary>
    /// "summarize" (default): evicted history is compressed into a digest by an extra
    /// LLM call; "truncate": evicted history is simply dropped (no extra cost).
    /// </summary>
    public string Strategy { get; set; } = SummarizeStrategy;

    /// <summary>Max output tokens for the summarization call.</summary>
    public int SummaryMaxTokens { get; set; } = 1024;

    public bool UseSummarization =>
        !string.Equals(Strategy, TruncateStrategy, StringComparison.OrdinalIgnoreCase);
}
