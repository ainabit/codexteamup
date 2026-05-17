using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CodexTeamUp.Core;

namespace CodexTeamUp.AppServer;

/// <summary>
/// App-server client that talks to the CodexTeamUp wrapper through a local named pipe.
/// </summary>
public sealed class WrapperPipeAppServerClient : IAppServerClient
{
    /// <summary>Default pipe name opened by the Codex Desktop CLI wrapper.</summary>
    public const string DefaultPipeName = "codexteamup-appserver";

    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _responseTimeout;

    public WrapperPipeAppServerClient(string? pipeName = null, TimeSpan? connectTimeout = null, TimeSpan? responseTimeout = null)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName.Trim();
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5);
        _responseTimeout = responseTimeout ?? ResolveResponseTimeout();
    }

    public async Task<AppServerCallResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        return await CallAsync("ctu/status", null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppServerCallResult> ListThreadsAsync(string? cwd, int limit, CancellationToken cancellationToken = default)
    {
        object parameters = string.IsNullOrWhiteSpace(cwd)
            ? new { limit, useStateDbOnly = true }
            : new { limit, cwd, useStateDbOnly = true };
        return await CallAsync("thread/list", parameters, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppServerCallResult> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken = default)
    {
        return await CallAsync("thread/read", new { threadId, includeTurns }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppServerCallResult> StartThreadAsync(
        string cwd,
        string? name,
        string? role,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        var developerInstructions = string.IsNullOrWhiteSpace(role)
            ? null
            : $"You are the CodexTeamUp agent role '{role}'. Keep your own thread context and communicate via .codexteamup/agentbus files when asked.";

        var parameters = new Dictionary<string, object?>
        {
            ["cwd"] = cwd,
            ["sandbox"] = "workspace-write",
            ["approvalPolicy"] = "on-request",
            ["developerInstructions"] = developerInstructions,
            ["threadSource"] = "user"
        };

        AddRuntimeSettings(parameters, settings);

        return await CallAsync("thread/start", parameters, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppServerCallResult> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        return await CallAsync("thread/resume", new { threadId }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AppServerCallResult> ResumeThreadForWakeupAsync(string threadId, CancellationToken cancellationToken)
    {
        var result = await CallAsync("thread/resume", new { threadId, excludeTurns = true }, cancellationToken)
            .ConfigureAwait(false);
        if (result.Succeeded || !IsThreadNotFound(result.Error))
        {
            return result;
        }

        return await CallAsync("thread/resume", new { threadId }, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AppServerCallResult> SendTurnAsync(
        string threadId,
        string message,
        string? cwd = null,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = BuildTurnStartParameters(threadId, message, cwd, settings);
        AppServerCallResult? last = null;

        for (var attempt = 0; attempt < 40; attempt++)
        {
            var resume = await ResumeThreadForWakeupAsync(threadId, cancellationToken).ConfigureAwait(false);
            if (!resume.Succeeded)
            {
                last = resume;
                if (!IsThreadTemporarilyUnavailable(resume.Error))
                {
                    return resume;
                }
            }

            last = await CallAsync("turn/start", parameters, cancellationToken).ConfigureAwait(false);
            if (last.Succeeded || !IsThreadTemporarilyUnavailable(last.Error))
            {
                return last;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        return last ?? new AppServerCallResult(false, null, $"Could not wake thread {threadId}.");
    }

    private static Dictionary<string, object?> BuildTurnStartParameters(
        string threadId,
        string message,
        string? cwd,
        AppServerAgentSettings? settings)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["threadId"] = threadId,
            ["input"] = new[] { new { type = "text", text = message } },
            ["approvalPolicy"] = "on-request"
        };

        if (!string.IsNullOrWhiteSpace(cwd))
        {
            parameters["cwd"] = cwd;
        }

        AddRuntimeSettings(parameters, settings);
        return parameters;
    }

    private static void AddRuntimeSettings(Dictionary<string, object?> parameters, AppServerAgentSettings? settings)
    {
        if (!string.IsNullOrWhiteSpace(settings?.Model))
        {
            parameters["model"] = settings.Model;
        }

        if (!string.IsNullOrWhiteSpace(settings?.ReasoningEffort))
        {
            parameters["effort"] = settings.ReasoningEffort;
        }
    }

    public async Task<AppServerCallResult> ListTurnsAsync(
        string threadId,
        string sortDirection = "asc",
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return await CallAsync("thread/turns/list", new { threadId, sortDirection, limit }, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TurnWaitResult> WaitForTurnAsync(
        string threadId,
        string turnId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var read = await ReadThreadAsync(threadId, includeTurns: true, cancellationToken).ConfigureAwait(false);
            if (!read.Succeeded || string.IsNullOrWhiteSpace(read.ResultJson))
            {
                return new TurnWaitResult(false, null, read.Error, read.ResultJson);
            }

            var status = TryFindTurnStatus(read.ResultJson, turnId);
            if (status is "completed" or "failed" or "cancelled")
            {
                return new TurnWaitResult(status == "completed", status, null, read.ResultJson);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        return new TurnWaitResult(false, "timeout", $"Timed out waiting for turn {turnId}.", null);
    }

    public async Task<AppServerCallResult> CallAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var line = BuildRequest(method, parameters);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_connectTimeout);

            await using var pipe = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
            {
                NewLine = "\n",
                AutoFlush = true
            };
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            await writer.WriteLineAsync(line).ConfigureAwait(false);
            using var responseCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            responseCancellation.CancelAfter(_responseTimeout);
            var response = await reader.ReadLineAsync(responseCancellation.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(response))
            {
                return new AppServerCallResult(false, null, "Wrapper pipe returned an empty response.");
            }

            return ParseResponse(response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AppServerCallResult(false, null, $"Timed out waiting for wrapper pipe '{_pipeName}' after {_responseTimeout.TotalSeconds:n0}s.");
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new AppServerCallResult(false, null, SafeText.Redact(ex.Message));
        }
    }

    private static string BuildRequest(string method, object? parameters)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("method", method);
            if (parameters is not null)
            {
                writer.WritePropertyName("params");
                JsonSerializer.Serialize(writer, parameters, JsonFile.Options);
            }
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static AppServerCallResult ParseResponse(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : error.GetRawText();
                return new AppServerCallResult(false, null, message);
            }

            if (root.TryGetProperty("result", out var result))
            {
                return new AppServerCallResult(true, result.GetRawText(), null);
            }

            return new AppServerCallResult(false, null, $"Unexpected wrapper response: {SafeText.Preview(response, 300)}");
        }
        catch (JsonException ex)
        {
            return new AppServerCallResult(false, null, $"Invalid wrapper JSON response: {ex.Message}");
        }
    }

    private static string? TryExtractThreadId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("thread", out var thread) && thread.TryGetProperty("id", out var nestedId))
            {
                return nestedId.GetString();
            }

            return root.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TimeSpan ResolveResponseTimeout()
    {
        var raw = Environment.GetEnvironmentVariable("CTU_APP_SERVER_RESPONSE_TIMEOUT_MS");
        return int.TryParse(raw, out var milliseconds) && milliseconds > 0
            ? TimeSpan.FromMilliseconds(milliseconds)
            : TimeSpan.FromSeconds(30);
    }

    private static bool IsThreadNotFound(string? error)
    {
        return !string.IsNullOrWhiteSpace(error)
            && error.Contains("thread not found", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsThreadTemporarilyUnavailable(string? error)
    {
        return IsThreadNotFound(error)
            || (!string.IsNullOrWhiteSpace(error)
                && (error.Contains("no rollout found for thread id", StringComparison.OrdinalIgnoreCase)
                    || (error.Contains("failed to read thread", StringComparison.OrdinalIgnoreCase)
                        && error.Contains("rollout", StringComparison.OrdinalIgnoreCase)
                        && error.Contains("is empty", StringComparison.OrdinalIgnoreCase))));
    }

    private static string? TryFindTurnStatus(string json, string turnId)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var thread = root.TryGetProperty("thread", out var threadElement) ? threadElement : root;
            if (!thread.TryGetProperty("turns", out var turns) || turns.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var turn in turns.EnumerateArray())
            {
                if (turn.TryGetProperty("id", out var id)
                    && string.Equals(id.GetString(), turnId, StringComparison.Ordinal)
                    && turn.TryGetProperty("status", out var status))
                {
                    return status.ValueKind == JsonValueKind.String ? status.GetString() : status.GetRawText();
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
