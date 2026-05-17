using CodexTeamUp.AppServer;

namespace CodexTeamUp.AppServer.WrapperPipeAdapter;

public sealed class WrapperPipeAdapterPlugin : IAppServerClientPlugin
{
    public string Name => "wrapper-pipe-adapter";

    public string Version => "1.0.0";

    public IAppServerClient Create(AppServerClientPluginContext context)
    {
        return new WrapperPipeAdapterClient(
            new WrapperPipeAppServerClient(context.PipeName),
            ReadInt(context.Options, "sendTurnAttempts", 40),
            TimeSpan.FromMilliseconds(ReadInt(context.Options, "sendTurnDelayMs", 250)));
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> options, string name, int fallback)
    {
        return options.TryGetValue(name, out var raw) && int.TryParse(raw, out var value) && value > 0
            ? value
            : fallback;
    }
}

public sealed class WrapperPipeAdapterClient(
    WrapperPipeAppServerClient inner,
    int sendTurnAttempts,
    TimeSpan sendTurnDelay) : IAppServerClient
{
    public Task<AppServerCallResult> ProbeAsync(CancellationToken cancellationToken = default)
        => inner.ProbeAsync(cancellationToken);

    public Task<AppServerCallResult> CallAsync(string method, object? parameters, CancellationToken cancellationToken = default)
        => inner.CallAsync(method, parameters, cancellationToken);

    public Task<AppServerCallResult> ListThreadsAsync(string? cwd, int limit, CancellationToken cancellationToken = default)
        => inner.ListThreadsAsync(cwd, limit, cancellationToken);

    public Task<AppServerCallResult> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken = default)
        => inner.ReadThreadAsync(threadId, includeTurns, cancellationToken);

    public Task<AppServerCallResult> StartThreadAsync(
        string cwd,
        string? name,
        string? role,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default)
        => inner.StartThreadAsync(cwd, name, role, settings, cancellationToken);

    public Task<AppServerCallResult> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default)
        => inner.ResumeThreadAsync(threadId, cancellationToken);

    public async Task<AppServerCallResult> SendTurnAsync(
        string threadId,
        string message,
        string? cwd = null,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        var turnParameters = BuildTurnStartParameters(threadId, message, cwd, settings);
        AppServerCallResult? last = null;

        for (var attempt = 0; attempt < sendTurnAttempts; attempt++)
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

            last = await inner.CallAsync("turn/start", turnParameters, cancellationToken).ConfigureAwait(false);
            if (last.Succeeded || !IsThreadTemporarilyUnavailable(last.Error))
            {
                return last;
            }

            await Task.Delay(sendTurnDelay, cancellationToken).ConfigureAwait(false);
        }

        return last ?? new AppServerCallResult(false, null, $"Could not wake thread {threadId}.");
    }

    public Task<AppServerCallResult> ListTurnsAsync(string threadId, string sortDirection = "asc", int limit = 50, CancellationToken cancellationToken = default)
        => inner.ListTurnsAsync(threadId, sortDirection, limit, cancellationToken);

    public Task<TurnWaitResult> WaitForTurnAsync(string threadId, string turnId, TimeSpan timeout, CancellationToken cancellationToken = default)
        => inner.WaitForTurnAsync(threadId, turnId, timeout, cancellationToken);

    private async Task<AppServerCallResult> ResumeThreadForWakeupAsync(string threadId, CancellationToken cancellationToken)
    {
        var result = await inner.CallAsync("thread/resume", new { threadId, excludeTurns = true }, cancellationToken)
            .ConfigureAwait(false);
        if (result.Succeeded || !IsThreadNotFound(result.Error))
        {
            return result;
        }

        return await inner.CallAsync("thread/resume", new { threadId }, cancellationToken)
            .ConfigureAwait(false);
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

        if (!string.IsNullOrWhiteSpace(settings?.Model))
        {
            parameters["model"] = settings.Model;
        }

        if (!string.IsNullOrWhiteSpace(settings?.ReasoningEffort))
        {
            parameters["effort"] = settings.ReasoningEffort;
        }

        return parameters;
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
}
