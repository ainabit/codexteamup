using System.Text.RegularExpressions;

namespace CodexTeamUp.Core;

public static partial class SafeText
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = TokenPattern().Replace(value, "$1[redacted]");
        text = BearerPattern().Replace(text, "Bearer [redacted]");
        text = ApiKeyPattern().Replace(text, "[redacted-api-key]");
        return text;
    }

    public static string Preview(string? value, int maxLength = 160)
    {
        var text = Redact(value).ReplaceLineEndings(" ").Trim();
        while (text.Contains("  ", StringComparison.Ordinal))
        {
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        }

        return text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength - 1), "...");
    }

    [GeneratedRegex("(?i)(token|api[_-]?key|secret|password)(\\s*[:=]\\s*)\\S+")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("(?i)Bearer\\s+[A-Za-z0-9._~+/-]+=*")]
    private static partial Regex BearerPattern();

    [GeneratedRegex("sk-[A-Za-z0-9]{20,}")]
    private static partial Regex ApiKeyPattern();
}
