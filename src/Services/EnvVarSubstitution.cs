using System.Text.RegularExpressions;
using MagenticWorkflowApp.Exceptions;

namespace MagenticWorkflowApp.Services;

internal static class EnvVarSubstitution
{
    private static readonly Regex Pattern = new(@"\$\{([A-Z_][A-Z0-9_]*)\}", RegexOptions.Compiled);

    public static string Apply(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        return Pattern.Replace(input, m =>
        {
            var name = m.Groups[1].Value;
            return Environment.GetEnvironmentVariable(name)
                ?? throw new WorkflowValidationException(
                       $"Environment variable '{name}' is not set");
        });
    }
}
