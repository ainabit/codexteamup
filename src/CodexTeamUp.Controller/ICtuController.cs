using System.Text.Json;

namespace CodexTeamUp.Controller;

public interface ICtuController
{
    IReadOnlyList<string> ToolNames { get; }

    CtuControllerRuntimeStatus Status { get; }

    Task<object> InvokeToolAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs one bounded background processing step for controller-owned channel/inbox work.
    /// </summary>
    Task RunStartupSweepAsync(CancellationToken cancellationToken = default);
}

public sealed record CtuControllerRuntimeStatus(
    string ActiveSource,
    string ActiveType,
    string? PluginPath,
    string? PluginType,
    DateTimeOffset LoadedAt,
    int ReloadCount,
    string? LastError,
    CtuControllerPolicyStatus Policy);
