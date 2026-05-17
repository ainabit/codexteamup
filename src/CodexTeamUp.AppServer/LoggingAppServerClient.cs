using CodexTeamUp.Core;

namespace CodexTeamUp.AppServer;

/// <summary>
/// Diagnostic wrapper around the hard Codex Desktop app-server API boundary.
/// </summary>
public sealed class LoggingAppServerClient(IAppServerClient inner, CtuJsonLogger logger) : IAppServerClient
{
    public Task<AppServerCallResult> ProbeAsync(CancellationToken cancellationToken = default)
        => CallResult("probe", () => inner.ProbeAsync(cancellationToken));

    public Task<AppServerCallResult> CallAsync(string method, object? parameters, CancellationToken cancellationToken = default)
        => CallResult("call", () => inner.CallAsync(method, parameters, cancellationToken), new { method });

    public Task<AppServerCallResult> ListThreadsAsync(string? cwd, int limit, CancellationToken cancellationToken = default)
        => CallResult("list_threads", () => inner.ListThreadsAsync(cwd, limit, cancellationToken), new { cwd, limit });

    public Task<AppServerCallResult> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken = default)
        => CallResult("read_thread", () => inner.ReadThreadAsync(threadId, includeTurns, cancellationToken), new { threadId, includeTurns });

    public Task<AppServerCallResult> StartThreadAsync(
        string cwd,
        string? name,
        string? role,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default)
        => CallResult("start_thread", () => inner.StartThreadAsync(cwd, name, role, settings, cancellationToken), new { cwd, name, role, settings });

    public Task<AppServerCallResult> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default)
        => CallResult("resume_thread", () => inner.ResumeThreadAsync(threadId, cancellationToken), new { threadId });

    public Task<AppServerCallResult> SendTurnAsync(
        string threadId,
        string message,
        string? cwd = null,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default)
        => CallResult("send_turn", () => inner.SendTurnAsync(threadId, message, cwd, settings, cancellationToken), new
        {
            threadId,
            cwd,
            settings,
            messagePreview = SafeText.Preview(message, 160)
        });

    public Task<AppServerCallResult> ListTurnsAsync(string threadId, string sortDirection = "asc", int limit = 50, CancellationToken cancellationToken = default)
        => CallResult("list_turns", () => inner.ListTurnsAsync(threadId, sortDirection, limit, cancellationToken), new { threadId, sortDirection, limit });

    public async Task<TurnWaitResult> WaitForTurnAsync(string threadId, string turnId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.Now;
        logger.Info("api.wait_turn.start", new { threadId, turnId, timeoutMs = timeout.TotalMilliseconds });
        try
        {
            var result = await inner.WaitForTurnAsync(threadId, turnId, timeout, cancellationToken).ConfigureAwait(false);
            logger.Info("api.wait_turn.complete", new { threadId, turnId, result.Completed, result.Status, result.Error, elapsedMs = (DateTimeOffset.Now - startedAt).TotalMilliseconds });
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.Error("api.wait_turn.exception", ex, new { threadId, turnId, elapsedMs = (DateTimeOffset.Now - startedAt).TotalMilliseconds });
            return new TurnWaitResult(false, null, SafeText.Redact(ex.Message), null);
        }
    }

    private async Task<AppServerCallResult> CallResult(
        string operation,
        Func<Task<AppServerCallResult>> action,
        object? payload = null)
    {
        var startedAt = DateTimeOffset.Now;
        logger.Info($"api.{operation}.start", payload);
        try
        {
            var result = await action().ConfigureAwait(false);
            logger.Info($"api.{operation}.complete", new
            {
                payload,
                result.Succeeded,
                result.Error,
                elapsedMs = (DateTimeOffset.Now - startedAt).TotalMilliseconds
            });
            return result;
        }
        catch (Exception ex)
        {
            logger.Error($"api.{operation}.exception", ex, payload);
            return new AppServerCallResult(false, null, SafeText.Redact(ex.Message));
        }
    }
}
