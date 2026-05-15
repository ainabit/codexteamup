using System.Text.Json;

namespace CodexTeamUp.AppServer;

public static class AppServerThreadMapper
{
    public static IReadOnlyList<CodexThreadRecord> ParseListResult(string resultJson)
    {
        using var doc = JsonDocument.Parse(resultJson);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return data.EnumerateArray().Select(ParseThread).ToList();
    }

    public static CodexThreadReadResult? ParseReadResult(string resultJson)
    {
        using var doc = JsonDocument.Parse(resultJson);
        var root = doc.RootElement;
        var threadElement = root.TryGetProperty("thread", out var thread)
            ? thread
            : root;
        var record = ParseThread(threadElement);
        var items = new List<CodexThreadItemRecord>();

        if (threadElement.TryGetProperty("turns", out var turns) && turns.ValueKind == JsonValueKind.Array)
        {
            foreach (var turn in turns.EnumerateArray())
            {
                var turnId = GetString(turn, "id") ?? "turn";
                var status = GetString(turn, "status") ?? "";
                items.Add(new CodexThreadItemRecord("", $"turn:{status}", turnId));
            }
        }

        return new CodexThreadReadResult(record, items);
    }

    private static CodexThreadRecord ParseThread(JsonElement thread)
    {
        var id = GetString(thread, "id") ?? GetString(thread, "threadId") ?? "";
        return new CodexThreadRecord(
            id,
            GetString(thread, "name"),
            GetString(thread, "preview"),
            GetString(thread, "cwd"),
            GetString(thread, "source"),
            ParseStatus(thread),
            ParseUnix(GetString(thread, "createdAt")),
            ParseUnix(GetString(thread, "updatedAt")),
            "app-server",
            GetString(thread, "path"));
    }

    private static string? ParseStatus(JsonElement thread)
    {
        if (!thread.TryGetProperty("status", out var status))
        {
            return null;
        }

        if (status.ValueKind == JsonValueKind.String)
        {
            return status.GetString();
        }

        if (status.ValueKind == JsonValueKind.Object)
        {
            return GetString(status, "type") ?? status.GetRawText();
        }

        return status.ToString();
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

    private static DateTimeOffset? ParseUnix(string? value)
    {
        return long.TryParse(value, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
    }
}
