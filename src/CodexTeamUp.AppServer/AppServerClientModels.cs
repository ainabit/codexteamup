using System.Text.Json;

namespace CodexTeamUp.AppServer;

/// <summary>
/// Abstraction over the experimental Codex app-server transport used by the bridge.
/// </summary>
public interface IAppServerClient
{
    /// <summary>Checks whether the configured transport can reach a live app-server.</summary>
    Task<AppServerCallResult> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a raw app-server JSON-RPC method call.</summary>
    Task<AppServerCallResult> CallAsync(string method, object? parameters, CancellationToken cancellationToken = default);

    /// <summary>Lists known Codex threads.</summary>
    Task<AppServerCallResult> ListThreadsAsync(string? cwd, int limit, CancellationToken cancellationToken = default);

    /// <summary>Reads a single Codex thread.</summary>
    Task<AppServerCallResult> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken = default);

    /// <summary>Starts a new Codex thread.</summary>
    Task<AppServerCallResult> StartThreadAsync(
        string cwd,
        string? name,
        string? role,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default);

    /// <summary>Loads a persisted thread into the live Desktop app-server registry.</summary>
    Task<AppServerCallResult> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default);

    /// <summary>Starts a turn in a Codex thread.</summary>
    Task<AppServerCallResult> SendTurnAsync(
        string threadId,
        string message,
        string? cwd = null,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists turns for a thread.</summary>
    Task<AppServerCallResult> ListTurnsAsync(string threadId, string sortDirection = "asc", int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>Waits until a turn appears completed or failed according to app-server reads.</summary>
    Task<TurnWaitResult> WaitForTurnAsync(string threadId, string turnId, TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional model/runtime settings passed to Codex app-server thread and turn calls.
/// </summary>
public sealed record AppServerAgentSettings(string? Model, string? ReasoningEffort);

/// <summary>
/// Result of waiting for a Codex turn.
/// </summary>
public sealed record TurnWaitResult(bool Completed, string? Status, string? Error, string? ThreadJson);

/// <summary>
/// JSON-RPC envelope returned by the wrapper side-channel.
/// </summary>
public sealed record AppServerRpcEnvelope(JsonElement? Result, AppServerRpcError? Error);

/// <summary>
/// JSON-RPC error returned by the Codex app-server.
/// </summary>
public sealed record AppServerRpcError(int Code, string Message);
