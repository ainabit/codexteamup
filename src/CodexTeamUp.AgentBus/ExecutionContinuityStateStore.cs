namespace CodexTeamUp.AgentBus;

/// <summary>
/// Persistent store for execution continuity state used by controller guardian loops.
/// </summary>
public sealed class ExecutionContinuityStateStore
{
    public ExecutionContinuityStateStore(string busRoot, string? continuityStateDirectory = null)
    {
        var normalizedBusRoot = CtuProjectLayout.NormalizeBusRoot(busRoot);
        var configuredDirectory = BlankToNull(continuityStateDirectory);
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            RootDirectory = CtuProjectLayout.StateRootForBusRoot(normalizedBusRoot);
            StatesDirectory = Path.Combine(RootDirectory, "continuity", "states");
            return;
        }

        RootDirectory = CtuProjectLayout.ProjectRootForBusRoot(normalizedBusRoot);
        StatesDirectory = Path.IsPathRooted(configuredDirectory)
            ? configuredDirectory
            : Path.Combine(RootDirectory, configuredDirectory);
    }

    public string RootDirectory { get; }
    public string StatesDirectory { get; }

    public void Initialize()
    {
        Directory.CreateDirectory(StatesDirectory);
    }

    public IReadOnlyList<ExecutionContinuityState> ListStates(string? correlationId = null, string? state = null)
    {
        if (!Directory.Exists(StatesDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(StatesDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(file => JsonFile.Read<ExecutionContinuityState>(file))
            .Where(record => record is not null)
            .Cast<ExecutionContinuityState>()
            .Where(record =>
                (string.IsNullOrWhiteSpace(correlationId)
                    || string.Equals(record.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(state)
                    || string.Equals(record.State, state, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(record => record.EnteredAt)
            .ToList();
    }

    public ExecutionContinuityState? ReadLatest(string correlationId)
    {
        return ListStates(correlationId)
            .OrderByDescending(record => record.UpdatedAt)
            .FirstOrDefault();
    }

    public ExecutionContinuityState? ReadById(string stateId)
    {
        Initialize();
        var path = StatePath(stateId);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonFile.Read<ExecutionContinuityState>(path);
    }

    public ExecutionContinuityState Upsert(ExecutionContinuityState state)
    {
        Initialize();
        var normalized = state with
        {
            StateId = string.IsNullOrWhiteSpace(state.StateId)
                ? CreateStateId()
                : state.StateId,
            UpdatedAt = DateTimeOffset.UtcNow,
            CorrelationId = state.CorrelationId.Trim()
        };
        JsonFile.WriteAtomic(StatePath(normalized.StateId), normalized);
        return normalized;
    }

    public string CreateStateId()
    {
        return $"state-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..38];
    }

    public string StatePath(string stateId)
    {
        return Path.Combine(StatesDirectory, $"{stateId}.json");
    }

    private static string? BlankToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
