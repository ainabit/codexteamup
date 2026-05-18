using System.Text.Json;

namespace CodexTeamUp.AgentBus;

public static class ExchangeTargetScope
{
    public const string System = "system";
    public const string Project = "project";
    public const string Agent = "agent";
}

public static class ExchangeEnvelopeStatus
{
    public const string Pending = "pending";
    public const string Leased = "leased";
    public const string Completed = "completed";
    public const string DeadLetter = "deadletter";
}

public static class ExchangeEnvelopeKind
{
    public const string Restart = "restart";
}

/// <summary>
/// Canonical durable envelope exchanged through CTU external channels.
/// </summary>
public sealed record ExchangeEnvelope
{
    public required string MessageId { get; init; }
    public required string Kind { get; init; }
    public required string TargetScope { get; init; }
    public string? TargetProject { get; init; }
    public string? TargetAgentId { get; init; }
    public string? TargetThreadName { get; init; }
    public required string CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? ResponseTo { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? NotBefore { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required string PayloadType { get; init; }
    public string? PayloadPath { get; init; }
    public JsonElement? Payload { get; init; }
    public int AttemptCount { get; init; }
    public string? LeaseOwner { get; init; }
    public DateTimeOffset? LeaseExpiresAt { get; init; }
    public string? LastError { get; init; }
    public required string Status { get; init; }
}

/// <summary>
/// Correlation state for exchange envelopes that belong to the same conversation or operation.
/// </summary>
public sealed record ExchangeCorrelationRecord
{
    public required string CorrelationId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<string> MessageIds { get; init; } = [];
    public string? LastMessageId { get; init; }
    public string? LastStatus { get; init; }
}

/// <summary>
/// Current known-good CTU runtime state for safe restart fallback.
/// </summary>
public sealed record KnownGoodRuntimeCheckpoint
{
    public required string Id { get; init; }
    public required string CheckoutCwd { get; init; }
    public required string BusRoot { get; init; }
    public required string RuntimeRoot { get; init; }
    public required string ToolsRoot { get; init; }
    public string? ControllerPluginPath { get; init; }
    public string? ControllerType { get; init; }
    public string? AppServerPluginPath { get; init; }
    public string? AppServerAdapterType { get; init; }
    public required DateTimeOffset VerifiedAt { get; init; }
    public bool IsVerified { get; init; }
    public string VerificationSource { get; init; } = "service_boot";
    public bool UseNoPublishOnRecovery { get; init; } = true;
}
