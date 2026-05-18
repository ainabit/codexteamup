using System.Text.Json;
using CodexTeamUp.Core;

namespace CodexTeamUp.AgentBus;

/// <summary>
/// Persists external-channel envelopes, leases, and correlation records next to AgentBus.
/// </summary>
public sealed class ExchangeStore
{
    public ExchangeStore(string busRoot)
    {
        var normalizedBusRoot = CtuProjectLayout.NormalizeBusRoot(busRoot);
        RootDirectory = CtuProjectLayout.ExchangeRootForBusRoot(normalizedBusRoot);
        CorrelationsDirectory = Path.Combine(RootDirectory, "correlations");
        DeadLetterDirectory = Path.Combine(RootDirectory, "deadletter");
        InboxDirectory = Path.Combine(RootDirectory, "inbox");
        StartupDirectory = Path.Combine(RootDirectory, "startup");
        LeasesDirectory = Path.Combine(RootDirectory, "leases");
        OutboxDirectory = Path.Combine(RootDirectory, "outbox");
        PayloadsDirectory = Path.Combine(RootDirectory, "payloads");
    }

    public string RootDirectory { get; }
    public string InboxDirectory { get; }
    public string StartupDirectory { get; }
    public string OutboxDirectory { get; }
    public string DeadLetterDirectory { get; }
    public string LeasesDirectory { get; }
    public string PayloadsDirectory { get; }
    public string CorrelationsDirectory { get; }

    public void Initialize()
    {
        Directory.CreateDirectory(InboxDirectory);
        Directory.CreateDirectory(StartupDirectory);
        Directory.CreateDirectory(Path.Combine(StartupDirectory, ExchangeTargetScope.System));
        Directory.CreateDirectory(Path.Combine(StartupDirectory, ExchangeTargetScope.Project));
        Directory.CreateDirectory(Path.Combine(StartupDirectory, ExchangeTargetScope.Agent));
        Directory.CreateDirectory(Path.Combine(InboxDirectory, ExchangeTargetScope.System));
        Directory.CreateDirectory(Path.Combine(InboxDirectory, ExchangeTargetScope.Project));
        Directory.CreateDirectory(Path.Combine(InboxDirectory, ExchangeTargetScope.Agent));
        Directory.CreateDirectory(OutboxDirectory);
        Directory.CreateDirectory(DeadLetterDirectory);
        Directory.CreateDirectory(LeasesDirectory);
        Directory.CreateDirectory(PayloadsDirectory);
        Directory.CreateDirectory(CorrelationsDirectory);
    }

    public ExchangeEnvelope CreateRestartHandoff(string operationPath, RestartOperationRecord operation)
    {
        Initialize();
        var message = new ExchangeEnvelope
        {
            MessageId = $"restart-handoff-{Guid.NewGuid():N}",
            Kind = ExchangeEnvelopeKind.Restart,
            TargetScope = ExchangeTargetScope.System,
            TargetProject = Path.GetFileName(CtuProjectLayout.NormalizeCheckoutPath(operation.TargetCwd)),
            TargetAgentId = operation.TargetAgentId,
            TargetThreadName = operation.TargetAgentId,
            CorrelationId = operation.Id,
            CreatedAt = DateTimeOffset.Now,
            ExpiresAt = DateTimeOffset.Now.AddHours(4),
            PayloadType = "application/json",
            Payload = JsonSerializer.SerializeToElement(new
            {
                operationId = operation.Id,
                operationPath = Path.GetFullPath(operationPath),
                sourceCwd = operation.SourceCwd,
                sourceBusRoot = operation.SourceBusRoot,
                targetCwd = operation.TargetCwd,
                targetBusRoot = operation.TargetBusRoot,
                targetAgentId = operation.TargetAgentId
            }, JsonFile.Options),
            AttemptCount = 0,
            Status = ExchangeEnvelopeStatus.Pending
        };

        Write(message);
        UpdateCorrelation(message);
        return message;
    }

    public void Write(ExchangeEnvelope envelope)
    {
        Initialize();
        JsonFile.WriteAtomic(MessagePath(envelope), envelope);
    }

    public IReadOnlyList<(string Path, ExchangeEnvelope Envelope)> ListPendingSystemMessages(string kind, int limit)
    {
        Initialize();
        return ListPendingMessages(InboxSystemMessageKindDirectory(kind), limit);
    }

    public IReadOnlyList<(string Path, ExchangeEnvelope Envelope)> ListPendingStartupSystemMessages(string kind, int limit)
    {
        Initialize();
        return ListPendingMessages(StartupSystemMessageKindDirectory(kind), limit);
    }

    private IReadOnlyList<(string Path, ExchangeEnvelope Envelope)> ListPendingMessages(string directory, int limit)
    {
        var pending = new List<(string Path, ExchangeEnvelope Envelope)>();
        foreach (var path in EnumerateEnvelopeFiles(directory, limit))
        {
            var envelope = TryReadEnvelope(path);
            if (envelope is null)
            {
                continue;
            }

            var isPending = string.Equals(envelope.Status, ExchangeEnvelopeStatus.Pending, StringComparison.OrdinalIgnoreCase);
            var isExpiredLease = string.Equals(envelope.Status, ExchangeEnvelopeStatus.Leased, StringComparison.OrdinalIgnoreCase)
                && envelope.LeaseExpiresAt is { } leaseExpiry
                && leaseExpiry <= DateTimeOffset.Now;
            if ((!isPending && !isExpiredLease) || envelope.NotBefore > DateTimeOffset.Now)
            {
                continue;
            }

            pending.Add((path, envelope));
        }

        return pending
            .OrderBy(row => row.Envelope.CreatedAt)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    public LeaseHandle? TryAcquireLease(string envelopePath, string owner, TimeSpan leaseDuration)
    {
        Initialize();
        var envelope = TryReadEnvelope(envelopePath);
        if (envelope is null)
        {
            return null;
        }

        if (envelope.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.Now)
        {
            DeadLetter(envelopePath, envelope, "Envelope expired before processing.");
            return null;
        }

        if (string.Equals(envelope.Status, ExchangeEnvelopeStatus.Completed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(envelope.Status, ExchangeEnvelopeStatus.DeadLetter, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var leasePath = LeasePath(envelope.MessageId);
        if (File.Exists(leasePath))
        {
            var existingLease = JsonFile.Read<ExchangeEnvelope>(leasePath);
            if (existingLease?.LeaseExpiresAt is { } currentLeaseExpiry && currentLeaseExpiry > DateTimeOffset.Now)
            {
                return null;
            }

            File.Delete(leasePath);
        }

        var leased = envelope with
        {
            Status = ExchangeEnvelopeStatus.Leased,
            LeaseOwner = owner,
            LeaseExpiresAt = DateTimeOffset.Now.Add(leaseDuration),
            AttemptCount = envelope.AttemptCount + 1
        };
        JsonFile.WriteAtomic(leasePath, leased);
        JsonFile.WriteAtomic(envelopePath, leased);
        UpdateCorrelation(leased);
        return new LeaseHandle(envelopePath, leasePath, leased);
    }

    public void Complete(string envelopePath, ExchangeEnvelope envelope)
    {
        var completed = envelope with
        {
            Status = ExchangeEnvelopeStatus.Completed,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            LastError = null
        };
        JsonFile.WriteAtomic(envelopePath, completed);
        DeleteLease(envelope.MessageId);
        UpdateCorrelation(completed);
    }

    public void Requeue(string envelopePath, ExchangeEnvelope envelope, string lastError)
    {
        var pending = envelope with
        {
            Status = ExchangeEnvelopeStatus.Pending,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            LastError = SafeText.Redact(lastError)
        };
        JsonFile.WriteAtomic(envelopePath, pending);
        DeleteLease(envelope.MessageId);
        UpdateCorrelation(pending);
    }

    public void DeadLetter(string envelopePath, ExchangeEnvelope envelope, string lastError)
    {
        Directory.CreateDirectory(DeadLetterDirectory);
        var dead = envelope with
        {
            Status = ExchangeEnvelopeStatus.DeadLetter,
            LeaseOwner = null,
            LeaseExpiresAt = null,
            LastError = SafeText.Redact(lastError)
        };
        var deadPath = Path.Combine(DeadLetterDirectory, $"{dead.MessageId}.json");
        JsonFile.WriteAtomic(deadPath, dead);
        if (File.Exists(envelopePath))
        {
            File.Delete(envelopePath);
        }

        DeleteLease(dead.MessageId);
        UpdateCorrelation(dead);
    }

    public sealed class LeaseHandle : IDisposable
    {
        private readonly string _leasePath;
        private bool _disposed;

        internal LeaseHandle(string envelopePath, string leasePath, ExchangeEnvelope envelope)
        {
            EnvelopePath = envelopePath;
            _leasePath = leasePath;
            Envelope = envelope;
        }

        public string EnvelopePath { get; }
        public ExchangeEnvelope Envelope { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (File.Exists(_leasePath))
            {
                File.Delete(_leasePath);
            }
        }
    }

    private IEnumerable<string> EnumerateEnvelopeFiles(string directory, int limit)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            yield return file;
            count++;
            if (count >= limit)
            {
                yield break;
            }
        }
    }

    private ExchangeEnvelope? TryReadEnvelope(string path)
    {
        try
        {
            return JsonFile.Read<ExchangeEnvelope>(path);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            DeadLetterInvalidEnvelope(path, ex.Message);
            return null;
        }
    }

    private void DeadLetterInvalidEnvelope(string envelopePath, string lastError)
    {
        try
        {
            Directory.CreateDirectory(DeadLetterDirectory);
            var stamp = DateTimeOffset.Now.ToString("yyyyMMddHHmmssfff");
            var sourceName = Path.GetFileName(envelopePath);
            var deadPath = Path.Combine(DeadLetterDirectory, $"invalid-{stamp}-{sourceName}");
            if (File.Exists(envelopePath))
            {
                File.Move(envelopePath, deadPath, overwrite: true);
            }

            var errorPath = $"{deadPath}.error.txt";
            File.WriteAllText(errorPath, SafeText.Redact(lastError));
        }
        catch
        {
            // Invalid external messages must not take down the controller sweep.
        }
    }

    private string MessagePath(ExchangeEnvelope envelope)
    {
        if (string.Equals(envelope.TargetScope, ExchangeTargetScope.System, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(envelope.Kind, ExchangeEnvelopeKind.Restart, StringComparison.OrdinalIgnoreCase))
            {
                var startupKindPath = StartupSystemMessageKindDirectory(envelope.Kind);
                Directory.CreateDirectory(startupKindPath);
                return Path.Combine(startupKindPath, $"{envelope.MessageId}.json");
            }

            var kindPath = Path.Combine(InboxDirectory, ExchangeTargetScope.System, envelope.Kind);
            Directory.CreateDirectory(kindPath);
            return Path.Combine(kindPath, $"{envelope.MessageId}.json");
        }

        if (string.Equals(envelope.TargetScope, ExchangeTargetScope.Project, StringComparison.OrdinalIgnoreCase))
        {
            var project = string.IsNullOrWhiteSpace(envelope.TargetProject) ? "default" : envelope.TargetProject!;
            var projectPath = Path.Combine(InboxDirectory, ExchangeTargetScope.Project, project);
            Directory.CreateDirectory(projectPath);
            return Path.Combine(projectPath, $"{envelope.MessageId}.json");
        }

        var agent = string.IsNullOrWhiteSpace(envelope.TargetAgentId)
            ? "unknown"
            : envelope.TargetAgentId!.Replace('/', '_').Replace('\\', '_');
        var agentPath = Path.Combine(InboxDirectory, ExchangeTargetScope.Agent, agent);
        Directory.CreateDirectory(agentPath);
        return Path.Combine(agentPath, $"{envelope.MessageId}.json");
    }

    private string LeasePath(string messageId)
        => Path.Combine(LeasesDirectory, $"{messageId}.json");

    private void DeleteLease(string messageId)
    {
        var leasePath = LeasePath(messageId);
        if (File.Exists(leasePath))
        {
            File.Delete(leasePath);
        }
    }

    private void UpdateCorrelation(ExchangeEnvelope envelope)
    {
        var path = Path.Combine(CorrelationsDirectory, $"{envelope.CorrelationId}.json");
        var existing = File.Exists(path)
            ? JsonFile.Read<ExchangeCorrelationRecord>(path)
            : null;
        var ids = (existing?.MessageIds ?? [])
            .Concat([envelope.MessageId])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var updated = new ExchangeCorrelationRecord
        {
            CorrelationId = envelope.CorrelationId,
            CreatedAt = existing?.CreatedAt ?? DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
            MessageIds = ids,
            LastMessageId = envelope.MessageId,
            LastStatus = envelope.Status
        };
        JsonFile.WriteAtomic(path, updated);
    }

    private string StartupSystemMessageKindDirectory(string kind)
        => Path.Combine(StartupDirectory, ExchangeTargetScope.System, kind);

    private string InboxSystemMessageKindDirectory(string kind)
        => Path.Combine(InboxDirectory, ExchangeTargetScope.System, kind);
}
