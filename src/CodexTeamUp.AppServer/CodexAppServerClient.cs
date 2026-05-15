using System.Text.Json;

namespace CodexTeamUp.AppServer;

public sealed class CodexAppServerClient : IAppServerClient
{
    private readonly string _codexExecutable;
    private readonly string? _socketPath;
    private readonly string? _codexHome;
    private readonly WrapperPipeAppServerClient _wrapperClient;

    public CodexAppServerClient(string codexExecutable = "codex", string? socketPath = null, string? codexHome = null)
    {
        _codexExecutable = codexExecutable;
        _socketPath = socketPath;
        _codexHome = codexHome;
        _wrapperClient = new WrapperPipeAppServerClient(socketPath);
    }

    public async Task<AppServerCallResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        return await _wrapperClient.ProbeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppServerCallResult> ListThreadsAsync(string? cwd, int limit, CancellationToken cancellationToken = default)
    {
        return await _wrapperClient.ListThreadsAsync(cwd, limit, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppServerCallResult> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken = default)
    {
        return await _wrapperClient.ReadThreadAsync(threadId, includeTurns, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppServerCallResult> StartThreadAsync(
        string cwd,
        string? name,
        string? role,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        return await _wrapperClient.StartThreadAsync(cwd, name, role, settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppServerCallResult> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        return await _wrapperClient.ResumeThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppServerCallResult> SendTurnAsync(
        string threadId,
        string message,
        string? cwd = null,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        return await _wrapperClient.SendTurnAsync(threadId, message, cwd, settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppServerCallResult> ListTurnsAsync(
        string threadId,
        string sortDirection = "asc",
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        return await _wrapperClient.ListTurnsAsync(threadId, sortDirection, limit, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TurnWaitResult> WaitForTurnAsync(
        string threadId,
        string turnId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return await _wrapperClient.WaitForTurnAsync(threadId, turnId, timeout, cancellationToken).ConfigureAwait(false);
    }

    public Task<AppServerCallResult> CallAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        _ = _codexExecutable;
        _ = _codexHome;
        _ = _socketPath;
        return _wrapperClient.CallAsync(method, parameters, cancellationToken);
    }
}
