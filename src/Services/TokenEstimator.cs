namespace MagenticWorkflowApp.Services;

/// <summary>
/// Heuristic token estimator (~4 chars per token for mixed latin/cyrillic text).
/// The ratio is a calibration knob (<see cref="Models.ContextBudgetConfiguration.CharsPerToken"/>).
/// ponytail: chars-based heuristic; swap for Microsoft.ML.Tokenizers when precision matters.
/// </summary>
public static class TokenEstimator
{
    public const double DefaultCharsPerToken = 4.0;

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
}
