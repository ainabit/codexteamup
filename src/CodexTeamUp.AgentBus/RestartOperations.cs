using System;
using System.IO;
using CodexTeamUp.Core;

namespace CodexTeamUp.AgentBus;

public static class RestartOperationKind
{
    public const string CtuDesktopRestart = "ctu.desktop-restart";
}

public static class RestartOperationStatus
{
    public const string Prepared = "prepared";
    public const string HelperStarted = "helper_started";
    public const string StoppingSource = "stopping_source";
    public const string StartingTarget = "starting_target";
    public const string TargetHealthy = "target_healthy";
    public const string ContinuationEnqueued = "continuation_enqueued";
    public const string ContinuationDispatched = "continuation_dispatched";
    public const string Completed = "completed";
    public const string RollbackStarting = "rollback_starting";
    public const string RolledBack = "rolled_back";
    public const string Failed = "failed";
}

public sealed record RestartOperationRecord
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset RequestedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? RequestedByAgentId { get; init; }
    public required string SourceCwd { get; init; }
    public required string SourceBusRoot { get; init; }
    public required string TargetCwd { get; init; }
    public required string TargetBusRoot { get; init; }
    public required string TargetAgentId { get; init; }
    public string? FallbackCwd { get; init; }
    public string? FallbackBusRoot { get; init; }
    public string? ContinueTitle { get; init; }
    public string? ContinuePrompt { get; init; }
    public string? ExpectedTargetBranch { get; init; }
    public string? HelperPid { get; init; }
    public string? StartupHandoffMessageId { get; init; }
    public string? LastError { get; init; }
    public string? ContinuationTaskId { get; init; }
    public string? KnownGoodCheckpointId { get; init; }
}

public sealed class RestartOperationStore
{
    public RestartOperationStore(string busRoot)
    {
        var fullBusRoot = Path.GetFullPath(busRoot);
        var rootDirectory = DirectoryNameForBusRoot(fullBusRoot);
        OperationsDirectory = Path.Combine(rootDirectory, "operations");
    }

    public string OperationsDirectory { get; }

    public string OperationPath(string operationId)
    {
        return Path.Combine(OperationsDirectory, $"{operationId}.json");
    }

    public RestartOperationRecord Create(
        string requestedByAgentId,
        string sourceCwd,
        string sourceBusRoot,
        string targetCwd,
        string targetBusRoot,
        string targetAgentId,
        string? fallbackCwd,
        string? fallbackBusRoot,
        string? continueTitle,
        string? continuePrompt,
        string? expectedTargetBranch,
        string? knownGoodCheckpointId = null)
    {
        var normalizedSourceCwd = NormalizeCheckoutPath(sourceCwd);
        var normalizedTargetCwd = NormalizeCheckoutPath(targetCwd);
        var normalizedFallbackCwd = fallbackCwd is null ? null : NormalizeCheckoutPath(fallbackCwd);
        ValidateCheckoutPair(normalizedSourceCwd, normalizedTargetCwd);
        ValidateCheckoutDirectory(normalizedSourceCwd, "sourceCwd");
        ValidateCheckoutDirectory(normalizedTargetCwd, "targetCwd");
        if (normalizedFallbackCwd is not null)
        {
            ValidateCheckoutDirectory(normalizedFallbackCwd, "fallbackCwd");
        }

        var operation = new RestartOperationRecord
        {
            Id = $"restart-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}",
            Kind = RestartOperationKind.CtuDesktopRestart,
            Status = RestartOperationStatus.Prepared,
            RequestedAt = DateTimeOffset.Now,
            SourceCwd = normalizedSourceCwd,
            SourceBusRoot = Path.GetFullPath(sourceBusRoot),
            TargetCwd = normalizedTargetCwd,
            TargetBusRoot = Path.GetFullPath(targetBusRoot),
            TargetAgentId = targetAgentId,
            RequestedByAgentId = requestedByAgentId,
            FallbackCwd = normalizedFallbackCwd,
            FallbackBusRoot = fallbackBusRoot is null ? null : Path.GetFullPath(fallbackBusRoot),
            ContinueTitle = continueTitle,
            ContinuePrompt = continuePrompt,
            ExpectedTargetBranch = expectedTargetBranch,
            KnownGoodCheckpointId = knownGoodCheckpointId
        };

        Write(operation);
        return operation;
    }

    public void Write(RestartOperationRecord operation)
    {
        Directory.CreateDirectory(OperationsDirectory);
        JsonFile.WriteAtomic(OperationPath(operation.Id), operation);
    }

    public RestartOperationRecord UpdateStatus(
        RestartOperationRecord operation,
        string status,
        string? helperPid = null,
        string? continuationTaskId = null,
        string? lastError = null,
        string? startupHandoffMessageId = null,
        string? knownGoodCheckpointId = null)
    {
        var nextStatus = NormalizeStatus(status);
        var currentStatus = NormalizeStatus(operation.Status);

        if (!CanTransition(currentStatus, nextStatus))
        {
            if (string.Equals(currentStatus, nextStatus, StringComparison.OrdinalIgnoreCase))
            {
                return operation with
                {
                    HelperPid = helperPid ?? operation.HelperPid,
                    ContinuationTaskId = continuationTaskId ?? operation.ContinuationTaskId,
                    StartupHandoffMessageId = startupHandoffMessageId ?? operation.StartupHandoffMessageId,
                    KnownGoodCheckpointId = knownGoodCheckpointId ?? operation.KnownGoodCheckpointId,
                    LastError = lastError ?? operation.LastError
                };
            }

            throw new InvalidOperationException($"Invalid restart status transition '{currentStatus}' -> '{nextStatus}'.");
        }

        var updated = operation with
        {
            Status = nextStatus,
            HelperPid = helperPid ?? operation.HelperPid,
            ContinuationTaskId = continuationTaskId ?? operation.ContinuationTaskId,
            StartupHandoffMessageId = startupHandoffMessageId ?? operation.StartupHandoffMessageId,
            KnownGoodCheckpointId = knownGoodCheckpointId ?? operation.KnownGoodCheckpointId,
            LastError = lastError is null ? operation.LastError : lastError
        };

        if (IsTerminalStatus(nextStatus))
        {
            updated = updated with { CompletedAt = updated.CompletedAt ?? DateTimeOffset.Now };
        }

        return updated;
    }

    public RestartOperationRecord UpdateStatus(
        string operationId,
        string status,
        string? helperPid = null,
        string? continuationTaskId = null,
        string? lastError = null)
    {
        var operation = Find(operationId) ?? throw new FileNotFoundException($"Restart operation not found: {operationId}");
        var updated = UpdateStatus(operation, status, helperPid, continuationTaskId, lastError);
        Write(updated);
        return updated;
    }

    public RestartOperationRecord? Find(string operationId)
    {
        var path = OperationPath(operationId);
        return File.Exists(path) ? JsonFile.Read<RestartOperationRecord>(path) : null;
    }

    public RestartOperationRecord? FindByPath(string operationPath)
    {
        if (!File.Exists(operationPath))
        {
            return null;
        }

        return JsonFile.Read<RestartOperationRecord>(operationPath);
    }

    public static string NormalizeCheckoutPath(string path)
    {
        return Path.GetFullPath(path);
    }

    public static void ValidateCheckoutDirectory(string checkout, string label)
    {
        if (!Directory.Exists(checkout))
        {
            throw new DirectoryNotFoundException($"{label} checkout directory not found: {checkout}");
        }

        var startupScript = Path.Combine(checkout, "scripts", "start-codexteamup.ps1");
        if (!File.Exists(startupScript))
        {
            throw new FileNotFoundException($"Required startup script not found at {startupScript}.");
        }
    }

    public static void ValidateCheckoutPair(string sourceCwd, string targetCwd)
    {
        if (string.Equals(NormalizeCheckoutPath(sourceCwd), NormalizeCheckoutPath(targetCwd), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("sourceCwd and targetCwd must be different checkouts.");
        }
    }

    private static string NormalizeStatus(string status)
    {
        return status.Trim().ToLowerInvariant();
    }

    private static bool CanTransition(string currentStatus, string nextStatus)
    {
        if (string.Equals(currentStatus, nextStatus, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (currentStatus, nextStatus) switch
        {
            (RestartOperationStatus.Prepared, RestartOperationStatus.HelperStarted) => true,
            (RestartOperationStatus.Prepared, RestartOperationStatus.Failed) => true,
            (RestartOperationStatus.HelperStarted, RestartOperationStatus.StoppingSource) => true,
            (RestartOperationStatus.HelperStarted, RestartOperationStatus.Failed) => true,
            (RestartOperationStatus.StoppingSource, RestartOperationStatus.StartingTarget) => true,
            (RestartOperationStatus.StoppingSource, RestartOperationStatus.RollbackStarting) => true,
            (RestartOperationStatus.StoppingSource, RestartOperationStatus.Failed) => true,
            (RestartOperationStatus.StartingTarget, RestartOperationStatus.TargetHealthy) => true,
            (RestartOperationStatus.StartingTarget, RestartOperationStatus.RollbackStarting) => true,
            (RestartOperationStatus.StartingTarget, RestartOperationStatus.Failed) => true,
            (RestartOperationStatus.TargetHealthy, RestartOperationStatus.ContinuationEnqueued) => true,
            (RestartOperationStatus.TargetHealthy, RestartOperationStatus.ContinuationDispatched) => true,
            (RestartOperationStatus.TargetHealthy, RestartOperationStatus.Failed) => true,
            (RestartOperationStatus.TargetHealthy, RestartOperationStatus.RollbackStarting) => true,
            (RestartOperationStatus.ContinuationEnqueued, RestartOperationStatus.ContinuationDispatched) => true,
            (RestartOperationStatus.ContinuationEnqueued, RestartOperationStatus.Completed) => true,
            (RestartOperationStatus.ContinuationEnqueued, RestartOperationStatus.Failed) => true,
            (RestartOperationStatus.ContinuationEnqueued, RestartOperationStatus.RollbackStarting) => true,
            (RestartOperationStatus.ContinuationDispatched, RestartOperationStatus.Completed) => true,
            (RestartOperationStatus.ContinuationDispatched, RestartOperationStatus.Failed) => true,
            (RestartOperationStatus.RollbackStarting, RestartOperationStatus.RolledBack) => true,
            (RestartOperationStatus.RollbackStarting, RestartOperationStatus.Failed) => true,
            _ => false
        };
    }

    private static bool IsTerminalStatus(string status)
    {
        return status is RestartOperationStatus.Completed or RestartOperationStatus.RolledBack or RestartOperationStatus.Failed;
    }

    private static string DirectoryNameForBusRoot(string busRoot)
    {
        var directory = new DirectoryInfo(busRoot);
        if (directory.Name.Equals("agentbus", StringComparison.OrdinalIgnoreCase)
            && directory.Parent is not null
            && directory.Parent.Name.Equals(".codexteamup", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(directory.Parent.FullName, "restart");
        }

        if (directory.Name.Equals(".codexteamup", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(directory.FullName, "restart");
        }

        var projectRoot = Directory.Exists(Path.Combine(busRoot, ".codexteamup"))
            ? busRoot
            : directory.Parent?.FullName ?? busRoot;
        return Path.Combine(projectRoot, ".codexteamup", "restart");
    }
}
