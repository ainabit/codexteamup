using System.Text.Json;
using System.Text.RegularExpressions;
using CodexTeamUp.Core;

namespace CodexTeamUp.Controller;

public sealed record CtuControllerPolicy(
    string TeamSendMessageDefaultDispatchMode,
    int WakeupTimeoutSeconds,
    int WaitResultTimeoutCapSeconds,
    bool EnsureThreadNameBeforePrime,
    bool PrimePromptStartsWithAgentId,
    string? ContinuityGuardianAgentId = null,
    string? ContinuityGuardianDisplayName = null,
    string? ContinuityStateDirectory = null,
    int StaleClaimedTaskRecoverySeconds = 10)
{
    public static CtuControllerPolicy Default { get; } = new("enqueue", 8, 10, true, true);
}

public sealed record CtuControllerPolicyStatus(
    string ActiveSource,
    string ActiveType,
    string? PolicyPath,
    DateTimeOffset LoadedAt,
    int ReloadCount,
    string? LastError,
    CtuControllerPolicy Policy);

public sealed class ReloadableCtuControllerPolicy
{
    private readonly object _gate = new();
    private readonly CtuJsonLogger? _logger;
    private string? _policyPath;
    private CtuControllerPolicy _policy = CtuControllerPolicy.Default;
    private DateTimeOffset _loadedAt = DateTimeOffset.Now;
    private int _reloadCount;
    private string? _lastError;

    public ReloadableCtuControllerPolicy(string? policyPath = null, CtuJsonLogger? logger = null)
    {
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(policyPath))
        {
            Reload(policyPath);
        }
    }

    public CtuControllerPolicy Policy
    {
        get
        {
            lock (_gate)
            {
                return _policy;
            }
        }
    }

    public CtuControllerPolicyStatus Status
    {
        get
        {
            lock (_gate)
            {
                return new CtuControllerPolicyStatus(
                    string.IsNullOrWhiteSpace(_policyPath) ? "built-in" : "policy",
                    nameof(CtuControllerPolicy),
                    _policyPath,
                    _loadedAt,
                    _reloadCount,
                    _lastError,
                    _policy);
            }
        }
    }

    public CtuControllerPolicyStatus Reload(string? policyPath)
    {
        lock (_gate)
        {
            _reloadCount++;
            try
            {
                if (string.IsNullOrWhiteSpace(policyPath))
                {
                    _policyPath = null;
                    _policy = CtuControllerPolicy.Default;
                    _loadedAt = DateTimeOffset.Now;
                    _lastError = null;
                    _logger?.Info("controller_policy.reload.built_in");
                    return Status;
                }

                var fullPath = Path.GetFullPath(policyPath);
                var json = File.ReadAllText(fullPath);
                var loaded = JsonSerializer.Deserialize<CtuControllerPolicy>(json, JsonFile.Options)
                    ?? throw new InvalidOperationException("Controller policy file did not contain a policy object.");
                _policy = Normalize(loaded);
                _policyPath = fullPath;
                _loadedAt = DateTimeOffset.Now;
                _lastError = null;
                _logger?.Info("controller_policy.reload.policy", new { policyPath = fullPath, _policy });
            }
            catch (Exception ex)
            {
                _lastError = SafeText.Redact(ex.Message);
                _logger?.Error("controller_policy.reload.failed", ex, new { policyPath });
            }

            return Status;
        }
    }

    private static CtuControllerPolicy Normalize(CtuControllerPolicy policy)
    {
        var dispatchMode = string.IsNullOrWhiteSpace(policy.TeamSendMessageDefaultDispatchMode)
            ? CtuControllerPolicy.Default.TeamSendMessageDefaultDispatchMode
            : policy.TeamSendMessageDefaultDispatchMode.Trim().ToLowerInvariant();
        if (dispatchMode is not ("enqueue" or "inline"))
        {
            dispatchMode = CtuControllerPolicy.Default.TeamSendMessageDefaultDispatchMode;
        }

        return policy with
            {
                TeamSendMessageDefaultDispatchMode = dispatchMode,
                WakeupTimeoutSeconds = Math.Clamp(policy.WakeupTimeoutSeconds, 1, 10),
                WaitResultTimeoutCapSeconds = Math.Clamp(policy.WaitResultTimeoutCapSeconds, 1, 10),
                ContinuityGuardianAgentId = NormalizeAgentId(policy.ContinuityGuardianAgentId),
                ContinuityGuardianDisplayName = BlankToNull(policy.ContinuityGuardianDisplayName)
                    ?? NormalizeAgentId(policy.ContinuityGuardianAgentId)
                    ?? "ctu/reviewer",
                ContinuityStateDirectory = NormalizeStateDirectory(policy.ContinuityStateDirectory),
                StaleClaimedTaskRecoverySeconds = Math.Clamp(policy.StaleClaimedTaskRecoverySeconds, 5, 300)
            };
    }

    private static string? NormalizeAgentId(string? value)
    {
        var normalized = BlankToNull(value);
        if (normalized is null)
        {
            return null;
        }

        return Regex.Replace(normalized, @"\\", "/", RegexOptions.None, TimeSpan.FromMilliseconds(100));
    }

    private static string? NormalizeStateDirectory(string? value)
    {
        return BlankToNull(value);
    }

    private static string? BlankToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
