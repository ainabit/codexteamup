using CodexTeamUp.AgentBus;
using CodexTeamUp.AppServer;
using CodexTeamUp.CodexWrapper;
using CodexTeamUp.Controller;
using CodexTeamUp.Core;
using CodexTeamUp.Mcp;
using CodexTeamUp.Service;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

var tests = new (string Name, Func<Task> Body)[]
{
    ("AgentBus lifecycle", () => Task.Run(AgentBusLifecycle)),
    ("Execution continuity state store reads and writes", () => Task.Run(ExecutionContinuityStoreReadsAndWrites)),
    ("AgentBus normalizes agent names", () => Task.Run(AgentBusNormalizesAgentNames)),
    ("AgentBus registry and result wait", () => Task.Run(AgentBusRegistryAndResultWait)),
    ("AgentBus clear tasks deletes task queues", () => Task.Run(AgentBusClearTasksDeletesTaskQueues)),
    ("AgentBus tiny result wait does not throw", () => Task.Run(AgentBusTinyResultWaitDoesNotThrow)),
    ("AgentBus event append is safe under parallel writes", () => Task.Run(AgentBusEventAppendIsSafeUnderParallelWrites)),
    ("Codex state reader parses rollout metadata", () => Task.Run(CodexStateReaderParsesRollouts)),
    ("SafeText redacts obvious secrets", () => Task.Run(SafeTextRedactsSecrets)),
    ("CTU JSON logger writes redacted machine and human logs", () => Task.Run(CtuJsonLoggerWritesRedactedMachineAndHumanLogs)),
    ("Table writes aligned rows", () => Task.Run(TableWritesAlignedRows)),
    ("Process runner captures output", ProcessRunnerCapturesOutput),
    ("Schema service extracts interesting methods", () => Task.Run(SchemaServiceExtractsMethods)),
    ("Wrapper protocol rewrites turn list sorting", () => Task.Run(WrapperProtocolRewritesTurnListSorting)),
    ("Wrapper protocol stamps live turn started notifications", () => Task.Run(WrapperProtocolStampsLiveTurnStartedNotifications)),
    ("Wrapper protocol normalizes git directive cwd paths", () => Task.Run(WrapperProtocolNormalizesGitDirectiveCwdPaths)),
    ("HTTP request reader preserves UTF-8 body", HttpRequestReaderPreservesUtf8Body),
    ("Wrapper protocol identifies bridge responses", () => Task.Run(WrapperProtocolIdentifiesBridgeResponses)),
    ("Wrapper pipe client parses JSON-RPC result", WrapperPipeClientParsesResult),
    ("Wrapper pipe client times out stalled responses", WrapperPipeClientTimesOutStalledResponses),
    ("Wrapper pipe send turn resumes without historical turns", WrapperPipeSendTurnResumesWithoutHistoricalTurns),
    ("Wrapper pipe send turn retries thread-not-found wakeups", WrapperPipeSendTurnRetriesThreadNotFoundWakeups),
    ("Wrapper pipe send turn retries missing rollout wakeups", WrapperPipeSendTurnRetriesMissingRolloutWakeups),
    ("Wrapper pipe sends runtime settings", WrapperPipeSendsRuntimeSettings),
    ("Reloadable app-server client loads plugin assembly", ReloadableAppServerClientLoadsPluginAssembly),
    ("Reloadable app-server client keeps active adapter on bad reload", ReloadableAppServerClientKeepsActiveAdapterOnBadReload),
    ("Logging app-server client catches and logs API failures", LoggingAppServerClientCatchesAndLogsApiFailures),
    ("Policy app-server client executes configured wakeup steps", PolicyAppServerClientExecutesConfiguredWakeupSteps),
    ("MCP tool metadata covers known tools", () => Task.Run(McpToolMetadataCoversKnownTools)),
    ("MCP registry exposes core tools", () => Task.Run(McpRegistryExposesCoreTools)),
    ("MCP registry reloads app-server adapter", () => Task.Run(McpRegistryReloadsAppServerAdapter)),
    ("MCP registry loads default controller plugin", () => Task.Run(McpRegistryLoadsDefaultControllerPlugin)),
    ("MCP registry has no hardcoded controller fallback", () => Task.Run(McpRegistryHasNoHardcodedControllerFallback)),
    ("MCP registry reloads controller runtime", () => Task.Run(McpRegistryReloadsControllerRuntime)),
    ("MCP registry reloads controller policy", () => Task.Run(McpRegistryReloadsControllerPolicy)),
    ("MCP registry archives Codex thread", () => Task.Run(McpRegistryArchivesCodexThread)),
    ("MCP registry derives bus root from cwd", () => Task.Run(McpRegistryDerivesBusRootFromCwd)),
    ("MCP registry normalizes project-root busRoot", () => Task.Run(McpRegistryNormalizesProjectRootBusRoot)),
    ("MCP registry preserves existing thread binding on re-register", () => Task.Run(McpRegistryPreservesExistingThreadBindingOnReregister)),
    ("MCP registry accepts chatName as agent display name", () => Task.Run(McpRegistryAcceptsChatNameAsAgentDisplayName)),
    ("MCP registry writes result file metadata", () => Task.Run(McpRegistryWritesResultFileMetadata)),
    ("AgentBus terminal outcome ignores continuation request", () => Task.Run(AgentBusTerminalOutcomeIgnoresContinuationRequest)),
    ("Controller continuation follow-up inherits dedupe key", () => Task.Run(ControllerContinuationFollowUpInheritsDedupeKey)),
    ("Controller continuation follow-up rejects loop beyond prompt limit", () => Task.Run(ControllerContinuationFollowUpRejectsLoopBeyondPromptLimit)),
    ("MCP registry ensures explicit ctu agents", () => Task.Run(McpRegistryEnsuresExplicitCtuAgents)),
    ("MCP registry primes agents without fallback tasks", () => Task.Run(McpRegistryPrimesAgentsWithoutFallbackTasks)),
    ("MCP registry ACKs deferred agent ensure", () => Task.Run(McpRegistryAcksDeferredAgentEnsure)),
    ("MCP registry names created agent before prime", () => Task.Run(McpRegistryNamesCreatedAgentBeforePrime)),
    ("MCP registry can skip fragile rename and prime calls", () => Task.Run(McpRegistryCanSkipFragileRenameAndPrimeCalls)),
    ("MCP registry defers stalled agent prime quickly", () => Task.Run(McpRegistryDefersStalledAgentPrimeQuickly)),
    ("MCP registry persists agent runtime settings", () => Task.Run(McpRegistryPersistsAgentRuntimeSettings)),
    ("MCP registry sends strict task wakeup", () => Task.Run(McpRegistrySendsStrictTaskWakeup)),
    ("MCP registry ACKs deferred task dispatch", () => Task.Run(McpRegistryAcksDeferredTaskDispatch)),
    ("Controller delivery loop waits before retrying queued team message", () => Task.Run(ControllerDeliveryLoopWaitsBeforeRetryingQueuedMessage)),
    ("Controller delivery loop supersedes older queued tasks for same target", () => Task.Run(ControllerDeliveryLoopSupersedesOlderQueuedTasks)),
    ("Controller delivery loop fails open task for retired agent", () => Task.Run(ControllerDeliveryLoopFailsOpenTaskForRetiredAgent)),
    ("Controller delivery loop recovers stale claimed task", () => Task.Run(ControllerDeliveryLoopRecoversStaleClaimedTask)),
    ("Controller result notify retry persists metadata and retries from startup sweep", () => Task.Run(ControllerResultNotifyRetryPersistsMetadataAndRetries)),
    ("Controller guardian evaluates result into continuity state", () => Task.Run(ControllerGuardianEvaluatesResultIntoContinuityState)),
    ("Controller guardian treats done result status as completed", () => Task.Run(ControllerGuardianTreatsDoneResultStatusAsCompleted)),
    ("Controller guardian skips unchanged queued task observation", () => Task.Run(ControllerGuardianSkipsUnchangedQueuedTaskObservation)),
    ("Controller guardian resumes dispatch from continuity state", () => Task.Run(ControllerGuardianResumesDispatchFromContinuityState)),
    ("Controller guardian does not redispatch claimed task", () => Task.Run(ControllerGuardianDoesNotRedispatchClaimedTask)),
    ("Controller guardian resumes notify from continuity state", () => Task.Run(ControllerGuardianResumesNotifyFromContinuityState)),
    ("Controller guardian blocks on orphan continuity dispatch", () => Task.Run(ControllerGuardianBlocksOnOrphanDispatchFromContinuityState)),
    ("Controller guardian blocks on orphan continuity notify", () => Task.Run(ControllerGuardianBlocksOnOrphanNotifyFromContinuityState)),
    ("Controller guardian resumes external continuity via correlation", () => Task.Run(ControllerGuardianResumesExternalContinuityViaCorrelation)),
    ("Execution continuity state store supports configured directory", () => Task.Run(ExecutionContinuityStoreSupportsConfiguredDirectory)),
    ("Controller guardian evaluates continuity with configurable identity", () => Task.Run(ControllerGuardianRespectsConfiguredContinuityPolicy)),
    ("Controller guardian emits canonical decision events", () => Task.Run(ControllerGuardianEmitsCanonicalContinuityDecisionEvents)),
    ("Controller guardian blocks when notify retries are exhausted", () => Task.Run(ControllerGuardianBlocksOnExhaustedNotifyRetries)),
    ("Controller agent continuation wakes idle owner", () => Task.Run(ControllerAgentContinuationWakesIdleOwner)),
    ("MCP registry rebinds stale agent before team message", () => Task.Run(McpRegistryRebindsStaleAgentBeforeTeamMessage)),
    ("MCP registry waits for AgentBus result", () => Task.Run(McpRegistryWaitsForAgentBusResult)),
    ("MCP registry clamps invalid AgentBus wait timeout", () => Task.Run(McpRegistryClampsInvalidAgentBusWaitTimeout)),
    ("MCP registry honors short AgentBus wait timeout", () => Task.Run(McpRegistryHonorsShortAgentBusWaitTimeout)),
    ("MCP team send message enqueues by default", () => Task.Run(McpTeamSendMessageEnqueuesByDefault)),
    ("MCP team send message waits for result", () => Task.Run(McpTeamSendMessageWaitsForResult)),
    ("MCP team send message defers stalled wakeup quickly", () => Task.Run(McpTeamSendMessageDefersStalledWakeupQuickly)),
    ("Bridge dispatch to retired agent is blocked", () => Task.Run(McpBridgeDispatchTaskBlocksRetiredTarget)),
    ("MCP registry recreates stale ctu agent threads", () => Task.Run(McpRegistryRecreatesStaleCtuAgentThreads)),
    ("MCP registry creates replacement when display name changes", () => Task.Run(McpRegistryCreatesReplacementWhenDisplayNameChanges)),
    ("MCP registry retries thread naming until created thread is visible", () => Task.Run(McpRegistryRetriesThreadNamingUntilCreatedThreadIsVisible)),
    ("MCP registry notifies result through service path", () => Task.Run(McpRegistryNotifiesResultThroughServicePath)),
    ("MCP registry defers result notify when read thread shows busy turn", () => Task.Run(McpRegistryDefersResultNotifyWhenReadThreadShowsBusyTurn)),
    ("MCP registry sends result notify when active thread has no busy turns", () => Task.Run(McpRegistrySendsResultNotifyWhenActiveThreadHasNoBusyTurns)),
    ("MCP registry resolves result notify target thread-id as agent-id", () => Task.Run(McpRegistryResolvesNotifyTargetThreadParamAsAgentId)),
    ("Agent thread matcher binds named team threads", () => Task.Run(AgentThreadMatcherBindsNamedTeamThreads)),
    ("Agent thread matcher binds exact preview names", () => Task.Run(AgentThreadMatcherBindsExactPreviewNames)),
    ("AgentBus dashboard creates snapshot", () => Task.Run(AgentBusDashboardCreatesSnapshot)),
    ("AgentBus dashboard renders communication", () => Task.Run(AgentBusDashboardRendersCommunication)),
    ("Restart orchestration record lifecycle", () => Task.Run(RestartOperationLifecycleAndPersistence)),
    ("Restart orchestration validation rejects missing target", () => Task.Run(RestartOperationRejectsInvalidTargetCwd)),
    ("Restart operation status updates are idempotent", () => Task.Run(RestartOperationStatusUpdateIsIdempotent)),
    ("Restart operation preserves imported continuation task id", () => Task.Run(RestartOperationPreservesImportedContinuationTaskId)),
    ("Restart operation records checkpoint id", () => Task.Run(RestartOperationPersistsStartupHandoffAndKnownGood)),
    ("Startup script records CTU session manifest", () => Task.Run(StartupScriptRecordsCtuSessionManifest)),
    ("Restart supervisor replaces manifest session and keeps target startup transient", () => Task.Run(RestartSupervisorUsesSessionManifestAndTransientTargetStartup)),
    ("Restart helper supervisor console is transient", () => Task.Run(RestartHelperSupervisorConsoleIsTransient)),
    ("Exchange restart handoff supports lease and completion flow", () => Task.Run(ExchangeHandoffLeaseAndCompletionFlow)),
    ("Exchange restart handoff accepts PowerShell casing", () => Task.Run(ExchangeHandoffAcceptsPowerShellCasing)),
    ("Exchange startup sweep isolates malformed envelopes", () => Task.Run(ExchangeStartupSweepIsolatesMalformedEnvelope)),
    ("Known-good checkpoint store records runtime", () => Task.Run(KnownGoodRuntimeCheckpointStoreRecordsRuntime)),
    ("Known-good checkpoint requires explicit verification", () => Task.Run(KnownGoodCheckpointRequiresExplicitVerification)),
    ("Startup sweep verifies known-good checkpoint on successful continuation dispatch", () => Task.Run(StartupSweepVerifiesKnownGoodCheckpointAfterDispatch)),
    ("Controller startup sweep imports target-side restart handoff", () => Task.Run(ControllerStartupSweepImportsRestartHandoff)),
    ("Controller startup sweep persists restart continuation as resume pending external", () => Task.Run(ControllerStartupSweepPersistsRestartContinuationAsResumePendingExternal)),
    ("Controller continuity guardian resumes restart continuation from external correlation", () => Task.Run(ControllerContinuityGuardianResumesRestartContinuationFromExternalCorrelation))
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Body().ConfigureAwait(false);
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static void AgentBusLifecycle()
{
    var root = NewTestDirectory();
    var bus = new AgentBusStore(Path.Combine(root, ".codexteamup/agentbus"));
    bus.Initialize();

    var task = bus.CreateTask(
        "architect",
        "01-web",
        "Implement slice",
        "Read docs/slice.md",
        "demo",
        root,
        ["web/", "shared/"],
        "architect");

    Equal("open", task.Status);
    True(File.Exists(Path.Combine(bus.TasksOpenDirectory, $"{task.Id}.json")));

    var claimed = bus.ClaimTask(task.Id, "01-web");
    Equal("claimed", claimed.Status);
    Equal("01-web", claimed.Owner);

    var result = bus.WriteResult(task.Id, "Done", "completed", null, null, "abc1234", ["dotnet build"], []);
    Equal(task.Id, result.TaskId);
    True(File.Exists(Path.Combine(bus.ResultsDirectory, $"{result.Id}.json")));
    True(File.Exists(Path.Combine(bus.TasksDoneDirectory, $"{task.Id}.json")));
    True(bus.ListEvents().Count >= 4);
}

static void ExecutionContinuityStoreReadsAndWrites()
{
    var root = NewTestDirectory();
    var continuity = new ExecutionContinuityStateStore(Path.Combine(root, ".codexteamup/agentbus"));
    continuity.Initialize();

    var created = continuity.Upsert(new ExecutionContinuityState
    {
        StateId = continuity.CreateStateId(),
        CorrelationId = "corr-task-1",
        InitiativeId = "initiative-1",
        TaskChainId = "task-chain-1",
        ShouldContinue = true,
        State = ExecutionContinuityStateKind.QueuedForDispatch,
        EnteredAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
        GuardianAgentId = "ctu/architect",
        GuardianDisplayName = "ctu/architect",
        LastOutcomeKind = "queued",
        LastOutcomeRef = "task-queued-1",
        NextActionKind = "task",
        NextActionRef = "task-queued-1",
        CurrentTargetAgentId = "ctu/architect",
        CurrentTargetDisplayName = "ctu/architect",
        AttemptCount = 1,
        MaxAttempts = 8,
        LastAttemptAt = DateTimeOffset.Now
    });

    var updated = continuity.Upsert(created with
    {
        State = ExecutionContinuityStateKind.Completed,
        ShouldContinue = false,
        LastOutcomeKind = "completed",
        LastOutcomeRef = "result-1",
        EnteredAt = created.EnteredAt
    });

    var latest = continuity.ReadLatest("corr-task-1");
    Equal(updated.StateId, latest?.StateId);
    Equal(ExecutionContinuityStateKind.Completed, latest?.State);
    Equal("result-1", latest?.LastOutcomeRef);
    Equal(2, continuity.ListStates("corr-task-1").Count);
    Equal(1, latest?.AttemptCount);
}

static void ExecutionContinuityStoreSupportsConfiguredDirectory()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup/agentbus");
    var relativeDirectory = ".ctu-continuity";
    var continuity = new ExecutionContinuityStateStore(busRoot, relativeDirectory);
    continuity.Initialize();

    var created = continuity.Upsert(new ExecutionContinuityState
    {
        StateId = continuity.CreateStateId(),
        CorrelationId = "corr-task-2",
        InitiativeId = "initiative-2",
        TaskChainId = "task-chain-2",
        ShouldContinue = true,
        State = ExecutionContinuityStateKind.QueuedForDispatch,
        EnteredAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now,
        GuardianAgentId = "ctu/reviewer",
        GuardianDisplayName = "ctu/reviewer",
        LastOutcomeKind = "queued",
        LastOutcomeRef = "task-2",
        NextActionKind = "task",
        NextActionRef = "task-2",
        CurrentTargetAgentId = "ctu/worker",
        CurrentTargetDisplayName = "ctu/worker",
        AttemptCount = 1,
        MaxAttempts = 8,
        LastAttemptAt = DateTimeOffset.Now
    });

    Equal(Path.Combine(root, relativeDirectory), continuity.StatesDirectory);
    Equal(1, continuity.ReadLatest("corr-task-2")?.AttemptCount);

    var absolute = Path.Combine(root, "custom-continuity");
    var absoluteContinuity = new ExecutionContinuityStateStore(busRoot, absolute);
    absoluteContinuity.Initialize();
    var absoluteCreated = absoluteContinuity.Upsert(created with
    {
        CorrelationId = "corr-task-3",
        StateId = absoluteContinuity.CreateStateId(),
        LastOutcomeRef = "task-3"
    });

    Equal(absolute, absoluteContinuity.StatesDirectory);
    Equal(absoluteCreated.StateId, absoluteContinuity.ReadLatest("corr-task-3")?.StateId);
}


static void AgentBusNormalizesAgentNames()
{
    var root = NewTestDirectory();
    var bus = new AgentBusStore(Path.Combine(root, ".codexteamup/agentbus"));
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/web",
        Role = "Web",
        DisplayName = "ctu/web",
        ThreadId = "thread-web",
        Cwd = root,
        Status = "active"
    });

    Equal("thread-web", bus.FindAgent("ctu\\web")?.ThreadId);

    var task = bus.CreateTask("ctu/architect", "ctu/web", "Slice", "Do it", "codexteamup", root, ["web/"], "ctu/architect");
    Equal(task.Id, bus.ListTasks("ctu\\web", "open").Single().Id);
}

static void AgentBusRegistryAndResultWait()
{
    var root = NewTestDirectory();
    var bus = new AgentBusStore(Path.Combine(root, ".codexteamup/agentbus"));
    bus.Initialize();
    var registered = bus.RegisterAgent(new AgentDefinition
    {
        Id = "01-web",
        Role = "Web",
        ThreadId = "thread-web",
        Cwd = root,
        AllowedPaths = ["web/"],
        ReturnTo = "architect",
        Status = "active"
    });
    Equal("thread-web", registered.ThreadId);
    Equal("thread-web", bus.FindAgent("01-web")?.ThreadId);

    var task = bus.CreateTask("architect", "01-web", "Slice", "Do it", "demo", root, ["web/"], "architect");
    _ = Task.Run(async () =>
    {
        await Task.Delay(150).ConfigureAwait(false);
        bus.WriteResult(task.Id, "Done", "completed", null, null, null, [], []);
    });
    var result = bus.WaitForResult(task.Id, TimeSpan.FromSeconds(5));
    True(result is not null);
    Equal(task.Id, result!.TaskId);
}

static void AgentBusTinyResultWaitDoesNotThrow()
{
    var root = NewTestDirectory();
    var bus = new AgentBusStore(Path.Combine(root, ".codexteamup/agentbus"));
    bus.Initialize();
    var task = bus.CreateTask("architect", "01-web", "Tiny wait", "Do it later", "demo", root, [], "architect");

    var result = bus.WaitForResult(task.Id, TimeSpan.FromMilliseconds(1));
    True(result is null);
}

static void AgentBusEventAppendIsSafeUnderParallelWrites()
{
    var root = NewTestDirectory();
    var bus = new AgentBusStore(Path.Combine(root, ".codexteamup/agentbus"));
    bus.Initialize();

    const int writes = 240;
    var startingCount = bus.ListEvents().Count;

    var tasks = Enumerable.Range(0, writes)
        .Select(i => Task.Run(() => bus.RecordEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = "agentbus.test.event_append",
            Message = $"parallel-{i}"
        })))
        .ToArray();

    Task.WaitAll(tasks);

    var lines = File.ReadAllLines(Path.Combine(root, ".codexteamup/agentbus/events.jsonl"))
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .ToList();
    var parsed = bus.ListEvents(5000)
        .Where(evt => string.Equals(evt.Type, "agentbus.test.event_append", StringComparison.Ordinal))
        .ToList();

    Equal(startingCount + writes, lines.Count);
    Equal(writes, parsed.Count);
}

static void AgentBusClearTasksDeletesTaskQueues()
{
    var root = NewTestDirectory();
    var bus = new AgentBusStore(Path.Combine(root, ".codexteamup/agentbus"));
    bus.Initialize();

    var open = bus.CreateTask("ctu/architect", "ctu/worker", "Open", "Open task.", "codexteamup", root, [], "ctu/architect");
    var claimed = bus.CreateTask("ctu/architect", "ctu/worker", "Claimed", "Claimed task.", "codexteamup", root, [], "ctu/architect");
    bus.ClaimTask(claimed.Id, "ctu/worker");
    var done = bus.CreateTask("ctu/architect", "ctu/worker", "Done", "Done task.", "codexteamup", root, [], "ctu/architect");
    bus.WriteResult(done.Id, "Done", "completed", "ctu/worker", "ctu/architect", null, [], []);

    var reset = bus.ClearTasks(includeResults: true);

    Equal(3, reset.DeletedTasks);
    Equal(1, reset.DeletedResults);
    Equal(0, bus.ListTasks().Count);
    Equal(0, bus.ListResults().Count);
    True(bus.ListEvents(100).Any(evt => evt.Type == "agentbus.tasks_cleared"));
    True(bus.FindTask(open.Id) is null);
}

static void CodexStateReaderParsesRollouts()
{
    var root = NewTestDirectory();
    var codexHome = Path.Combine(root, ".codex");
    var sessions = Path.Combine(codexHome, "sessions", "2026", "05", "14");
    Directory.CreateDirectory(sessions);
    File.WriteAllText(Path.Combine(codexHome, "session_index.jsonl"),
        "{\"id\":\"thread-1\",\"thread_name\":\"Synthetic Thread\",\"updated_at\":\"2026-05-14T12:00:00+02:00\"}\n");
    File.WriteAllText(Path.Combine(sessions, "rollout-thread-1.jsonl"),
        """
        {"timestamp":"2026-05-14T10:00:00Z","type":"session_meta","payload":{"id":"thread-1","timestamp":"2026-05-14T10:00:00Z","cwd":"S:\\demo","originator":"Codex Desktop","source":"vscode"}}
        {"timestamp":"2026-05-14T10:01:00Z","type":"response_item","payload":{"item":{"type":"message","role":"user","content":[{"type":"input_text","text":"hello"}]}}}

        """);

    var reader = new CodexStateReader(codexHome);
    var threads = reader.ListThreads(limit: 10);
    Equal(1, threads.Count);
    Equal("thread-1", threads[0].Id);
    Equal("Synthetic Thread", threads[0].Name);

    var read = reader.ReadThread("thread-1", includeTurns: true);
    True(read is not null);
    True(read!.Items.Count > 0);
}

static void SafeTextRedactsSecrets()
{
    var redacted = SafeText.Redact("token=abc123 Bearer xyz sk-abcdefghijklmnopqrstuvwxyz");
    True(!redacted.Contains("abc123", StringComparison.Ordinal));
    True(!redacted.Contains("Bearer xyz", StringComparison.Ordinal));
    True(!redacted.Contains("sk-abcdefghijklmnopqrstuvwxyz", StringComparison.Ordinal));
}

static void CtuJsonLoggerWritesRedactedMachineAndHumanLogs()
{
    var root = NewTestDirectory();
    var path = Path.Combine(root, "controller.jsonl");
    var logger = new CtuJsonLogger(path);

    logger.Info("test.info", new { token = "token=abc123", message = "hello" });
    logger.Error("test.error", new InvalidOperationException("Bearer xyz"), new { apiKey = "sk-abcdefghijklmnopqrstuvwxyz" });

    True(File.Exists(logger.Path));
    True(File.Exists(logger.HumanPath));
    var jsonl = File.ReadAllText(logger.Path);
    var human = File.ReadAllText(logger.HumanPath);
    True(jsonl.Contains("test.info", StringComparison.Ordinal));
    True(human.Contains("test.error", StringComparison.Ordinal));
    True(!jsonl.Contains("abc123", StringComparison.Ordinal));
    True(!human.Contains("sk-abcdefghijklmnopqrstuvwxyz", StringComparison.Ordinal));
}

static void TableWritesAlignedRows()
{
    using var writer = new StringWriter();
    Table.Write(writer, ["Name", "Role"], new[]
    {
        new string?[] { "ctu/architect", "planner" },
        new string?[] { "ctu/web", null }
    });

    var output = writer.ToString();
    True(output.Contains("Name", StringComparison.Ordinal));
    True(output.Contains("ctu/architect", StringComparison.Ordinal));
    True(output.Contains("planner", StringComparison.Ordinal));
}

static async Task ProcessRunnerCapturesOutput()
{
    var runner = new ProcessRunner();
    var result = await runner.RunAsync(
        "dotnet",
        "--version",
        timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);

    True(result.Succeeded);
    True(!string.IsNullOrWhiteSpace(result.StandardOutput));
}

static void SchemaServiceExtractsMethods()
{
    var root = NewTestDirectory();
    var schema = Path.Combine(root, "ClientRequest.json");
    File.WriteAllText(schema, "{\"oneOf\":[{\"properties\":{\"method\":{\"enum\":[\"thread/list\"]}}},{\"properties\":{\"method\":{\"enum\":[\"turn/start\"]}}}]}");
    var methods = new CodexSchemaService().ExtractMethods(schema);
    True(methods.Contains("thread/list"));
    True(methods.Contains("turn/start"));
}

static void WrapperProtocolRewritesTurnListSorting()
{
    var rewritten = WrapperProtocol.RewriteTurnsListAscending(
        "{\"id\":1,\"method\":\"thread/turns/list\",\"params\":{\"threadId\":\"t1\"}}",
        enabled: true);
    True(rewritten.Contains("\"sortDirection\":\"asc\"", StringComparison.Ordinal));

    var explicitDesc = WrapperProtocol.RewriteTurnsListAscending(
        "{\"id\":1,\"method\":\"thread/turns/list\",\"params\":{\"threadId\":\"t1\",\"sortDirection\":\"desc\"}}",
        enabled: true);
    True(explicitDesc.Contains("\"sortDirection\":\"desc\"", StringComparison.Ordinal));
    True(!explicitDesc.Contains("\"sortDirection\":\"asc\"", StringComparison.Ordinal));
}

static void WrapperProtocolStampsLiveTurnStartedNotifications()
{
    var rewritten = WrapperProtocol.StampTurnStartedAt(
        "{\"method\":\"turn/started\",\"params\":{\"threadId\":\"t1\",\"turn\":{\"id\":\"turn1\",\"items\":[],\"itemsView\":\"notLoaded\",\"status\":\"inProgress\",\"startedAt\":null}}}",
        enabled: true,
        unixTimeProvider: () => 1778829000);
    True(rewritten.Contains("\"startedAt\":1778829000", StringComparison.Ordinal));

    var unchanged = WrapperProtocol.StampTurnStartedAt(
        "{\"method\":\"turn/started\",\"params\":{\"threadId\":\"t1\",\"turn\":{\"id\":\"turn1\",\"items\":[],\"status\":\"inProgress\",\"startedAt\":1778828000}}}",
        enabled: true,
        unixTimeProvider: () => 1778829000);
    True(unchanged.Contains("\"startedAt\":1778828000", StringComparison.Ordinal));
    True(!unchanged.Contains("\"startedAt\":1778829000", StringComparison.Ordinal));
}

static void WrapperProtocolNormalizesGitDirectiveCwdPaths()
{
    var line = """
        {"method":"turn/completed","params":{"turn":{"items":[{"type":"message","content":[{"type":"output_text","text":"Done\n\n::git-stage{cwd=\"X:\\repo\\codexteamup\"}\n::git-push{cwd=\"X:\\repo\\codexteamup\" branch=\"codex/fix\"}"}]}]}}}
        """;

    var rewritten = WrapperProtocol.RewriteGitDirectiveCwdWindowsPaths(line, enabled: true);
    using var rewrittenDoc = JsonDocument.Parse(rewritten);
    var text = rewrittenDoc.RootElement
        .GetProperty("params")
        .GetProperty("turn")
        .GetProperty("items")[0]
        .GetProperty("content")[0]
        .GetProperty("text")
        .GetString();
    True(text?.Contains("::git-stage{cwd=\"X:/repo/codexteamup\"}", StringComparison.Ordinal) == true);
    True(text?.Contains("::git-push{cwd=\"X:/repo/codexteamup\" branch=\"codex/fix\"}", StringComparison.Ordinal) == true);
    True(text?.Contains("cwd=\"X:\\repo", StringComparison.Ordinal) == false);

    var unchanged = WrapperProtocol.RewriteGitDirectiveCwdWindowsPaths(
        "{\"method\":\"event\",\"params\":{\"message\":\"plain cwd=\\\"X:\\\\repo\\\\codexteamup\\\"\"}}",
        enabled: true);
    using var unchangedDoc = JsonDocument.Parse(unchanged);
    var message = unchangedDoc.RootElement.GetProperty("params").GetProperty("message").GetString();
    True(message?.Contains("cwd=\"X:\\repo", StringComparison.Ordinal) == true);
}

static async Task HttpRequestReaderPreservesUtf8Body()
{
    var body = "{\"prompt\":\"Bitte pruefe: äöü ÄÖÜ ß\"}";
    var bytes = Encoding.UTF8.GetBytes(
        $"POST /mcp/tools/team_send_message HTTP/1.1\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}");
    await using var stream = new MemoryStream(bytes);

    Equal("POST /mcp/tools/team_send_message HTTP/1.1", await HttpRequestReader.ReadAsciiLineAsync(stream).ConfigureAwait(false));
    Equal($"Content-Length: {Encoding.UTF8.GetByteCount(body)}", await HttpRequestReader.ReadAsciiLineAsync(stream).ConfigureAwait(false));
    Equal(string.Empty, await HttpRequestReader.ReadAsciiLineAsync(stream).ConfigureAwait(false));
    Equal(body, await HttpRequestReader.ReadUtf8BodyAsync(stream, Encoding.UTF8.GetByteCount(body)).ConfigureAwait(false));
}

static void WrapperProtocolIdentifiesBridgeResponses()
{
    True(WrapperProtocol.IsBridgeResponseId("ctu:abc"));
    True(!WrapperProtocol.IsBridgeResponseId("desktop:abc"));
    var summary = WrapperProtocol.SummarizeJsonLine("{\"id\":\"ctu:abc\",\"method\":\"thread/list\",\"params\":{}}");
    Equal("ctu:abc", summary.Id);
    Equal("thread/list", summary.Method);
    True(summary.HasParams);
}

static async Task WrapperPipeClientParsesResult()
{
    var pipeName = $"ctu-test-{Guid.NewGuid():N}";
    var serverTask = Task.Run(async () =>
    {
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await server.WaitForConnectionAsync().ConfigureAwait(false);
        using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true
        };
        var request = await reader.ReadLineAsync().ConfigureAwait(false);
        True(request?.Contains("\"method\":\"thread/list\"", StringComparison.Ordinal) == true);
        await writer.WriteLineAsync("{\"result\":{\"data\":[]}}").ConfigureAwait(false);
    });

    var client = new WrapperPipeAppServerClient(pipeName, TimeSpan.FromSeconds(5));
    var result = await client.CallAsync("thread/list", new { limit = 1 }).ConfigureAwait(false);
    await serverTask.ConfigureAwait(false);
    True(result.Succeeded);
    Equal("{\"data\":[]}", result.ResultJson);
}

static async Task WrapperPipeClientTimesOutStalledResponses()
{
    var pipeName = $"ctu-test-{Guid.NewGuid():N}";
    var serverTask = Task.Run(async () =>
    {
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await server.WaitForConnectionAsync().ConfigureAwait(false);
        using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var request = await reader.ReadLineAsync().ConfigureAwait(false);
        True(request?.Contains("\"method\":\"thread/list\"", StringComparison.Ordinal) == true);
        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
    });

    var client = new WrapperPipeAppServerClient(pipeName, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100));
    var result = await client.CallAsync("thread/list", new { limit = 1 }).ConfigureAwait(false);
    await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

    True(!result.Succeeded);
    True(result.Error?.Contains("Timed out waiting for wrapper pipe", StringComparison.Ordinal) == true);
}

static async Task WrapperPipeSendTurnResumesWithoutHistoricalTurns()
{
    var pipeName = $"ctu-test-{Guid.NewGuid():N}";
    var requests = new List<string>();
    var serverTask = Task.Run(async () =>
    {
        for (var i = 0; i < 2; i++)
        {
            await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync().ConfigureAwait(false);
            using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true)
            {
                NewLine = "\n",
                AutoFlush = true
            };

            var request = await reader.ReadLineAsync().ConfigureAwait(false);
            True(!string.IsNullOrWhiteSpace(request));
            requests.Add(request!);
            await writer.WriteLineAsync(i == 0
                ? "{\"result\":{\"thread\":{\"id\":\"thread-1\"}}}"
                : "{\"result\":{\"turn\":{\"id\":\"turn-1\"}}}").ConfigureAwait(false);
        }
    });

    var client = new WrapperPipeAppServerClient(pipeName, TimeSpan.FromSeconds(5));
    var result = await client.SendTurnAsync("thread-1", "hello").ConfigureAwait(false);
    await serverTask.ConfigureAwait(false);

    True(result.Succeeded);
    True(requests[0].Contains("\"method\":\"thread/resume\"", StringComparison.Ordinal));
    True(requests[0].Contains("\"excludeTurns\":true", StringComparison.Ordinal));
    True(requests[1].Contains("\"method\":\"turn/start\"", StringComparison.Ordinal));
}

static async Task WrapperPipeSendTurnRetriesThreadNotFoundWakeups()
{
    var pipeName = $"ctu-test-{Guid.NewGuid():N}";
    var requests = new List<string>();
    var serverTask = Task.Run(async () =>
    {
        for (var i = 0; i < 5; i++)
        {
            await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync().ConfigureAwait(false);
            using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true)
            {
                NewLine = "\n",
                AutoFlush = true
            };

            var request = await reader.ReadLineAsync().ConfigureAwait(false);
            True(!string.IsNullOrWhiteSpace(request));
            requests.Add(request!);
            await writer.WriteLineAsync(i switch
            {
                0 => "{\"error\":{\"message\":\"thread not found: thread-1\"}}",
                1 => "{\"error\":{\"message\":\"thread not found: thread-1\"}}",
                2 => "{\"error\":{\"message\":\"thread not found: thread-1\"}}",
                3 => "{\"result\":{\"thread\":{\"id\":\"thread-1\"}}}",
                _ => "{\"result\":{\"turn\":{\"id\":\"turn-1\"}}}"
            }).ConfigureAwait(false);
        }
    });

    var client = new WrapperPipeAppServerClient(pipeName, TimeSpan.FromSeconds(5));
    var result = await client.SendTurnAsync("thread-1", "hello").ConfigureAwait(false);
    await serverTask.ConfigureAwait(false);

    True(result.Succeeded);
    Equal(5, requests.Count);
    True(requests[0].Contains("\"method\":\"thread/resume\"", StringComparison.Ordinal));
    True(requests[0].Contains("\"excludeTurns\":true", StringComparison.Ordinal));
    True(requests[1].Contains("\"method\":\"thread/resume\"", StringComparison.Ordinal));
    True(!requests[1].Contains("\"excludeTurns\"", StringComparison.Ordinal));
    True(requests[2].Contains("\"method\":\"turn/start\"", StringComparison.Ordinal));
    True(requests[3].Contains("\"method\":\"thread/resume\"", StringComparison.Ordinal));
    True(requests[4].Contains("\"method\":\"turn/start\"", StringComparison.Ordinal));
}

static async Task WrapperPipeSendTurnRetriesMissingRolloutWakeups()
{
    var pipeName = $"ctu-test-{Guid.NewGuid():N}";
    var requests = new List<string>();
    var serverTask = Task.Run(async () =>
    {
        for (var i = 0; i < 4; i++)
        {
            await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync().ConfigureAwait(false);
            using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true)
            {
                NewLine = "\n",
                AutoFlush = true
            };

            var request = await reader.ReadLineAsync().ConfigureAwait(false);
            True(!string.IsNullOrWhiteSpace(request));
            requests.Add(request!);
            await writer.WriteLineAsync(i switch
            {
                0 => "{\"error\":{\"message\":\"no rollout found for thread id thread-1\"}}",
                1 => "{\"error\":{\"message\":\"no rollout found for thread id thread-1\"}}",
                2 => "{\"result\":{\"thread\":{\"id\":\"thread-1\"}}}",
                _ => "{\"result\":{\"turn\":{\"id\":\"turn-1\"}}}"
            }).ConfigureAwait(false);
        }
    });

    var client = new WrapperPipeAppServerClient(pipeName, TimeSpan.FromSeconds(5));
    var result = await client.SendTurnAsync("thread-1", "hello").ConfigureAwait(false);
    await serverTask.ConfigureAwait(false);

    True(result.Succeeded);
    Equal(4, requests.Count);
    True(requests[0].Contains("\"method\":\"thread/resume\"", StringComparison.Ordinal));
    True(requests[1].Contains("\"method\":\"turn/start\"", StringComparison.Ordinal));
    True(requests[2].Contains("\"method\":\"thread/resume\"", StringComparison.Ordinal));
    True(requests[3].Contains("\"method\":\"turn/start\"", StringComparison.Ordinal));
}

static async Task WrapperPipeSendsRuntimeSettings()
{
    var pipeName = $"ctu-test-{Guid.NewGuid():N}";
    var requests = new List<string>();
    var serverTask = Task.Run(async () =>
    {
        for (var i = 0; i < 2; i++)
        {
            await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync().ConfigureAwait(false);
            using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true)
            {
                NewLine = "\n",
                AutoFlush = true
            };

            var request = await reader.ReadLineAsync().ConfigureAwait(false);
            True(!string.IsNullOrWhiteSpace(request));
            requests.Add(request!);
            await writer.WriteLineAsync(i == 0
                ? "{\"result\":{\"thread\":{\"id\":\"thread-1\"}}}"
                : "{\"result\":{\"turn\":{\"id\":\"turn-1\"}}}").ConfigureAwait(false);
        }
    });

    var client = new WrapperPipeAppServerClient(pipeName, TimeSpan.FromSeconds(5));
    var result = await client.SendTurnAsync(
        "thread-1",
        "hello",
        "S:\\demo",
        new AppServerAgentSettings("gpt-5.4-mini", "low")).ConfigureAwait(false);
    await serverTask.ConfigureAwait(false);

    True(result.Succeeded);
    True(requests[1].Contains("\"model\":\"gpt-5.4-mini\"", StringComparison.Ordinal));
    True(requests[1].Contains("\"effort\":\"low\"", StringComparison.Ordinal));
}

static Task ReloadableAppServerClientLoadsPluginAssembly()
{
    var reloadable = ReloadableAppServerClient.CreateDefault("unused");
    var status = reloadable.Reload(
        typeof(TestAppServerClientPlugin).Assembly.Location,
        typeof(TestAppServerClientPlugin).FullName);

    Equal("plugin", status.ActiveSource);
    Equal(typeof(TestAppServerClientPlugin).FullName, status.PluginType);

    var probe = reloadable.ProbeAsync().GetAwaiter().GetResult();
    True(probe.Succeeded);
    Equal("""{"plugin":"test"}""", probe.ResultJson);
    return Task.CompletedTask;
}

static Task ReloadableAppServerClientKeepsActiveAdapterOnBadReload()
{
    var reloadable = ReloadableAppServerClient.CreateDefault("unused");
    var first = reloadable.Reload(
        typeof(TestAppServerClientPlugin).Assembly.Location,
        typeof(TestAppServerClientPlugin).FullName);
    Equal("plugin", first.ActiveSource);

    var failed = reloadable.Reload(Path.Combine(NewTestDirectory(), "missing-plugin.dll"));
    Equal("plugin", failed.ActiveSource);
    True(!string.IsNullOrWhiteSpace(failed.LastError));

    var probe = reloadable.ProbeAsync().GetAwaiter().GetResult();
    True(probe.Succeeded);
    Equal("""{"plugin":"test"}""", probe.ResultJson);
    return Task.CompletedTask;
}

static Task LoggingAppServerClientCatchesAndLogsApiFailures()
{
    var root = NewTestDirectory();
    var logger = new CtuJsonLogger(Path.Combine(root, "api-adapter.jsonl"));
    var client = new LoggingAppServerClient(new ThrowingAppServerClient(), logger);

    var result = client.ListThreadsAsync("S:/demo", 10).GetAwaiter().GetResult();

    True(!result.Succeeded);
    var success = new LoggingAppServerClient(new FakeAppServerClient("""{"data":[]}"""), logger);
    True(success.ProbeAsync().GetAwaiter().GetResult().Succeeded);
    True(success.SendTurnAsync("thread-1", "hello", "S:/demo", new AppServerAgentSettings("gpt-5.4-mini", "low")).GetAwaiter().GetResult().Succeeded);
    True(success.WaitForTurnAsync("thread-1", "turn-1", TimeSpan.FromMilliseconds(1)).GetAwaiter().GetResult().Completed);
    True(success.StartThreadAsync("S:/demo", "ctu/test", "role").GetAwaiter().GetResult().Succeeded);
    True(success.ReadThreadAsync("thread-1", includeTurns: true).GetAwaiter().GetResult().Succeeded);
    True(File.ReadAllText(logger.Path).Contains("api.list_threads.exception", StringComparison.Ordinal));
    True(File.ReadAllText(logger.HumanPath).Contains("api.list_threads.exception", StringComparison.Ordinal));
    return Task.CompletedTask;
}

static Task PolicyAppServerClientExecutesConfiguredWakeupSteps()
{
    var root = NewTestDirectory();
    var policyPath = Path.Combine(root, "appserver-policy.json");
    File.WriteAllText(policyPath, """
    {
      "sendTurn": {
        "attempts": 2,
        "delayMs": 1,
        "steps": ["resume", "turnStart"],
        "temporaryErrors": ["thread not found"]
      }
    }
    """);
    var inner = new RecordingCallAppServerClient();
    var client = new PolicyAppServerClient(inner, policyPath);

    var result = client.SendTurnAsync(
        "thread-1",
        "hello",
        "S:/demo",
        new AppServerAgentSettings("gpt-5.4-mini", "low")).GetAwaiter().GetResult();

    True(result.Succeeded);
    Equal(2, inner.Calls.Count);
    Equal("thread/resume", inner.Calls[0].Method);
    Equal("turn/start", inner.Calls[1].Method);
    return Task.CompletedTask;
}

static void McpToolMetadataCoversKnownTools()
{
    foreach (var tool in McpToolRegistry.KnownToolNames)
    {
        var description = McpToolMetadata.ToolDescription(tool);
        var annotations = JsonSerializer.Serialize(McpToolMetadata.ToolAnnotations(tool), JsonFile.Options);
        var schema = JsonSerializer.Serialize(McpToolMetadata.ToolInputSchema(tool), JsonFile.Options);
        True(!string.IsNullOrWhiteSpace(description));
        True(!string.IsNullOrWhiteSpace(annotations));
        True(!string.IsNullOrWhiteSpace(schema));
    }
}

static void McpRegistryExposesCoreTools()
{
    var root = NewTestDirectory();
    var registry = McpToolRegistry.CreateDefault(Path.Combine(root, ".codexteamup/agentbus"), new WrapperPipeAppServerClient("unused"));
    True(registry.ToolNames.Contains("agentbus_init"));
    True(registry.ToolNames.Contains("agentbus_wait_result"));
    True(registry.ToolNames.Contains("agentbus_list_events"));
    True(registry.ToolNames.Contains("codex_thread_archive"));
    True(registry.ToolNames.Contains("codex_appserver_adapter_status"));
    True(registry.ToolNames.Contains("codex_appserver_adapter_reload"));
    True(registry.ToolNames.Contains("codex_controller_status"));
    True(registry.ToolNames.Contains("codex_controller_reload"));
    True(registry.ToolNames.Contains("codex_controller_policy_status"));
    True(registry.ToolNames.Contains("codex_controller_policy_reload"));
    True(registry.ToolNames.Contains("bridge_dispatch_task"));
    True(registry.ToolNames.Contains("bridge_notify_result"));
    True(registry.ToolNames.Contains("team_discover_agents"));
    True(registry.ToolNames.Contains("team_send_message"));
    True(registry.ToolNames.Contains("team_dashboard_export"));
}

static void McpRegistryReloadsAppServerAdapter()
{
    var root = NewTestDirectory();
    var appServer = ReloadableAppServerClient.CreateDefault("unused");
    var registry = McpToolRegistry.CreateDefault(Path.Combine(root, ".codexteamup/agentbus"), appServer);
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "pluginPath": {{JsonSerializer.Serialize(typeof(TestAppServerClientPlugin).Assembly.Location)}},
      "pluginType": {{JsonSerializer.Serialize(typeof(TestAppServerClientPlugin).FullName)}}
    }
    """);

    var result = registry.InvokeAsync("codex_appserver_adapter_reload", args).GetAwaiter().GetResult();

    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonFile.Options));
    Equal("plugin", doc.RootElement.GetProperty("activeSource").GetString() ?? doc.RootElement.GetProperty("ActiveSource").GetString());
    var probe = appServer.ProbeAsync().GetAwaiter().GetResult();
    Equal("""{"plugin":"test"}""", probe.ResultJson);
}

static void McpRegistryLoadsDefaultControllerPlugin()
{
    var root = NewTestDirectory();
    var controller = ReloadableCtuController.CreateDefault(Path.Combine(root, ".codexteamup/agentbus"), new FakeAppServerClient("""{"data":[]}"""));

    Equal("plugin", controller.Status.ActiveSource);
    True(controller.Status.PluginPath?.EndsWith("CodexTeamUp.Controller.Default.dll", StringComparison.OrdinalIgnoreCase) == true);
    True(controller.ToolNames.Contains("team_send_message"));
}

static void McpRegistryHasNoHardcodedControllerFallback()
{
    var root = NewTestDirectory();
    var previousPath = Environment.GetEnvironmentVariable("CTU_CONTROLLER_PLUGIN_PATH");
    var previousType = Environment.GetEnvironmentVariable("CTU_CONTROLLER_PLUGIN_TYPE");
    try
    {
        Environment.SetEnvironmentVariable("CTU_CONTROLLER_PLUGIN_PATH", Path.Combine(root, "missing-controller.dll"));
        Environment.SetEnvironmentVariable("CTU_CONTROLLER_PLUGIN_TYPE", null);

        var controller = ReloadableCtuController.CreateDefault(Path.Combine(root, ".codexteamup/agentbus"), new FakeAppServerClient("""{"data":[]}"""));

        Equal("unloaded", controller.Status.ActiveSource);
        True(controller.Status.LastError?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true);
        True(!controller.ToolNames.Contains("team_send_message"));
    }
    finally
    {
        Environment.SetEnvironmentVariable("CTU_CONTROLLER_PLUGIN_PATH", previousPath);
        Environment.SetEnvironmentVariable("CTU_CONTROLLER_PLUGIN_TYPE", previousType);
    }
}

static void McpRegistryReloadsControllerRuntime()
{
    var root = NewTestDirectory();
    var policyPath = Path.Combine(root, "policy.json");
    File.WriteAllText(policyPath, """
    {
      "teamSendMessageDefaultDispatchMode": "inline",
      "wakeupTimeoutSeconds": 2,
      "waitResultTimeoutCapSeconds": 2,
      "ensureThreadNameBeforePrime": true,
      "primePromptStartsWithAgentId": true
    }
    """);
    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var controller = ReloadableCtuController.CreateDefault(Path.Combine(root, ".codexteamup/agentbus"), appServer);
    var registry = McpToolRegistry.CreateDefault(controller);
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "policyPath": {{JsonSerializer.Serialize(policyPath)}},
      "reloadPolicy": true
    }
    """);

    var reload = registry.InvokeAsync("codex_controller_reload", args).GetAwaiter().GetResult();
    var status = registry.InvokeAsync("codex_controller_status", JsonSerializer.Deserialize<JsonElement>("{}")).GetAwaiter().GetResult();

    using var reloadDoc = JsonDocument.Parse(JsonSerializer.Serialize(reload, JsonFile.Options));
    Equal("plugin", reloadDoc.RootElement.GetProperty("activeSource").GetString());
    using var statusDoc = JsonDocument.Parse(JsonSerializer.Serialize(status, JsonFile.Options));
    Equal("inline", statusDoc.RootElement.GetProperty("policy").GetProperty("policy").GetProperty("teamSendMessageDefaultDispatchMode").GetString());
}

static void McpRegistryReloadsControllerPolicy()
{
    var root = NewTestDirectory();
    var policyPath = Path.Combine(root, "policy.json");
    File.WriteAllText(policyPath, """
    {
      "teamSendMessageDefaultDispatchMode": "inline",
      "wakeupTimeoutSeconds": 3,
      "waitResultTimeoutCapSeconds": 4,
      "ensureThreadNameBeforePrime": true,
      "primePromptStartsWithAgentId": false
    }
    """);
    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var registry = McpToolRegistry.CreateDefault(Path.Combine(root, ".codexteamup/agentbus"), appServer);
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "policyPath": {{JsonSerializer.Serialize(policyPath)}}
    }
    """);

    _ = registry.InvokeAsync("codex_controller_policy_reload", args).GetAwaiter().GetResult();
    var status = registry.InvokeAsync("codex_controller_policy_status", JsonSerializer.Deserialize<JsonElement>("{}")).GetAwaiter().GetResult();

    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(status, JsonFile.Options));
    Equal("policy", doc.RootElement.GetProperty("activeSource").GetString());
    Equal("inline", doc.RootElement.GetProperty("policy").GetProperty("teamSendMessageDefaultDispatchMode").GetString());
    Equal(3, doc.RootElement.GetProperty("policy").GetProperty("wakeupTimeoutSeconds").GetInt32());
}

static void McpRegistryArchivesCodexThread()
{
    var root = NewTestDirectory();
    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var registry = McpToolRegistry.CreateDefault(Path.Combine(root, ".codexteamup/agentbus"), appServer);
    var args = JsonSerializer.Deserialize<JsonElement>("""{"threadId":"thread-test"}""");

    var result = registry.InvokeAsync("codex_thread_archive", args).GetAwaiter().GetResult();

    Equal("thread-test", appServer.ArchivedThreads.Single());
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonFile.Options));
    True(doc.RootElement.GetProperty("succeeded").GetBoolean());
}

static void McpRegistryDerivesBusRootFromCwd()
{
    var root = NewTestDirectory();
    var registry = McpToolRegistry.CreateDefault(Path.Combine(root, "default", ".codexteamup/agentbus"), new WrapperPipeAppServerClient("unused"));
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}}
    }
    """);
    var result = registry.InvokeAsync("agentbus_init", args).GetAwaiter().GetResult();
    True(File.Exists(Path.Combine(root, ".codexteamup/agentbus", "events.jsonl")));
}

static void McpRegistryNormalizesProjectRootBusRoot()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Check project-root busRoot",
        "List this task.",
        "codexteamup.test",
        root,
        [],
        "ctu/architect");

    var registry = McpToolRegistry.CreateDefault(Path.Combine(root, "default", ".codexteamup/agentbus"), new WrapperPipeAppServerClient("unused"));
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "busRoot": {{JsonSerializer.Serialize(root)}} ,
      "to": "ctu/worker",
      "status": "open"
    }
    """);

    var response = registry.InvokeAsync("agentbus_list_tasks", args).GetAwaiter().GetResult();

    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonFile.Options));
    var tasks = doc.RootElement.GetProperty("tasks");
    Equal(1, tasks.GetArrayLength());
    Equal(task.Id, tasks[0].GetProperty("id").GetString());
}

static void McpRegistryPreservesExistingThreadBindingOnReregister()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu-test/architect",
        Role = "Controller",
        DisplayName = "ctu-test/architect",
        ThreadId = "thread-architect",
        Cwd = root,
        AllowedPaths = ["docs/"],
        InstructionFiles = ["AGENTS.md"],
        ReturnTo = "ctu-test/runner",
        Status = "active"
    });

    var registry = McpToolRegistry.CreateDefault(busRoot, new WrapperPipeAppServerClient("unused"));
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}} ,
      "id": "ctu-test/architect",
      "displayName": "ctu-test/architect",
      "role": "architect",
      "status": "active",
      "reasoningEffort": "medium"
    }
    """);

    _ = registry.InvokeAsync("agentbus_register_agent", args).GetAwaiter().GetResult();

    var agent = bus.FindAgent("ctu-test/architect");
    Equal("thread-architect", agent?.ThreadId);
    Equal(root, agent?.Cwd);
    Equal("docs/", agent?.AllowedPaths.Single());
    Equal("AGENTS.md", agent?.InstructionFiles.Single());
    Equal("ctu-test/runner", agent?.ReturnTo);
}

static void McpRegistryAcceptsChatNameAsAgentDisplayName()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var registry = McpToolRegistry.CreateDefault(busRoot, new WrapperPipeAppServerClient("unused"));
    var args = JsonSerializer.Deserialize<JsonElement>("""
    {
      "id": "ctu/service",
      "role": "Service maintainer",
      "chatName": "Service Implementation Chat",
      "status": "active"
    }
    """);

    _ = registry.InvokeAsync("agentbus_register_agent", args).GetAwaiter().GetResult();

    var agent = new AgentBusStore(busRoot).FindAgent("ctu/service");
    Equal("Service Implementation Chat", agent?.DisplayName);
    Equal("active", agent?.Status);
}

static void McpRegistryWritesResultFileMetadata()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var registry = McpToolRegistry.CreateDefault(busRoot, new WrapperPipeAppServerClient("unused"));
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/developer",
        "Implement sample page",
        "Create app/index.html.",
        "codexteamup",
        root,
        [],
        "ctu/architect");
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "busRoot": {{JsonSerializer.Serialize(busRoot)}},
      "taskId": {{JsonSerializer.Serialize(task.Id)}},
      "summary": "Implemented sample page.",
      "from": "ctu/developer",
      "to": "ctu/architect",
      "changedFiles": "app/index.html, docs/note.md",
      "checks": "manual browser check",
      "artifacts": "app/index.html",
      "openQuestions": "None",
      "nextSuggestedAction": "Review in browser",
      "outcome": "self_continue",
      "continuationOwner": "ctu/developer",
      "continuationWakeAfterSeconds": 30,
      "continuationReason": "Continue local polish after review.",
      "continuationDedupeKey": "sample-page-polish",
      "continuationMaxAttempts": 3
    }
    """);

    _ = registry.InvokeAsync("agentbus_write_result", args).GetAwaiter().GetResult();
    var result = bus.ListResults().Single();
    Equal("app/index.html", result.ChangedFiles[0]);
    Equal("docs/note.md", result.ChangedFiles[1]);
    Equal("manual browser check", result.Tests.Single());
    Equal("app/index.html", result.Artifacts.Single());
    Equal("Review in browser", result.NextSuggestedAction);
    Equal("self_continue", result.Outcome);
    True(!string.IsNullOrWhiteSpace(result.ContinuationId));
    var continuation = bus.ListContinuations().Single();
    Equal("ctu/developer", continuation.Owner);
    Equal("sample-page-polish", continuation.DedupeKey);
    Equal("open", continuation.Status);
    Equal(3, continuation.MaxAttempts);
}

static void AgentBusTerminalOutcomeIgnoresContinuationRequest()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Terminal outcome check",
        "Return explicit terminal outcome.",
        "codexteamup",
        root,
        [],
        "ctu/architect");
    bus.ClaimTask(task.Id, "ctu/worker");
    var registry = McpToolRegistry.CreateDefault(busRoot, new FakeAppServerClient("""{"data":[]}"""));
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "taskId": {{JsonSerializer.Serialize(task.Id)}},
      "summary": "No continuation needed.",
      "status": "completed",
      "from": "ctu/worker",
      "to": "ctu/architect",
      "outcome": "done",
      "continuationOwner": "ctu/worker",
      "continuationDedupeKey": "should-not-persist",
      "continuationReason": "Explicit done outcome should cancel continuation."
    }
    """);
    var resultObject = registry.InvokeAsync("agentbus_write_result", args).GetAwaiter().GetResult();
    var result = (AgentBusResult?)resultObject.GetType().GetProperty("result")?.GetValue(resultObject);
    Equal("done", result?.Outcome);
    True(result?.Continuation is null);
    Equal(0, bus.ListContinuations(status: "open").Count);
}

static void ControllerContinuationFollowUpInheritsDedupeKey()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/designer",
        Role = "Designer",
        DisplayName = "ctu/designer",
        ThreadId = "thread-designer",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/designer",
        "Design slice",
        "Continue until self-reported done.",
        "codexteamup",
        root,
        [],
        "ctu/architect");
    bus.ClaimTask(task.Id, "ctu/designer");

    var firstResult = bus.WriteResult(
        task.Id,
        "Need one more pass after local cooldown.",
        "completed",
        "ctu/designer",
        "ctu/architect",
        null,
        [],
        [],
        outcome: "self_continue",
        continuation: new AgentBusContinuationRequest
        {
            Owner = "ctu/designer",
            WakeAfterSeconds = 0,
            MaxAttempts = 2,
            DedupeKey = "design-review",
            Reason = "Continue the design review loop."
        });
    Equal("self_continue", firstResult.Outcome);

    var firstContinuation = bus.ListContinuations("ctu/designer", "open").Single();
    Equal("design-review", firstContinuation.DedupeKey);

    var appServer = new ScriptedSendTurnAppServerClient(
        "thread-designer",
        "ctu/designer",
        root);
    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    var followUpTask = bus.ListTasks("ctu/designer", "open")
        .Single(t => string.Equals(t.Title, $"Self-continuation: ctu/designer", StringComparison.Ordinal));
    True(followUpTask.Prompt.Contains("Continuation dedupe key:", StringComparison.Ordinal));
    True(followUpTask.Prompt.Contains("design-review", StringComparison.Ordinal));
    True(followUpTask.Prompt.Contains("1 of 2", StringComparison.Ordinal));

    bus.ClaimTask(followUpTask.Id, "ctu/designer");
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var secondArgs = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "taskId": {{JsonSerializer.Serialize(followUpTask.Id)}},
      "summary": "Need another follow-up pass.",
      "status": "completed",
      "from": "ctu/designer",
      "to": "ctu/architect",
      "outcome": "self_continue",
      "continuationOwner": "ctu/designer",
      "continuationWakeAfterSeconds": 0,
      "continuationReason": "Continue the design review loop."
    }
    """);
    var secondResultObject = registry.InvokeAsync("agentbus_write_result", secondArgs).GetAwaiter().GetResult();
    var secondResult = (AgentBusResult?)secondResultObject.GetType().GetProperty("result")?.GetValue(secondResultObject);
    Equal("self_continue", secondResult?.Outcome);
    var secondContinuation = bus.ListContinuations("ctu/designer", "open").Single();
    Equal(firstContinuation.DedupeKey, secondContinuation.DedupeKey);
    Equal(2, secondContinuation.MaxAttempts);
}

static void ControllerContinuationFollowUpRejectsLoopBeyondPromptLimit()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/designer",
        Role = "Designer",
        DisplayName = "ctu/designer",
        ThreadId = "thread-designer",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "active"
    });

    var appServer = new ScriptedSendTurnAppServerClient(
        "thread-designer",
        "ctu/designer",
        root);
    var controller = new DefaultCtuController(busRoot, appServer);

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/designer",
        "Design slice",
        "Continue until self-reported done.",
        "codexteamup",
        root,
        [],
        "ctu/architect");
    bus.ClaimTask(task.Id, "ctu/designer");

    bus.WriteResult(
        task.Id,
        "Need one more pass after local cooldown.",
        "completed",
        "ctu/designer",
        "ctu/architect",
        null,
        [],
        [],
        outcome: "self_continue",
        continuation: new AgentBusContinuationRequest
        {
            Owner = "ctu/designer",
            WakeAfterSeconds = 0,
            MaxAttempts = 1,
            DedupeKey = "design-review",
            Reason = "Continue the design review loop."
        });

    controller.RunStartupSweepAsync().GetAwaiter().GetResult();
    var followUpTask = bus.ListTasks("ctu/designer", "open")
        .Single(t => string.Equals(t.Title, $"Self-continuation: ctu/designer", StringComparison.Ordinal));
    True(followUpTask.Prompt.Contains("1 of 1", StringComparison.Ordinal));
    bus.ClaimTask(followUpTask.Id, "ctu/designer");
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var rejectedArgs = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "taskId": {{JsonSerializer.Serialize(followUpTask.Id)}},
      "summary": "This would loop forever without a guard.",
      "status": "completed",
      "from": "ctu/designer",
      "to": "ctu/architect",
      "outcome": "self_continue",
      "continuationOwner": "ctu/designer",
      "continuationWakeAfterSeconds": 0,
      "continuationReason": "Continue the design review loop."
    }
    """);
    var rejectedResultObject = registry.InvokeAsync("agentbus_write_result", rejectedArgs).GetAwaiter().GetResult();
    var rejectedResult = (AgentBusResult?)rejectedResultObject.GetType().GetProperty("result")?.GetValue(rejectedResultObject);
    Equal("failed", rejectedResult?.Outcome);
    Equal(0, bus.ListContinuations("ctu/designer", "open").Count);
    Equal(0, bus.ListContinuations("ctu/designer", "failed").Count);
}

static void McpRegistryEnsuresExplicitCtuAgents()
{
    var root = NewTestDirectory();
    var projectCwd = Path.Combine(root, "project");
    var appServer = new FakeAppServerClient($$"""
        {"data":[{"id":"thread-web","name":"ctu/web","cwd":{{JsonSerializer.Serialize(projectCwd)}},"status":"idle"}]}
        """);
    var registry = McpToolRegistry.CreateDefault(Path.Combine(root, "default", ".codexteamup/agentbus"), appServer);
    var agentsJson = JsonSerializer.Serialize(new[]
    {
        new
        {
            id = "ctu/web",
            role = "Frontend",
            allowedPaths = new[] { "web/", "shared/" },
            instructionFiles = new[] { "web/AGENTS.md" }
        }
    });
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(projectCwd)}},
      "busRoot": {{JsonSerializer.Serialize(Path.Combine(root, ".codexteamup", "agentbus"))}},
      "agentsJson": {{JsonSerializer.Serialize(agentsJson)}},
      "createMissing": "false",
      "prime": "false"
    }
    """);

    _ = registry.InvokeAsync("team_ensure_agents", args).GetAwaiter().GetResult();
    var bus = new AgentBusStore(Path.Combine(root, ".codexteamup", "agentbus"));
    var agent = bus.FindAgent("ctu\\web");
    Equal("thread-web", agent?.ThreadId);
    Equal("web/AGENTS.md", agent?.InstructionFiles.Single());
    Equal("medium", agent?.ReasoningEffort);
    Equal("standard", agent?.Speed);
}

static void McpRegistryPrimesAgentsWithoutFallbackTasks()
{
    var root = NewTestDirectory();
    var appServer = new FakeAppServerClient(
        """{"data":[{"id":"thread-greeter","name":"ctu/greeter","cwd":"ROOT","status":"idle"}]}"""
            .Replace("ROOT", JsonEscaped(root), StringComparison.Ordinal));
    var registry = McpToolRegistry.CreateDefault(Path.Combine(root, ".codexteamup", "agentbus"), appServer);
    var agentsJson = JsonSerializer.Serialize(new[] { new { id = "ctu/greeter", role = "Greeter" } });
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}},
      "busRoot": {{JsonSerializer.Serialize(Path.Combine(root, ".codexteamup", "agentbus"))}},
      "agentsJson": {{JsonSerializer.Serialize(agentsJson)}},
      "createMissing": "false",
      "prime": "true"
    }
    """);

    _ = registry.InvokeAsync("team_ensure_agents", args).GetAwaiter().GetResult();
    Equal(1, appServer.SentTurns.Count);
    var prompt = appServer.SentTurns[0].Message;
    True(prompt.StartsWith("ctu/greeter\n\nYou are ctu/greeter", StringComparison.Ordinal));
    True(prompt.Contains("do not create a replacement task", StringComparison.Ordinal));
    True(prompt.Contains("do not reconstruct a task from chat text", StringComparison.Ordinal));
    Equal("medium", appServer.SentTurns[0].Settings?.ReasoningEffort);
}

static void McpRegistryAcksDeferredAgentEnsure()
{
    var root = NewTestDirectory();
    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var agentsJson = JsonSerializer.Serialize(new[] { new { id = "ctu/deferred", role = "Deferred Agent" } });
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}} ,
      "busRoot": {{JsonSerializer.Serialize(busRoot)}} ,
      "agentsJson": {{JsonSerializer.Serialize(agentsJson)}} ,
      "createMissing": "true",
      "prime": "true",
      "defer": true
    }
    """);

    var response = registry.InvokeAsync("team_ensure_agents", args).GetAwaiter().GetResult();

    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonFile.Options));
    True(doc.RootElement.GetProperty("accepted").GetBoolean());
    True(doc.RootElement.GetProperty("deferred").GetBoolean());
    True(!string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("operationId").GetString()));

    var bus = new AgentBusStore(busRoot);
    var deadline = DateTimeOffset.Now.AddSeconds(5);
    AgentDefinition? agent = null;
    while (DateTimeOffset.Now < deadline)
    {
        agent = bus.FindAgent("ctu/deferred");
        if (!string.IsNullOrWhiteSpace(agent?.ThreadId))
        {
            break;
        }

        Thread.Sleep(50);
    }

    Equal("created-thread", agent?.ThreadId);
}

static void McpRegistryNamesCreatedAgentBeforePrime()
{
    var root = NewTestDirectory();
    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var registry = McpToolRegistry.CreateDefault(Path.Combine(root, ".codexteamup", "agentbus"), appServer);
    var agentsJson = JsonSerializer.Serialize(new[] { new { id = "ctu/new-agent", role = "New Agent" } });
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}} ,
      "busRoot": {{JsonSerializer.Serialize(Path.Combine(root, ".codexteamup", "agentbus"))}} ,
      "agentsJson": {{JsonSerializer.Serialize(agentsJson)}} ,
      "createMissing": "true",
      "prime": "true"
    }
    """);

    _ = registry.InvokeAsync("team_ensure_agents", args).GetAwaiter().GetResult();

    Equal("ctu/new-agent", appServer.NamedThreads.Single().Name);
    Equal(1, appServer.SentTurns.Count);
    True(appServer.SentTurns.Single().Message.StartsWith("ctu/new-agent\n\nYou are ctu/new-agent", StringComparison.Ordinal));
}

static void McpRegistryCanSkipFragileRenameAndPrimeCalls()
{
    var root = NewTestDirectory();
    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var registry = McpToolRegistry.CreateDefault(Path.Combine(root, ".codexteamup", "agentbus"), appServer);
    var agentsJson = JsonSerializer.Serialize(new[] { new { id = "ctu/smoke-agent", role = "Smoke Agent" } });
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}} ,
      "busRoot": {{JsonSerializer.Serialize(Path.Combine(root, ".codexteamup", "agentbus"))}} ,
      "agentsJson": {{JsonSerializer.Serialize(agentsJson)}} ,
      "createMissing": "true",
      "prime": "false",
      "setName": "false"
    }
    """);

    _ = registry.InvokeAsync("team_ensure_agents", args).GetAwaiter().GetResult();

    var agent = new AgentBusStore(Path.Combine(root, ".codexteamup", "agentbus")).FindAgent("ctu/smoke-agent");
    Equal("created-thread", agent?.ThreadId);
    Equal(0, appServer.NamedThreads.Count);
    Equal(0, appServer.SentTurns.Count);
}

static void McpRegistryDefersStalledAgentPrimeQuickly()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var appServer = new FakeAppServerClient("""{"data":[]}""", sendTurnDelay: TimeSpan.FromSeconds(5));
    var policy = new ReloadableCtuControllerPolicy();
    var policyFile = Path.Combine(root, "ctu-controller-policy.json");
    File.WriteAllText(policyFile, JsonSerializer.Serialize(new CtuControllerPolicy("enqueue", 1, 2, false, true), JsonFile.Options));
    policy.Reload(policyFile);
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer, policy);
    var agentsJson = JsonSerializer.Serialize(new[] { new { id = "ctu/smoke-agent", role = "Smoke Agent" } });
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}} ,
      "busRoot": {{JsonSerializer.Serialize(busRoot)}} ,
      "agentsJson": {{JsonSerializer.Serialize(agentsJson)}} ,
      "createMissing": "true",
      "prime": "true",
      "setName": "false"
    }
    """);

    var stopwatch = Stopwatch.StartNew();
    _ = registry.InvokeAsync("team_ensure_agents", args).GetAwaiter().GetResult();
    stopwatch.Stop();

    True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
    var bus = new AgentBusStore(busRoot);
    Equal("created-thread", bus.FindAgent("ctu/smoke-agent")?.ThreadId);
    True(bus.ListEvents(100).Any(evt => evt.Type == "agent.prime_deferred" && evt.To == "ctu/smoke-agent"));
}

static void McpRegistryPersistsAgentRuntimeSettings()
{
    var root = NewTestDirectory();
    var appServer = new FakeAppServerClient(
        """{"data":[{"id":"thread-ux","name":"ctu/ux","cwd":"ROOT","status":"idle"}]}"""
            .Replace("ROOT", JsonEscaped(root), StringComparison.Ordinal));
    var registry = McpToolRegistry.CreateDefault(Path.Combine(root, ".codexteamup", "agentbus"), appServer);
    var agentsJson = JsonSerializer.Serialize(new[]
    {
        new
        {
            id = "ctu/ux",
            role = "UX",
            model = "gpt-5.4-mini",
            reasoningEffort = "high",
            speed = "fast"
        }
    });
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}},
      "busRoot": {{JsonSerializer.Serialize(busRoot)}},
      "agentsJson": {{JsonSerializer.Serialize(agentsJson)}},
      "createMissing": "false",
      "prime": "true"
    }
    """);

    _ = registry.InvokeAsync("team_ensure_agents", args).GetAwaiter().GetResult();
    var agent = new AgentBusStore(busRoot).FindAgent("ctu/ux");
    Equal("gpt-5.4-mini", agent?.Model);
    Equal("high", agent?.ReasoningEffort);
    Equal("fast", agent?.Speed);
    Equal("gpt-5.4-mini", appServer.SentTurns.Single().Settings?.Model);
    Equal("high", appServer.SentTurns.Single().Settings?.ReasoningEffort);
}

static void McpRegistrySendsStrictTaskWakeup()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/greeter",
        Role = "Greeter",
        ThreadId = "thread-greeter",
        Cwd = root,
        Model = "gpt-5.4-mini",
        ReasoningEffort = "low",
        Speed = "fast",
        Status = "active"
    });
    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}},
      "busRoot": {{JsonSerializer.Serialize(busRoot)}},
      "from": "ctu/architect",
      "to": "ctu/greeter",
      "title": "Ping",
      "message": "Reply briefly.",
      "project": "codexteamup",
      "dispatchMode": "inline"
    }
    """);

    _ = registry.InvokeAsync("team_send_message", args).GetAwaiter().GetResult();
    Equal(1, appServer.SentTurns.Count);
    var wake = appServer.SentTurns[0].Message;
    True(wake.StartsWith("ctu/greeter\n\nNew CodexTeamUp message/task", StringComparison.Ordinal));
    True(wake.Contains("Verify that the task file exists", StringComparison.Ordinal));
    True(wake.Contains("do not create a replacement task", StringComparison.Ordinal));
    True(wake.Contains("do not write a result", StringComparison.Ordinal));
    Equal("gpt-5.4-mini", appServer.SentTurns[0].Settings?.Model);
    Equal("low", appServer.SentTurns[0].Settings?.ReasoningEffort);
}

static void McpRegistryAcksDeferredTaskDispatch()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        ThreadId = "thread-worker",
        Cwd = root,
        Status = "active"
    });
    var task = bus.CreateTask("ctu/architect", "ctu/worker", "Ping", "Reply briefly.", "demo", root, [], "ctu/architect");
    var appServer = new FakeAppServerClient("""{"data":[{"id":"thread-worker","name":"ctu/worker","cwd":"ROOT","status":"idle"}]}"""
        .Replace("ROOT", JsonEscaped(root), StringComparison.Ordinal));
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}} ,
      "busRoot": {{JsonSerializer.Serialize(busRoot)}} ,
      "taskId": {{JsonSerializer.Serialize(task.Id)}} ,
      "defer": true
    }
    """);

    var response = registry.InvokeAsync("bridge_dispatch_task", args).GetAwaiter().GetResult();

    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonFile.Options));
    True(doc.RootElement.GetProperty("accepted").GetBoolean());
    True(doc.RootElement.GetProperty("deferred").GetBoolean());
    var deadline = DateTimeOffset.Now.AddSeconds(5);
    while (DateTimeOffset.Now < deadline && appServer.SentTurns.Count == 0)
    {
        Thread.Sleep(50);
    }

    Equal(1, appServer.SentTurns.Count);
    True(bus.ListEvents(100).Any(evt => evt.Type == "task.dispatch_accepted" && evt.TaskId == task.Id));
    True(bus.ListEvents(100).Any(evt => evt.Type == "task.dispatched" && evt.TaskId == task.Id));
}

static void McpRegistryRebindsStaleAgentBeforeTeamMessage()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/tester",
        Role = "Tester",
        DisplayName = "ctu/tester",
        ThreadId = "stale-thread",
        Cwd = root,
        Status = "active"
    });

    var appServer = new FakeAppServerClient(
        """{"data":[{"id":"fresh-thread","name":"ctu/tester","cwd":"ROOT","status":"idle"}]}"""
            .Replace("ROOT", JsonEscaped(root), StringComparison.Ordinal),
        resumeThreadError: "thread not found: stale-thread",
        readThreadError: "thread not found: stale-thread");
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}},
      "busRoot": {{JsonSerializer.Serialize(busRoot)}},
      "from": "ctu/architect",
      "to": "ctu/tester",
      "title": "Ping tester",
      "message": "Reply briefly.",
      "project": "codexteamup",
      "dispatchMode": "inline"
    }
    """);

    _ = registry.InvokeAsync("team_send_message", args).GetAwaiter().GetResult();

    Equal("fresh-thread", appServer.SentTurns.Single().ThreadId);
    var rebound = bus.FindAgent("ctu/tester");
    Equal("fresh-thread", rebound?.ThreadId);
    Equal("active", rebound?.Status);
    True(bus.ListEvents().Any(evt => evt.Type == "agent.binding_stale" && evt.To == "ctu/tester"));
}

static void McpRegistryWaitsForAgentBusResult()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    var task = bus.CreateTask("ctu/architect", "ctu/worker", "Fast wait", "Reply briefly.", "demo", root, [], "ctu/architect");
    _ = Task.Run(async () =>
    {
        await Task.Delay(150).ConfigureAwait(false);
        bus.ClaimTask(task.Id, "ctu/worker");
        bus.WriteResult(task.Id, "fast result", "completed", "ctu/worker", "ctu/architect", null, [], []);
    });

    var registry = McpToolRegistry.CreateDefault(busRoot, new FakeAppServerClient("""{"data":[]}"""));
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "busRoot": {{JsonSerializer.Serialize(busRoot)}},
      "taskId": {{JsonSerializer.Serialize(task.Id)}},
      "timeoutSeconds": 5
    }
    """);

    var response = registry.InvokeAsync("agentbus_wait_result", args).GetAwaiter().GetResult();
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonFile.Options));
    True(doc.RootElement.GetProperty("completed").GetBoolean());
    Equal("fast result", doc.RootElement.GetProperty("result").GetProperty("summary").GetString());
}

static void McpRegistryClampsInvalidAgentBusWaitTimeout()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    var task = bus.CreateTask("ctu/architect", "ctu/worker", "Invalid wait", "Reply briefly.", "demo", root, [], "ctu/architect");
    _ = Task.Run(async () =>
    {
        await Task.Delay(100).ConfigureAwait(false);
        bus.ClaimTask(task.Id, "ctu/worker");
        bus.WriteResult(task.Id, "clamped result", "completed", "ctu/worker", "ctu/architect", null, [], []);
    });

    var registry = McpToolRegistry.CreateDefault(busRoot, new FakeAppServerClient("""{"data":[]}"""));
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "busRoot": {{JsonSerializer.Serialize(busRoot)}} ,
      "taskId": {{JsonSerializer.Serialize(task.Id)}} ,
      "timeoutSeconds": -2
    }
    """);

    var response = registry.InvokeAsync("agentbus_wait_result", args).GetAwaiter().GetResult();
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonFile.Options));
    True(doc.RootElement.GetProperty("completed").GetBoolean());
    Equal("clamped result", doc.RootElement.GetProperty("result").GetProperty("summary").GetString());
}

static void McpRegistryHonorsShortAgentBusWaitTimeout()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    var task = bus.CreateTask("ctu/architect", "ctu/worker", "Short wait", "Reply later.", "demo", root, [], "ctu/architect");

    var registry = McpToolRegistry.CreateDefault(busRoot, new FakeAppServerClient("""{"data":[]}"""));
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "busRoot": {{JsonSerializer.Serialize(busRoot)}} ,
      "taskId": {{JsonSerializer.Serialize(task.Id)}} ,
      "timeoutSeconds": 1
    }
    """);

    var stopwatch = Stopwatch.StartNew();
    var response = registry.InvokeAsync("agentbus_wait_result", args).GetAwaiter().GetResult();
    stopwatch.Stop();

    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonFile.Options));
    Equal(false, doc.RootElement.GetProperty("completed").GetBoolean());
    Equal(1, doc.RootElement.GetProperty("timeoutSeconds").GetInt32());
    True(stopwatch.Elapsed < TimeSpan.FromSeconds(2.5));
}

static void McpTeamSendMessageEnqueuesByDefault()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/ux",
        Role = "UX",
        ThreadId = "thread-ux",
        Cwd = root,
        Status = "active"
    });

    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}} ,
      "busRoot": {{JsonSerializer.Serialize(busRoot)}} ,
      "from": "ctu/dashboard",
      "to": "ctu/ux",
      "title": "Queued peer task",
      "message": "Queue this without direct Desktop wakeup.",
      "project": "codexteamup",
      "waitResult": true
    }
    """);

    var response = registry.InvokeAsync("team_send_message", args).GetAwaiter().GetResult();

    Equal(0, appServer.SentTurns.Count);
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonFile.Options));
    Equal("enqueue", doc.RootElement.GetProperty("dispatchMode").GetString());
    True(doc.RootElement.GetProperty("accepted").GetBoolean());
    True(!doc.RootElement.TryGetProperty("wait", out var wait) || wait.ValueKind == JsonValueKind.Null);
    True(bus.ListTasks("ctu/ux", "open").Count == 1);
    True(bus.ListEvents().Any(evt => evt.Type == "team.message.enqueued"));
}

static void McpTeamSendMessageWaitsForResult()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/ux",
        Role = "UX",
        ThreadId = "thread-ux",
        Cwd = root,
        Status = "active"
    });

    var appServer = new FakeAppServerClient("""{"data":[]}""", (_, message, _) =>
    {
        var taskId = ExtractTaskIdFromWakeMessage(message);
        var workerBus = new AgentBusStore(busRoot);
        workerBus.ClaimTask(taskId, "ctu/ux");
        workerBus.WriteResult(taskId, "sync result", "completed", "ctu/ux", "ctu/dashboard", null, [], []);
    });
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}},
      "busRoot": {{JsonSerializer.Serialize(busRoot)}},
      "from": "ctu/dashboard",
      "to": "ctu/ux",
      "title": "Synchronous peer task",
      "message": "Bitte schreibe sofort ein Result.",
      "project": "codexteamup",
      "dispatchMode": "inline",
      "waitResult": true,
      "timeoutSeconds": 5
    }
    """);

    var response = registry.InvokeAsync("team_send_message", args).GetAwaiter().GetResult();
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonFile.Options));
    True(doc.RootElement.GetProperty("wait").GetProperty("completed").GetBoolean());
    Equal("sync result", doc.RootElement.GetProperty("wait").GetProperty("result").GetProperty("summary").GetString());
    Equal("ctu/dashboard", doc.RootElement.GetProperty("task").GetProperty("returnTo").GetString());
    True(bus.ListEvents().Any(evt => evt.Type == "team.wait.completed"));
}

static void McpTeamSendMessageDefersStalledWakeupQuickly()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/slow",
        Role = "Slow",
        ThreadId = "thread-slow",
        Cwd = root,
        Status = "active"
    });

    var appServer = new FakeAppServerClient("""{"data":[]}""", sendTurnDelay: TimeSpan.FromSeconds(5));
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}} ,
      "busRoot": {{JsonSerializer.Serialize(busRoot)}} ,
      "from": "ctu/dashboard",
      "to": "ctu/slow",
      "title": "Stalled wakeup",
      "message": "This should be deferred quickly.",
      "project": "codexteamup",
      "dispatchMode": "inline",
      "waitResult": true,
      "timeoutSeconds": 5,
      "wakeupTimeoutSeconds": 1
    }
    """);

    var stopwatch = Stopwatch.StartNew();
    var response = registry.InvokeAsync("team_send_message", args).GetAwaiter().GetResult();
    stopwatch.Stop();

    True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonFile.Options));
    True(doc.RootElement.GetProperty("wakeup").GetProperty("deferred").GetBoolean());
    True(!doc.RootElement.TryGetProperty("wait", out var wait) || wait.ValueKind == JsonValueKind.Null);
    True(bus.ListEvents().Any(evt => evt.Type == "team.message.deferred"));
}

static void McpBridgeDispatchTaskBlocksRetiredTarget()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/wrapper",
        Role = "Wrapper",
        ThreadId = "thread-wrapper",
        Cwd = root,
        Status = "retired",
        ReturnTo = "ctu/architect"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/wrapper",
        "Retired agent task",
        "Do not dispatch to retired agent.",
        "codexteamup",
        root,
        [],
        "ctu/architect");

    var appServer = new FakeAppServerClient("""{"data":[{"id":"thread-wrapper","name":"ctu/wrapper","cwd":"ROOT","status":"idle"}]}"""
        .Replace("ROOT", JsonEscaped(root), StringComparison.Ordinal));
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "busRoot": {{JsonSerializer.Serialize(busRoot)}},
      "taskId": {{JsonSerializer.Serialize(task.Id)}}
    }
    """);

    _ = registry.InvokeAsync("bridge_dispatch_task", args).GetAwaiter().GetResult();

    Equal(0, appServer.SentTurns.Count);
    Equal(0, bus.ListTasks("ctu/wrapper", "open").Count());
    var failedResult = bus.WaitForResult(task.Id, TimeSpan.FromMilliseconds(100));
    Equal("failed", failedResult?.Status);
    Equal("ctu/controller", failedResult?.From);
    True(bus.ListEvents(300).Any(evt => evt.TaskId == task.Id && evt.Type == "task.dispatch_blocked_retired_agent"));
}

static void McpRegistryRecreatesStaleCtuAgentThreads()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/foo",
        Role = "Foo",
        DisplayName = "ctu/foo",
        ThreadId = "stale-thread",
        Cwd = root,
        Status = "active"
    });

    var appServer = new FakeAppServerClient(
        """{"data":[{"id":"architect-thread","name":"ctu/architect","preview":"ctu/foo was mentioned here","cwd":"ROOT","status":"idle"}]}"""
            .Replace("ROOT", JsonEscaped(root), StringComparison.Ordinal));
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var agentsJson = JsonSerializer.Serialize(new[] { new { id = "ctu/foo", role = "Foo" } });
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}},
      "busRoot": {{JsonSerializer.Serialize(busRoot)}},
      "agentsJson": {{JsonSerializer.Serialize(agentsJson)}},
      "createMissing": "true",
      "prime": "false"
    }
    """);

    _ = registry.InvokeAsync("team_ensure_agents", args).GetAwaiter().GetResult();
    var agent = bus.FindAgent("ctu/foo");
    Equal("created-thread", agent?.ThreadId);
    Equal("ctu/foo", appServer.NamedThreads.Single().Name);
}

static void McpRegistryCreatesReplacementWhenDisplayNameChanges()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/foo",
        Role = "Foo",
        DisplayName = "ctu/foo",
        ThreadId = "stale-thread",
        Cwd = root,
        Status = "active"
    });

    var appServer = new FakeAppServerClient(
        """{"data":[{"id":"thread-old","name":"ctu/foo","cwd":"ROOT","status":"idle"}]}"""
            .Replace("ROOT", JsonEscaped(root), StringComparison.Ordinal));
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var agentsJson = JsonSerializer.Serialize(new[]
    {
        new { id = "ctu/foo", role = "Replacement Foo", displayName = "ctu/foo-replacement" }
    });
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}},
      "busRoot": {{JsonSerializer.Serialize(busRoot)}},
      "agentsJson": {{JsonSerializer.Serialize(agentsJson)}},
      "createMissing": "true",
      "prime": "false"
    }
    """);

    _ = registry.InvokeAsync("team_ensure_agents", args).GetAwaiter().GetResult();

    var agent = bus.FindAgent("ctu/foo");
    Equal("created-thread", agent?.ThreadId);
    Equal("ctu/foo-replacement", agent?.DisplayName);
    Equal("ctu/foo-replacement", appServer.NamedThreads.Single().Name);
}

static void McpRegistryRetriesThreadNamingUntilCreatedThreadIsVisible()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var appServer = new FakeAppServerClient("""{"data":[]}""", nameSetFailuresBeforeSuccess: 2);
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var agentsJson = JsonSerializer.Serialize(new[]
    {
        new { id = "ctu/foo", role = "Foo", displayName = "ctu/foo" }
    });
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "cwd": {{JsonSerializer.Serialize(root)}},
      "busRoot": {{JsonSerializer.Serialize(busRoot)}},
      "agentsJson": {{JsonSerializer.Serialize(agentsJson)}},
      "createMissing": "true",
      "prime": "false"
    }
    """);

    _ = registry.InvokeAsync("team_ensure_agents", args).GetAwaiter().GetResult();

    Equal("created-thread", new AgentBusStore(busRoot).FindAgent("ctu/foo")?.ThreadId);
    Equal("ctu/foo", appServer.NamedThreads.Single().Name);
}

static void McpRegistryNotifiesResultThroughServicePath()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/foo",
        Role = "Foo",
        ThreadId = "thread-foo",
        Cwd = root,
        Status = "active"
    });
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/bar",
        Role = "Bar",
        ThreadId = "thread-bar",
        Cwd = root,
        ReturnTo = "ctu/foo",
        Status = "active"
    });

    var task = bus.CreateTask("ctu/foo", "ctu/bar", "Ping", "Please answer.", "demo", root, [], "ctu/foo");
    bus.ClaimTask(task.Id, "ctu/bar");
    var busResult = bus.WriteResult(task.Id, "Pong", "completed", "ctu/bar", "ctu/foo", null, [], []);

    var appServer = new FakeAppServerClient("""{"data":[{"id":"thread-foo","name":"ctu/foo","cwd":"ROOT","status":{"type":"idle"}}]}"""
        .Replace("ROOT", JsonEscaped(root), StringComparison.Ordinal));
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "busRoot": {{JsonSerializer.Serialize(busRoot)}},
      "resultId": {{JsonSerializer.Serialize(busResult.Id)}}
    }
    """);

    _ = registry.InvokeAsync("bridge_notify_result", args).GetAwaiter().GetResult();
    Equal(1, appServer.SentTurns.Count);
    Equal("thread-foo", appServer.SentTurns[0].ThreadId);
    True(appServer.SentTurns[0].Message.Contains(busResult.Id, StringComparison.Ordinal));
    var notifyEvent = bus.ListEvents().Single(evt => evt.Type == "result.notified" && evt.ResultId == busResult.Id);
    True(notifyEvent.Message?.Contains("latencyMs=", StringComparison.Ordinal) == true);
    True(notifyEvent.Message?.Contains("turnId=turn-fake", StringComparison.Ordinal) == true);
    True(notifyEvent.Message?.Contains("targetStatus=idle", StringComparison.Ordinal) == true);
}

static void McpRegistryDefersResultNotifyWhenReadThreadShowsBusyTurn()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/foo",
        Role = "Foo",
        ThreadId = "thread-foo",
        Cwd = root,
        Status = "active"
    });
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/bar",
        Role = "Bar",
        ThreadId = "thread-bar",
        Cwd = root,
        ReturnTo = "ctu/foo",
        Status = "active"
    });

    var task = bus.CreateTask("ctu/foo", "ctu/bar", "Ping", "Please answer.", "demo", root, [], "ctu/foo");
    bus.ClaimTask(task.Id, "ctu/bar");
    var busResult = bus.WriteResult(task.Id, "Pong", "completed", "ctu/bar", "ctu/foo", null, [], []);

    var appServer = new FakeAppServerClient(
        """{"data":[{"id":"thread-foo","name":"ctu/foo","cwd":"ROOT","status":{"type":"active"}}]}"""
            .Replace("ROOT", JsonEscaped(root), StringComparison.Ordinal),
        readThreadJson: """{"thread":{"id":"thread-foo","status":{"type":"active"},"turns":[{"id":"turn-1","status":"in_progress"}]}}""");
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "busRoot": {{JsonSerializer.Serialize(busRoot)}},
      "resultId": {{JsonSerializer.Serialize(busResult.Id)}}
    }
    """);

    var response = registry.InvokeAsync("bridge_notify_result", args).GetAwaiter().GetResult();
    Equal(0, appServer.SentTurns.Count);
    var deferredEvent = bus.ListEvents().Single(evt => evt.Type == "result.notify_deferred" && evt.ResultId == busResult.Id);
    True(deferredEvent.Message?.Contains("turn/start deferred", StringComparison.Ordinal) == true);
    True(deferredEvent.Message?.Contains("targetStatus=in_progress", StringComparison.Ordinal) == true);
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonFile.Options));
    True(doc.RootElement.GetProperty("wakeup").GetProperty("deferred").GetBoolean());
}

static void McpRegistrySendsResultNotifyWhenActiveThreadHasNoBusyTurns()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/foo",
        Role = "Foo",
        ThreadId = "thread-foo",
        Cwd = root,
        Status = "active"
    });
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/bar",
        Role = "Bar",
        ThreadId = "thread-bar",
        Cwd = root,
        ReturnTo = "ctu/foo",
        Status = "active"
    });

    var task = bus.CreateTask("ctu/foo", "ctu/bar", "Ping", "Please answer.", "demo", root, [], "ctu/foo");
    bus.ClaimTask(task.Id, "ctu/bar");
    var busResult = bus.WriteResult(task.Id, "Pong", "completed", "ctu/bar", "ctu/foo", null, [], []);

    var appServer = new FakeAppServerClient(
        """{"data":[{"id":"thread-foo","name":"ctu/foo","cwd":"ROOT","status":{"type":"active"}}]}"""
            .Replace("ROOT", JsonEscaped(root), StringComparison.Ordinal),
        readThreadJson: """{"thread":{"id":"thread-foo","status":{"type":"active"},"turns":[]}}""");
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "busRoot": {{JsonSerializer.Serialize(busRoot)}},
      "resultId": {{JsonSerializer.Serialize(busResult.Id)}}
    }
    """);

    var response = registry.InvokeAsync("bridge_notify_result", args).GetAwaiter().GetResult();
    Equal(1, appServer.SentTurns.Count);
    var notifyEvent = bus.ListEvents().Single(evt => evt.Type == "result.notified" && evt.ResultId == busResult.Id);
    True(notifyEvent.Message?.Contains("turn/start sent", StringComparison.Ordinal) == true);
    True(notifyEvent.Message?.Contains("targetStatus=active", StringComparison.Ordinal) == true);
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonFile.Options));
    True(doc.RootElement.GetProperty("target").GetProperty("initialStatus").GetString() == "active");
    True(doc.RootElement.GetProperty("wakeup").GetProperty("deferred").GetBoolean() == false);
}

static void McpRegistryResolvesNotifyTargetThreadParamAsAgentId()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect / Coordinator",
        ThreadId = "thread-architect",
        Cwd = root,
        Status = "active"
    });
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/bar",
        Role = "Bar",
        ThreadId = "thread-bar",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "active"
    });

    var task = bus.CreateTask("ctu/bar", "ctu/architect", "Ping", "Please answer.", "demo", root, [], "ctu/bar");
    bus.ClaimTask(task.Id, "ctu/architect");
    var busResult = bus.WriteResult(task.Id, "Pong", "completed", "ctu/bar", "ctu/architect", null, [], []);

    var appServer = new FakeAppServerClient("""{"data":[{"id":"thread-architect","name":"ctu/architect","cwd":"ROOT","status":{"type":"idle"}}]}"""
        .Replace("ROOT", JsonEscaped(root), StringComparison.Ordinal));
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "busRoot": {{JsonSerializer.Serialize(busRoot)}},
      "resultId": {{JsonSerializer.Serialize(busResult.Id)}},
      "toThread": "ctu/architect"
    }
    """);

    _ = registry.InvokeAsync("bridge_notify_result", args).GetAwaiter().GetResult();
    True(appServer.SentTurns.Count >= 1);
    Equal("thread-architect", appServer.SentTurns[0].ThreadId);
    True(appServer.SentTurns[0].Message.Contains(busResult.Id, StringComparison.Ordinal));
    var resolutionEvent = bus.ListEvents().Single(evt => evt.Type == "result.notify_target_resolved" && evt.ResultId == busResult.Id);
    True(resolutionEvent.Message?.Contains("agent-id", StringComparison.Ordinal) == true);
}

static void AgentThreadMatcherBindsNamedTeamThreads()
{
    var cwd = Path.Combine(NewTestDirectory(), "project");
    var threads = new[]
    {
        new CodexThreadRecord("t0", "ctu/architect", null, cwd, null, "idle", null, DateTimeOffset.Now, "test", null),
        new CodexThreadRecord("t1", "ctu/web", null, cwd, null, "idle", null, DateTimeOffset.Now, "test", null),
        new CodexThreadRecord("t3", "ctu/designer", null, cwd, null, "idle", null, DateTimeOffset.Now, "test", null)
    };
    var bindings = AgentThreadMatcher.MatchAgents(["ctu/architect", "ctu/web", "ctu/designer"], threads, cwd);
    Equal("t0", bindings.Single(binding => binding.AgentId == "ctu/architect").ThreadId);
    Equal("t1", bindings.Single(binding => binding.AgentId == "ctu/web").ThreadId);
    Equal("t3", bindings.Single(binding => binding.AgentId == "ctu/designer").ThreadId);
}

static void AgentThreadMatcherBindsExactPreviewNames()
{
    var cwd = Path.Combine(NewTestDirectory(), "project");
    var threads = new[]
    {
        new CodexThreadRecord("t0", null, "ctu-test/architect", cwd, null, "idle", null, DateTimeOffset.Now, "test", null),
        new CodexThreadRecord("t1", "Architect ctu-test", "ctu-test/architect mentioned in a note", cwd, null, "idle", null, DateTimeOffset.Now.AddMinutes(-1), "test", null)
    };

    var binding = AgentThreadMatcher.MatchAgents(["ctu-test/architect"], threads, cwd).Single();

    Equal("t0", binding.ThreadId);
}

static void AgentBusDashboardRendersCommunication()
{
    var root = NewTestDirectory();
    var bus = new AgentBusStore(Path.Combine(root, ".codexteamup/agentbus"));
    bus.Initialize();
    var task = bus.CreateTask("ctu/architect", "ctu/web", "Build editor", "Implement slice", "codexteamup", root, ["web/"], "ctu/architect");
    bus.ClaimTask(task.Id, "ctu/web");
    var result = bus.WriteResult(task.Id, "Implemented", "completed", "ctu/web", "ctu/architect", null, ["dotnet build"], []);
    bus.RecordEvent(new AgentBusEvent
    {
        Timestamp = DateTimeOffset.Now,
        Type = "result.notified",
        ResultId = result.Id,
        From = "ctu/web",
        To = "ctu/architect",
        Message = "Architect notified."
    });
    var path = AgentBusDashboard.Export(bus);
    True(File.Exists(path));
    var html = File.ReadAllText(path);
    True(!html.Contains("http-equiv=\"refresh\"", StringComparison.OrdinalIgnoreCase));
    True(html.Contains("id=\"app\"", StringComparison.Ordinal));
    True(html.Contains("<script>", StringComparison.Ordinal));
    True(html.Contains("/api/snapshot", StringComparison.Ordinal));
    True(html.Contains("Codex</span><span class=\"team\">Team</span><span class=\"up\">Up", StringComparison.Ordinal));
    True(html.Contains("data-theme-value=\"system\"", StringComparison.Ordinal));
    True(html.Contains("class=\"meta-toolbar\"", StringComparison.Ordinal));
    True(html.Contains("class=\"flow-overview\"", StringComparison.Ordinal));
    True(html.Contains("Situation", StringComparison.Ordinal));
    True(html.Contains("Route Map", StringComparison.Ordinal));
    True(html.Contains("Latest Handoffs", StringComparison.Ordinal));
    True(html.Contains("No stuck work detected.", StringComparison.Ordinal));
    True(html.Contains(".side-panel{min-height:0;display:grid;grid-template-rows:auto auto minmax(0,1fr)}", StringComparison.Ordinal));
    True(!html.Contains("flows.reduce((sum, flow) => sum + flow.tasks", StringComparison.Ordinal));
    True(!html.Contains("class=\"hero-card\"", StringComparison.Ordinal));
    True(!html.Contains("class=\"project-card\"", StringComparison.Ordinal));
    True(!html.Contains("Relationship sketch", StringComparison.Ordinal));
    True(html.Contains("Clear view", StringComparison.Ordinal));
    True(html.Contains("<h2>Communication</h2>", StringComparison.Ordinal));
    True(!html.Contains("<h2>Events</h2>", StringComparison.Ordinal));
    True(!html.Contains("<h2>Results</h2>", StringComparison.Ordinal));
    True(html.Contains("<h2>Inspector</h2>", StringComparison.Ordinal));
    True(html.Contains("data-ref=\"relationshipsButton\"", StringComparison.Ordinal));
    True(html.Contains("data-ref=\"projectButton\"", StringComparison.Ordinal));
    True(html.Contains("Operations / System", StringComparison.Ordinal));
    True(html.Contains(".handoff-row.completed,.handoff-row.done", StringComparison.Ordinal));
    True(html.Contains("Architect notified.", StringComparison.Ordinal));
    True(html.Contains("Open inspector", StringComparison.Ordinal));
    True(html.Contains("Build editor", StringComparison.Ordinal));
    True(html.Contains("Implemented", StringComparison.Ordinal));
    True(html.IndexOf("Build editor", StringComparison.Ordinal) < html.IndexOf("Implemented", StringComparison.Ordinal));
}

static void AgentBusDashboardCreatesSnapshot()
{
    var root = NewTestDirectory();
    var bus = new AgentBusStore(Path.Combine(root, ".codexteamup/agentbus"));
    bus.Initialize();
    var task = bus.CreateTask("ctu/architect", "ctu/web", "Build editor", "Implement slice", "codexteamup", root, ["web/"], "ctu/architect");
    bus.ClaimTask(task.Id, "ctu/web");
    bus.WriteResult(task.Id, "Implemented", "completed", "ctu/web", "ctu/architect", null, ["dotnet build"], []);

    var snapshot = AgentBusDashboard.CreateSnapshot(bus);

    Equal(bus.RootDirectory, snapshot.BusRoot);
    Equal(snapshot.Agents.Count, snapshot.Stats.Agents);
    Equal(1, snapshot.Stats.Tasks);
    Equal(0, snapshot.Stats.OpenTasks);
    Equal(0, snapshot.Stats.ClaimedTasks);
    Equal(1, snapshot.Stats.DoneTasks);
    Equal(0, snapshot.Stats.FailedTasks);
    Equal(1, snapshot.Stats.Results);
    Equal(0, snapshot.Stats.OpenContinuations);
    True(snapshot.Tasks.Single().Title.Contains("Build editor", StringComparison.Ordinal));
    True(snapshot.Results.Single().Summary.Contains("Implemented", StringComparison.Ordinal));
    True(snapshot.Events.Count > 0);
}

static void StartupScriptRecordsCtuSessionManifest()
{
    var text = File.ReadAllText(Path.Combine(TestRepoRoot(), "scripts", "start-codexteamup.ps1"));

    True(text.Contains(".ctu\\sessions", StringComparison.Ordinal));
    True(text.Contains("Write-CtuSessionManifest", StringComparison.Ordinal));
    True(text.Contains("launcherPid = $PID", StringComparison.Ordinal));
    True(text.Contains("desktopPid = $desktopPid", StringComparison.Ordinal));
    True(text.Contains("servicePid = $servicePid", StringComparison.Ordinal));
    True(text.Contains("wrapperPids = @(Get-CtuWrapperProcessIds)", StringComparison.Ordinal));
    True(text.Contains("Add-CtuSessionProcessCandidates", StringComparison.Ordinal));
    True(text.Contains("ServiceProcessId", StringComparison.Ordinal));
}

static void RestartSupervisorUsesSessionManifestAndTransientTargetStartup()
{
    var text = File.ReadAllText(Path.Combine(TestRepoRoot(), "scripts", "restart-supervisor.ps1"));

    True(text.Contains("Read-SourceSessionManifest", StringComparison.Ordinal));
    True(text.Contains("Stop-SourceSessionProcesses", StringComparison.Ordinal));
    True(text.Contains("launcherPid", StringComparison.Ordinal));
    True(text.Contains("startup-console", StringComparison.Ordinal));
    True(!text.Contains("\"-NoExit\"", StringComparison.Ordinal));
    True(text.Contains("-WindowStyle Normal", StringComparison.Ordinal));
    True(text.Contains("Stop-SourceSessionProcesses -sourceCwd", StringComparison.Ordinal));
}

static void RestartHelperSupervisorConsoleIsTransient()
{
    var text = File.ReadAllText(Path.Combine(TestRepoRoot(), "src", "CodexTeamUp.Controller.Default", "DefaultCtuController.cs"));

    True(text.Contains("var command = $\"-ExecutionPolicy Bypass -File", StringComparison.Ordinal));
    True(!text.Contains("var command = $\"-NoExit -ExecutionPolicy Bypass -File", StringComparison.Ordinal));
}

static void RestartOperationLifecycleAndPersistence()
{
    var root = NewTestDirectory();
    var sourceCwd = Path.Combine(root, "source-checkout");
    var targetCwd = Path.Combine(root, "target-checkout");
    var fallbackCwd = Path.Combine(root, "fallback-checkout");
    SeedRestartCheckout(sourceCwd);
    SeedRestartCheckout(targetCwd);
    SeedRestartCheckout(fallbackCwd);

    var store = new RestartOperationStore(Path.Combine(root, ".codexteamup", "agentbus"));
    var op = store.Create(
        requestedByAgentId: "ctu/architect",
        sourceCwd: sourceCwd,
        sourceBusRoot: Path.Combine(sourceCwd, ".codexteamup", "agentbus"),
        targetCwd: targetCwd,
        targetBusRoot: Path.Combine(targetCwd, ".codexteamup", "agentbus"),
        targetAgentId: "ctu/architect",
        fallbackCwd: fallbackCwd,
        fallbackBusRoot: Path.Combine(fallbackCwd, ".codexteamup", "agentbus"),
        continueTitle: "Continue after restart",
        continuePrompt: "Validate restart handoff.",
        expectedTargetBranch: null);

    Equal(RestartOperationStatus.Prepared, op.Status);
    True(!string.IsNullOrWhiteSpace(op.Id));
    var path = store.OperationPath(op.Id);
    True(File.Exists(path));
    var read = store.Find(op.Id);
    Equal(op.Id, read?.Id);
    Equal(op.Status, read?.Status);
    Equal("ctu.desktop-restart", read?.Kind);
    Equal(sourceCwd, read?.SourceCwd);
    Equal(targetCwd, read?.TargetCwd);
    Equal("ctu/architect", read?.TargetAgentId);
    Equal("Continue after restart", read?.ContinueTitle);
    Equal(fallbackCwd, read?.FallbackCwd);

    Equal(RestartOperationStatus.HelperStarted, store.UpdateStatus(op.Id, RestartOperationStatus.HelperStarted, helperPid: "555").Status);
    Equal(RestartOperationStatus.StoppingSource, store.UpdateStatus(op.Id, RestartOperationStatus.StoppingSource).Status);
    Equal(RestartOperationStatus.StartingTarget, store.UpdateStatus(op.Id, RestartOperationStatus.StartingTarget).Status);
    Equal(RestartOperationStatus.TargetHealthy, store.UpdateStatus(op.Id, RestartOperationStatus.TargetHealthy).Status);
    Equal(RestartOperationStatus.ContinuationEnqueued, store.UpdateStatus(op.Id, RestartOperationStatus.ContinuationEnqueued).Status);
    Equal(RestartOperationStatus.ContinuationDispatched, store.UpdateStatus(op.Id, RestartOperationStatus.ContinuationDispatched).Status);

    var completed = store.UpdateStatus(op.Id, RestartOperationStatus.Completed);
    Equal(RestartOperationStatus.Completed, completed.Status);
    True(completed.CompletedAt is not null);
}

static void RestartOperationRejectsInvalidTargetCwd()
{
    var root = NewTestDirectory();
    var store = new RestartOperationStore(Path.Combine(root, ".codexteamup", "agentbus"));
    var sourceCwd = Path.Combine(root, "source");
    SeedRestartCheckout(sourceCwd);

    try
    {
        _ = store.Create(
            "ctu/architect",
            sourceCwd,
            Path.Combine(sourceCwd, ".codexteamup", "agentbus"),
            Path.Combine(root, "missing-target"),
            Path.Combine(root, "missing-target", ".codexteamup", "agentbus"),
            "ctu/architect",
            null,
            null,
            "Continue after restart",
            "Continue from next agent.",
            null);
        throw new InvalidOperationException("Expected missing target checkout failure.");
    }
    catch (DirectoryNotFoundException)
    {
        // expected.
    }

    var sameCwd = Path.Combine(root, "same");
    Directory.CreateDirectory(sameCwd);
    try
    {
        _ = store.Create(
            "ctu/architect",
            sameCwd,
            Path.Combine(sameCwd, ".codexteamup", "agentbus"),
            sameCwd,
            Path.Combine(sameCwd, ".codexteamup", "agentbus"),
            "ctu/architect",
            null,
            null,
            "Continue after restart",
            "Continue from same path.",
            null);
        throw new InvalidOperationException("Expected same-checkout failure.");
    }
    catch (InvalidOperationException)
    {
        // expected.
    }
}

static void RestartOperationStatusUpdateIsIdempotent()
{
    var root = NewTestDirectory();
    var sourceCwd = Path.Combine(root, "source");
    var targetCwd = Path.Combine(root, "target");
    SeedRestartCheckout(sourceCwd);
    SeedRestartCheckout(targetCwd);
    var store = new RestartOperationStore(Path.Combine(root, ".codexteamup", "agentbus"));

    var op = store.Create(
        "ctu/architect",
        sourceCwd,
        Path.Combine(sourceCwd, ".codexteamup", "agentbus"),
        targetCwd,
        Path.Combine(targetCwd, ".codexteamup", "agentbus"),
        "ctu/architect",
        null,
        null,
        "Continue after restart",
        "Continue from prior work.",
        null);

    var first = store.UpdateStatus(op.Id, RestartOperationStatus.HelperStarted, helperPid: "111");
    var second = store.UpdateStatus(op.Id, RestartOperationStatus.HelperStarted);
    Equal(first.Id, second.Id);
    Equal(RestartOperationStatus.HelperStarted, second.Status);
    Equal(first.CompletedAt, second.CompletedAt);
    Equal("111", second.HelperPid);

    var stopping = store.UpdateStatus(op.Id, RestartOperationStatus.StoppingSource);
    Equal(RestartOperationStatus.StoppingSource, stopping.Status);
    var rollback = store.UpdateStatus(op.Id, RestartOperationStatus.RollbackStarting);
    Equal(RestartOperationStatus.RollbackStarting, rollback.Status);
    var rolledBack = store.UpdateStatus(op.Id, RestartOperationStatus.RolledBack);
    Equal(RestartOperationStatus.RolledBack, rolledBack.Status);
    True(rolledBack.CompletedAt >= rolledBack.RequestedAt);
    var replay = store.UpdateStatus(op.Id, RestartOperationStatus.RolledBack);
    Equal(rolledBack.CompletedAt, replay.CompletedAt);

    var completed = store.Create(
        "ctu/architect",
        sourceCwd,
        Path.Combine(sourceCwd, ".codexteamup", "agentbus"),
        targetCwd,
        Path.Combine(targetCwd, ".codexteamup", "agentbus"),
        "ctu/architect",
        null,
        null,
        "Continue after restart",
        "Continue second scenario.",
        null);

    completed = store.UpdateStatus(completed.Id, RestartOperationStatus.HelperStarted);
    completed = store.UpdateStatus(completed.Id, RestartOperationStatus.StoppingSource);
    completed = store.UpdateStatus(completed.Id, RestartOperationStatus.StartingTarget);
    completed = store.UpdateStatus(completed.Id, RestartOperationStatus.TargetHealthy);
    completed = store.UpdateStatus(completed.Id, RestartOperationStatus.ContinuationEnqueued);
    var completedViaTerminal = store.UpdateStatus(completed.Id, RestartOperationStatus.Completed);
    Equal(RestartOperationStatus.Completed, completedViaTerminal.Status);
    var terminalReplay = store.UpdateStatus(completed.Id, RestartOperationStatus.Completed);
    Equal(completedViaTerminal.CompletedAt, terminalReplay.CompletedAt);
}

static void RestartOperationPreservesImportedContinuationTaskId()
{
    var root = NewTestDirectory();
    var sourceCwd = Path.Combine(root, "source");
    var targetCwd = Path.Combine(root, "target");
    SeedRestartCheckout(sourceCwd);
    SeedRestartCheckout(targetCwd);
    var store = new RestartOperationStore(Path.Combine(sourceCwd, ".codexteamup", "agentbus"));

    var operation = store.Create(
        "ctu/architect",
        sourceCwd,
        Path.Combine(sourceCwd, ".codexteamup", "agentbus"),
        targetCwd,
        Path.Combine(targetCwd, ".codexteamup", "agentbus"),
        "ctu/architect",
        null,
        null,
        "Continue after restart",
        "Continue with imported task.",
        null);

    operation = store.UpdateStatus(
        operation,
        RestartOperationStatus.ContinuationEnqueued,
        startupHandoffMessageId: "restart-handoff-1");
    Equal("restart-handoff-1", operation.StartupHandoffMessageId);
    Equal(null, operation.ContinuationTaskId);

    operation = store.UpdateStatus(
        operation,
        RestartOperationStatus.ContinuationDispatched,
        continuationTaskId: "task-actual-1",
        startupHandoffMessageId: "restart-handoff-1");
    Equal("task-actual-1", operation.ContinuationTaskId);
    Equal("restart-handoff-1", operation.StartupHandoffMessageId);

    operation = store.UpdateStatus(
        operation,
        RestartOperationStatus.Completed,
        startupHandoffMessageId: "restart-handoff-1",
        lastError: "phase=completed");
    Equal("task-actual-1", operation.ContinuationTaskId);
    Equal("restart-handoff-1", operation.StartupHandoffMessageId);
}

static void RestartOperationPersistsStartupHandoffAndKnownGood()
{
    var root = NewTestDirectory();
    var sourceCwd = Path.Combine(root, "source-checkout");
    var targetCwd = Path.Combine(root, "target-checkout");
    SeedRestartCheckout(sourceCwd);
    SeedRestartCheckout(targetCwd);
    var sourceBusRoot = Path.Combine(sourceCwd, ".codexteamup", "agentbus");

    var knownGoodStore = new KnownGoodRuntimeCheckpointStore(sourceBusRoot);
    var knownGood = knownGoodStore.WriteHealthy(
        sourceCwd,
        sourceBusRoot,
        null,
        nameof(DefaultCtuController),
        null,
        "pipe");

    var store = new RestartOperationStore(sourceBusRoot);
    var operation = store.Create(
        requestedByAgentId: "ctu/architect",
        sourceCwd: sourceCwd,
        sourceBusRoot: sourceBusRoot,
        targetCwd: targetCwd,
        targetBusRoot: Path.Combine(targetCwd, ".codexteamup", "agentbus"),
        targetAgentId: "ctu/architect",
        fallbackCwd: null,
        fallbackBusRoot: null,
        continueTitle: "Continue after restart",
        continuePrompt: "Validate restart handoff.",
        expectedTargetBranch: null,
        knownGoodCheckpointId: knownGood.Id);

    Equal(knownGood.Id, operation.KnownGoodCheckpointId);
    var reloaded = store.Find(operation.Id);
    Equal(RestartOperationStatus.Prepared, reloaded?.Status);
    Equal(knownGood.Id, reloaded?.KnownGoodCheckpointId);
}

static void ExchangeHandoffLeaseAndCompletionFlow()
{
    var root = NewTestDirectory();
    var sourceCwd = Path.Combine(root, "source-checkout");
    var targetCwd = Path.Combine(root, "target-checkout");
    SeedRestartCheckout(sourceCwd);
    SeedRestartCheckout(targetCwd);
    var sourceBusRoot = Path.Combine(sourceCwd, ".codexteamup", "agentbus");
    var targetBusRoot = Path.Combine(targetCwd, ".codexteamup", "agentbus");
    var exchangeRoot = Path.Combine(Path.GetDirectoryName(targetBusRoot)!, "exchange");

    var sourceExchange = new ExchangeStore(sourceBusRoot);
    True(sourceExchange.ListPendingStartupSystemMessages(ExchangeEnvelopeKind.Restart, 10).Count == 0);

    var operationStore = new RestartOperationStore(sourceBusRoot);
    var operation = operationStore.Create(
        requestedByAgentId: "ctu/architect",
        sourceCwd: sourceCwd,
        sourceBusRoot: sourceBusRoot,
        targetCwd: targetCwd,
        targetBusRoot: Path.Combine(targetCwd, ".codexteamup", "agentbus"),
        targetAgentId: "ctu/architect",
        fallbackCwd: null,
        fallbackBusRoot: null,
        continueTitle: "Continue after restart",
        continuePrompt: "Validate restart handoff.",
        expectedTargetBranch: null);

    var exchange = new ExchangeStore(targetBusRoot);
    var envelope = exchange.CreateRestartHandoff(operationStore.OperationPath(operation.Id), operation);
    var startupExpectedPath = Path.Combine(
        exchangeRoot,
        "startup",
        ExchangeTargetScope.System,
        ExchangeEnvelopeKind.Restart,
        $"{envelope.MessageId}.json");
    var pending = exchange.ListPendingStartupSystemMessages(ExchangeEnvelopeKind.Restart, 10);
    Equal(1, pending.Count);
    Equal(startupExpectedPath, pending.Single().Path);
    True(!File.Exists(Path.Combine(exchangeRoot, "inbox", ExchangeTargetScope.System, ExchangeEnvelopeKind.Restart, $"{envelope.MessageId}.json")));

    using var lease = exchange.TryAcquireLease(pending[0].Path, "ctu-controller", TimeSpan.FromMinutes(1));
    True(lease is not null);

    exchange.Complete(pending[0].Path, lease!.Envelope);
    pending = exchange.ListPendingStartupSystemMessages(ExchangeEnvelopeKind.Restart, 10);
    Equal(0, pending.Count);
}

static void ExchangeHandoffAcceptsPowerShellCasing()
{
    var root = NewTestDirectory();
    var sourceCwd = Path.Combine(root, "source-checkout");
    var targetCwd = Path.Combine(root, "target-checkout");
    SeedRestartCheckout(sourceCwd);
    SeedRestartCheckout(targetCwd);
    var targetBusRoot = Path.Combine(targetCwd, ".codexteamup", "agentbus");
    var exchange = new ExchangeStore(targetBusRoot);
    exchange.Initialize();

    var startupDirectory = Path.Combine(
        Path.GetDirectoryName(targetBusRoot)!,
        "exchange",
        "startup",
        ExchangeTargetScope.System,
        ExchangeEnvelopeKind.Restart);
    Directory.CreateDirectory(startupDirectory);
    var messagePath = Path.Combine(startupDirectory, "restart-handoff-pwsh.json");
    File.WriteAllText(messagePath, """
        {
          "MessageId": "restart-handoff-pwsh",
          "Kind": "restart",
          "TargetScope": "system",
          "TargetProject": "target-checkout",
          "TargetAgentId": "ctu/architect",
          "TargetThreadName": "ctu/architect",
          "CorrelationId": "restart-test",
          "CausationId": "restart-test",
          "CreatedAt": "2026-05-18T12:00:00+00:00",
          "ExpiresAt": "2026-05-18T16:00:00+00:00",
          "PayloadType": "application/json",
          "Payload": {
            "operationId": "restart-test",
            "operationPath": "S:\\tmp\\operation.json",
            "targetAgentId": "ctu/architect"
          },
          "AttemptCount": 0,
          "Status": "pending"
        }
        """);

    var pending = exchange.ListPendingStartupSystemMessages(ExchangeEnvelopeKind.Restart, 10);
    Equal(1, pending.Count);
    Equal("restart-handoff-pwsh", pending.Single().Envelope.MessageId);

    using var lease = exchange.TryAcquireLease(messagePath, "ctu-controller", TimeSpan.FromMinutes(1));
    True(lease is not null);
    Equal("restart-handoff-pwsh", lease!.Envelope.MessageId);
}

static void ExchangeStartupSweepIsolatesMalformedEnvelope()
{
    var root = NewTestDirectory();
    var targetCwd = Path.Combine(root, "target-checkout");
    SeedRestartCheckout(targetCwd);
    var targetBusRoot = Path.Combine(targetCwd, ".codexteamup", "agentbus");
    var exchange = new ExchangeStore(targetBusRoot);
    exchange.Initialize();

    var startupDirectory = Path.Combine(
        Path.GetDirectoryName(targetBusRoot)!,
        "exchange",
        "startup",
        ExchangeTargetScope.System,
        ExchangeEnvelopeKind.Restart);
    Directory.CreateDirectory(startupDirectory);
    var malformedPath = Path.Combine(startupDirectory, "broken.json");
    File.WriteAllText(malformedPath, """{ "messageId": "broken" }""");

    var pending = exchange.ListPendingStartupSystemMessages(ExchangeEnvelopeKind.Restart, 10);
    Equal(0, pending.Count);
    True(!File.Exists(malformedPath));
    True(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(targetBusRoot)!, "exchange", "deadletter"), "invalid-*-broken.json").Length == 1);
}

static void KnownGoodRuntimeCheckpointStoreRecordsRuntime()
{
    var root = NewTestDirectory();
    var checkout = Path.Combine(root, "checkout");
    Directory.CreateDirectory(checkout);
    var busRoot = Path.Combine(checkout, ".codexteamup", "agentbus");

    var store = new KnownGoodRuntimeCheckpointStore(busRoot);
    var checkpoint = store.WriteHealthy(
        checkout,
        busRoot,
        Path.Combine(root, "controller.dll"),
        nameof(DefaultCtuController),
        Path.Combine(root, "server.dll"),
        nameof(CodexTeamUp.AppServer.WrapperPipeAppServerClient));

    True(File.Exists(store.CheckpointPath));
    Equal(checkout, checkpoint.CheckoutCwd);
    var read = store.Read();
    True(read is not null);
    Equal(read?.Id, checkpoint.Id);
    Equal(checkpoint.Id, read!.Id);
}

static void KnownGoodCheckpointRequiresExplicitVerification()
{
    var root = NewTestDirectory();
    var checkout = Path.Combine(root, "checkout");
    Directory.CreateDirectory(checkout);
    var busRoot = Path.Combine(checkout, ".codexteamup", "agentbus");

    var store = new KnownGoodRuntimeCheckpointStore(busRoot);
    store.WriteHealthy(
        checkout,
        busRoot,
        Path.Combine(root, "controller.dll"),
        nameof(DefaultCtuController),
        Path.Combine(root, "server.dll"),
        nameof(CodexTeamUp.AppServer.WrapperPipeAppServerClient));

    var bootOnly = store.Read();
    True(bootOnly is not null);
    Equal(false, bootOnly!.IsVerified);
    True(store.ReadVerified() is null);

    var verified = store.WriteHealthy(
        checkout,
        busRoot,
        Path.Combine(root, "controller.dll"),
        nameof(DefaultCtuController),
        Path.Combine(root, "server.dll"),
        nameof(CodexTeamUp.AppServer.WrapperPipeAppServerClient),
        isVerified: true,
        verificationSource: "startup_sweep");

    var verifiedRead = store.Read();
    True(verifiedRead?.IsVerified == true);
    var verifiedOnly = store.ReadVerified();
    True(verifiedOnly is not null);
    Equal(verified.Id, verifiedOnly?.Id);
}

static void ControllerStartupSweepImportsRestartHandoff()
{
    var root = NewTestDirectory();
    var sourceCwd = Path.Combine(root, "source-checkout");
    var targetCwd = Path.Combine(root, "target-checkout");
    SeedRestartCheckout(sourceCwd);
    SeedRestartCheckout(targetCwd);

    var sourceBusRoot = Path.Combine(sourceCwd, ".codexteamup", "agentbus");
    var targetBusRoot = Path.Combine(targetCwd, ".codexteamup", "agentbus");
    var exchangeRoot = Path.Combine(Path.GetDirectoryName(targetBusRoot)!, "exchange");

    var targetBus = new AgentBusStore(targetBusRoot);
    targetBus.Initialize();
    targetBus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        DisplayName = "ctu/architect",
        ThreadId = "thread-architect",
        Cwd = targetCwd,
        ReturnTo = null,
        Status = "active"
    });

    var operationStore = new RestartOperationStore(sourceBusRoot);
    var targetExchange = new ExchangeStore(targetBusRoot);
    var operation = operationStore.Create(
        requestedByAgentId: "ctu/architect",
        sourceCwd: sourceCwd,
        sourceBusRoot: sourceBusRoot,
        targetCwd: targetCwd,
        targetBusRoot: targetBusRoot,
        targetAgentId: "ctu/architect",
        fallbackCwd: null,
        fallbackBusRoot: null,
        continueTitle: "Continue after restart",
        continuePrompt: "Resume from restart handoff.",
        expectedTargetBranch: null);

    var envelope = targetExchange.CreateRestartHandoff(operationStore.OperationPath(operation.Id), operation);
    Equal(1, targetExchange.ListPendingStartupSystemMessages(ExchangeEnvelopeKind.Restart, 10).Count);
    Equal(0, targetExchange.ListPendingSystemMessages(ExchangeEnvelopeKind.Restart, 10).Count);
    True(!File.Exists(Path.Combine(exchangeRoot, "inbox", ExchangeTargetScope.System, ExchangeEnvelopeKind.Restart, $"{envelope.MessageId}.json")));
    True(File.Exists(Path.Combine(exchangeRoot, "startup", ExchangeTargetScope.System, ExchangeEnvelopeKind.Restart, $"{envelope.MessageId}.json")));

    var appServer = new FakeAppServerClient(
        $$"""
        {
          "data":[{"id":"thread-architect","name":"ctu/architect","cwd":{{JsonSerializer.Serialize(targetCwd)}}, "status":"idle"}]
        }
        """);

    var controller = new DefaultCtuController(targetBusRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    Equal(0, targetExchange.ListPendingStartupSystemMessages(ExchangeEnvelopeKind.Restart, 10).Count);
    var reloaded = operationStore.Find(operation.Id);
    Equal(RestartOperationStatus.ContinuationDispatched, reloaded?.Status);
    Equal(envelope.MessageId, reloaded?.StartupHandoffMessageId);
    True(!string.IsNullOrWhiteSpace(reloaded?.ContinuationTaskId));

    var importedTask = targetBus.FindTask(reloaded!.ContinuationTaskId!);
    True(importedTask is not null);
    Equal("Continue after restart", importedTask!.Title);
}

static void ControllerStartupSweepPersistsRestartContinuationAsResumePendingExternal()
{
    var root = NewTestDirectory();
    var sourceCwd = Path.Combine(root, "source-checkout");
    var targetCwd = Path.Combine(root, "target-checkout");
    SeedRestartCheckout(sourceCwd);
    SeedRestartCheckout(targetCwd);

    var sourceBusRoot = Path.Combine(sourceCwd, ".codexteamup", "agentbus");
    var targetBusRoot = Path.Combine(targetCwd, ".codexteamup", "agentbus");
    var exchangeRoot = Path.Combine(Path.GetDirectoryName(targetBusRoot)!, "exchange");

    var targetBus = new AgentBusStore(targetBusRoot);
    targetBus.Initialize();
    targetBus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        DisplayName = "ctu/architect",
        ThreadId = null,
        Cwd = targetCwd,
        ReturnTo = null,
        Status = "active"
    });

    var operationStore = new RestartOperationStore(sourceBusRoot);
    var targetExchange = new ExchangeStore(targetBusRoot);
    var operation = operationStore.Create(
        requestedByAgentId: "ctu/architect",
        sourceCwd: sourceCwd,
        sourceBusRoot: sourceBusRoot,
        targetCwd: targetCwd,
        targetBusRoot: targetBusRoot,
        targetAgentId: "ctu/architect",
        fallbackCwd: null,
        fallbackBusRoot: null,
        continueTitle: "Continue after restart",
        continuePrompt: "Resume from restart handoff.",
        expectedTargetBranch: null);

    var handoff = targetExchange.CreateRestartHandoff(operationStore.OperationPath(operation.Id), operation);
    var startupMessage = targetExchange.ListPendingStartupSystemMessages(ExchangeEnvelopeKind.Restart, 10).Single();
    Equal(1, targetExchange.ListPendingStartupSystemMessages(ExchangeEnvelopeKind.Restart, 10).Count);
    Equal(handoff.MessageId, startupMessage.Envelope.MessageId);
    True(!File.Exists(Path.Combine(exchangeRoot, "inbox", ExchangeTargetScope.System, ExchangeEnvelopeKind.Restart, $"{handoff.MessageId}.json")));
    True(File.Exists(startupMessage.Path));

    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var controller = new DefaultCtuController(targetBusRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    var reloaded = operationStore.Find(operation.Id);
    Equal(RestartOperationStatus.ContinuationEnqueued, reloaded?.Status);

    var continuity = new ExecutionContinuityStateStore(targetBusRoot);
    continuity.Initialize();
    var state = continuity.ReadLatest(operation.Id);
    Equal(ExecutionContinuityStateKind.ResumePendingExternal, state?.State);
    Equal(true, state?.ShouldContinue ?? false);
    True(!string.IsNullOrWhiteSpace(state?.ResumeCorrelationId));

    var continuationTask = targetBus.FindTask(reloaded!.ContinuationTaskId!);
    True(continuationTask is not null);
    Equal(continuationTask!.Id, state?.ResumeCorrelationId);
}

static void ControllerContinuityGuardianResumesRestartContinuationFromExternalCorrelation()
{
    var root = NewTestDirectory();
    var sourceCwd = Path.Combine(root, "source-checkout");
    var targetCwd = Path.Combine(root, "target-checkout");
    SeedRestartCheckout(sourceCwd);
    SeedRestartCheckout(targetCwd);

    var sourceBusRoot = Path.Combine(sourceCwd, ".codexteamup", "agentbus");
    var targetBusRoot = Path.Combine(targetCwd, ".codexteamup", "agentbus");

    var targetBus = new AgentBusStore(targetBusRoot);
    targetBus.Initialize();
    targetBus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        DisplayName = "ctu/architect",
        ThreadId = null,
        Cwd = targetCwd,
        ReturnTo = null,
        Status = "active"
    });

    var operationStore = new RestartOperationStore(sourceBusRoot);
    var targetExchange = new ExchangeStore(targetBusRoot);
    var operation = operationStore.Create(
        requestedByAgentId: "ctu/architect",
        sourceCwd: sourceCwd,
        sourceBusRoot: sourceBusRoot,
        targetCwd: targetCwd,
        targetBusRoot: targetBusRoot,
        targetAgentId: "ctu/architect",
        fallbackCwd: null,
        fallbackBusRoot: null,
        continueTitle: "Continue after restart",
        continuePrompt: "Resume from restart handoff.",
        expectedTargetBranch: null);

    targetExchange.CreateRestartHandoff(operationStore.OperationPath(operation.Id), operation);
    var firstController = new DefaultCtuController(targetBusRoot, new FakeAppServerClient("""{"data":[]}"""));
    firstController.RunStartupSweepAsync().GetAwaiter().GetResult();

    var reloaded = operationStore.Find(operation.Id);
    var continuationTask = targetBus.FindTask(reloaded!.ContinuationTaskId!);
    True(continuationTask is not null);

    targetBus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        DisplayName = "ctu/architect",
        ThreadId = "thread-architect",
        Cwd = targetCwd,
        ReturnTo = null,
        Status = "active"
    });

    var appServer = new FakeAppServerClient(
        $$"""
        {
          "data":[{"id":"thread-architect","name":"ctu/architect","cwd":{{JsonSerializer.Serialize(targetCwd)}}, "status":"idle"}]
        }
        """);
    var secondController = new DefaultCtuController(targetBusRoot, appServer);
    secondController.RunStartupSweepAsync().GetAwaiter().GetResult();

    Equal(1, appServer.SentTurns.Count);
    Equal("thread-architect", appServer.SentTurns[0].ThreadId);
    True(appServer.SentTurns.Single().Message.Contains(continuationTask.Id));
    True(targetBus.ListEvents(300).Any(evt => evt.Type == "continuity.dispatch_from_state" && evt.TaskId == continuationTask.Id));
}

static void StartupSweepVerifiesKnownGoodCheckpointAfterDispatch()
{
    var root = NewTestDirectory();
    var sourceCwd = Path.Combine(root, "source-checkout");
    var targetCwd = Path.Combine(root, "target-checkout");
    SeedRestartCheckout(sourceCwd);
    SeedRestartCheckout(targetCwd);

    var sourceBusRoot = Path.Combine(sourceCwd, ".codexteamup", "agentbus");
    var targetBusRoot = Path.Combine(targetCwd, ".codexteamup", "agentbus");

    var targetBus = new AgentBusStore(targetBusRoot);
    targetBus.Initialize();
    targetBus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        DisplayName = "ctu/architect",
        ThreadId = "thread-architect",
        Cwd = targetCwd,
        ReturnTo = null,
        Status = "active"
    });

    var sourceKnownGoodStore = new KnownGoodRuntimeCheckpointStore(sourceBusRoot);
    var knownGood = sourceKnownGoodStore.WriteHealthy(
        sourceCwd,
        sourceBusRoot,
        null,
        nameof(DefaultCtuController),
        null,
        nameof(CodexTeamUp.AppServer.WrapperPipeAppServerClient));

    var operationStore = new RestartOperationStore(sourceBusRoot);
    var operation = operationStore.Create(
        requestedByAgentId: "ctu/architect",
        sourceCwd: sourceCwd,
        sourceBusRoot: sourceBusRoot,
        targetCwd: targetCwd,
        targetBusRoot: targetBusRoot,
        targetAgentId: "ctu/architect",
        fallbackCwd: null,
        fallbackBusRoot: null,
        continueTitle: "Continue after restart",
        continuePrompt: "Resume from restart handoff.",
        expectedTargetBranch: null,
        knownGoodCheckpointId: knownGood.Id);

    var targetExchange = new ExchangeStore(targetBusRoot);
    targetExchange.CreateRestartHandoff(operationStore.OperationPath(operation.Id), operation);

    var appServer = new FakeAppServerClient(
        $$"""
        {
          "data":[{"id":"thread-architect","name":"ctu/architect","cwd":{{JsonSerializer.Serialize(targetCwd)}}, "status":"idle"}]
        }
        """);

    var controller = new DefaultCtuController(targetBusRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    var verified = sourceKnownGoodStore.ReadVerified();
    True(verified is not null);
    Equal(knownGood.Id, verified!.Id);
    True(verified.IsVerified);
    Equal("startup_sweep:continuation_dispatched", verified.VerificationSource);
}

static void ControllerDeliveryLoopWaitsBeforeRetryingQueuedMessage()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Queued follow-up",
        "Retry this when callable.",
        "codexteamup",
        root,
        [],
        "ctu/architect");

    var appServer = new FakeAppServerClient(
        """{"data":[{"id":"thread-worker","name":"ctu/worker","cwd":"ROOT","status":{"type":"active"}}]}"""
            .Replace("ROOT", JsonEscaped(root), StringComparison.Ordinal),
        readThreadJson: """{"thread":{"id":"thread-worker","status":{"type":"active"},"turns":[{"id":"turn-1","status":"in_progress"}]}}""");
    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    True(bus.ListEvents(100).Any(evt => evt.Type == "task.delivery_failed" && evt.TaskId == task.Id));
    Equal("open", bus.FindTask(task.Id)?.Status);
    Equal(0, appServer.SentTurns.Count);

    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    Equal(0, appServer.SentTurns.Count);
    Equal("open", bus.FindTask(task.Id)?.Status);
    Equal(
        1,
        bus.ListEvents(100).Count(evt => evt.Type == "task.delivery_failed" && evt.TaskId == task.Id));
}

static void ControllerDeliveryLoopSupersedesOlderQueuedTasks()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        Status = "active"
    });

    var older = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Queued first",
        "Send first (stale).",
        "codexteamup",
        root,
        [],
        "ctu/architect");
    Thread.Sleep(10);
    var newer = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Queued latest",
        "Send latest only.",
        "codexteamup",
        root,
        [],
        "ctu/architect");

    var appServer = new ScriptedSendTurnAppServerClient(
        "thread-worker",
        "ctu/worker",
        root);
    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    Equal(1, appServer.SentTurns.Count);
    True(appServer.SentTurns[0].Message.Contains(newer.Id, StringComparison.Ordinal));
    Equal("open", bus.FindTask(newer.Id)?.Status);
    Equal("superseded", bus.FindTask(older.Id)?.Status);
    True(bus.ListEvents(100).Any(evt => evt.Type == "task.superseded" && evt.TaskId == older.Id));
}

static void ControllerDeliveryLoopRecoversStaleClaimedTask()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Queued stale claim",
        "Recover this claim.",
        "codexteamup",
        root,
        [],
        "ctu/architect");
    var claimed = bus.ClaimTask(task.Id, "ctu/worker");
    bus.UpdateTask(claimed.Id, existing => existing with
    {
        ClaimedAt = DateTimeOffset.Now - TimeSpan.FromMinutes(10)
    });

    var appServer = new ScriptedSendTurnAppServerClient(
        "thread-worker",
        "ctu/worker",
        root);
    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    var recovered = bus.FindTask(task.Id);
    Equal("open", recovered?.Status);
    Equal(1, recovered?.DeliveryAttempts);
    True(recovered?.LastDeliveryAttemptAt is not null);
    True(recovered?.LastDeliveryError is null);
    Equal(1, appServer.SentTurns.Count);
    Equal("thread-worker", appServer.SentTurns.Single().ThreadId);
    True(bus.ListEvents(200).Any(evt => evt.Type == "task.recovered_claimed" && evt.TaskId == task.Id));
}

static void ControllerResultNotifyRetryPersistsMetadataAndRetries()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        ThreadId = "thread-architect",
        Cwd = root,
        Status = "active"
    });
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Retry result notify",
        "Notify once when ready.",
        "codexteamup",
        root,
        [],
        "ctu/architect");
    bus.ClaimTask(task.Id, "ctu/worker");
    var result = bus.WriteResult(task.Id, "Done", "completed", "ctu/worker", "ctu/architect", null, [], []);

    var appServer = new ScriptedSendTurnAppServerClient(
        "thread-architect",
        "ctu/architect",
        root,
        sendTurnFailuresBeforeSuccess: 1);
    var registry = McpToolRegistry.CreateDefault(busRoot, appServer);
    var args = JsonSerializer.Deserialize<JsonElement>($$"""
    {
      "busRoot": {{JsonSerializer.Serialize(busRoot)}},
      "resultId": {{JsonSerializer.Serialize(result.Id)}},
      "toAgent": "ctu/architect"
    }
    """);

    _ = registry.InvokeAsync("bridge_notify_result", args).GetAwaiter().GetResult();
    var afterFirst = bus.FindResult(result.Id);
    Equal(1, afterFirst?.NotifyAttempts);
    Equal(0, appServer.SentTurns.Count);
    True(afterFirst?.LastNotifiedAt is null);
    True(!string.IsNullOrWhiteSpace(afterFirst?.LastNotifyError));

    bus.UpdateResult(result.Id, existing => existing with
    {
        LastNotifyAttemptAt = DateTimeOffset.Now - TimeSpan.FromSeconds(4),
        LastNotifyError = null
    });
    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    var afterSecond = bus.FindResult(result.Id);
    Equal(2, afterSecond?.NotifyAttempts);
    True(afterSecond?.LastNotifiedAt is not null);
    True(afterSecond?.LastNotifyError is null);
    Equal(1, appServer.SentTurns.Count);
    Equal("thread-architect", appServer.SentTurns.Single().ThreadId);
    True(bus.ListEvents(300).Any(evt =>
        (evt.Type == "result.notify_deferred" || evt.Type == "result.notify_failed")
        && evt.ResultId == result.Id));
    True(bus.ListEvents(300).Any(evt => evt.Type == "result.notified" && evt.ResultId == result.Id));
}

static void ControllerGuardianEvaluatesResultIntoContinuityState()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        ThreadId = "thread-architect",
        Cwd = root,
        Status = "active"
    });
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Continue slice",
        "Return completion state.",
        "codexteamup",
        root,
        [],
        "ctu/architect");
    bus.ClaimTask(task.Id, "ctu/worker");
    var result = bus.WriteResult(task.Id, "Done", "completed", "ctu/worker", "ctu/architect", null, [], []);

    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    var continuity = new ExecutionContinuityStateStore(busRoot);
    continuity.Initialize();
    var state = continuity.ReadLatest(task.Id);
    Equal(ExecutionContinuityStateKind.Completed, state?.State);
    Equal(false, state?.ShouldContinue);
    Equal(result.Id, state?.LastOutcomeRef);
    Equal(task.Id, state?.CorrelationId);
    True(bus.ListEvents(300).Any(evt => evt.Type == "continuity.state_created"));
    True(bus.ListEvents(300).Any(evt => evt.TaskId == task.Id && evt.Type.StartsWith("continuity.state")));
}

static void ControllerGuardianTreatsDoneResultStatusAsCompleted()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        ThreadId = "thread-architect",
        Cwd = root,
        Status = "active"
    });
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Return done state",
        "Return done state.",
        "codexteamup",
        root,
        [],
        "ctu/architect");
    bus.ClaimTask(task.Id, "ctu/worker");
    var result = bus.WriteResult(task.Id, "Done", "done", "ctu/worker", "ctu/architect", null, [], []);

    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    var continuity = new ExecutionContinuityStateStore(busRoot);
    continuity.Initialize();
    var state = continuity.ReadLatest(task.Id);
    Equal(ExecutionContinuityStateKind.Completed, state?.State);
    Equal(result.Id, state?.LastOutcomeRef);
}

static void ControllerGuardianSkipsUnchangedQueuedTaskObservation()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Queued follow-up",
        "Wait until the target is callable.",
        "codexteamup",
        root,
        [],
        "ctu/architect");

    var appServer = new FakeAppServerClient(
        """{"data":[{"id":"thread-worker","name":"ctu/worker","cwd":"ROOT","status":{"type":"active"}}]}"""
            .Replace("ROOT", JsonEscaped(root), StringComparison.Ordinal),
        readThreadJson: """{"thread":{"id":"thread-worker","status":{"type":"active"},"turns":[{"id":"turn-1","status":"in_progress"}]}}""");
    var controller = new DefaultCtuController(busRoot, appServer);

    controller.RunStartupSweepAsync().GetAwaiter().GetResult();
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    Equal(0, appServer.SentTurns.Count);
    Equal(
        1,
        bus.ListEvents(300)
            .Count(evt => evt.TaskId == task.Id && evt.Type == "continuity.guardian_evaluated"));
}

static void ControllerGuardianResumesDispatchFromContinuityState()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        ThreadId = "thread-architect",
        Cwd = root,
        Status = "active"
    });
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Resume from continuity",
        "Dispatch from continuity state on startup.",
        "codexteamup",
        root,
        [],
        "ctu/architect");
    var continuity = new ExecutionContinuityStateStore(busRoot);
    continuity.Initialize();
    continuity.Upsert(new ExecutionContinuityState
    {
        StateId = continuity.CreateStateId(),
        CorrelationId = task.Id,
        TaskChainId = task.Id,
        ShouldContinue = true,
        State = ExecutionContinuityStateKind.QueuedForDispatch,
        EnteredAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        GuardianAgentId = "ctu/continuity-reviewer",
        GuardianDisplayName = "Continuity Reviewer",
        LastOutcomeKind = "task",
        LastOutcomeRef = task.Id,
        NextActionKind = "task",
        NextActionRef = task.Id,
        CurrentTargetAgentId = "ctu/worker",
        CurrentTargetDisplayName = "ctu/worker",
        AttemptCount = 1,
        MaxAttempts = 8,
        LastAttemptAt = DateTimeOffset.UtcNow
    });

    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    Equal(1, appServer.SentTurns.Count);
    Equal("thread-worker", appServer.SentTurns.Single().ThreadId);
    True(bus.ListEvents(300).Any(evt => evt.Type == "continuity.dispatch_from_state"));
}

static void ControllerGuardianDoesNotRedispatchClaimedTask()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/guardian",
        "ctu/worker",
        "Already claimed",
        "Do not redispatch a claimed worker task.",
        "codexteamup",
        root,
        [],
        "ctu/guardian");
    bus.ClaimTask(task.Id, "ctu/worker");

    var continuity = new ExecutionContinuityStateStore(busRoot);
    continuity.Initialize();
    continuity.Upsert(new ExecutionContinuityState
    {
        StateId = continuity.CreateStateId(),
        CorrelationId = task.Id,
        TaskChainId = task.Id,
        ShouldContinue = true,
        State = ExecutionContinuityStateKind.QueuedForDispatch,
        EnteredAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        GuardianAgentId = "ctu/guardian",
        GuardianDisplayName = "ctu/guardian",
        LastOutcomeKind = "task",
        LastOutcomeRef = task.Id,
        NextActionKind = "task",
        NextActionRef = task.Id,
        CurrentTargetAgentId = "ctu/worker",
        CurrentTargetDisplayName = "ctu/worker",
        AttemptCount = 1,
        MaxAttempts = 8,
        LastAttemptAt = DateTimeOffset.UtcNow
    });

    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    Equal(0, appServer.SentTurns.Count);
    var state = continuity.ReadLatest(task.Id);
    Equal(ExecutionContinuityStateKind.WaitingOnWorker, state?.State);
    Equal(false, state?.ShouldContinue);
    True(bus.ListEvents(300).Any(evt => evt.Type == "continuity.dispatch_satisfied"));
}

static void ControllerDeliveryLoopFailsOpenTaskForRetiredAgent()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/wrapper",
        Role = "Wrapper",
        DisplayName = "ctu/wrapper",
        ThreadId = "thread-wrapper",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "retired"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/wrapper",
        "Should not dispatch",
        "Do not deliver work to retired agents.",
        "codexteamup",
        root,
        [],
        "ctu/architect");

    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    Equal(0, appServer.SentTurns.Count);
    Equal(0, bus.ListTasks(status: "open").Count);
    Equal(1, bus.ListTasks(status: "failed").Count(taskItem => taskItem.Id == task.Id));
    var result = bus.WaitForResult(task.Id, TimeSpan.FromMilliseconds(50));
    Equal("failed", result?.Status);
    Equal("ctu/controller", result?.From);
    True(bus.ListEvents(300).Any(evt => evt.TaskId == task.Id && evt.Type == "task.dispatch_blocked_retired_agent"));
}

static void ControllerGuardianResumesNotifyFromContinuityState()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        ThreadId = "thread-architect",
        Cwd = root,
        Status = "active"
    });
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Resume notify",
        "Notify from continuity state.",
        "codexteamup",
        root,
        [],
        "ctu/architect");
    bus.ClaimTask(task.Id, "ctu/worker");
    var result = bus.WriteResult(task.Id, "Done", "completed", "ctu/worker", "ctu/architect", null, [], []);
    bus.UpdateResult(result.Id, existing => existing with
    {
        NotifyAttempts = 3,
        LastNotifyAttemptAt = DateTimeOffset.Now,
        LastNotifiedAt = null
    });

    var continuity = new ExecutionContinuityStateStore(busRoot);
    continuity.Initialize();
    continuity.Upsert(new ExecutionContinuityState
    {
        StateId = continuity.CreateStateId(),
        CorrelationId = task.Id,
        TaskChainId = task.Id,
        ShouldContinue = true,
        State = ExecutionContinuityStateKind.NotifyRetryPending,
        EnteredAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        GuardianAgentId = "ctu/continuity-reviewer",
        GuardianDisplayName = "Continuity Reviewer",
        LastOutcomeKind = "completed",
        LastOutcomeRef = result.Id,
        NextActionKind = "result",
        NextActionRef = result.Id,
        CurrentTargetAgentId = "ctu/architect",
        CurrentTargetDisplayName = "ctu/architect",
        AttemptCount = 1,
        MaxAttempts = 8,
        LastAttemptAt = DateTimeOffset.UtcNow
    });

    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    Equal(1, appServer.SentTurns.Count);
    Equal("thread-architect", appServer.SentTurns.Single().ThreadId);
    True(bus.ListEvents(300).Any(evt => evt.Type == "continuity.notify_from_state"));
}

static void ControllerGuardianBlocksOnOrphanDispatchFromContinuityState()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        ThreadId = "thread-architect",
        Cwd = root,
        Status = "active"
    });
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "active"
    });

    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var continuity = new ExecutionContinuityStateStore(busRoot);
    continuity.Initialize();
    continuity.Upsert(new ExecutionContinuityState
    {
        StateId = continuity.CreateStateId(),
        CorrelationId = "orphan-task-id",
        TaskChainId = "orphan-task-id",
        ShouldContinue = true,
        State = ExecutionContinuityStateKind.QueuedForDispatch,
        EnteredAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        GuardianAgentId = "ctu/continuity-reviewer",
        GuardianDisplayName = "Continuity Reviewer",
        LastOutcomeKind = "task",
        LastOutcomeRef = "orphan-task-id",
        NextActionKind = "task",
        NextActionRef = "missing-task-id",
        CurrentTargetAgentId = "ctu/worker",
        CurrentTargetDisplayName = "ctu/worker",
        AttemptCount = 1,
        MaxAttempts = 8,
        LastAttemptAt = DateTimeOffset.UtcNow
    });

    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    var state = continuity.ReadLatest("orphan-task-id");
    Equal(ExecutionContinuityStateKind.BlockedNeedsHuman, state?.State);
    Equal("ctu/worker", state?.BlockingOwner);
    True(!string.IsNullOrWhiteSpace(state?.BlockingReason));
    True(!string.IsNullOrWhiteSpace(state?.LastError));
    True(bus.ListEvents(300).Any(evt => evt.Type == "continuity.terminal_recorded" && evt.TaskId == "orphan-task-id"));
    Equal(0, appServer.SentTurns.Count);
}

static void ControllerGuardianBlocksOnOrphanNotifyFromContinuityState()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        ThreadId = "thread-architect",
        Cwd = root,
        Status = "active"
    });
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Orphan notify",
        "Notify continuation does not have matching result.",
        "codexteamup",
        root,
        [],
        "ctu/architect");

    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var continuity = new ExecutionContinuityStateStore(busRoot);
    continuity.Initialize();
    continuity.Upsert(new ExecutionContinuityState
    {
        StateId = continuity.CreateStateId(),
        CorrelationId = task.Id,
        TaskChainId = task.Id,
        ShouldContinue = true,
        State = ExecutionContinuityStateKind.NotifyRetryPending,
        EnteredAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        GuardianAgentId = "ctu/continuity-reviewer",
        GuardianDisplayName = "Continuity Reviewer",
        LastOutcomeKind = "result",
        LastOutcomeRef = "missing-result-id",
        NextActionKind = "result",
        NextActionRef = "missing-result-id",
        CurrentTargetAgentId = "ctu/architect",
        CurrentTargetDisplayName = "ctu/architect",
        AttemptCount = 1,
        MaxAttempts = 8,
        LastAttemptAt = DateTimeOffset.UtcNow
    });

    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    var state = continuity.ReadLatest(task.Id);
    Equal(ExecutionContinuityStateKind.BlockedNeedsHuman, state?.State);
    Equal("ctu/architect", state?.BlockingOwner);
    True(!string.IsNullOrWhiteSpace(state?.BlockingReason));
    True(!string.IsNullOrWhiteSpace(state?.LastError));
    True(bus.ListEvents(300).Any(evt => evt.Type == "continuity.terminal_recorded" && evt.TaskId == task.Id));
    Equal(0, appServer.SentTurns.Count);
}

static void ControllerGuardianResumesExternalContinuityViaCorrelation()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        ThreadId = "thread-architect",
        Cwd = root,
        Status = "active"
    });
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Resume via external",
        "Dispatch from external correlation on startup.",
        "codexteamup",
        root,
        [],
        "ctu/architect");
    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var continuity = new ExecutionContinuityStateStore(busRoot);
    continuity.Initialize();
    continuity.Upsert(new ExecutionContinuityState
    {
        StateId = continuity.CreateStateId(),
        CorrelationId = "resume-orphan-correlation",
        TaskChainId = task.Id,
        ShouldContinue = true,
        State = ExecutionContinuityStateKind.ResumePendingExternal,
        EnteredAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        GuardianAgentId = "ctu/continuity-reviewer",
        GuardianDisplayName = "Continuity Reviewer",
        LastOutcomeKind = "resume_pending_external",
        LastOutcomeRef = "resume-orphan-correlation",
        NextActionKind = "task",
        NextActionRef = "resume-orphan-correlation",
        CurrentTargetAgentId = "ctu/architect",
        CurrentTargetDisplayName = "ctu/architect",
        AttemptCount = 1,
        MaxAttempts = 8,
        LastAttemptAt = DateTimeOffset.UtcNow,
        ResumeCorrelationId = task.Id
    });

    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    Equal(1, appServer.SentTurns.Count);
    Equal("thread-worker", appServer.SentTurns.Single().ThreadId);
    True(bus.ListEvents(300).Any(evt => evt.Type == "continuity.dispatch_from_state"));
}

static void ControllerGuardianRespectsConfiguredContinuityPolicy()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        ThreadId = "thread-architect",
        Cwd = root,
        Status = "active"
    });
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Continue slice",
        "Continue with policy.",
        "codexteamup",
        root,
        [],
        "ctu/architect");

    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var policy = new ReloadableCtuControllerPolicy();
    var policyFile = Path.Combine(root, "ctu-controller-policy.json");
    File.WriteAllText(policyFile, JsonSerializer.Serialize(new CtuControllerPolicy(
        TeamSendMessageDefaultDispatchMode: "enqueue",
        WakeupTimeoutSeconds: 1,
        WaitResultTimeoutCapSeconds: 10,
        EnsureThreadNameBeforePrime: true,
        PrimePromptStartsWithAgentId: true,
        ContinuityGuardianAgentId: "ctu/continuity-reviewer",
        ContinuityGuardianDisplayName: "Continuity Reviewer",
        ContinuityStateDirectory: ".ctu-continuity"), JsonFile.Options));
    policy.Reload(policyFile);
    var controller = new DefaultCtuController(busRoot, appServer, policy);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    var continuity = new ExecutionContinuityStateStore(busRoot, ".ctu-continuity");
    continuity.Initialize();
    var state = continuity.ReadLatest(task.Id);
    Equal("ctu/continuity-reviewer", state?.GuardianAgentId);
    Equal("Continuity Reviewer", state?.GuardianDisplayName);
    Equal(Path.Combine(root, ".ctu-continuity"), continuity.StatesDirectory);
}

static void ControllerGuardianEmitsCanonicalContinuityDecisionEvents()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        ThreadId = "thread-architect",
        Cwd = root,
        Status = "active"
    });
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Repeat attempt",
        "Trigger dispatch retry metadata.",
        "codexteamup",
        root,
        [],
        "ctu/architect");

    var appServer = new ScriptedSendTurnAppServerClient(
        "thread-worker",
        "ctu/worker",
        root,
        sendTurnFailuresBeforeSuccess: 5);
    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    var continuity = new ExecutionContinuityStateStore(busRoot);
    var state = continuity.ReadLatest(task.Id);
    Equal(1, state?.AttemptCount);

    var events = bus.ListEvents(300).Where(evt => evt.TaskId == task.Id).ToList();
    True(events.Any(evt => evt.Type == "continuity.guardian_evaluated"));
    True(events.Any(evt => evt.Type == "continuity.dispatch_requested"));
    True(events.All(evt => evt.Type != "continuity.dispatch_retry_scheduled"));
}

static void ControllerGuardianBlocksOnExhaustedNotifyRetries()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/architect",
        Role = "Architect",
        ThreadId = "thread-architect",
        Cwd = root,
        Status = "active"
    });
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/worker",
        Role = "Worker",
        DisplayName = "ctu/worker",
        ThreadId = "thread-worker",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/worker",
        "Exhaust notify",
        "Cannot notify.",
        "codexteamup",
        root,
        [],
        "ctu/architect");
    var result = bus.WriteResult(task.Id, "Done", "completed", "ctu/worker", "ctu/architect", null, [], []);
    bus.UpdateResult(result.Id, existing => existing with
    {
        NotifyAttempts = 8,
        LastNotifiedAt = null,
        LastNotifyError = "notify exhausted",
        LastNotifyAttemptAt = DateTimeOffset.Now
    });

    var appServer = new FakeAppServerClient("""{"data":[]}""");
    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    var continuity = new ExecutionContinuityStateStore(busRoot);
    var state = continuity.ReadLatest(task.Id);
    Equal(ExecutionContinuityStateKind.BlockedNeedsHuman, state?.State);
    Equal(result.Id, state?.LastOutcomeRef);
    Equal("ctu/architect", state?.BlockingOwner);
    True(!string.IsNullOrWhiteSpace(state?.BlockingReason));
    True(!string.IsNullOrWhiteSpace(state?.LastError));
    True(bus.ListEvents(300).Any(evt => evt.Type == "continuity.terminal_recorded" && evt.TaskId == task.Id));
}

static void ControllerAgentContinuationWakesIdleOwner()
{
    var root = NewTestDirectory();
    var busRoot = Path.Combine(root, ".codexteamup", "agentbus");
    var bus = new AgentBusStore(busRoot);
    bus.Initialize();
    bus.RegisterAgent(new AgentDefinition
    {
        Id = "ctu/designer",
        Role = "Designer",
        DisplayName = "ctu/designer",
        ThreadId = "thread-designer",
        Cwd = root,
        ReturnTo = "ctu/architect",
        Status = "active"
    });

    var task = bus.CreateTask(
        "ctu/architect",
        "ctu/designer",
        "Design slice",
        "Continue until self-reported done.",
        "codexteamup",
        root,
        [],
        "ctu/architect");
    bus.ClaimTask(task.Id, "ctu/designer");
    var result = bus.WriteResult(
        task.Id,
        "Need one more pass after local cooldown.",
        "completed",
        "ctu/designer",
        "ctu/architect",
        null,
        [],
        [],
        outcome: "self_continue",
        continuation: new AgentBusContinuationRequest
        {
            Owner = "ctu/designer",
            WakeAfterSeconds = 0,
            DedupeKey = "design-self-pass",
            Reason = "Continue the design review loop.",
            MaxAttempts = 2
        });
    Equal("self_continue", result.Outcome);
    Equal(1, bus.ListContinuations("ctu/designer", "open").Count);

    var appServer = new ScriptedSendTurnAppServerClient(
        "thread-designer",
        "ctu/designer",
        root);
    var controller = new DefaultCtuController(busRoot, appServer);
    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    Equal(1, appServer.SentTurns.Count);
    Equal("thread-designer", appServer.SentTurns.Single().ThreadId);
    Equal(1, bus.ListTasks("ctu/designer", "open").Count);
    Equal(0, bus.ListContinuations("ctu/designer", "open").Count);
    Equal(1, bus.ListContinuations("ctu/designer", "done").Count);
    True(bus.ListEvents(300).Any(evt => evt.Type == "continuation.wakeup_enqueued"));

    controller.RunStartupSweepAsync().GetAwaiter().GetResult();

    Equal(1, appServer.SentTurns.Count);
}

static void SeedRestartCheckout(string checkout)
{
    Directory.CreateDirectory(Path.Combine(checkout, "scripts"));
    File.WriteAllText(
        Path.Combine(checkout, "scripts", "start-codexteamup.ps1"),
        @"# synthetic startup script for deterministic tests");
}

static string NewTestDirectory()
{
    var baseRoot = Environment.GetEnvironmentVariable("CTU_TEST_RUN_ROOT");
    if (string.IsNullOrWhiteSpace(baseRoot))
    {
        baseRoot = Path.Combine(Path.GetTempPath(), "codexteamup-test-runs");
    }

    var root = Path.Combine(baseRoot, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
}

static string TestRepoRoot()
{
    var fromEnv = Environment.GetEnvironmentVariable("CTU_TEST_REPO_ROOT");
    if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(Path.Combine(fromEnv, "CodexTeamUp.slnx")))
    {
        return fromEnv;
    }

    var current = Environment.CurrentDirectory;
    if (File.Exists(Path.Combine(current, "CodexTeamUp.slnx")))
    {
        return current;
    }

    throw new InvalidOperationException("CTU_TEST_REPO_ROOT must point to the CodexTeamUp repository root.");
}

static string JsonEscaped(string value)
{
    return value.Replace("\\", "\\\\", StringComparison.Ordinal);
}

static string ExtractTaskIdFromWakeMessage(string message)
{
    var start = message.IndexOf("task-", StringComparison.Ordinal);
    if (start < 0)
    {
        throw new InvalidOperationException($"No task id found in wake message: {message}");
    }

    var end = start;
    while (end < message.Length && !char.IsWhiteSpace(message[end]) && message[end] != '.')
    {
        end++;
    }

    return message[start..end];
}

static void True(bool condition, [CallerArgumentExpression(nameof(condition))] string? expression = null)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Expected condition to be true: {expression}.");
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

sealed class FakeAppServerClient(
    string threadListJson,
    Action<string, string, string?>? onSendTurn = null,
    string? readThreadJson = null,
    string? readThreadError = null,
    string? resumeThreadError = null,
    int nameSetFailuresBeforeSuccess = 0,
    TimeSpan? sendTurnDelay = null) : IAppServerClient
{
    private int _nameSetFailuresRemaining = nameSetFailuresBeforeSuccess;

    public List<(string ThreadId, string Message, string? Cwd, AppServerAgentSettings? Settings)> SentTurns { get; } = [];
    public List<(string ThreadId, string Name)> NamedThreads { get; } = [];
    public List<string> ArchivedThreads { get; } = [];

    public Task<AppServerCallResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, "{}", null));
    }

    public Task<AppServerCallResult> CallAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        if (string.Equals(method, "thread/name/set", StringComparison.Ordinal) && parameters is not null)
        {
            if (_nameSetFailuresRemaining > 0)
            {
                _nameSetFailuresRemaining -= 1;
                return Task.FromResult(new AppServerCallResult(false, null, "thread not found: created-thread"));
            }

            var json = JsonSerializer.Serialize(parameters);
            using var doc = JsonDocument.Parse(json);
            NamedThreads.Add((
                doc.RootElement.GetProperty("threadId").GetString()!,
                doc.RootElement.GetProperty("name").GetString()!));
        }
        else if (string.Equals(method, "thread/archive", StringComparison.Ordinal) && parameters is not null)
        {
            var json = JsonSerializer.Serialize(parameters);
            using var doc = JsonDocument.Parse(json);
            ArchivedThreads.Add(doc.RootElement.GetProperty("threadId").GetString()!);
        }

        return Task.FromResult(new AppServerCallResult(true, "{}", null));
    }

    public Task<AppServerCallResult> ListThreadsAsync(string? cwd, int limit, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, threadListJson, null));
    }

    public Task<AppServerCallResult> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(readThreadError))
        {
            return Task.FromResult(new AppServerCallResult(false, null, readThreadError));
        }

        return Task.FromResult(new AppServerCallResult(true, readThreadJson ?? "{}", null));
    }

    public Task<AppServerCallResult> StartThreadAsync(
        string cwd,
        string? name,
        string? role,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, """{"id":"created-thread"}""", null));
    }

    public Task<AppServerCallResult> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(resumeThreadError))
        {
            return Task.FromResult(new AppServerCallResult(false, null, resumeThreadError));
        }

        return Task.FromResult(new AppServerCallResult(true, "{}", null));
    }

    public async Task<AppServerCallResult> SendTurnAsync(
        string threadId,
        string message,
        string? cwd = null,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        if (sendTurnDelay is not null)
        {
            await Task.Delay(sendTurnDelay.Value, cancellationToken).ConfigureAwait(false);
        }

        SentTurns.Add((threadId, message, cwd, settings));
        onSendTurn?.Invoke(threadId, message, cwd);
        return new AppServerCallResult(true, """{"turn":{"id":"turn-fake","status":"inProgress"}}""", null);
    }

    public Task<AppServerCallResult> ListTurnsAsync(string threadId, string sortDirection = "asc", int limit = 50, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, "{}", null));
    }

    public Task<TurnWaitResult> WaitForTurnAsync(string threadId, string turnId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TurnWaitResult(true, "completed", null, "{}"));
    }
}

/// A test app-server client that can simulate transient send failures before succeeding.
sealed class ScriptedSendTurnAppServerClient : IAppServerClient
{
    private int _sendTurnFailuresRemaining;
    private readonly IReadOnlyList<string> _threadStatuses;
    private int _listThreadsCallIndex;
    private readonly string _threadId;
    private readonly string _threadAgentId;
    private readonly string _cwd;

    public List<(string ThreadId, string Message, string? Cwd, AppServerAgentSettings? Settings)> SentTurns { get; } = [];
    public List<(string ThreadId, string Name)> NamedThreads { get; } = [];
    public List<string> ArchivedThreads { get; } = [];

    public ScriptedSendTurnAppServerClient(
        string threadId,
        string threadAgentId,
        string cwd,
        int sendTurnFailuresBeforeSuccess = 0,
        IReadOnlyList<string>? threadStatuses = null)
    {
        _sendTurnFailuresRemaining = sendTurnFailuresBeforeSuccess;
        _threadStatuses = threadStatuses is null || threadStatuses.Count == 0
            ? ["idle"]
            : threadStatuses;
        _threadId = threadId;
        _threadAgentId = threadAgentId;
        _cwd = cwd;
    }

    public Task<AppServerCallResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, "{}", null));
    }

    public Task<AppServerCallResult> CallAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        if (string.Equals(method, "thread/name/set", StringComparison.Ordinal) && parameters is not null)
        {
            var json = JsonSerializer.Serialize(parameters);
            using var doc = JsonDocument.Parse(json);
            NamedThreads.Add((
                doc.RootElement.GetProperty("threadId").GetString()!,
                doc.RootElement.GetProperty("name").GetString()!));
        }
        else if (string.Equals(method, "thread/archive", StringComparison.Ordinal) && parameters is not null)
        {
            var json = JsonSerializer.Serialize(parameters);
            using var doc = JsonDocument.Parse(json);
            ArchivedThreads.Add(doc.RootElement.GetProperty("threadId").GetString()!);
        }

        return Task.FromResult(new AppServerCallResult(true, "{}", null));
    }

    public Task<AppServerCallResult> ListThreadsAsync(string? cwd, int limit, CancellationToken cancellationToken = default)
    {
        var status = _threadStatuses[
            Math.Min(_listThreadsCallIndex, _threadStatuses.Count - 1)];
        _listThreadsCallIndex++;
        return Task.FromResult(new AppServerCallResult(true, ThreadPayload(_threadId, _threadAgentId, _cwd, status), null));
    }

    public Task<AppServerCallResult> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, "{}", null));
    }

    public Task<AppServerCallResult> StartThreadAsync(string cwd, string? name, string? role, AppServerAgentSettings? settings = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, """{"id":"created-thread"}""", null));
    }

    public Task<AppServerCallResult> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, "{}", null));
    }

    public Task<AppServerCallResult> SendTurnAsync(string threadId, string message, string? cwd = null, AppServerAgentSettings? settings = null, CancellationToken cancellationToken = default)
    {
        if (_sendTurnFailuresRemaining > 0)
        {
            _sendTurnFailuresRemaining -= 1;
            return Task.FromResult(new AppServerCallResult(false, null, "thread unavailable"));
        }

        SentTurns.Add((threadId, message, cwd, settings));
        return Task.FromResult(new AppServerCallResult(true, """{"turn":{"id":"turn-fake","status":"inProgress"}}""", null));
    }

    public Task<AppServerCallResult> ListTurnsAsync(string threadId, string sortDirection = "asc", int limit = 50, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, "{}", null));
    }

    public Task<TurnWaitResult> WaitForTurnAsync(string threadId, string turnId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TurnWaitResult(true, "completed", null, "{}"));
    }

    private static string ThreadPayload(string threadId, string threadAgentId, string cwd, string status)
    {
        return $$"""
        {
          "data":[{"id":{{JsonSerializer.Serialize(threadId)}}, "name":{{JsonSerializer.Serialize(threadAgentId)}}, "cwd":{{JsonSerializer.Serialize(cwd)}}, "status":{{JsonSerializer.Serialize(status)}}}]
        }
        """;
    }
}

sealed class ThrowingAppServerClient : IAppServerClient
{
    public Task<AppServerCallResult> ProbeAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("probe failed");

    public Task<AppServerCallResult> CallAsync(string method, object? parameters, CancellationToken cancellationToken = default) => throw new InvalidOperationException("call failed");

    public Task<AppServerCallResult> ListThreadsAsync(string? cwd, int limit, CancellationToken cancellationToken = default) => throw new InvalidOperationException("list failed");

    public Task<AppServerCallResult> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken = default) => throw new InvalidOperationException("read failed");

    public Task<AppServerCallResult> StartThreadAsync(string cwd, string? name, string? role, AppServerAgentSettings? settings = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("start failed");

    public Task<AppServerCallResult> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("resume failed");

    public Task<AppServerCallResult> SendTurnAsync(string threadId, string message, string? cwd = null, AppServerAgentSettings? settings = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("turn failed");

    public Task<AppServerCallResult> ListTurnsAsync(string threadId, string sortDirection = "asc", int limit = 50, CancellationToken cancellationToken = default) => throw new InvalidOperationException("turns failed");

    public Task<TurnWaitResult> WaitForTurnAsync(string threadId, string turnId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new InvalidOperationException("wait failed");
}

sealed class RecordingCallAppServerClient : IAppServerClient
{
    public List<(string Method, object? Parameters)> Calls { get; } = [];

    public Task<AppServerCallResult> ProbeAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new AppServerCallResult(true, "{}", null));

    public Task<AppServerCallResult> CallAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        Calls.Add((method, parameters));
        return Task.FromResult(new AppServerCallResult(true, "{}", null));
    }

    public Task<AppServerCallResult> ListThreadsAsync(string? cwd, int limit, CancellationToken cancellationToken = default)
        => Task.FromResult(new AppServerCallResult(true, """{"data":[]}""", null));

    public Task<AppServerCallResult> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken = default)
        => Task.FromResult(new AppServerCallResult(true, "{}", null));

    public Task<AppServerCallResult> StartThreadAsync(string cwd, string? name, string? role, AppServerAgentSettings? settings = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new AppServerCallResult(true, """{"id":"thread-1"}""", null));

    public Task<AppServerCallResult> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default)
        => Task.FromResult(new AppServerCallResult(true, "{}", null));

    public Task<AppServerCallResult> SendTurnAsync(string threadId, string message, string? cwd = null, AppServerAgentSettings? settings = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new AppServerCallResult(true, """{"turn":{"id":"turn-1"}}""", null));

    public Task<AppServerCallResult> ListTurnsAsync(string threadId, string sortDirection = "asc", int limit = 50, CancellationToken cancellationToken = default)
        => Task.FromResult(new AppServerCallResult(true, "{}", null));

    public Task<TurnWaitResult> WaitForTurnAsync(string threadId, string turnId, TimeSpan timeout, CancellationToken cancellationToken = default)
        => Task.FromResult(new TurnWaitResult(true, "completed", null, "{}"));
}

public sealed class TestAppServerClientPlugin : IAppServerClientPlugin
{
    public string Name => "test";

    public string Version => "1.0.0";

    public IAppServerClient Create(AppServerClientPluginContext context)
    {
        return new TestPluginAppServerClient();
    }
}

public sealed class TestPluginAppServerClient : IAppServerClient
{
    public Task<AppServerCallResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, """{"plugin":"test"}""", null));
    }

    public Task<AppServerCallResult> CallAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, """{"plugin":"test"}""", null));
    }

    public Task<AppServerCallResult> ListThreadsAsync(string? cwd, int limit, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, """{"data":[]}""", null));
    }

    public Task<AppServerCallResult> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, """{"thread":{"id":"test"}}""", null));
    }

    public Task<AppServerCallResult> StartThreadAsync(
        string cwd,
        string? name,
        string? role,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, """{"thread":{"id":"test"}}""", null));
    }

    public Task<AppServerCallResult> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, """{"thread":{"id":"test"}}""", null));
    }

    public Task<AppServerCallResult> SendTurnAsync(
        string threadId,
        string message,
        string? cwd = null,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, """{"turn":{"id":"test-turn"}}""", null));
    }

    public Task<AppServerCallResult> ListTurnsAsync(string threadId, string sortDirection = "asc", int limit = 50, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, """{"turns":[]}""", null));
    }

    public Task<TurnWaitResult> WaitForTurnAsync(string threadId, string turnId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TurnWaitResult(true, "completed", null, """{"thread":{"id":"test"}}"""));
    }
}
