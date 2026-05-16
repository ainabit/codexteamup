using System.Diagnostics;
using System.Text.Json;
using CodexTeamUp.AgentBus;
using CodexTeamUp.AppServer;
using CodexTeamUp.Core;

namespace CodexTeamUp.Mcp;

/// <summary>
/// Lightweight tool registry for the CodexTeamUp MCP-style JSONL host.
/// </summary>
public sealed class McpToolRegistry
{
    private const string DefaultSpeed = "standard";
    private static readonly TimeSpan WakeupReadyTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WakeupReadyPollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>All tool names exposed by CodexTeamUp.</summary>
    public static readonly IReadOnlyList<string> KnownToolNames =
    [
        "agentbus_init",
        "agentbus_list_agents",
        "agentbus_register_agent",
        "agentbus_create_task",
        "agentbus_list_tasks",
        "agentbus_claim_task",
        "agentbus_write_result",
        "agentbus_wait_result",
        "codex_thread_list",
        "codex_thread_read",
        "codex_turn_start",
        "bridge_dispatch_task",
        "bridge_notify_result",
        "team_create_agent",
        "team_ensure_agents",
        "team_discover_agents",
        "team_send_message",
        "team_dashboard_export"
    ];

    private readonly Dictionary<string, Func<JsonElement, CancellationToken, Task<object>>> _handlers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the names of registered tools.</summary>
    public IReadOnlyList<string> ToolNames => _handlers.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Registers a tool handler.</summary>
    public void Register(string name, Func<JsonElement, CancellationToken, Task<object>> handler)
    {
        _handlers[name] = handler;
    }

    /// <summary>Invokes a registered tool.</summary>
    public Task<object> InvokeAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!_handlers.TryGetValue(name, out var handler))
        {
            throw new InvalidOperationException($"Unknown tool: {name}");
        }

        return handler(arguments, cancellationToken);
    }

    /// <summary>Creates the default CodexTeamUp tool registry.</summary>
    public static McpToolRegistry CreateDefault(string busRoot, IAppServerClient appServer)
    {
        var registry = new McpToolRegistry();

        registry.Register("agentbus_init", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            bus.Initialize();
            return Task.FromResult<object>(new { busRoot = bus.RootDirectory });
        });

        registry.Register("agentbus_list_agents", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            return Task.FromResult<object>(new { agents = bus.ListAgents() });
        });

        registry.Register("agentbus_register_agent", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            var agent = new AgentDefinition
            {
                Id = Required(args, "id"),
                Role = Optional(args, "role") ?? Required(args, "id"),
                ThreadId = Optional(args, "threadId"),
                Cwd = Optional(args, "cwd"),
                AllowedPaths = Csv(args, "allowedPaths"),
                InstructionFiles = Csv(args, "instructionFiles"),
                ReturnTo = Optional(args, "returnTo"),
                Model = Optional(args, "model"),
                ReasoningEffort = NormalizeReasoningEffort(Optional(args, "reasoningEffort") ?? Optional(args, "effort")),
                Speed = NormalizeSpeed(Optional(args, "speed")),
                Status = Optional(args, "status") ?? "active"
            };
            return Task.FromResult<object>(new { agent = bus.RegisterAgent(ApplyAgentRuntimeDefaults(agent, bus.FindAgent(agent.Id))) });
        });

        registry.Register("agentbus_create_task", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            var cwd = Optional(args, "cwd") ?? Environment.CurrentDirectory;
            var task = bus.CreateTask(
                Required(args, "from"),
                Required(args, "to"),
                Required(args, "title"),
                Required(args, "prompt"),
                Optional(args, "project") ?? new DirectoryInfo(cwd).Name,
                cwd,
                Csv(args, "allowedPaths"),
                Optional(args, "returnTo"));
            return Task.FromResult<object>(new { task });
        });

        registry.Register("agentbus_list_tasks", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            return Task.FromResult<object>(new { tasks = bus.ListTasks(Optional(args, "to"), Optional(args, "status")) });
        });

        registry.Register("agentbus_claim_task", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            return Task.FromResult<object>(new { task = bus.ClaimTask(Required(args, "taskId"), Optional(args, "owner")) });
        });

        registry.Register("agentbus_write_result", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            var result = bus.WriteResult(
                Required(args, "taskId"),
                Required(args, "summary"),
                Optional(args, "status") ?? "completed",
                Optional(args, "from"),
                Optional(args, "to"),
                Optional(args, "commit"),
                Csv(args, "tests", "checks"),
                Csv(args, "openQuestions"),
                Csv(args, "changedFiles"),
                Csv(args, "artifacts"),
                Optional(args, "nextSuggestedAction"));
            return Task.FromResult<object>(new { result });
        });

        registry.Register("agentbus_wait_result", (args, ct) =>
        {
            var bus = Bus(args, busRoot);
            var taskId = Required(args, "taskId");
            var timeoutSeconds = Math.Clamp(Int(args, "timeoutSeconds", 300), 1, 1800);
            var stopwatch = Stopwatch.StartNew();
            var result = bus.WaitForResult(taskId, TimeSpan.FromSeconds(timeoutSeconds), ct);
            stopwatch.Stop();
            return Task.FromResult<object>(new
            {
                taskId,
                completed = result is not null,
                result,
                waitedMs = stopwatch.ElapsedMilliseconds
            });
        });

        registry.Register("codex_thread_list", async (args, ct) =>
        {
            var result = await appServer.ListThreadsAsync(Optional(args, "cwd"), Int(args, "limit", 30), ct).ConfigureAwait(false);
            return new { result.Succeeded, result.ResultJson, result.Error };
        });

        registry.Register("codex_thread_read", async (args, ct) =>
        {
            var result = await appServer.ReadThreadAsync(Required(args, "threadId"), Bool(args, "includeTurns"), ct).ConfigureAwait(false);
            return new { result.Succeeded, result.ResultJson, result.Error };
        });

        registry.Register("codex_turn_start", async (args, ct) =>
        {
            var settings = RuntimeSettings(Optional(args, "model"), Optional(args, "reasoningEffort") ?? Optional(args, "effort"), Optional(args, "speed"), null);
            var result = await appServer.SendTurnAsync(Required(args, "threadId"), Required(args, "message"), Optional(args, "cwd"), settings, ct).ConfigureAwait(false);
            return new { result.Succeeded, result.ResultJson, result.Error };
        });

        registry.Register("bridge_dispatch_task", async (args, ct) =>
        {
            var bus = Bus(args, busRoot);
            var task = bus.FindTask(Required(args, "taskId")) ?? throw new FileNotFoundException("Task not found.");
            var agent = await EnsureAgentBoundAsync(bus, appServer, task.To, task.Cwd, ct).ConfigureAwait(false);

            var message = BuildTaskWakeMessage(task.Id, task.To, DefaultArchitectFor(task.To));
            var wakeup = await SendTurnWhenReadyAsync(appServer, agent.ThreadId!, message, task.Cwd, RuntimeSettings(agent), ct).ConfigureAwait(false);
            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = wakeup.Deferred ? "task.dispatch_deferred" : wakeup.Result.Succeeded ? "task.dispatched" : "task.dispatch_failed",
                TaskId = task.Id,
                From = task.From,
                To = task.To,
                Message = WakeupEventMessage(wakeup),
                Payload = WakeupEventPayload(agent.ThreadId!, wakeup)
            });
            return new { wakeup.Result.Succeeded, wakeup.Result.ResultJson, wakeup.Result.Error, wakeup.Deferred, wakeup.InitialStatus, wakeup.FinalStatus };
        });

        registry.Register("bridge_notify_result", async (args, ct) =>
        {
            var bus = Bus(args, busRoot);
            var busResult = bus.FindResult(Required(args, "resultId")) ?? throw new FileNotFoundException("Result not found.");
            var targetThreadId = Optional(args, "toThread");
            var targetAgent = Optional(args, "toAgent") ?? busResult.To;
            var cwd = Optional(args, "cwd");

            if (string.IsNullOrWhiteSpace(targetThreadId))
            {
                var agent = await EnsureAgentBoundAsync(bus, appServer, targetAgent, cwd, ct).ConfigureAwait(false);
                targetThreadId = agent.ThreadId;
                cwd ??= agent.Cwd;
            }

            if (string.IsNullOrWhiteSpace(targetThreadId))
            {
                throw new InvalidOperationException($"No target thread found for result {busResult.Id}.");
            }

            var message =
                $"CodexTeamUp result arrived: {busResult.Id}.\n" +
                $"Please read .codexteamup/agentbus/results/{busResult.Id}.json and review the result, scope, and next steps.";
            var notifyStartedAt = DateTimeOffset.Now;
            var targetSettings = !string.IsNullOrWhiteSpace(targetAgent)
                ? RuntimeSettings(bus.FindAgent(targetAgent))
                : null;
            var wakeup = await SendTurnWhenReadyAsync(appServer, targetThreadId, message, cwd, targetSettings, ct).ConfigureAwait(false);
            var result = wakeup.Result;
            var turnId = TryExtractTurnId(result.ResultJson);
            var notifyMessage = WakeupEventMessage(wakeup, turnId);
            if (result.Succeeded)
            {
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = "result.notified",
                    ResultId = busResult.Id,
                    From = busResult.From,
                    To = targetAgent,
                    Message = notifyMessage,
                    Payload = new
                    {
                        targetThreadId,
                        targetStatus = wakeup.FinalStatus,
                        initialStatus = wakeup.InitialStatus,
                        turnId,
                        notifyStartedAt,
                        notifyCompletedAt = DateTimeOffset.Now,
                        notifyLatencyMs = wakeup.ElapsedMs,
                        waitedMs = wakeup.WaitedMs
                    }
                });
            }
            else if (wakeup.Deferred)
            {
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = "result.notify_deferred",
                    ResultId = busResult.Id,
                    From = busResult.From,
                    To = targetAgent,
                    Message = notifyMessage,
                    Payload = new
                    {
                        targetThreadId,
                        targetStatus = wakeup.FinalStatus,
                        initialStatus = wakeup.InitialStatus,
                        notifyStartedAt,
                        notifyCompletedAt = DateTimeOffset.Now,
                        notifyLatencyMs = wakeup.ElapsedMs,
                        waitedMs = wakeup.WaitedMs,
                        result.Error
                    }
                });
            }
            else
            {
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = "result.notify_failed",
                    ResultId = busResult.Id,
                    From = busResult.From,
                    To = targetAgent,
                    Message = notifyMessage,
                    Payload = new
                    {
                        targetThreadId,
                        targetStatus = wakeup.FinalStatus,
                        initialStatus = wakeup.InitialStatus,
                        notifyStartedAt,
                        notifyCompletedAt = DateTimeOffset.Now,
                        notifyLatencyMs = wakeup.ElapsedMs,
                        waitedMs = wakeup.WaitedMs,
                        result.Error
                    }
                });
            }

            return new
            {
                result = busResult,
                target = new { agent = targetAgent, threadId = targetThreadId, status = wakeup.FinalStatus, initialStatus = wakeup.InitialStatus },
                wakeup = new { result.Succeeded, result.ResultJson, result.Error, turnId, wakeup.Deferred, notifyLatencyMs = wakeup.ElapsedMs, wakeup.WaitedMs }
            };
        });

        registry.Register("team_discover_agents", async (args, ct) =>
        {
            var bus = Bus(args, busRoot);
            bus.Initialize();
            var agents = Csv(args, "agents");
            if (agents.Count == 0)
            {
                throw new ArgumentException("team_discover_agents requires an explicit agents list. CodexTeamUp does not choose project roles.");
            }

            var cwd = Optional(args, "cwd") ?? Environment.CurrentDirectory;
            var project = Optional(args, "project") ?? new DirectoryInfo(cwd).Name;
            var list = await appServer.ListThreadsAsync(cwd, Int(args, "limit", 100), ct).ConfigureAwait(false);
            if (!list.Succeeded || string.IsNullOrWhiteSpace(list.ResultJson))
            {
                return new { list.Succeeded, list.Error, bindings = Array.Empty<object>() };
            }

            var threads = AppServerThreadMapper.ParseListResult(list.ResultJson);
            var bindings = AgentThreadMatcher.MatchAgents(agents, threads, cwd);
            var registered = bindings.Select(binding => bus.RegisterAgent(new AgentDefinition
            {
                Id = binding.AgentId,
                Role = RoleFromAgentId(binding.AgentId),
                DisplayName = binding.AgentId,
                ThreadId = binding.ThreadId,
                Cwd = cwd,
                ReturnTo = IsArchitect(binding.AgentId) ? null : DefaultArchitectFor(binding.AgentId),
                Model = null,
                ReasoningEffort = DefaultReasoningEffortForSpeed(null),
                Speed = DefaultSpeed,
                Status = binding.ThreadId is null ? "missing-thread" : "active"
            })).ToList();

            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = "team.discovered",
                Message = $"Project {project}: {registered.Count} agents discovered."
            });

            return new { project, bindings, registered };
        });

        registry.Register("team_create_agent", async (args, ct) =>
        {
            var bus = Bus(args, busRoot);
            bus.Initialize();
            var cwd = Optional(args, "cwd") ?? Environment.CurrentDirectory;
            var spec = new TeamAgentSpec(
                Required(args, "id"),
                Optional(args, "role") ?? Required(args, "id"),
                Optional(args, "displayName") ?? Required(args, "id"),
                Csv(args, "allowedPaths"),
                Csv(args, "instructionFiles"),
                Optional(args, "returnTo"),
                Optional(args, "initialPrompt"),
                Optional(args, "model"),
                Optional(args, "reasoningEffort") ?? Optional(args, "effort"),
                Optional(args, "speed"));

            var agent = await EnsureOrCreateAgentAsync(bus, appServer, spec, cwd, createMissing: true, prime: true, ct).ConfigureAwait(false);
            return new { agent };
        });

        registry.Register("team_ensure_agents", async (args, ct) =>
        {
            var bus = Bus(args, busRoot);
            bus.Initialize();
            var cwd = Optional(args, "cwd") ?? Environment.CurrentDirectory;
            var createMissing = !string.Equals(Optional(args, "createMissing"), "false", StringComparison.OrdinalIgnoreCase);
            var prime = !string.Equals(Optional(args, "prime"), "false", StringComparison.OrdinalIgnoreCase);
            var specs = ParseTeamAgentSpecs(args).ToList();
            if (specs.Count == 0)
            {
                throw new ArgumentException("team_ensure_agents requires agentsJson or agents.");
            }

            var registered = new List<AgentDefinition>();
            foreach (var spec in specs)
            {
                registered.Add(await EnsureOrCreateAgentAsync(bus, appServer, spec, cwd, createMissing, prime, ct).ConfigureAwait(false));
            }

            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = "team.ensured",
                Message = $"Ensured {registered.Count} CodexTeamUp agents."
            });

            return new { agents = registered };
        });

        registry.Register("team_send_message", async (args, ct) =>
        {
            var bus = Bus(args, busRoot);
            var from = Required(args, "from");
            var to = Required(args, "to");
            var message = Required(args, "message");
            var title = Optional(args, "title") ?? $"Message from {from} to {to}";
            var cwd = Optional(args, "cwd") ?? Environment.CurrentDirectory;
            var task = bus.CreateTask(
                from,
                to,
                title,
                message,
                Optional(args, "project") ?? new DirectoryInfo(cwd).Name,
                cwd,
                Csv(args, "allowedPaths"),
                Optional(args, "returnTo") ?? from);

            var agent = await EnsureAgentBoundAsync(bus, appServer, to, cwd, ct).ConfigureAwait(false);

            var wake = BuildTaskWakeMessage(task.Id, to, Optional(args, "returnTo") ?? from);
            var wakeup = await SendTurnWhenReadyAsync(appServer, agent.ThreadId!, wake, cwd, RuntimeSettings(agent), ct).ConfigureAwait(false);
            var result = wakeup.Result;
            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = wakeup.Deferred ? "team.message.deferred" : result.Succeeded ? "team.message.sent" : "team.message.failed",
                TaskId = task.Id,
                From = from,
                To = to,
                Message = wakeup.Deferred ? $"{title}; {WakeupEventMessage(wakeup)}" : title,
                Payload = WakeupEventPayload(agent.ThreadId!, wakeup)
            });

            AgentBusResult? waitedResult = null;
            long? waitedMs = null;
            if (Bool(args, "waitResult") && result.Succeeded)
            {
                var timeoutSeconds = Math.Clamp(Int(args, "timeoutSeconds", 300), 1, 1800);
                var waitStopwatch = Stopwatch.StartNew();
                waitedResult = bus.WaitForResult(task.Id, TimeSpan.FromSeconds(timeoutSeconds), ct);
                waitStopwatch.Stop();
                waitedMs = waitStopwatch.ElapsedMilliseconds;
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = waitedResult is null ? "team.wait.timeout" : "team.wait.completed",
                    TaskId = task.Id,
                    ResultId = waitedResult?.Id,
                    From = to,
                    To = from,
                    Message = waitedResult is null
                        ? $"timeout after {waitedMs}ms"
                        : $"result observed after {waitedMs}ms"
                });
            }

            var wait = Bool(args, "waitResult")
                ? new { completed = waitedResult is not null, waitedMs, result = waitedResult }
                : null;

            return new { task, wakeup = new { result.Succeeded, result.ResultJson, result.Error, wakeup.Deferred, wakeup.InitialStatus, wakeup.FinalStatus }, wait };
        });

        registry.Register("team_dashboard_export", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            var path = AgentBusDashboard.Export(bus, Optional(args, "outputPath"));
            return Task.FromResult<object>(new { path });
        });

        return registry;
    }

    /// <summary>Creates a registry whose tools forward to the HTTP backend service.</summary>
    public static McpToolRegistry CreateServiceBacked(Uri serviceUri)
    {
        var registry = new McpToolRegistry();
        var client = new ServiceMcpBackendClient(serviceUri);
        foreach (var name in KnownToolNames)
        {
            registry.Register(name, (args, ct) => client.CallToolAsync(name, args, ct));
        }

        return registry;
    }

    private static AgentBusStore Bus(JsonElement args, string defaultBusRoot)
    {
        var busRoot = Optional(args, "busRoot");
        if (!string.IsNullOrWhiteSpace(busRoot))
        {
            return new AgentBusStore(busRoot);
        }

        var cwd = Optional(args, "cwd");
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            return new AgentBusStore(DefaultBusRootForCwd(cwd));
        }

        return new AgentBusStore(defaultBusRoot);
    }

    private static string RoleFromAgentId(string agentId)
    {
        var lower = agentId.ToLowerInvariant();
        if (lower.Contains("architect")) return "Architect / Coordinator";
        if (lower.Contains("web")) return "Web Implementation";
        if (lower.Contains("backend") || lower.Contains("database")) return "Backend / Data";
        if (lower.Contains("designer") || lower.Contains("design")) return "Designer / UX";
        return agentId;
    }

    private static string DefaultBusRootForCwd(string cwd)
    {
        return Path.Combine(cwd, ".codexteamup", "agentbus");
    }

    private static bool IsArchitect(string agentId)
    {
        var normalized = agentId.Replace("\\", "/", StringComparison.Ordinal).ToLowerInvariant();
        return normalized is "ctu/architect" or "ag/architect" or "architect" || normalized.Contains("architect", StringComparison.Ordinal);
    }

    private static string DefaultArchitectFor(string agentId)
    {
        var normalized = agentId.Replace("\\", "/", StringComparison.Ordinal).ToLowerInvariant();
        if (normalized.StartsWith("ag/", StringComparison.Ordinal))
        {
            return "ag/architect";
        }

        return "ctu/architect";
    }

    private sealed record TeamAgentSpec(
        string Id,
        string Role,
        string DisplayName,
        IReadOnlyList<string> AllowedPaths,
        IReadOnlyList<string> InstructionFiles,
        string? ReturnTo,
        string? InitialPrompt,
        string? Model,
        string? ReasoningEffort,
        string? Speed);

    private static IEnumerable<TeamAgentSpec> ParseTeamAgentSpecs(JsonElement args)
    {
        var agentsJson = Optional(args, "agentsJson");
        if (!string.IsNullOrWhiteSpace(agentsJson))
        {
            using var doc = JsonDocument.Parse(agentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("agentsJson must be a JSON array.");
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var id = JsonString(item, "id") ?? throw new ArgumentException("Agent spec is missing id.");
                yield return new TeamAgentSpec(
                    id,
                    JsonString(item, "role") ?? id,
                    JsonString(item, "displayName") ?? id,
                    JsonStringList(item, "allowedPaths"),
                    JsonStringList(item, "instructionFiles"),
                    JsonString(item, "returnTo"),
                    JsonString(item, "initialPrompt"),
                    JsonString(item, "model"),
                    JsonString(item, "reasoningEffort") ?? JsonString(item, "effort"),
                    JsonString(item, "speed"));
            }

            yield break;
        }

        foreach (var id in Csv(args, "agents"))
        {
            yield return new TeamAgentSpec(id, id, id, [], [], IsArchitect(id) ? null : DefaultArchitectFor(id), null, null, null, null);
        }
    }

    private static async Task<AgentDefinition> EnsureOrCreateAgentAsync(
        AgentBusStore bus,
        IAppServerClient appServer,
        TeamAgentSpec spec,
        string cwd,
        bool createMissing,
        bool prime,
        CancellationToken cancellationToken)
    {
        var existing = bus.FindAgent(spec.Id);
        var threadId = existing?.ThreadId;

        var list = await appServer.ListThreadsAsync(cwd, 100, cancellationToken).ConfigureAwait(false);
        var threads = list.Succeeded && !string.IsNullOrWhiteSpace(list.ResultJson)
            ? AppServerThreadMapper.ParseListResult(list.ResultJson)
            : [];

        if (!string.IsNullOrWhiteSpace(threadId) && !ThreadExists(threads, threadId, cwd))
        {
            threadId = null;
        }

        if (string.IsNullOrWhiteSpace(threadId) && threads.Count > 0)
        {
            threadId = AgentThreadMatcher.MatchAgents([spec.Id], threads, cwd).FirstOrDefault()?.ThreadId;
        }

        AppServerCallResult? created = null;
        if (string.IsNullOrWhiteSpace(threadId) && createMissing)
        {
            var createSettings = RuntimeSettings(spec.Model, spec.ReasoningEffort, spec.Speed, existing);
            created = await appServer.StartThreadAsync(cwd, spec.DisplayName, spec.Role, createSettings, cancellationToken).ConfigureAwait(false);
            if (!created.Succeeded || string.IsNullOrWhiteSpace(created.ResultJson))
            {
                throw new InvalidOperationException($"Could not create visible Codex thread for {spec.Id}: {created.Error}");
            }

            threadId = TryExtractThreadId(created.ResultJson)
                ?? throw new InvalidOperationException($"Could not read new thread id for {spec.Id}: {created.ResultJson}");
        }

        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException($"Agent {spec.Id} has no visible thread and createMissing=false.");
        }

        await EnsureThreadNameAsync(appServer, threadId, spec.DisplayName, cancellationToken).ConfigureAwait(false);

        var agent = bus.RegisterAgent(new AgentDefinition
        {
            Id = spec.Id,
            Role = spec.Role,
            DisplayName = spec.DisplayName,
            ThreadId = threadId,
            Cwd = cwd,
            AllowedPaths = spec.AllowedPaths,
            InstructionFiles = spec.InstructionFiles,
            ReturnTo = spec.ReturnTo ?? (IsArchitect(spec.Id) ? null : DefaultArchitectFor(spec.Id)),
            Model = RuntimeSettings(spec.Model, spec.ReasoningEffort, spec.Speed, existing)?.Model,
            ReasoningEffort = RuntimeSettings(spec.Model, spec.ReasoningEffort, spec.Speed, existing)?.ReasoningEffort,
            Speed = NormalizeSpeed(spec.Speed) ?? NormalizeSpeed(existing?.Speed) ?? DefaultSpeed,
            Status = "active"
        });

        bus.RecordEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = created is null ? "agent.bound" : "agent.created",
            To = spec.Id,
            Message = created is null ? "Visible thread bound." : "Visible thread created."
        });

        if (prime)
        {
            var prompt = BuildAgentPrimePrompt(agent, spec.InitialPrompt);
            var wake = await appServer.SendTurnAsync(threadId, prompt, cwd, RuntimeSettings(agent), cancellationToken).ConfigureAwait(false);
            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = "agent.primed",
                To = spec.Id,
                Message = wake.Succeeded ? "Initial prompt sent." : wake.Error
            });
        }

        return agent;
    }

    private static bool ThreadExists(IReadOnlyList<CodexThreadRecord> threads, string threadId, string cwd)
    {
        return threads.Any(thread =>
            string.Equals(thread.Id, threadId, StringComparison.Ordinal) &&
            (string.IsNullOrWhiteSpace(thread.Cwd) || string.Equals(thread.Cwd, cwd, StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task EnsureThreadNameAsync(
        IAppServerClient appServer,
        string threadId,
        string displayName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        var nameResult = await appServer.CallAsync("thread/name/set", new { threadId, name = displayName }, cancellationToken)
            .ConfigureAwait(false);
        if (!nameResult.Succeeded)
        {
            throw new InvalidOperationException($"Could not name visible Codex thread {threadId} as {displayName}: {nameResult.Error}");
        }
    }

    private static string BuildAgentPrimePrompt(AgentDefinition agent, string? initialPrompt)
    {
        var allowedPaths = agent.AllowedPaths.Count == 0
            ? "- no explicit path restriction provided"
            : string.Join("\n", agent.AllowedPaths.Select(path => $"- {path}"));
        var instructionFiles = agent.InstructionFiles.Count == 0
            ? "- AGENTS.md\n- .codexteamup/agentbus/agents.json"
            : string.Join("\n", agent.InstructionFiles.Select(path => $"- {path}"));

        return $"""
        You are {agent.Id} in project {new DirectoryInfo(agent.Cwd ?? Environment.CurrentDirectory).Name}.

        Working directory:
        {agent.Cwd}

        Role:
        {agent.Role}

        Runtime:
        speed={agent.Speed ?? DefaultSpeed}
        model={agent.Model ?? "(Codex default)"}
        reasoningEffort={agent.ReasoningEffort ?? DefaultReasoningEffortForSpeed(agent.Speed)}

        Primary work areas:
        {allowedPaths}

        Read first:
        {instructionFiles}

        Communication:
        Use CodexTeamUp MCP.
        If a task needs another ctu/* agent, you may use team_send_message. Set returnTo to your own agent ID. Use waitResult=true for quick questions and omit waitResult for asynchronous delegation.
        Read only open tasks for {agent.Id} from .codexteamup/agentbus.
        Work only on concrete AgentBus tasks addressed to {agent.Id}, or on a task ID explicitly named in a wakeup.
        Claim tasks before working on them.
        Keep your visible chat useful for humans: briefly state what you are about to do, important decisions or blockers, and the final outcome. Do not rely only on AgentBus files for human-visible progress.
        Write results with agentbus_write_result. If you edited files, set changedFiles explicitly. Put verification work in tests, or checks if your tool schema exposes that alias.
        Notify {agent.ReturnTo ?? DefaultArchitectFor(agent.Id)} afterwards with bridge_notify_result.
        Do not use PowerShell/ctu.ps1 commands as the normal communication path.
        If no matching open task file exists: do not create a replacement task, do not reconstruct a task from chat text, and do not write a result. Reply briefly in chat with a diagnosis of which task ID or file is missing.

        {initialPrompt}
        """;
    }

    private static string BuildTaskWakeMessage(string taskId, string agentId, string returnTo)
    {
        return $"""
        New CodexTeamUp message/task for {agentId}: {taskId}.

        Please read exactly this task file:
        .codexteamup/agentbus/tasks/open/{taskId}.json

        Flow:
        1. Verify that the task file exists and is addressed to {agentId}.
        2. Claim exactly this task.
        3. Write a short visible-chat note about what you are doing now, so the human can inspect this thread without opening AgentBus files.
        4. Work on the task.
        5. If you make a meaningful decision, hit a blocker, ask another agent, or finish a major step, leave a short visible-chat note.
        6. Write exactly one result with agentbus_write_result. If files changed, include changedFiles. Put verification work in tests/checks.
        7. Notify {returnTo} with bridge_notify_result.

        If the task file is missing or not addressed to {agentId}: do not create a replacement task, do not reconstruct a task from chat text, and do not write a result. Reply only briefly in chat with the diagnosis.
        """;
    }

    private static string? TryExtractThreadId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("thread", out var thread) && thread.TryGetProperty("id", out var nestedId))
            {
                return nestedId.GetString();
            }

            return root.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryExtractTurnId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("turn", out var turn) && turn.TryGetProperty("id", out var nestedId))
            {
                return nestedId.GetString();
            }

            return root.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? JsonString(JsonElement item, string name)
    {
        return item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static IReadOnlyList<string> JsonStringList(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value))
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(entry => entry.ValueKind == JsonValueKind.String)
            .Select(entry => entry.GetString()!)
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .ToList();
    }

    private static async Task<AgentDefinition> EnsureAgentBoundAsync(
        AgentBusStore bus,
        IAppServerClient appServer,
        string agentId,
        string? cwd,
        CancellationToken cancellationToken)
    {
        var existing = bus.FindAgent(agentId);
        if (!string.IsNullOrWhiteSpace(existing?.ThreadId))
        {
            return existing;
        }

        var list = await appServer.ListThreadsAsync(cwd, 100, cancellationToken).ConfigureAwait(false);
        if (list.Succeeded && !string.IsNullOrWhiteSpace(list.ResultJson))
        {
            var threads = AppServerThreadMapper.ParseListResult(list.ResultJson);
            var binding = AgentThreadMatcher.MatchAgents([agentId], threads, cwd).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(binding?.ThreadId))
            {
                return bus.RegisterAgent(new AgentDefinition
                {
                    Id = agentId,
                    Role = RoleFromAgentId(agentId),
                    DisplayName = agentId,
                    ThreadId = binding.ThreadId,
                    Cwd = cwd,
                    ReturnTo = IsArchitect(agentId) ? null : DefaultArchitectFor(agentId),
                    ReasoningEffort = DefaultReasoningEffortForSpeed(null),
                    Speed = DefaultSpeed,
                    Status = "active"
                });
            }
        }

        if (existing is not null)
        {
            throw new InvalidOperationException($"Agent {agentId} is registered but has no bound visible thread. Open or name the Codex Desktop thread as '{agentId}'.");
        }

        throw new InvalidOperationException($"Agent {agentId} could not be auto-bound. Open a visible Codex Desktop thread named '{agentId}'.");
    }

    private static async Task<string?> TryReadThreadStatusAsync(
        IAppServerClient appServer,
        string threadId,
        string? cwd,
        CancellationToken cancellationToken)
    {
        string? listStatus = null;
        try
        {
            var list = await appServer.ListThreadsAsync(cwd, 100, cancellationToken).ConfigureAwait(false);
            if (list.Succeeded && !string.IsNullOrWhiteSpace(list.ResultJson))
            {
                listStatus = AppServerThreadMapper.ParseListResult(list.ResultJson)
                    .FirstOrDefault(thread => string.Equals(thread.Id, threadId, StringComparison.Ordinal))?
                    .Status;

                if (!IsUncertainThreadStatus(listStatus))
                {
                    return listStatus;
                }
            }
        }
        catch (Exception)
        {
        }

        try
        {
            var read = await appServer.ReadThreadAsync(threadId, includeTurns: true, cancellationToken).ConfigureAwait(false);
            if (!read.Succeeded || string.IsNullOrWhiteSpace(read.ResultJson))
            {
                return listStatus;
            }

            var readStatus = AppServerThreadMapper.ParseReadResult(read.ResultJson)?.Thread.Status;
            if (!IsUncertainThreadStatus(readStatus))
            {
                return readStatus;
            }

            return TryFindBusyTurnStatus(read.ResultJson) ?? readStatus ?? listStatus;
        }
        catch (Exception)
        {
            return listStatus;
        }
    }

    private static bool IsUncertainThreadStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        var normalized = status.Trim().ToLowerInvariant();
        return normalized is "notloaded" or "not_loaded" or "unknown";
    }

    private static string? TryFindBusyTurnStatus(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var thread = root.TryGetProperty("thread", out var threadElement) ? threadElement : root;
            if (!thread.TryGetProperty("turns", out var turns) || turns.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? lastStatus = null;
            foreach (var turn in turns.EnumerateArray())
            {
                var status = JsonString(turn, "status");
                if (string.IsNullOrWhiteSpace(status))
                {
                    continue;
                }

                lastStatus = status;
                if (IsBusyThreadStatus(status))
                {
                    return status;
                }
            }

            return lastStatus;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<WakeupSendResult> SendTurnWhenReadyAsync(
        IAppServerClient appServer,
        string threadId,
        string message,
        string? cwd,
        AppServerAgentSettings? settings,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var initialStatus = await TryReadThreadStatusAsync(appServer, threadId, cwd, cancellationToken).ConfigureAwait(false);
        var finalStatus = initialStatus;
        var waitedMs = 0L;

        while (IsBusyThreadStatus(finalStatus) && stopwatch.Elapsed < WakeupReadyTimeout)
        {
            await Task.Delay(WakeupReadyPollInterval, cancellationToken).ConfigureAwait(false);
            waitedMs = stopwatch.ElapsedMilliseconds;
            finalStatus = await TryReadThreadStatusAsync(appServer, threadId, cwd, cancellationToken).ConfigureAwait(false);
        }

        if (IsBusyThreadStatus(finalStatus))
        {
            stopwatch.Stop();
            return new WakeupSendResult(
                new AppServerCallResult(false, null, $"Target thread {threadId} is still busy ({finalStatus}); wakeup deferred."),
                initialStatus,
                finalStatus,
                Deferred: true,
                waitedMs,
                stopwatch.ElapsedMilliseconds);
        }

        var result = await appServer.SendTurnAsync(threadId, message, cwd, settings, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        return new WakeupSendResult(result, initialStatus, finalStatus, Deferred: false, waitedMs, stopwatch.ElapsedMilliseconds);
    }

    private static bool IsBusyThreadStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        var normalized = status.Trim().ToLowerInvariant();
        return normalized.Contains("active", StringComparison.Ordinal)
            || normalized.Contains("running", StringComparison.Ordinal)
            || normalized.Contains("inprogress", StringComparison.Ordinal)
            || normalized.Contains("in_progress", StringComparison.Ordinal)
            || normalized.Contains("busy", StringComparison.Ordinal);
    }

    private static string WakeupEventMessage(WakeupSendResult wakeup, string? turnId = null)
    {
        if (wakeup.Deferred)
        {
            return $"turn/start deferred; latencyMs={wakeup.ElapsedMs}; waitedMs={wakeup.WaitedMs}; targetStatus={wakeup.FinalStatus ?? "unknown"}; error={SafeText.Preview(wakeup.Result.Error, 180)}";
        }

        return wakeup.Result.Succeeded
            ? $"turn/start sent; latencyMs={wakeup.ElapsedMs}; waitedMs={wakeup.WaitedMs}; turnId={turnId ?? TryExtractTurnId(wakeup.Result.ResultJson) ?? "unknown"}; targetStatus={wakeup.FinalStatus ?? "unknown"}"
            : $"turn/start failed; latencyMs={wakeup.ElapsedMs}; waitedMs={wakeup.WaitedMs}; targetStatus={wakeup.FinalStatus ?? "unknown"}; error={SafeText.Preview(wakeup.Result.Error, 180)}";
    }

    private static object WakeupEventPayload(string threadId, WakeupSendResult wakeup)
    {
        return new
        {
            targetThreadId = threadId,
            initialStatus = wakeup.InitialStatus,
            targetStatus = wakeup.FinalStatus,
            wakeup.Deferred,
            wakeup.WaitedMs,
            wakeup.ElapsedMs,
            wakeup.Result.ResultJson,
            wakeup.Result.Error
        };
    }

    private sealed record WakeupSendResult(
        AppServerCallResult Result,
        string? InitialStatus,
        string? FinalStatus,
        bool Deferred,
        long WaitedMs,
        long ElapsedMs);

    private static string Required(JsonElement args, string name)
    {
        return Optional(args, name) ?? throw new ArgumentException($"Missing argument: {name}");
    }

    private static string? Optional(JsonElement args, string name)
    {
        return args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static AgentDefinition ApplyAgentRuntimeDefaults(AgentDefinition agent, AgentDefinition? existing = null)
    {
        var settings = RuntimeSettings(agent.Model, agent.ReasoningEffort, agent.Speed, existing);
        return agent with
        {
            Model = settings?.Model,
            ReasoningEffort = settings?.ReasoningEffort,
            Speed = NormalizeSpeed(agent.Speed) ?? NormalizeSpeed(existing?.Speed) ?? DefaultSpeed
        };
    }

    private static AppServerAgentSettings? RuntimeSettings(AgentDefinition? agent)
    {
        return agent is null
            ? null
            : RuntimeSettings(agent.Model, agent.ReasoningEffort, agent.Speed, agent);
    }

    private static AppServerAgentSettings? RuntimeSettings(
        string? model,
        string? reasoningEffort,
        string? speed,
        AgentDefinition? existing)
    {
        var speedSpecified = !string.IsNullOrWhiteSpace(speed);
        var normalizedSpeed = NormalizeSpeed(speed) ?? NormalizeSpeed(existing?.Speed) ?? DefaultSpeed;
        var resolvedModel = BlankToNull(model)
            ?? (speedSpecified ? DefaultModelForSpeed(normalizedSpeed) : BlankToNull(existing?.Model) ?? DefaultModelForSpeed(normalizedSpeed));
        var resolvedEffort = NormalizeReasoningEffort(reasoningEffort)
            ?? (speedSpecified
                ? DefaultReasoningEffortForSpeed(normalizedSpeed)
                : NormalizeReasoningEffort(existing?.ReasoningEffort) ?? DefaultReasoningEffortForSpeed(normalizedSpeed));
        return string.IsNullOrWhiteSpace(resolvedModel) && string.IsNullOrWhiteSpace(resolvedEffort)
            ? null
            : new AppServerAgentSettings(resolvedModel, resolvedEffort);
    }

    private static string? DefaultModelForSpeed(string? speed)
    {
        return NormalizeSpeed(speed) switch
        {
            "fast" => "gpt-5.4-mini",
            _ => null
        };
    }

    private static string DefaultReasoningEffortForSpeed(string? speed)
    {
        return NormalizeSpeed(speed) switch
        {
            "fast" => "low",
            "deep" => "high",
            "max" => "xhigh",
            _ => "medium"
        };
    }

    private static string? NormalizeSpeed(string? value)
    {
        var normalized = BlankToNull(value)?.ToLowerInvariant();
        return normalized switch
        {
            null => null,
            "fast" or "standard" or "deep" or "max" => normalized,
            "speed" => "fast",
            "quality" => "deep",
            "extra" or "extra-high" or "xhigh" => "max",
            _ => throw new ArgumentException($"Unsupported speed: {value}. Use fast, standard, deep, or max.")
        };
    }

    private static string? NormalizeReasoningEffort(string? value)
    {
        var normalized = BlankToNull(value)?.ToLowerInvariant();
        return normalized switch
        {
            null => null,
            "none" or "minimal" or "low" or "medium" or "high" or "xhigh" => normalized,
            "extra-high" or "extra_high" => "xhigh",
            _ => throw new ArgumentException($"Unsupported reasoning effort: {value}.")
        };
    }

    private static string? BlankToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool Bool(JsonElement args, string name)
    {
        return args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.True;
    }

    private static int Int(JsonElement args, string name, int defaultValue)
    {
        return args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var value)
            && value.TryGetInt32(out var parsed)
            ? parsed
            : defaultValue;
    }

    private static IReadOnlyList<string> Csv(JsonElement args, params string[] names)
    {
        var values = names
            .Select(name => Optional(args, name))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        return values.Count == 0
            ? []
            : values;
    }
}
