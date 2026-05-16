using CodexTeamUp.AgentBus;
using CodexTeamUp.AppServer;
using CodexTeamUp.CodexWrapper;
using CodexTeamUp.Core;
using CodexTeamUp.Mcp;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

var tests = new (string Name, Func<Task> Body)[]
{
    ("AgentBus lifecycle", () => Task.Run(AgentBusLifecycle)),
    ("AgentBus normalizes agent names", () => Task.Run(AgentBusNormalizesAgentNames)),
    ("AgentBus registry and result wait", () => Task.Run(AgentBusRegistryAndResultWait)),
    ("Codex state reader parses rollout metadata", () => Task.Run(CodexStateReaderParsesRollouts)),
    ("SafeText redacts obvious secrets", () => Task.Run(SafeTextRedactsSecrets)),
    ("Schema service extracts interesting methods", () => Task.Run(SchemaServiceExtractsMethods)),
    ("Wrapper protocol rewrites turn list sorting", () => Task.Run(WrapperProtocolRewritesTurnListSorting)),
    ("Wrapper protocol stamps live turn started notifications", () => Task.Run(WrapperProtocolStampsLiveTurnStartedNotifications)),
    ("Wrapper protocol identifies bridge responses", () => Task.Run(WrapperProtocolIdentifiesBridgeResponses)),
    ("Wrapper pipe client parses JSON-RPC result", WrapperPipeClientParsesResult),
    ("Wrapper pipe send turn resumes without historical turns", WrapperPipeSendTurnResumesWithoutHistoricalTurns),
    ("Wrapper pipe sends runtime settings", WrapperPipeSendsRuntimeSettings),
    ("MCP registry exposes core tools", () => Task.Run(McpRegistryExposesCoreTools)),
    ("MCP registry derives bus root from cwd", () => Task.Run(McpRegistryDerivesBusRootFromCwd)),
    ("MCP registry writes result file metadata", () => Task.Run(McpRegistryWritesResultFileMetadata)),
    ("MCP registry ensures explicit ctu agents", () => Task.Run(McpRegistryEnsuresExplicitCtuAgents)),
    ("MCP registry primes agents without fallback tasks", () => Task.Run(McpRegistryPrimesAgentsWithoutFallbackTasks)),
    ("MCP registry persists agent runtime settings", () => Task.Run(McpRegistryPersistsAgentRuntimeSettings)),
    ("MCP registry sends strict task wakeup", () => Task.Run(McpRegistrySendsStrictTaskWakeup)),
    ("MCP registry waits for AgentBus result", () => Task.Run(McpRegistryWaitsForAgentBusResult)),
    ("MCP team send message waits for result", () => Task.Run(McpTeamSendMessageWaitsForResult)),
    ("MCP registry recreates stale ctu agent threads", () => Task.Run(McpRegistryRecreatesStaleCtuAgentThreads)),
    ("MCP registry notifies result through service path", () => Task.Run(McpRegistryNotifiesResultThroughServicePath)),
    ("MCP registry defers result notify while target active", () => Task.Run(McpRegistryDefersResultNotifyWhileTargetActive)),
    ("Agent thread matcher binds named team threads", () => Task.Run(AgentThreadMatcherBindsNamedTeamThreads)),
    ("AgentBus dashboard creates snapshot", () => Task.Run(AgentBusDashboardCreatesSnapshot)),
    ("AgentBus dashboard renders communication", () => Task.Run(AgentBusDashboardRendersCommunication))
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

static void McpRegistryExposesCoreTools()
{
    var root = NewTestDirectory();
    var registry = McpToolRegistry.CreateDefault(Path.Combine(root, ".codexteamup/agentbus"), new WrapperPipeAppServerClient("unused"));
    True(registry.ToolNames.Contains("agentbus_init"));
    True(registry.ToolNames.Contains("agentbus_wait_result"));
    True(registry.ToolNames.Contains("bridge_dispatch_task"));
    True(registry.ToolNames.Contains("bridge_notify_result"));
    True(registry.ToolNames.Contains("team_discover_agents"));
    True(registry.ToolNames.Contains("team_send_message"));
    True(registry.ToolNames.Contains("team_dashboard_export"));
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
      "nextSuggestedAction": "Review in browser"
    }
    """);

    _ = registry.InvokeAsync("agentbus_write_result", args).GetAwaiter().GetResult();
    var result = bus.ListResults().Single();
    Equal("app/index.html", result.ChangedFiles[0]);
    Equal("docs/note.md", result.ChangedFiles[1]);
    Equal("manual browser check", result.Tests.Single());
    Equal("app/index.html", result.Artifacts.Single());
    Equal("Review in browser", result.NextSuggestedAction);
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
    True(prompt.Contains("do not create a replacement task", StringComparison.Ordinal));
    True(prompt.Contains("do not reconstruct a task from chat text", StringComparison.Ordinal));
    Equal("medium", appServer.SentTurns[0].Settings?.ReasoningEffort);
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
      "project": "codexteamup"
    }
    """);

    _ = registry.InvokeAsync("team_send_message", args).GetAwaiter().GetResult();
    Equal(1, appServer.SentTurns.Count);
    var wake = appServer.SentTurns[0].Message;
    True(wake.Contains("Verify that the task file exists", StringComparison.Ordinal));
    True(wake.Contains("do not create a replacement task", StringComparison.Ordinal));
    True(wake.Contains("do not write a result", StringComparison.Ordinal));
    Equal("gpt-5.4-mini", appServer.SentTurns[0].Settings?.Model);
    Equal("low", appServer.SentTurns[0].Settings?.ReasoningEffort);
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

static void McpRegistryDefersResultNotifyWhileTargetActive()
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

    var appServer = new FakeAppServerClient("""{"data":[{"id":"thread-foo","name":"ctu/foo","cwd":"ROOT","status":{"type":"active"}}]}"""
        .Replace("ROOT", JsonEscaped(root), StringComparison.Ordinal));
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
    True(deferredEvent.Message?.Contains("targetStatus=active", StringComparison.Ordinal) == true);
    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonFile.Options));
    True(doc.RootElement.GetProperty("wakeup").GetProperty("deferred").GetBoolean());
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

static void AgentBusDashboardRendersCommunication()
{
    var root = NewTestDirectory();
    var bus = new AgentBusStore(Path.Combine(root, ".codexteamup/agentbus"));
    bus.Initialize();
    var task = bus.CreateTask("ctu/architect", "ctu/web", "Build editor", "Implement slice", "codexteamup", root, ["web/"], "ctu/architect");
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
    True(snapshot.Tasks.Single().Title.Contains("Build editor", StringComparison.Ordinal));
    True(snapshot.Results.Single().Summary.Contains("Implemented", StringComparison.Ordinal));
    True(snapshot.Events.Count > 0);
}

static string NewTestDirectory()
{
    var root = Path.Combine(Environment.CurrentDirectory, ".ctu", "test-runs", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
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

static void True(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Expected condition to be true.");
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

sealed class FakeAppServerClient(string threadListJson, Action<string, string, string?>? onSendTurn = null) : IAppServerClient
{
    public List<(string ThreadId, string Message, string? Cwd, AppServerAgentSettings? Settings)> SentTurns { get; } = [];
    public List<(string ThreadId, string Name)> NamedThreads { get; } = [];

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

        return Task.FromResult(new AppServerCallResult(true, "{}", null));
    }

    public Task<AppServerCallResult> ListThreadsAsync(string? cwd, int limit, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, threadListJson, null));
    }

    public Task<AppServerCallResult> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppServerCallResult(true, "{}", null));
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
        return Task.FromResult(new AppServerCallResult(true, "{}", null));
    }

    public Task<AppServerCallResult> SendTurnAsync(
        string threadId,
        string message,
        string? cwd = null,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        SentTurns.Add((threadId, message, cwd, settings));
        onSendTurn?.Invoke(threadId, message, cwd);
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
}
