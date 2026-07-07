using MagenticWorkflowApp.Models;
using Microsoft.ML.Tokenizers;

namespace MagenticWorkflowApp.Services;

/// <summary>
/// Token estimator. Budget-aware overloads use the tiktoken cl100k_base tokenizer
/// (accurate for OpenAI-style models, good approximation for Ollama models);
/// the chars-based heuristic (~4 chars per token) remains as the "chars" mode
/// and as a fallback when the tokenizer cannot be created.
/// </summary>
public static class TokenEstimator
{
    public const double DefaultCharsPerToken = 4.0;

    private static readonly Lazy<Tokenizer?> Cl100k = new(() =>
    {
        try
        {
            return TiktokenTokenizer.CreateForEncoding("cl100k_base");
        }
        catch (Exception)
        {
            // Tokenizer data unavailable — chars heuristic takes over.
            return null;
        }
    });

    /// <summary>Estimate token count according to the budget's tokenizer mode.</summary>
    public static int Estimate(string? text, ContextBudgetConfiguration budget)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }
        if (budget.UseAccurateTokenizer && Cl100k.Value is { } tokenizer)
        {
            return tokenizer.CountTokens(text);
        }
        return Estimate(text, budget.CharsPerToken);
    }

    /// <summary>Chars-based heuristic estimate.</summary>
    public static int Estimate(string? text, double charsPerToken = DefaultCharsPerToken)
        => EstimateChars(text?.Length ?? 0, charsPerToken);

    public static int EstimateChars(int charCount, double charsPerToken = DefaultCharsPerToken)
    {
        if (charCount <= 0)
        {
            return 0;
        }
        var ratio = charsPerToken > 0 ? charsPerToken : DefaultCharsPerToken;
        return (int)Math.Ceiling(charCount / ratio);
    }

    /// <summary>Char index that keeps at most <paramref name="maxTokens"/> tokens of the text.</summary>
    public static int GetTruncationIndex(string text, int maxTokens, ContextBudgetConfiguration budget)
    {
        if (maxTokens <= 0)
        {
            return 0;
        }
        if (budget.UseAccurateTokenizer && Cl100k.Value is { } tokenizer)
        {
            return tokenizer.GetIndexByTokenCount(text, maxTokens, out _, out _);
        }
        var ratio = budget.CharsPerToken > 0 ? budget.CharsPerToken : DefaultCharsPerToken;
        return Math.Min(text.Length, (int)(maxTokens * ratio));
    }
}
