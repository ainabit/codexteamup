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
        string? nextSuggestedAction = null)
    {
        InitializeIfMissing();
        var task = FindTask(taskId) ?? throw new FileNotFoundException($"Task not found: {taskId}");
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
        return result;
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
        if (!Directory.Exists(RootDirectory))
        {
            Initialize();
        }
    }

    private void AppendEvent(AgentBusEvent evt)
    {
        Directory.CreateDirectory(RootDirectory);
        var lineOptions = new JsonSerializerOptions(JsonFile.Options) { WriteIndented = false };
        File.AppendAllText(EventsPath, JsonSerializer.Serialize(evt, lineOptions) + Environment.NewLine);
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

    private FileStream? TryCreateLock(string name)
    {
        Directory.CreateDirectory(LocksDirectory);
        var path = Path.Combine(LocksDirectory, name);
        try
        {
            return new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
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
