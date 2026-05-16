namespace CodexTeamUp.AppServer;

/// <summary>
/// Matches desired agent names to visible Codex Desktop thread records.
/// </summary>
public static class AgentThreadMatcher
{
    /// <summary>
    /// Matches each requested agent id to the best thread by visible name.
    /// </summary>
    public static IReadOnlyList<AgentThreadBinding> MatchAgents(
        IEnumerable<string> agentIds,
        IEnumerable<CodexThreadRecord> threads,
        string? cwd)
    {
        var candidates = threads
            .Where(thread => string.IsNullOrWhiteSpace(cwd)
                || PathEquals(thread.Cwd, cwd))
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = threads.ToList();
        }

        return agentIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => MatchAgent(id.Trim(), candidates))
            .ToList();
    }

    private static AgentThreadBinding MatchAgent(string agentId, IReadOnlyList<CodexThreadRecord> threads)
    {
        var normalizedAgent = Normalize(agentId);
        var withoutPrefix = StripNumericPrefix(normalizedAgent);

        var best = threads
            .Select(thread => new
            {
                Thread = thread,
                Score = Score(thread, normalizedAgent, withoutPrefix)
            })
            .OrderByDescending(row => row.Score)
            .ThenByDescending(row => row.Thread.UpdatedAt)
            .FirstOrDefault(row => row.Score > 0);

        return new AgentThreadBinding(
            agentId,
            best?.Thread.Id,
            best?.Thread.Name ?? best?.Thread.Preview,
            best?.Score ?? 0,
            best?.Thread.Cwd);
    }

    private static int Score(CodexThreadRecord thread, string normalizedAgent, string withoutPrefix)
    {
        var haystack = Normalize(thread.Name);
        if (haystack.Length == 0)
        {
            return 0;
        }

        if (haystack == normalizedAgent)
        {
            return 100;
        }

        if (haystack.Contains(normalizedAgent, StringComparison.Ordinal))
        {
            return 90;
        }

        if (!string.IsNullOrWhiteSpace(withoutPrefix) && haystack.Contains(withoutPrefix, StringComparison.Ordinal))
        {
            return 70;
        }

        return 0;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch))
            .ToArray();
        return new string(chars);
    }

    private static bool PathEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string value)
    {
        return value.Replace('\\', '/').TrimEnd('/');
    }

    private static string StripNumericPrefix(string value)
    {
        var index = 0;
        while (index < value.Length && char.IsDigit(value[index]))
        {
            index++;
        }

        return index == 0 ? value : value[index..];
    }
}

/// <summary>
/// Result of binding one requested team agent to a visible Codex thread.
/// </summary>
public sealed record AgentThreadBinding(
    string AgentId,
    string? ThreadId,
    string? ThreadTitle,
    int Confidence,
    string? Cwd);
