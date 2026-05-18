using CodexTeamUp.Core;

namespace CodexTeamUp.AgentBus;

/// <summary>
/// Stores the last verified healthy CTU runtime for restart fallback.
/// </summary>
public sealed class KnownGoodRuntimeCheckpointStore
{
    public KnownGoodRuntimeCheckpointStore(string busRoot)
    {
        var normalizedBusRoot = CtuProjectLayout.NormalizeBusRoot(busRoot);
        DirectoryPath = CtuProjectLayout.RuntimeCheckpointRootForBusRoot(normalizedBusRoot);
        CheckpointPath = Path.Combine(DirectoryPath, "known-good.json");
    }

    public string DirectoryPath { get; }
    public string CheckpointPath { get; }

    public KnownGoodRuntimeCheckpoint? Read()
        => File.Exists(CheckpointPath)
            ? JsonFile.Read<KnownGoodRuntimeCheckpoint>(CheckpointPath)
            : null;

    /// <summary>
    /// Returns the latest known-good checkpoint only when explicitly verified.
    /// </summary>
    public KnownGoodRuntimeCheckpoint? ReadVerified()
    {
        var checkpoint = Read();
        return checkpoint is { IsVerified: true } ? checkpoint : null;
    }

    /// <summary>
    /// Marks the latest checkpoint as verified when it matches the expected checkpoint id.
    /// </summary>
    public KnownGoodRuntimeCheckpoint? MarkVerifiedIfMatch(string checkpointId, string verificationSource)
    {
        var checkpoint = Read();
        if (checkpoint is null || !string.Equals(checkpoint.Id, checkpointId, StringComparison.Ordinal))
        {
            return null;
        }

        var verified = checkpoint with
        {
            IsVerified = true,
            VerificationSource = verificationSource,
            VerifiedAt = DateTimeOffset.Now
        };
        JsonFile.WriteAtomic(CheckpointPath, verified);
        return verified;
    }

    /// <summary>
    /// Writes the current runtime checkpoint with optional explicit verification state.
    /// </summary>
    public KnownGoodRuntimeCheckpoint WriteHealthy(
        string checkoutCwd,
        string busRoot,
        string? controllerPluginPath,
        string? controllerType,
        string? appServerPluginPath,
        string? appServerAdapterType,
        bool isVerified = false,
        string verificationSource = "service_boot")
    {
        Directory.CreateDirectory(DirectoryPath);
        var normalizedCheckout = CtuProjectLayout.NormalizeCheckoutPath(checkoutCwd);
        var checkpoint = new KnownGoodRuntimeCheckpoint
        {
            Id = $"known-good-{DateTimeOffset.Now:yyyyMMdd-HHmmss}",
            CheckoutCwd = normalizedCheckout,
            BusRoot = CtuProjectLayout.NormalizeBusRoot(busRoot),
            RuntimeRoot = Path.Combine(normalizedCheckout, ".ctu", "runtime"),
            ToolsRoot = Path.Combine(normalizedCheckout, ".ctu", "tools"),
            ControllerPluginPath = controllerPluginPath,
            ControllerType = controllerType,
            AppServerPluginPath = appServerPluginPath,
            AppServerAdapterType = appServerAdapterType,
            VerifiedAt = DateTimeOffset.Now,
            IsVerified = isVerified,
            VerificationSource = verificationSource,
            UseNoPublishOnRecovery = true
        };
        JsonFile.WriteAtomic(CheckpointPath, checkpoint);
        return checkpoint;
    }
}
