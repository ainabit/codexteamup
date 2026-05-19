using System.Text;
using System.Text.Json;
using CodexTeamUp.Core;

namespace CodexTeamUp.AgentBus;

public sealed class AgentBusStore
{
    public AgentBusStore(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }
    public string TasksOpenDirectory => Path.Combine(RootDirectory, "tasks", "open");
    public string TasksClaimedDirectory => Path.Combine(RootDirectory, "tasks", "claimed");
    public string TasksDoneDirectory => Path.Combine(RootDirectory, "tasks", "done");
    public string TasksFailedDirectory => Path.Combine(RootDirectory, "tasks", "failed");
    public string PromptsDirectory => Path.Combine(RootDirectory, "prompts");
    public string ResultsDirectory => Path.Combine(RootDirectory, "results");
    public string ContinuationsOpenDirectory => Path.Combine(RootDirectory, "continuations", "open");
    public string ContinuationsDoneDirectory => Path.Combine(RootDirectory, "continuations", "done");
    public string ContinuationsFailedDirectory => Path.Combine(RootDirectory, "continuations", "failed");
    public string LocksDirectory => Path.Combine(RootDirectory, "locks");
    public string InboxDirectory => Path.Combine(RootDirectory, "inbox");
    public string EventsPath => Path.Combine(RootDirectory, "events.jsonl");
    public string AgentsPath => Path.Combine(RootDirectory, "agents.json");

    public void Initialize()
    {
        Directory.CreateDirectory(TasksOpenDirectory);
        Directory.CreateDirectory(TasksClaimedDirectory);
        Directory.CreateDirectory(TasksDoneDirectory);
        Directory.CreateDirectory(TasksFailedDirectory);
        Directory.CreateDirectory(PromptsDirectory);
        Directory.CreateDirectory(ResultsDirectory);
        Directory.CreateDirectory(ContinuationsOpenDirectory);
        Directory.CreateDirectory(ContinuationsDoneDirectory);
        Directory.CreateDirectory(ContinuationsFailedDirectory);
        Directory.CreateDirectory(LocksDirectory);
        Directory.CreateDirectory(InboxDirectory);

        if (!File.Exists(AgentsPath))
        {
            JsonFile.WriteAtomic(AgentsPath, new AgentRegistryDocument
            {
                Agents =
                [
                    new AgentDefinition { Id = "architect", Role = "Coordinator / Architect", DisplayName = "Architect", Status = "planned" },
                    new AgentDefinition { Id = "01-web", Role = "Web Implementation", DisplayName = "01 Web", ReturnTo = "architect", Status = "planned" },
                    new AgentDefinition { Id = "02-design", Role = "Design", DisplayName = "02 Design", ReturnTo = "architect", Status = "planned" },
                    new AgentDefinition { Id = "03-reviewer", Role = "Reviewer / Test", DisplayName = "03 Reviewer", ReturnTo = "architect", Status = "planned" },
                    new AgentDefinition { Id = "04-contract", Role = "Contract / Schema", DisplayName = "04 Contract", ReturnTo = "architect", Status = "planned" }
                ]
            });
        }

        if (!File.Exists(EventsPath))
        {
            File.WriteAllText(EventsPath, string.Empty);
        }

        AppendEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = "bus.initialized",
            Message = "AgentBus directories are ready."
        });
    }

    public void MarkTaskSuperseded(AgentBusTask task)
    {
        MarkTaskToDirectory(task, DateTimeOffset.Now, "superseded");
    }

    /// <summary>Atomically updates a task record if present in any task directory.</summary>
    public AgentBusTask? UpdateTask(string taskId, Func<AgentBusTask, AgentBusTask> updater)
    {
        InitializeIfMissing();
        foreach (var directory in new[] { TasksOpenDirectory, TasksClaimedDirectory, TasksDoneDirectory, TasksFailedDirectory })
        {
            var path = TaskPath(directory, taskId);
            if (!File.Exists(path))
            {
                continue;
            }

            var existing = JsonFile.Read<AgentBusTask>(path);
            if (existing is null)
            {
                return null;
            }

            var updated = updater(existing);
            JsonFile.WriteAtomic(path, updated);
            return updated;
        }

        return null;
    }

    /// <summary>Atomically updates a result record if present.</summary>
    public AgentBusResult? UpdateResult(string resultId, Func<AgentBusResult, AgentBusResult> updater)
    {
        InitializeIfMissing();
        var normalized = resultId.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(resultId)
            : resultId;
        var path = ResultPath(normalized);
        var existing = File.Exists(path) ? JsonFile.Read<AgentBusResult>(path) : null;
        if (existing is null)
        {
            return null;
        }

        var updated = updater(existing);
        JsonFile.WriteAtomic(path, updated);
        return updated;
    }

    public AgentBusTask CreateTask(
        string from,
        string to,
        string title,
        string prompt,
        string project,
        string? cwd,
        IReadOnlyList<string> allowedPaths,
        string? returnTo,
        string? promptFile = null,
        string? priority = null)
    {
        InitializeIfMissing();
        var id = CreateTaskId();
        var task = new AgentBusTask
        {
            Id = id,
            Project = project,
            From = from,
            To = to,
            Status = "open",
            CreatedAt = DateTimeOffset.Now,
            Cwd = cwd,
            Title = title,
            Prompt = prompt,
            PromptFile = promptFile,
            AllowedPaths = allowedPaths,
            ReturnTo = returnTo,
            Priority = priority,
            ResultExpected = true
        };

        JsonFile.WriteAtomic(TaskPath(TasksOpenDirectory, id), task);
        AppendEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = "task.created",
            TaskId = id,
            From = from,
            To = to,
            Message = title
        });
        return task;
    }

    public AgentBusTask CreateTaskFromPromptFile(
        string from,
        string to,
        string title,
        string promptFile,
        string project,
        string? cwd,
        IReadOnlyList<string> allowedPaths,
        string? returnTo,
        string? priority = null)
    {
        var fullPromptPath = Path.GetFullPath(promptFile);
        var prompt = File.ReadAllText(fullPromptPath);
        return CreateTask(from, to, title, prompt, project, cwd, allowedPaths, returnTo, fullPromptPath, priority);
    }

    public IReadOnlyList<AgentDefinition> ListAgents()
    {
        InitializeIfMissing();
        return ReadRegistry().Agents.OrderBy(agent => agent.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public AgentDefinition? FindAgent(string id)
    {
        var normalized = NormalizeAgentKey(id);
        return ListAgents().FirstOrDefault(agent =>
            string.Equals(NormalizeAgentKey(agent.Id), normalized, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeAgentKey(agent.DisplayName), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public AgentDefinition RegisterAgent(AgentDefinition definition)
    {
        InitializeIfMissing();
        var registry = ReadRegistry();
        var normalized = NormalizeAgentKey(definition.Id);
        var agents = registry.Agents
            .Where(agent => !string.Equals(NormalizeAgentKey(agent.Id), normalized, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(NormalizeAgentKey(agent.DisplayName), normalized, StringComparison.OrdinalIgnoreCase))
            .Append(definition)
            .OrderBy(agent => agent.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        JsonFile.WriteAtomic(AgentsPath, registry with { Agents = agents });
        Directory.CreateDirectory(Path.Combine(InboxDirectory, definition.Id));
        AppendEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = "agent.registered",
            To = definition.Id,
            Message = definition.Role
        });
        return definition;
    }

    public IReadOnlyList<AgentBusTask> ListTasks(string? to = null, string? status = null)
    {
        InitializeIfMissing();
        return EnumerateTaskFiles(status)
            .Select(path => JsonFile.Read<AgentBusTask>(path))
            .Where(task => task is not null)
            .Cast<AgentBusTask>()
            .Where(task => string.IsNullOrWhiteSpace(to)
                || string.Equals(NormalizeAgentKey(task.To), NormalizeAgentKey(to), StringComparison.OrdinalIgnoreCase))
            .OrderBy(task => task.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Deletes task queue files for disposable test resets while preserving agents and events.
    /// </summary>
    public AgentBusClearResult ClearTasks(bool includeResults = false)
    {
        InitializeIfMissing();
        var deletedTasks = 0;
        foreach (var path in EnumerateTaskFiles(status: null).ToList())
        {
            File.Delete(path);
            deletedTasks++;
        }

        var deletedResults = 0;
        if (includeResults && Directory.Exists(ResultsDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(ResultsDirectory, "*.json", SearchOption.TopDirectoryOnly).ToList())
            {
                File.Delete(path);
                deletedResults++;
            }
        }

        var clearedAt = DateTimeOffset.Now;
        AppendEvent(new AgentBusEvent
        {
            Timestamp = clearedAt,
            Type = "agentbus.tasks_cleared",
            Message = $"Deleted {deletedTasks} task files and {deletedResults} result files.",
            Payload = new { includeResults, deletedTasks, deletedResults }
        });
        return new AgentBusClearResult(deletedTasks, deletedResults, includeResults, clearedAt);
    }

    public AgentBusTask ClaimTask(string taskId, string? owner)
    {
        InitializeIfMissing();
        var openPath = TaskPath(TasksOpenDirectory, taskId);
        var claimedPath = TaskPath(TasksClaimedDirectory, taskId);

        if (!File.Exists(openPath))
        {
            if (File.Exists(claimedPath))
            {
                throw new InvalidOperationException($"Task already claimed: {taskId}");
            }

            throw new FileNotFoundException($"Open task not found: {taskId}", openPath);
        }

        Directory.CreateDirectory(TasksClaimedDirectory);
        using var claimLock = TryCreateLock($"task-{taskId}.claim.lock");
        if (claimLock is null)
        {
            throw new InvalidOperationException($"Task claim is locked: {taskId}");
        }

        File.Move(openPath, claimedPath, overwrite: false);
        var task = JsonFile.Read<AgentBusTask>(claimedPath)
            ?? throw new InvalidOperationException($"Could not read claimed task: {taskId}");
        var claimed = task with
        {
            Status = "claimed",
            ClaimedAt = DateTimeOffset.Now,
            Owner = string.IsNullOrWhiteSpace(owner) ? task.To : owner
        };
        JsonFile.WriteAtomic(claimedPath, claimed);
        AppendEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = "task.claimed",
            TaskId = taskId,
            To = claimed.Owner
        });
        return claimed;
    }

    /// <summary>
    /// Requeues a claimed task back into the open queue so the controller can retry delivery.
    /// </summary>
    public AgentBusTask? RequeueClaimedTask(string taskId)
    {
        InitializeIfMissing();
        var claimedPath = TaskPath(TasksClaimedDirectory, taskId);
        if (!File.Exists(claimedPath))
        {
            return null;
        }

        Directory.CreateDirectory(TasksOpenDirectory);
        using var requeueLock = TryCreateLock($"task-{taskId}.requeue.lock");
        if (requeueLock is null)
        {
            throw new InvalidOperationException($"Task requeue is locked: {taskId}");
        }

        var task = JsonFile.Read<AgentBusTask>(claimedPath)
            ?? throw new InvalidOperationException($"Could not read claimed task for requeue: {taskId}");
        var requeued = task with
        {
            Status = "open",
            ClaimedAt = null,
            Owner = null
        };
        var openPath = TaskPath(TasksOpenDirectory, taskId);
        JsonFile.WriteAtomic(claimedPath, requeued);
        if (File.Exists(openPath))
        {
            File.Delete(openPath);
        }

        File.Move(claimedPath, openPath);
        return requeued;
    }

    public AgentBusResult WriteResult(
        string taskId,
        string summary,
        string status,
        string? fromOverride,
        string? toOverride,
        string? commit,
        IReadOnlyList<string> tests,
        IReadOnlyList<string> openQuestions,
        IReadOnlyList<string>? changedFiles = null,
        IReadOnlyList<string>? artifacts = null,
        string? nextSuggestedAction = null,
        string? outcome = null,
        AgentBusContinuationRequest? continuation = null)
    {
        InitializeIfMissing();
        var task = FindTask(taskId) ?? throw new FileNotFoundException($"Task not found: {taskId}");
        var normalizedOutcome = NormalizeOutcome(outcome, status, continuation);
        var normalizedContinuation = NormalizeContinuationRequest(continuation, task, normalizedOutcome);
        var continuationId = normalizedContinuation is null ? null : CreateContinuationId(taskId);
        var result = new AgentBusResult
        {
            Id = CreateResultId(taskId),
            TaskId = taskId,
            From = string.IsNullOrWhiteSpace(fromOverride) ? task.To : fromOverride,
            To = string.IsNullOrWhiteSpace(toOverride) ? task.ReturnTo ?? task.From : toOverride,
            Status = status,
            CompletedAt = DateTimeOffset.Now,
            Summary = summary,
            Commit = commit,
            Outcome = normalizedOutcome,
            ContinuationId = continuationId,
            Continuation = normalizedContinuation,
            ChangedFiles = changedFiles ?? [],
            Tests = tests,
            Artifacts = artifacts ?? [],
            OpenQuestions = openQuestions,
            NextSuggestedAction = nextSuggestedAction
        };

        JsonFile.WriteAtomic(ResultPath(result.Id), result);
        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            MarkTaskFailed(task, result.CompletedAt);
        }
        else
        {
            MarkTaskDone(task, result.CompletedAt);
        }
        AppendEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = "result.written",
            TaskId = taskId,
            ResultId = result.Id,
            From = result.From,
            To = result.To,
            Message = result.Status
        });

        if (normalizedContinuation is not null)
        {
            RegisterContinuation(result, normalizedContinuation, continuationId!);
        }

        return result;
    }

    /// <summary>
    /// Returns scheduled self-continuations, optionally filtered by owner and status.
    /// </summary>
    public IReadOnlyList<AgentBusContinuation> ListContinuations(string? owner = null, string? status = null)
    {
        InitializeIfMissing();
        return EnumerateContinuationFiles(status)
            .Select(path => JsonFile.Read<AgentBusContinuation>(path))
            .Where(continuation => continuation is not null)
            .Cast<AgentBusContinuation>()
            .Where(continuation => string.IsNullOrWhiteSpace(owner)
                || string.Equals(NormalizeAgentKey(continuation.Owner), NormalizeAgentKey(owner), StringComparison.OrdinalIgnoreCase))
            .OrderBy(continuation => continuation.DueAt)
            .ToList();
    }

    /// <summary>Atomically updates a continuation record if it exists.</summary>
    public AgentBusContinuation? UpdateContinuation(string continuationId, Func<AgentBusContinuation, AgentBusContinuation> updater)
    {
        InitializeIfMissing();
        foreach (var directory in new[] { ContinuationsOpenDirectory, ContinuationsDoneDirectory, ContinuationsFailedDirectory })
        {
            var path = ContinuationPath(directory, continuationId);
            if (!File.Exists(path))
            {
                continue;
            }

            var existing = JsonFile.Read<AgentBusContinuation>(path);
            if (existing is null)
            {
                return null;
            }

            var updated = updater(existing);
            JsonFile.WriteAtomic(path, updated);
            return updated;
        }

        return null;
    }

    /// <summary>Moves an open continuation into a terminal status directory.</summary>
    public AgentBusContinuation? CompleteContinuation(string continuationId, string status, string? lastError = null, string? lastWakeTaskId = null)
    {
        InitializeIfMissing();
        var sourcePath = ContinuationPath(ContinuationsOpenDirectory, continuationId);
        if (!File.Exists(sourcePath))
        {
            return null;
        }

        var existing = JsonFile.Read<AgentBusContinuation>(sourcePath)
            ?? throw new InvalidOperationException($"Could not read continuation: {continuationId}");
        var terminalStatus = string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ? "failed" : "done";
        var targetDirectory = string.Equals(terminalStatus, "failed", StringComparison.OrdinalIgnoreCase)
            ? ContinuationsFailedDirectory
            : ContinuationsDoneDirectory;
        var completed = existing with
        {
            Status = terminalStatus,
            CompletedAt = DateTimeOffset.Now,
            LastWakeError = lastError ?? existing.LastWakeError,
            LastWakeTaskId = lastWakeTaskId ?? existing.LastWakeTaskId
        };
        var targetPath = ContinuationPath(targetDirectory, continuationId);
        JsonFile.WriteAtomic(sourcePath, completed);
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(sourcePath, targetPath);
        AppendEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = $"continuation.{terminalStatus}",
            TaskId = completed.TaskId,
            ResultId = completed.ResultId,
            To = completed.Owner,
            Message = completed.LastWakeError ?? completed.Reason,
            Payload = new { continuationId = completed.Id, completed.LastWakeTaskId }
        });
        return completed;
    }

    public AgentBusTask? FindTask(string taskId)
    {
        InitializeIfMissing();
        foreach (var directory in new[] { TasksOpenDirectory, TasksClaimedDirectory, TasksDoneDirectory, TasksFailedDirectory })
        {
            var path = TaskPath(directory, taskId);
            if (File.Exists(path))
            {
                return JsonFile.Read<AgentBusTask>(path);
            }
        }

        return null;
    }

    public AgentBusResult? WaitForResult(string taskId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        InitializeIfMissing();
        var existing = FindResultByTaskId(taskId);
        if (existing is not null)
        {
            return existing;
        }

        using var watcher = new FileSystemWatcher(ResultsDirectory, "*.json")
        {
            EnableRaisingEvents = true,
            IncludeSubdirectories = false
        };

        using var signal = new ManualResetEventSlim(false);
        FileSystemEventHandler handler = (_, _) => signal.Set();
        watcher.Created += handler;
        watcher.Changed += handler;
        watcher.Renamed += (_, _) => signal.Set();

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            existing = FindResultByTaskId(taskId);
            if (existing is not null)
            {
                return existing;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var waitFor = remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
            if (waitFor <= TimeSpan.Zero)
            {
                break;
            }

            signal.Wait(waitFor, cancellationToken);
            signal.Reset();
        }

        return null;
    }

    public AgentBusResult? FindResult(string resultId)
    {
        InitializeIfMissing();
        var normalized = resultId.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(resultId)
            : resultId;
        var path = ResultPath(normalized);
        return File.Exists(path) ? JsonFile.Read<AgentBusResult>(path) : null;
    }

    public IReadOnlyList<AgentBusResult> ListResults()
    {
        InitializeIfMissing();
        if (!Directory.Exists(ResultsDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(ResultsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => JsonFile.Read<AgentBusResult>(path))
            .Where(result => result is not null)
            .Cast<AgentBusResult>()
            .OrderBy(result => result.CompletedAt)
            .ToList();
    }

    public IReadOnlyList<AgentBusEvent> ListEvents(int limit = 100)
    {
        InitializeIfMissing();
        if (!File.Exists(EventsPath))
        {
            return [];
        }

        var rows = new List<AgentBusEvent>();
        foreach (var line in File.ReadLines(EventsPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var evt = JsonSerializer.Deserialize<AgentBusEvent>(line, JsonFile.Options);
                if (evt is not null)
                {
                    rows.Add(evt);
                }
            }
            catch (JsonException)
            {
                // Ignore corrupt or partially-written event lines.
            }
        }

        return rows.TakeLast(Math.Max(1, limit)).ToList();
    }

    public void RecordEvent(AgentBusEvent evt)
    {
        InitializeIfMissing();
        AppendEvent(evt);
    }

    private void MarkTaskDone(AgentBusTask task, DateTimeOffset completedAt)
    {
        var sourcePath = TaskPath(TasksClaimedDirectory, task.Id);
        if (!File.Exists(sourcePath))
        {
            sourcePath = TaskPath(TasksOpenDirectory, task.Id);
        }

        if (!File.Exists(sourcePath))
        {
            return;
        }

        var done = task with
        {
            Status = "done",
            CompletedAt = completedAt
        };
        var donePath = TaskPath(TasksDoneDirectory, task.Id);
        JsonFile.WriteAtomic(sourcePath, done);
        if (File.Exists(donePath))
        {
            File.Delete(donePath);
        }

        File.Move(sourcePath, donePath);
    }

    private IEnumerable<string> EnumerateTaskFiles(string? status)
    {
        string[] directories = string.IsNullOrWhiteSpace(status)
            ? [TasksOpenDirectory, TasksClaimedDirectory, TasksDoneDirectory, TasksFailedDirectory]
            : [StatusDirectory(status)];

        return directories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly));
    }

    private string StatusDirectory(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "open" => TasksOpenDirectory,
            "claimed" => TasksClaimedDirectory,
            "done" or "completed" => TasksDoneDirectory,
            "failed" => TasksFailedDirectory,
            _ => throw new ArgumentException($"Unknown task status: {status}", nameof(status))
        };
    }

    private void InitializeIfMissing()
    {
        if (!Directory.Exists(RootDirectory)
            || !Directory.Exists(TasksOpenDirectory)
            || !Directory.Exists(ResultsDirectory)
            || !Directory.Exists(ContinuationsOpenDirectory))
        {
            Initialize();
        }
    }

    /// <summary>Serializes event appends and tolerates short external file locks from live CTU readers.</summary>
    private void AppendEvent(AgentBusEvent evt)
    {
        Directory.CreateDirectory(RootDirectory);
        var lineOptions = new JsonSerializerOptions(JsonFile.Options) { WriteIndented = false };
        var line = JsonSerializer.Serialize(evt, lineOptions) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetBytes(line);
        const int lockWaitAttempts = 400;
        const int lockWaitMilliseconds = 10;

        for (var attempt = 0; attempt < lockWaitAttempts; attempt++)
        {
            using var appendLock = TryCreateLock("events.append.lock");
            if (appendLock is null)
            {
                Thread.Sleep(lockWaitMilliseconds);
                continue;
            }

            try
            {
                using var stream = new FileStream(
                    EventsPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                stream.Write(bytes);
                stream.Flush();
                return;
            }
            catch (IOException) when (attempt + 1 < lockWaitAttempts)
            {
                Thread.Sleep(lockWaitMilliseconds);
            }
            catch (UnauthorizedAccessException) when (attempt + 1 < lockWaitAttempts)
            {
                Thread.Sleep(lockWaitMilliseconds);
            }
        }

        throw new IOException("Timed out acquiring event append lock.");
    }

    private AgentRegistryDocument ReadRegistry()
    {
        try
        {
            var registry = JsonFile.Read<AgentRegistryDocument>(AgentsPath);
            if (registry is not null)
            {
                return registry;
            }
        }
        catch (JsonException)
        {
        }

        try
        {
            var legacy = JsonFile.Read<List<AgentDefinition>>(AgentsPath);
            return new AgentRegistryDocument { Agents = legacy ?? [] };
        }
        catch (JsonException)
        {
            return new AgentRegistryDocument();
        }
    }

    private AgentBusResult? FindResultByTaskId(string taskId)
    {
        if (!Directory.Exists(ResultsDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(ResultsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => JsonFile.Read<AgentBusResult>(path))
            .Where(result => result is not null)
            .Cast<AgentBusResult>()
            .OrderByDescending(result => result.CompletedAt)
            .FirstOrDefault(result => string.Equals(result.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
    }

    private AgentBusContinuation RegisterContinuation(
        AgentBusResult result,
        AgentBusContinuationRequest request,
        string continuationId)
    {
        var owner = string.IsNullOrWhiteSpace(request.Owner) ? result.From : request.Owner!.Trim();
        var dedupeKey = string.IsNullOrWhiteSpace(request.DedupeKey) ? result.TaskId : request.DedupeKey!.Trim();
        var now = DateTimeOffset.Now;
        var existing = ListContinuations(owner, "open")
            .FirstOrDefault(continuation => string.Equals(continuation.DedupeKey, dedupeKey, StringComparison.OrdinalIgnoreCase));
        var continuation = new AgentBusContinuation
        {
            Id = existing?.Id ?? continuationId,
            ResultId = result.Id,
            TaskId = result.TaskId,
            Owner = owner,
            Status = "open",
            CreatedAt = existing?.CreatedAt ?? now,
            DueAt = now.AddSeconds(Math.Clamp(request.WakeAfterSeconds, 0, 86400)),
            DedupeKey = dedupeKey,
            Reason = request.Reason,
            ReturnTo = result.To,
            Attempts = existing?.Attempts ?? 0,
            MaxAttempts = Math.Clamp(request.MaxAttempts <= 0 ? 5 : request.MaxAttempts, 1, 50),
            LastWakeAttemptAt = existing?.LastWakeAttemptAt,
            LastWakeTaskId = existing?.LastWakeTaskId,
            LastWakeError = null
        };

        JsonFile.WriteAtomic(ContinuationPath(ContinuationsOpenDirectory, continuation.Id), continuation);
        AppendEvent(new AgentBusEvent
        {
            Timestamp = now,
            Type = existing is null ? "continuation.scheduled" : "continuation.deduped",
            TaskId = result.TaskId,
            ResultId = result.Id,
            From = result.From,
            To = owner,
            Message = request.Reason ?? "Agent requested self-continuation.",
            Payload = new { continuationId = continuation.Id, continuation.DedupeKey, continuation.DueAt }
        });
        return continuation;
    }

    private static string NormalizeOutcome(string? outcome, string status, AgentBusContinuationRequest? continuation)
    {
        if (continuation is not null)
        {
            return "self_continue";
        }

        var normalized = string.IsNullOrWhiteSpace(outcome)
            ? null
            : outcome.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal);
        if (normalized is "done" or "handed_off" or "self_continue" or "human" or "failed")
        {
            return normalized;
        }

        return string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ? "failed" : "done";
    }

    private static AgentBusContinuationRequest? NormalizeContinuationRequest(
        AgentBusContinuationRequest? continuation,
        AgentBusTask task,
        string outcome)
    {
        if (!string.Equals(outcome, "self_continue", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        continuation ??= new AgentBusContinuationRequest();
        return continuation with
        {
            Owner = string.IsNullOrWhiteSpace(continuation.Owner) ? task.To : continuation.Owner!.Trim(),
            DedupeKey = string.IsNullOrWhiteSpace(continuation.DedupeKey) ? task.Id : continuation.DedupeKey!.Trim(),
            WakeAfterSeconds = Math.Clamp(continuation.WakeAfterSeconds, 0, 86400),
            MaxAttempts = Math.Clamp(continuation.MaxAttempts <= 0 ? 5 : continuation.MaxAttempts, 1, 50)
        };
    }

    private FileStream? TryCreateLock(string name)
    {
        Directory.CreateDirectory(LocksDirectory);
        var path = Path.Combine(LocksDirectory, name);
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private string TaskPath(string directory, string taskId)
    {
        return Path.Combine(directory, $"{taskId}.json");
    }

    private string ResultPath(string resultId)
    {
        return Path.Combine(ResultsDirectory, $"{resultId}.json");
    }

    private IEnumerable<string> EnumerateContinuationFiles(string? status)
    {
        string[] directories = string.IsNullOrWhiteSpace(status)
            ? [ContinuationsOpenDirectory, ContinuationsDoneDirectory, ContinuationsFailedDirectory]
            : [ContinuationStatusDirectory(status)];

        return directories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly));
    }

    private string ContinuationStatusDirectory(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "open" or "pending" => ContinuationsOpenDirectory,
            "done" or "completed" => ContinuationsDoneDirectory,
            "failed" => ContinuationsFailedDirectory,
            _ => throw new ArgumentException($"Unknown continuation status: {status}", nameof(status))
        };
    }

    private string ContinuationPath(string directory, string continuationId)
    {
        return Path.Combine(directory, $"{continuationId}.json");
    }

    private void MarkTaskFailed(AgentBusTask task, DateTimeOffset completedAt)
    {
        MoveTaskToTerminalDirectory(task, completedAt, "failed", TasksFailedDirectory);
    }

    private void MoveTaskToTerminalDirectory(AgentBusTask task, DateTimeOffset completedAt, string status, string targetDirectory)
    {
        var sourcePath = TaskPath(TasksClaimedDirectory, task.Id);
        if (!File.Exists(sourcePath))
        {
            sourcePath = TaskPath(TasksOpenDirectory, task.Id);
        }

        if (!File.Exists(sourcePath))
        {
            return;
        }

        var terminal = task with
        {
            Status = status,
            CompletedAt = completedAt
        };
        var targetPath = TaskPath(targetDirectory, task.Id);
        JsonFile.WriteAtomic(sourcePath, terminal);
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(sourcePath, targetPath);
    }

    private void MarkTaskToDirectory(AgentBusTask task, DateTimeOffset completedAt, string status)
    {
        var sourcePath = TaskPath(TasksOpenDirectory, task.Id);
        if (!File.Exists(sourcePath))
        {
            sourcePath = TaskPath(TasksClaimedDirectory, task.Id);
            if (!File.Exists(sourcePath))
            {
                return;
            }
        }

        var updated = task with
        {
            Status = status,
            CompletedAt = completedAt
        };

        var targetPath = TaskPath(TasksDoneDirectory, task.Id);
        JsonFile.WriteAtomic(sourcePath, updated);
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(sourcePath, targetPath);
    }

    private static string CreateTaskId()
    {
        return $"task-{DateTimeOffset.Now:yyyy-MM-dd-HHmmss}-{Guid.NewGuid():N}"[..38];
    }

    private static string CreateResultId(string taskId)
    {
        return $"result-{taskId}-{DateTimeOffset.Now:yyyyMMddHHmmss}";
    }

    private static string CreateContinuationId(string taskId)
    {
        return $"continuation-{taskId}-{Guid.NewGuid():N}";
    }

    private static string NormalizeAgentKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }
}
