using System.Text;
using System.Text.Json;

namespace CodexTeamUp.CodexWrapper;

/// <summary>
/// Contains JSON-RPC transformations used by the Codex Desktop stdio wrapper.
/// </summary>
public static class WrapperProtocol
{
    /// <summary>
    /// Prefix used for bridge-owned requests so responses can be hidden from the Desktop client.
    /// </summary>
    public const string BridgeRequestIdPrefix = "ctu:";

    /// <summary>
    /// Returns true when a JSON-RPC response id belongs to CodexTeamUp.
    /// </summary>
    public static bool IsBridgeResponseId(string? id)
    {
        return id?.StartsWith(BridgeRequestIdPrefix, StringComparison.Ordinal) == true;
    }

    /// <summary>
    /// Builds a JSON-RPC request for the app-server from a bridge-side request line.
    /// </summary>
    public static string BuildAppServerRequest(string requestId, JsonElement clientRequest)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", requestId);
            writer.WriteString("method", clientRequest.GetProperty("method").GetString());
            if (clientRequest.TryGetProperty("params", out var parameters))
            {
                writer.WritePropertyName("params");
                parameters.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Rewrites Desktop turn-list requests to chronological order when the caller opts into the mitigation.
    /// </summary>
    public static string RewriteTurnsListAscending(string line, bool enabled, Action? onRewrite = null)
    {
        if (!enabled)
        {
            return line;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var methodElement)
                || methodElement.ValueKind != JsonValueKind.String
                || !string.Equals(methodElement.GetString(), "thread/turns/list", StringComparison.Ordinal))
            {
                return line;
            }

            if (!root.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
            {
                return line;
            }

            if (parameters.TryGetProperty("sortDirection", out var sortDirection)
                && sortDirection.ValueKind != JsonValueKind.Null)
            {
                return line;
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in root.EnumerateObject())
                {
                    if (property.NameEquals("params"))
                    {
                        writer.WritePropertyName("params");
                        writer.WriteStartObject();
                        foreach (var paramProperty in parameters.EnumerateObject())
                        {
                            paramProperty.WriteTo(writer);
                        }
                        writer.WriteString("sortDirection", "asc");
                        writer.WriteEndObject();
                    }
                    else
                    {
                        property.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }

            onRewrite?.Invoke();
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return line;
        }
    }

    /// <summary>
    /// Adds a best-effort start timestamp to live turn-start notifications when the app-server
    /// initially emits the turn with a null timestamp. Persisted history remains authoritative.
    /// </summary>
    public static string StampTurnStartedAt(string line, bool enabled, Func<long> unixTimeProvider, Action? onRewrite = null)
    {
        if (!enabled)
        {
            return line;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var methodElement)
                || methodElement.ValueKind != JsonValueKind.String
                || !string.Equals(methodElement.GetString(), "turn/started", StringComparison.Ordinal))
            {
                return line;
            }

            if (!root.TryGetProperty("params", out var parameters)
                || parameters.ValueKind != JsonValueKind.Object
                || !parameters.TryGetProperty("turn", out var turn)
                || turn.ValueKind != JsonValueKind.Object)
            {
                return line;
            }

            if (turn.TryGetProperty("startedAt", out var startedAt)
                && startedAt.ValueKind != JsonValueKind.Null)
            {
                return line;
            }

            var timestamp = unixTimeProvider();
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in root.EnumerateObject())
                {
                    if (property.NameEquals("params"))
                    {
                        writer.WritePropertyName("params");
                        writer.WriteStartObject();
                        foreach (var paramProperty in parameters.EnumerateObject())
                        {
                            if (paramProperty.NameEquals("turn"))
                            {
                                writer.WritePropertyName("turn");
                                writer.WriteStartObject();
                                var wroteStartedAt = false;
                                foreach (var turnProperty in turn.EnumerateObject())
                                {
                                    if (turnProperty.NameEquals("startedAt"))
                                    {
                                        writer.WriteNumber("startedAt", timestamp);
                                        wroteStartedAt = true;
                                    }
                                    else
                                    {
                                        turnProperty.WriteTo(writer);
                                    }
                                }

                                if (!wroteStartedAt)
                                {
                                    writer.WriteNumber("startedAt", timestamp);
                                }

                                writer.WriteEndObject();
                            }
                            else
                            {
                                paramProperty.WriteTo(writer);
                            }
                        }

                        writer.WriteEndObject();
                    }
                    else
                    {
                        property.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }

            onRewrite?.Invoke();
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return line;
        }
    }

    /// <summary>
    /// Extracts the JSON-RPC id and method from a line without throwing for malformed input.
    /// </summary>
    public static WrapperRpcSummary SummarizeJsonLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var method = root.TryGetProperty("method", out var methodElement) && methodElement.ValueKind == JsonValueKind.String
                ? methodElement.GetString()
                : null;
            var id = root.TryGetProperty("id", out var idElement)
                ? IdToString(idElement)
                : null;
            var hasParams = root.TryGetProperty("params", out _);
            return new WrapperRpcSummary(id, method, hasParams);
        }
        catch (JsonException)
        {
            return new WrapperRpcSummary(null, "[non-json]", false);
        }
    }

    private static string? IdToString(JsonElement idElement)
    {
        return idElement.ValueKind switch
        {
            JsonValueKind.String => idElement.GetString(),
            JsonValueKind.Number => idElement.GetRawText(),
            _ => idElement.GetRawText()
        };
    }
}

/// <summary>
/// Summary of a single JSON-RPC line observed by the wrapper.
/// </summary>
public readonly record struct WrapperRpcSummary(string? Id, string? Method, bool HasParams);
