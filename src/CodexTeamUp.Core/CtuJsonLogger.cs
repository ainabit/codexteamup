using System.Text.Json;

namespace CodexTeamUp.Core;

/// <summary>
/// Small JSONL logger for CTU runtime diagnostics. Logging must never break coordination.
/// </summary>
public sealed class CtuJsonLogger
{
    private static readonly JsonSerializerOptions LogOptions = new(JsonFile.Options) { WriteIndented = false };
    private readonly object _gate = new();
    private readonly string _path;
    private readonly string _humanPath;

    public CtuJsonLogger(string path)
    {
        _path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path) ?? Environment.CurrentDirectory);
        _humanPath = System.IO.Path.ChangeExtension(_path, ".log");
    }

    public string Path => _path;
    public string HumanPath => _humanPath;

    public void Info(string eventName, object? payload = null) => Write("info", eventName, payload, null);

    public void Error(string eventName, Exception exception, object? payload = null)
    {
        Write("error", eventName, payload, exception);
    }

    private void Write(string level, string eventName, object? payload, Exception? exception)
    {
        try
        {
            var safePayload = RedactPayload(payload);
            var safePayloadJson = safePayload is null ? "null" : JsonSerializer.Serialize(safePayload, LogOptions);
            var entry = new
            {
                timestamp = DateTimeOffset.Now,
                level,
                eventName,
                payload = safePayload,
                error = exception is null
                    ? null
                    : new
                    {
                        type = exception.GetType().FullName,
                        message = SafeText.Redact(exception.Message),
                        stackTrace = SafeText.Redact(exception.StackTrace ?? "")
                    }
            };

            var line = JsonSerializer.Serialize(entry, LogOptions);
            var humanLine = exception is null
                ? $"{entry.timestamp:O} [{level}] {eventName} {safePayloadJson}"
                : $"{entry.timestamp:O} [{level}] {eventName} {SafeText.Redact(exception.GetType().Name)}: {SafeText.Redact(exception.Message)} {safePayloadJson}";
            lock (_gate)
            {
                File.AppendAllText(_path, line + Environment.NewLine);
                File.AppendAllText(_humanPath, humanLine + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics are best-effort only.
        }
    }

    private static object? RedactPayload(object? payload)
    {
        if (payload is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(payload, LogOptions);
        var redacted = SafeText.Redact(json);
        try
        {
            using var document = JsonDocument.Parse(redacted);
            return document.RootElement.Clone();
        }
        catch
        {
            return SafeText.Preview(redacted, 400);
        }
    }
}
