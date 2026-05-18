namespace CodexTeamUp.AgentBus;

/// <summary>
/// Registered CodexTeamUp agent and its preferred Desktop thread binding.
/// </summary>
public sealed record AgentDefinition
{
    public required string Id { get; init; }
    public required string Role { get; init; }
    public string? DisplayName { get; init; }
    public string? ThreadId { get; init; }
    public string? Cwd { get; init; }
    public IReadOnlyList<string> AllowedPaths { get; init; } = [];
    public IReadOnlyList<string> InstructionFiles { get; init; } = [];
    public string? ReturnTo { get; init; }
    public string? NotifyPolicy { get; init; }
    public string? DefaultPromptPrefix { get; init; }
    public string? Model { get; init; }
    public string? ReasoningEffort { get; init; }
    public string? Speed { get; init; }
    public string? Status { get; init; }
}

/// <summary>
/// File format for .codexteamup/agentbus/agents.json.
/// </summary>
public sealed record AgentRegistryDocument
{
    public int Version { get; init; } = 1;
    public IReadOnlyList<AgentDefinition> Agents { get; init; } = [];
}

/// <summary>
/// Durable work item exchanged between visible Codex threads.
/// </summary>
public sealed record AgentBusTask
{
    public required string Id { get; init; }
    public required string Project { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ClaimedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? Owner { get; init; }
    public string? Cwd { get; init; }
    public required string Title { get; init; }
    public required string Prompt { get; init; }
    public string? PromptFile { get; init; }
    public IReadOnlyList<string> AllowedPaths { get; init; } = [];
    public string? ReturnTo { get; init; }
    public bool ResultExpected { get; init; } = true;
    public string? Priority { get; init; }
    public TimeSpan? Timeout { get; init; }
    public int DeliveryAttempts { get; init; }
    public DateTimeOffset? LastDeliveryAttemptAt { get; init; }
    public string? LastDeliveryError { get; init; }
}

/// <summary>
/// Result written by a worker agent after completing or failing a task.
/// </summary>
public sealed record AgentBusResult
{
    public required string Id { get; init; }
    public required string TaskId { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required string Summary { get; init; }
    public string? Commit { get; init; }
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
    public IReadOnlyList<string> Tests { get; init; } = [];
    public IReadOnlyList<string> Artifacts { get; init; } = [];
    public IReadOnlyList<string> OpenQuestions { get; init; } = [];
    public string? NextSuggestedAction { get; init; }
    public int NotifyAttempts { get; init; }
    public DateTimeOffset? LastNotifyAttemptAt { get; init; }
    public DateTimeOffset? LastNotifiedAt { get; init; }
    public string? LastNotifyError { get; init; }
}

/// <summary>
/// Append-only audit event for AgentBus state transitions.
/// </summary>
public sealed record AgentBusEvent
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Type { get; init; }
    public string? TaskId { get; init; }
    public string? ResultId { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Message { get; init; }
    public object? Payload { get; init; }
}

/// <summary>
/// Summary returned after deleting AgentBus task state for test resets.
/// </summary>
public sealed record AgentBusClearResult(
    int DeletedTasks,
    int DeletedResults,
    bool IncludedResults,
    DateTimeOffset ClearedAt);
