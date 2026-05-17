using System.Text.Json;

namespace CodexTeamUp.AppServer;

/// <summary>
/// Small interpreted adapter policy for Desktop lifecycle behavior that should change without rebuilding the service.
/// </summary>
public sealed class PolicyAppServerClient(IAppServerClient inner, string? policyPath = null, IReadOnlyDictionary<string, string>? options = null) : IAppServerClient
{
    private readonly object _gate = new();
    private readonly IReadOnlyDictionary<string, string> _options = options ?? new Dictionary<string, string>();
    private AdapterPolicy? _policy;
    private DateTimeOffset _policyLoadedAt;
    private DateTime _policyLastWriteUtc;

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
        var policy = LoadPolicy();
        var parameters = BuildTurnStartParameters(threadId, message, cwd, settings);
        AppServerCallResult? last = null;

        for (var attempt = 0; attempt < policy.SendTurn.Attempts; attempt++)
        {
            foreach (var step in policy.SendTurn.Steps)
            {
                last = await ExecuteSendTurnStepAsync(step, threadId, parameters, cancellationToken).ConfigureAwait(false);
                if (last.Succeeded)
                {
                    if (step.Equals("turnStart", StringComparison.OrdinalIgnoreCase))
                    {
                        return last;
                    }

                    continue;
                }

                if (!IsTemporaryError(last.Error, policy.SendTurn.TemporaryErrors))
                {
                    return last;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(policy.SendTurn.DelayMs), cancellationToken).ConfigureAwait(false);
        }

        return last ?? new AppServerCallResult(false, null, $"Could not wake thread {threadId}.");
    }

    public Task<AppServerCallResult> ListTurnsAsync(string threadId, string sortDirection = "asc", int limit = 50, CancellationToken cancellationToken = default)
        => inner.ListTurnsAsync(threadId, sortDirection, limit, cancellationToken);

    public Task<TurnWaitResult> WaitForTurnAsync(string threadId, string turnId, TimeSpan timeout, CancellationToken cancellationToken = default)
        => inner.WaitForTurnAsync(threadId, turnId, timeout, cancellationToken);

    private async Task<AppServerCallResult> ExecuteSendTurnStepAsync(
        string step,
        string threadId,
        Dictionary<string, object?> turnStartParameters,
        CancellationToken cancellationToken)
    {
        return step.Trim().ToLowerInvariant() switch
        {
            "resumeexcludeturns" => await inner.CallAsync("thread/resume", new { threadId, excludeTurns = true }, cancellationToken).ConfigureAwait(false),
            "resume" => await inner.CallAsync("thread/resume", new { threadId }, cancellationToken).ConfigureAwait(false),
            "turnstart" => await inner.CallAsync("turn/start", turnStartParameters, cancellationToken).ConfigureAwait(false),
            _ => new AppServerCallResult(false, null, $"Unknown app-server adapter policy step: {step}")
        };
    }

    private AdapterPolicy LoadPolicy()
    {
        var configuredPath = policyPath ?? ReadOption("policyPath") ?? ReadOption("scriptPath");
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return AdapterPolicy.Default;
        }

        var fullPath = Path.GetFullPath(configuredPath);
        var lastWrite = File.Exists(fullPath)
            ? File.GetLastWriteTimeUtc(fullPath)
            : throw new FileNotFoundException("App-server adapter policy file was not found.", fullPath);

        lock (_gate)
        {
            if (_policy is not null && lastWrite == _policyLastWriteUtc)
            {
                return _policy;
            }

            using var stream = File.OpenRead(fullPath);
            _policy = JsonSerializer.Deserialize<AdapterPolicy>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? AdapterPolicy.Default;
            _policy = _policy.Normalized();
            _policyLastWriteUtc = lastWrite;
            _policyLoadedAt = DateTimeOffset.Now;
            return _policy;
        }
    }

    private string? ReadOption(string name)
    {
        return _options.TryGetValue(name, out var value) ? value : null;
    }

    private static bool IsTemporaryError(string? error, IReadOnlyList<string> temporaryErrors)
    {
        return !string.IsNullOrWhiteSpace(error)
            && temporaryErrors.Any(pattern => error.Contains(pattern, StringComparison.OrdinalIgnoreCase));
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

    private sealed record AdapterPolicy(SendTurnPolicy SendTurn)
    {
        public static AdapterPolicy Default { get; } = new(new SendTurnPolicy(
            Attempts: 40,
            DelayMs: 250,
            Steps: ["resumeExcludeTurns", "turnStart"],
            TemporaryErrors: ["thread not found", "no rollout found for thread id"]));

        public AdapterPolicy Normalized()
        {
            return new AdapterPolicy(SendTurn.Normalized());
        }
    }

    private sealed record SendTurnPolicy(
        int Attempts,
        int DelayMs,
        IReadOnlyList<string> Steps,
        IReadOnlyList<string> TemporaryErrors)
    {
        public SendTurnPolicy Normalized()
        {
            return new SendTurnPolicy(
                Attempts > 0 ? Attempts : AdapterPolicy.Default.SendTurn.Attempts,
                DelayMs > 0 ? DelayMs : AdapterPolicy.Default.SendTurn.DelayMs,
                Steps.Count > 0 ? Steps : AdapterPolicy.Default.SendTurn.Steps,
                TemporaryErrors.Count > 0 ? TemporaryErrors : AdapterPolicy.Default.SendTurn.TemporaryErrors);
        }
    }
}
