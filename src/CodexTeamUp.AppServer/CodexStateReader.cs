using System.Text.Json;
using CodexTeamUp.Core;

namespace CodexTeamUp.AppServer;

public sealed class CodexStateReader
{
    private readonly string _codexHome;

    public CodexStateReader(string? codexHome = null)
    {
        _codexHome = CodexHome.Resolve(codexHome);
    }

    public string CodexHomePath => _codexHome;

    public IReadOnlyList<CodexThreadRecord> ListThreads(string? cwdFilter = null, int limit = 50)
    {
        var indexNames = ReadIndexNames();
        var threads = EnumerateRollouts()
            .Select(ReadMeta)
            .Where(t => t is not null)
            .Cast<CodexThreadRecord>()
            .Select(t => indexNames.TryGetValue(t.Id, out var indexedName) && string.IsNullOrWhiteSpace(t.Name)
                ? t with { Name = indexedName }
                : t)
            .Where(t => MatchesCwd(t.Cwd, cwdFilter))
            .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt ?? DateTimeOffset.MinValue)
            .Take(Math.Max(1, limit))
            .ToList();

        if (threads.Count > 0)
        {
            return threads;
        }

        return ReadIndexOnly(cwdFilter, limit);
    }

    public CodexThreadReadResult? ReadThread(string threadId, bool includeTurns)
    {
        foreach (var file in EnumerateRollouts())
        {
            var meta = ReadMeta(file);
            if (meta is null || !string.Equals(meta.Id, threadId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var items = includeTurns ? ReadItems(file, maxItems: 300) : [];
            return new CodexThreadReadResult(meta, items);
        }

        var indexOnly = ReadIndexOnly(null, 500)
            .FirstOrDefault(t => string.Equals(t.Id, threadId, StringComparison.OrdinalIgnoreCase));
        return indexOnly is null ? null : new CodexThreadReadResult(indexOnly, []);
    }

    private Dictionary<string, string> ReadIndexNames()
    {
        var path = Path.Combine(_codexHome, "session_index.jsonl");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return result;
        }

        foreach (var line in ReadSharedLines(path))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var id = GetString(root, "id");
                var name = GetString(root, "thread_name");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                {
                    result[id] = name;
                }
            }
            catch (JsonException)
            {
                // Ignore corrupt or partially-written lines.
            }
        }

        return result;
    }

    private IReadOnlyList<CodexThreadRecord> ReadIndexOnly(string? cwdFilter, int limit)
    {
        if (!string.IsNullOrWhiteSpace(cwdFilter))
        {
            return [];
        }

        var path = Path.Combine(_codexHome, "session_index.jsonl");
        if (!File.Exists(path))
        {
            return [];
        }

        var rows = new List<CodexThreadRecord>();
        foreach (var line in ReadSharedLines(path))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var id = GetString(root, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                rows.Add(new CodexThreadRecord(
                    id,
                    GetString(root, "thread_name"),
                    null,
                    null,
                    "session_index",
                    "persisted",
                    null,
                    ParseDate(GetString(root, "updated_at")),
                    "state-jsonl",
                    path));
            }
            catch (JsonException)
            {
                // Ignore corrupt or partially-written lines.
            }
        }

        return rows
            .OrderByDescending(r => r.UpdatedAt ?? DateTimeOffset.MinValue)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    private IEnumerable<string> EnumerateRollouts()
    {
        var sessions = Path.Combine(_codexHome, "sessions");
        return Directory.Exists(sessions)
            ? Directory.EnumerateFiles(sessions, "*.jsonl", SearchOption.AllDirectories)
            : [];
    }

    private static CodexThreadRecord? ReadMeta(string path)
    {
        foreach (var line in ReadSharedLines(path).Take(25))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!string.Equals(GetString(root, "type"), "session_meta", StringComparison.Ordinal))
                {
                    continue;
                }

                var payload = root.GetProperty("payload");
                var id = GetString(payload, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    return null;
                }

                var timestamp = ParseDate(GetString(payload, "timestamp")) ?? File.GetLastWriteTimeUtc(path);
                return new CodexThreadRecord(
                    id,
                    null,
                    null,
                    GetString(payload, "cwd"),
                    GetString(payload, "source") ?? GetString(payload, "originator"),
                    "persisted",
                    timestamp,
                    File.GetLastWriteTimeUtc(path),
                    "rollout-jsonl",
                    path);
            }
            catch (JsonException)
            {
                // Keep scanning in case the first line was partial.
            }
        }

        return null;
    }

    private static IReadOnlyList<CodexThreadItemRecord> ReadItems(string path, int maxItems)
    {
        var result = new List<CodexThreadItemRecord>();
        foreach (var line in ReadSharedLines(path))
        {
            if (result.Count >= maxItems)
            {
                break;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var type = GetString(root, "type") ?? "unknown";
                if (type == "session_meta")
                {
                    continue;
                }

                var timestamp = GetString(root, "timestamp") ?? string.Empty;
                var payload = root.TryGetProperty("payload", out var payloadElement)
                    ? payloadElement
                    : default;

                result.Add(new CodexThreadItemRecord(
                    timestamp,
                    DescribeType(type, payload),
                    ExtractPreview(payload)));
            }
            catch (JsonException)
            {
                // Ignore corrupt or partially-written lines.
            }
        }

        return result;
    }

    private static string DescribeType(string topLevelType, JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object)
        {
            var nestedType = GetString(payload, "type");
            if (!string.IsNullOrWhiteSpace(nestedType))
            {
                return $"{topLevelType}:{nestedType}";
            }

            if (payload.TryGetProperty("item", out var item))
            {
                var itemType = GetString(item, "type");
                if (!string.IsNullOrWhiteSpace(itemType))
                {
                    return $"{topLevelType}:{itemType}";
                }
            }
        }

        return topLevelType;
    }

    private static string ExtractPreview(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var property in new[] { "message", "text", "msg" })
        {
            var value = GetString(payload, property);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return SafeText.Preview(value);
            }
        }

        if (payload.TryGetProperty("item", out var item))
        {
            var role = GetString(item, "role");
            var text = ExtractTextFromContent(item);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return SafeText.Preview(string.IsNullOrWhiteSpace(role) ? text : $"{role}: {text}");
            }
        }

        return string.Empty;
    }

    private static string? ExtractTextFromContent(JsonElement item)
    {
        if (!item.TryGetProperty("content", out var content))
        {
            return GetString(item, "text");
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            var text = GetString(part, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text);
            }
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static bool MatchesCwd(string? actual, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        var actualFull = NormalizePath(actual);
        var filterFull = NormalizePath(filter);
        return string.Equals(actualFull, filterFull, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return long.TryParse(value, out var unixSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
            : null;
    }

    private static IEnumerable<string> ReadSharedLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
