namespace CodexTeamUp.AgentBus;

/// <summary>
/// Canonical execution continuity states for cross-turn continuation and guardian evaluation.
/// </summary>
public static class ExecutionContinuityStateKind
{
    public const string Completed = "completed";
    public const string DelegatedNextTask = "delegated_next_task";
    public const string VerificationStarted = "verification_started";
    public const string BlockedNeedsHuman = "blocked_needs_human";
    public const string PendingReview = "pending_review";
    public const string QueuedForDispatch = "queued_for_dispatch";
    public const string DispatchRetryPending = "dispatch_retry_pending";
    public const string WaitingOnWorker = "waiting_on_worker";
    public const string NotifyRetryPending = "notify_retry_pending";
    public const string SupersedeEvaluation = "supersede_evaluation";
    public const string ResumePendingExternal = "resume_pending_external";

    public static IReadOnlySet<string> TerminalStates { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Completed,
        DelegatedNextTask,
        VerificationStarted,
        BlockedNeedsHuman
    };
}

/// <summary>
/// Durable continuity state tracked by the controller guardian loop.
/// </summary>
public sealed record ExecutionContinuityState
{
    public required string StateId { get; init; }
    public required string CorrelationId { get; init; }
    public string? InitiativeId { get; init; }
    public string? TaskChainId { get; init; }
    public bool ShouldContinue { get; init; }
    public required string State { get; init; }
    public required DateTimeOffset EnteredAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public string? GuardianAgentId { get; init; }
    public string? GuardianDisplayName { get; init; }
    public string? CurrentOwner { get; init; }
    public string? LastOutcomeKind { get; init; }
    public string? LastOutcomeRef { get; init; }
    public string? NextAction { get; init; }
    public string? NextActionKind { get; init; }
    public string? NextActionRef { get; init; }
    public string? CurrentTargetAgentId { get; init; }
    public string? CurrentTargetDisplayName { get; init; }
    public ExecutionContinuityAttemptMetadata? AttemptMetadata { get; init; }
    public int AttemptCount { get; init; }
    public int MaxAttempts { get; init; }
    public DateTimeOffset? LastAttemptAt { get; init; }
    public string? LastError { get; init; }
    public string? BlockingOwner { get; init; }
    public string? BlockingReason { get; init; }
    public string? ResumeCorrelationId { get; init; }
    public string? SupersedesStateId { get; init; }
}

/// <summary>
/// Durable attempt metadata for controller-owned continuity retries.
/// </summary>
public sealed record ExecutionContinuityAttemptMetadata
{
    public int Attempt { get; init; }
    public int MaxAttempts { get; init; }
    public DateTimeOffset? LastAttemptAt { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
    public string? FailureReason { get; init; }
}
