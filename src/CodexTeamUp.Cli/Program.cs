using CodexTeamUp.AgentBus;
using CodexTeamUp.AppServer;
using CodexTeamUp.Core;
using System.Text.Json;

return await Cli.RunAsync(args).ConfigureAwait(false);

internal static class Cli
{
    public static async Task<int> RunAsync(string[] rawArgs)
    {
        var args = Args.Parse(rawArgs);
        if (args.Positionals.Count > 0 && string.Equals(args.Positionals[0], "ctu", StringComparison.OrdinalIgnoreCase))
        {
            args = args.SkipFirstPositional();
        }

        if (args.Positionals.Count == 0
            || args.Has("help")
            || args.Has("h")
            || string.Equals(args.Positionals[0], "help", StringComparison.OrdinalIgnoreCase))
        {
            WriteUsage();
            return 0;
        }

        try
        {
            return args.Positionals[0].ToLowerInvariant() switch
            {
                "codex" => await RunCodexAsync(args).ConfigureAwait(false),
                "wrapper" => await RunWrapperAsync(args).ConfigureAwait(false),
                "doctor" => await RunDoctorAsync(args).ConfigureAwait(false),
                "threads" => await RunThreadsAsync(args).ConfigureAwait(false),
                "turns" => await RunTurnsAsync(args).ConfigureAwait(false),
                "bus" => RunBus(args),
                "dispatch" => await RunDispatchAsync(args).ConfigureAwait(false),
                "notify" => await RunNotifyAsync(args).ConfigureAwait(false),
                "delegate" => await RunDelegateAsync(args).ConfigureAwait(false),
                "dashboard" => RunDashboard(args),
                _ => UsageError($"Unknown command: {args.Positionals[0]}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(SafeText.Redact(ex.Message));
            return 1;
        }
    }

    private static async Task<int> RunWrapperAsync(Args args)
    {
        var sub = args.PositionalAt(1);
        return sub switch
        {
            "status" => await RunWrapperStatusAsync(args).ConfigureAwait(false),
            "rpc" => await RunWrapperRpcAsync(args).ConfigureAwait(false),
            _ => UsageError("Expected: ctu wrapper status|rpc")
        };
    }

    private static async Task<int> RunWrapperStatusAsync(Args args)
    {
        var result = await CreateAppServerClient(args).ProbeAsync().ConfigureAwait(false);
        return WriteAppServerMutationResult(result);
    }

    private static async Task<int> RunWrapperRpcAsync(Args args)
    {
        var method = args.Require("method");
        object? parameters = null;
        var raw = args.Value("params-json");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            parameters = JsonSerializer.Deserialize<JsonElement>(raw);
        }

        var result = await CreateAppServerClient(args).CallAsync(method, parameters).ConfigureAwait(false);
        return WriteAppServerMutationResult(result);
    }

    private static async Task<int> RunDoctorAsync(Args args)
    {
        Console.WriteLine("CodexTeamUp doctor");
        Console.WriteLine($"  cwd: {Environment.CurrentDirectory}");
        Console.WriteLine($"  busRoot: {CreateBus(args).RootDirectory}");
        Console.WriteLine($"  codexHome: {CodexHome.Resolve(args.Value("codex-home"))}");

        var wrapper = await CreateAppServerClient(args).ProbeAsync().ConfigureAwait(false);
        Console.WriteLine($"  wrapperPipe: {(wrapper.Succeeded ? "ok" : "unavailable")}");
        if (!wrapper.Succeeded)
        {
            Console.WriteLine($"  wrapperError: {SafeText.Preview(wrapper.Error, 300)}");
        }

        Console.WriteLine($"  agentBusExists: {Directory.Exists(CreateBus(args).RootDirectory)}");
        return wrapper.Succeeded ? 0 : 2;
    }

    private static async Task<int> RunCodexAsync(Args args)
    {
        var sub = args.PositionalAt(1);
        return sub switch
        {
            "info" => await RunCodexInfoAsync(args).ConfigureAwait(false),
            "schema" => await RunCodexSchemaAsync(args).ConfigureAwait(false),
            _ => UsageError("Expected: ctu codex info|schema")
        };
    }

    private static async Task<int> RunCodexInfoAsync(Args args)
    {
        var runner = new ProcessRunner();
        var checks = new[]
        {
            ("codex --help", "codex", "--help"),
            ("codex app-server --help", "codex", "app-server --help"),
            ("codex app-server proxy --help", "codex", "app-server proxy --help"),
            ("codex remote-control --help", "codex", "remote-control --help"),
            ("codex app-server generate-json-schema --help", "codex", "app-server generate-json-schema --help")
        };

        var where = await runner.RunAsync("where.exe", "codex", timeout: TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Console.WriteLine("Codex CLI");
        Console.WriteLine($"  path: {SafeText.Preview(where.StandardOutput, 300)}");

        foreach (var (name, file, arguments) in checks)
        {
            var result = await runner.RunAsync(file, arguments, timeout: TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            Console.WriteLine($"  {name}: {(result.Succeeded ? "ok" : "failed")}");
        }

        var codexHome = CodexHome.Resolve(args.Value("codex-home"));
        Console.WriteLine();
        Console.WriteLine("Codex state");
        Console.WriteLine($"  codexHome: {codexHome}");
        Console.WriteLine($"  session_index.jsonl: {File.Exists(Path.Combine(codexHome, "session_index.jsonl"))}");
        Console.WriteLine($"  state_5.sqlite: {File.Exists(Path.Combine(codexHome, "state_5.sqlite"))}");
        Console.WriteLine($"  logs_2.sqlite: {File.Exists(Path.Combine(codexHome, "logs_2.sqlite"))}");
        Console.WriteLine($"  app-server-control: {Directory.Exists(Path.Combine(codexHome, "app-server-control"))}");

        var probe = await CreateAppServerClient(args).ProbeAsync().ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine("Desktop app-server proxy");
        Console.WriteLine($"  reachable: {probe.Succeeded}");
        if (!probe.Succeeded)
        {
            Console.WriteLine($"  error: {SafeText.Preview(probe.Error, 300)}");
        }

        return 0;
    }

    private static async Task<int> RunCodexSchemaAsync(Args args)
    {
        var outputDirectory = args.Value("out")
            ?? Path.Combine(Environment.CurrentDirectory, ".ctu", "schemas", DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
        var summary = await new CodexSchemaService().GenerateAsync(outputDirectory).ConfigureAwait(false);
        Console.WriteLine($"schemaDir: {summary.OutputDirectory}");
        Console.WriteLine("formalMethods:");
        foreach (var method in summary.Methods)
        {
            Console.WriteLine($"  - {method}");
        }

        return 0;
    }

    private static async Task<int> RunThreadsAsync(Args args)
    {
        var sub = args.PositionalAt(1);
        return sub switch
        {
            "list" => await RunThreadsListAsync(args).ConfigureAwait(false),
            "read" => await RunThreadsReadAsync(args).ConfigureAwait(false),
            "resume" => await RunThreadsResumeAsync(args).ConfigureAwait(false),
            "start" => await RunThreadsStartAsync(args).ConfigureAwait(false),
            "send" => await RunThreadsSendAsync(args).ConfigureAwait(false),
            _ => UsageError("Expected: ctu threads list|read|resume|start|send")
        };
    }

    private static async Task<int> RunThreadsListAsync(Args args)
    {
        var source = args.Value("source") ?? "auto";
        var cwd = args.Value("cwd");
        var limit = args.IntValue("limit", 30);

        if (!string.Equals(source, "state", StringComparison.OrdinalIgnoreCase))
        {
            var appServer = await CreateAppServerClient(args).ListThreadsAsync(cwd, limit).ConfigureAwait(false);
            if (appServer.Succeeded && appServer.ResultJson is not null)
            {
                WriteThreads(AppServerThreadMapper.ParseListResult(appServer.ResultJson));
                return 0;
            }

            if (string.Equals(source, "appserver", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "wrapper", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(SafeText.Preview(appServer.Error, 500));
                return 2;
            }

            Console.Error.WriteLine($"app-server unavailable, falling back to read-only state: {SafeText.Preview(appServer.Error, 220)}");
        }

        var reader = new CodexStateReader(args.Value("codex-home"));
        WriteThreads(reader.ListThreads(cwd, limit));
        return 0;
    }

    private static async Task<int> RunThreadsReadAsync(Args args)
    {
        var threadId = args.Require("thread-id");
        var includeTurns = args.Has("include-turns");
        var source = args.Value("source") ?? "auto";

        if (!string.Equals(source, "state", StringComparison.OrdinalIgnoreCase))
        {
            var appServer = await CreateAppServerClient(args).ReadThreadAsync(threadId, includeTurns).ConfigureAwait(false);
            if (appServer.Succeeded && appServer.ResultJson is not null)
            {
                var result = AppServerThreadMapper.ParseReadResult(appServer.ResultJson);
                if (result is not null)
                {
                    WriteThread(result);
                    return 0;
                }
            }

            if (string.Equals(source, "appserver", StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "wrapper", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(SafeText.Preview(appServer.Error, 500));
                return 2;
            }

            Console.Error.WriteLine($"app-server unavailable, falling back to read-only state: {SafeText.Preview(appServer.Error, 220)}");
        }

        var reader = new CodexStateReader(args.Value("codex-home"));
        var stateResult = reader.ReadThread(threadId, includeTurns);
        if (stateResult is null)
        {
            Console.Error.WriteLine($"Thread not found in state: {threadId}");
            return 3;
        }

        WriteThread(stateResult);
        return 0;
    }

    private static async Task<int> RunThreadsStartAsync(Args args)
    {
        var cwd = args.Require("cwd");
        var name = args.Value("name");
        var role = args.Value("role");

        if (!await ConfirmAsync(args, $"Start a new Codex Desktop thread in {cwd}?").ConfigureAwait(false))
        {
            return 4;
        }

        var settings = new AppServerAgentSettings(args.Value("model"), args.Value("reasoning-effort") ?? args.Value("effort"));
        var result = await CreateAppServerClient(args).StartThreadAsync(cwd, name, role, settings).ConfigureAwait(false);
        return WriteAppServerMutationResult(result);
    }

    private static async Task<int> RunThreadsResumeAsync(Args args)
    {
        var threadId = args.Require("thread-id");
        var result = await CreateAppServerClient(args).ResumeThreadAsync(threadId).ConfigureAwait(false);
        return WriteAppServerMutationResult(result);
    }

    private static async Task<int> RunThreadsSendAsync(Args args)
    {
        var threadId = args.Require("thread-id");
        var message = args.Value("message") ?? ReadPromptFile(args);
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Missing --message or --prompt-file.");
        }

        await WarnIfThreadActiveAsync(args, threadId).ConfigureAwait(false);
        if (!await ConfirmAsync(args, $"Send turn/start to thread {threadId}?").ConfigureAwait(false))
        {
            return 4;
        }

        var settings = new AppServerAgentSettings(args.Value("model"), args.Value("reasoning-effort") ?? args.Value("effort"));
        var result = await CreateAppServerClient(args).SendTurnAsync(threadId, message, args.Value("cwd"), settings).ConfigureAwait(false);
        return WriteAppServerMutationResult(result);
    }

    private static async Task<int> RunTurnsAsync(Args args)
    {
        var sub = args.PositionalAt(1);
        return sub switch
        {
            "list" => await RunTurnsListAsync(args).ConfigureAwait(false),
            "wait" => await RunTurnsWaitAsync(args).ConfigureAwait(false),
            _ => UsageError("Expected: ctu turns list|wait")
        };
    }

    private static async Task<int> RunTurnsListAsync(Args args)
    {
        var result = await CreateAppServerClient(args).ListTurnsAsync(
            args.Require("thread-id"),
            args.Value("sort-direction") ?? "asc",
            args.IntValue("limit", 50)).ConfigureAwait(false);
        return WriteAppServerMutationResult(result);
    }

    private static async Task<int> RunTurnsWaitAsync(Args args)
    {
        var timeout = TimeSpan.FromSeconds(args.IntValue("timeout-seconds", 300));
        var result = await CreateAppServerClient(args).WaitForTurnAsync(
            args.Require("thread-id"),
            args.Require("turn-id"),
            timeout).ConfigureAwait(false);
        if (result.Completed)
        {
            Console.WriteLine($"turn completed: {result.Status}");
            return 0;
        }

        Console.Error.WriteLine($"turn wait failed: {result.Status} {SafeText.Preview(result.Error, 300)}");
        return 2;
    }

    private static int RunBus(Args args)
    {
        var sub = args.PositionalAt(1);
        return sub switch
        {
            "init" => RunBusInit(args),
            "agent" => RunBusAgent(args),
            "task" => RunBusTask(args),
            "result" => RunBusResult(args),
            "event" => RunBusEvent(args),
            "wait" => RunBusWait(args),
            _ => UsageError("Expected: ctu bus init|agent|task|result|event|wait")
        };
    }

    private static int RunBusInit(Args args)
    {
        var bus = CreateBus(args);
        bus.Initialize();
        Console.WriteLine($"initialized: {bus.RootDirectory}");
        return 0;
    }

    private static int RunBusTask(Args args)
    {
        var action = args.PositionalAt(2);
        var bus = CreateBus(args);
        switch (action)
        {
            case "create":
                var promptFile = args.Value("prompt-file");
                var task = string.IsNullOrWhiteSpace(promptFile)
                    ? bus.CreateTask(
                        args.Require("from"),
                        args.Require("to"),
                        args.Require("title"),
                        args.Require("prompt"),
                        args.Value("project") ?? new DirectoryInfo(Environment.CurrentDirectory).Name,
                        args.Value("cwd") ?? Environment.CurrentDirectory,
                        SplitCsv(args.Value("allowed-paths")),
                        args.Value("return-to"),
                        priority: args.Value("priority"))
                    : bus.CreateTaskFromPromptFile(
                        args.Require("from"),
                        args.Require("to"),
                        args.Require("title"),
                        promptFile,
                        args.Value("project") ?? new DirectoryInfo(Environment.CurrentDirectory).Name,
                        args.Value("cwd") ?? Environment.CurrentDirectory,
                        SplitCsv(args.Value("allowed-paths")),
                        args.Value("return-to"),
                        args.Value("priority"));
                Console.WriteLine($"created: {task.Id}");
                Console.WriteLine(Path.Combine(bus.TasksOpenDirectory, $"{task.Id}.json"));
                return 0;
            case "list":
                WriteTasks(bus.ListTasks(args.Value("to"), args.Value("status")));
                return 0;
            case "claim":
                var claimed = bus.ClaimTask(args.Require("task-id"), args.Value("owner"));
                Console.WriteLine($"claimed: {claimed.Id} by {claimed.Owner}");
                return 0;
            default:
                return UsageError("Expected: ctu bus task create|list|claim");
        }
    }

    private static int RunBusAgent(Args args)
    {
        var action = args.PositionalAt(2);
        var bus = CreateBus(args);
        switch (action)
        {
            case "list":
                WriteAgents(bus.ListAgents());
                return 0;
            case "register":
                var agent = new AgentDefinition
                {
                    Id = args.Require("id"),
                    Role = args.Value("role") ?? args.Require("id"),
                    DisplayName = args.Value("display-name"),
                    ThreadId = args.Value("thread-id"),
                    Cwd = args.Value("cwd") ?? Environment.CurrentDirectory,
                    AllowedPaths = SplitCsv(args.Value("allowed-paths")),
                    ReturnTo = args.Value("return-to"),
                    NotifyPolicy = args.Value("notify-policy") ?? "manual",
                    Model = args.Value("model"),
                    ReasoningEffort = args.Value("reasoning-effort") ?? args.Value("effort"),
                    Speed = args.Value("speed"),
                    Status = args.Value("status") ?? "active"
                };
                bus.RegisterAgent(agent);
                Console.WriteLine($"agent: {agent.Id}");
                return 0;
            default:
                return UsageError("Expected: ctu bus agent list|register");
        }
    }

    private static int RunBusResult(Args args)
    {
        var action = args.PositionalAt(2);
        if (action != "write")
        {
            return UsageError("Expected: ctu bus result write");
        }

        var summary = args.Value("summary") ?? ReadTextFile(args.Value("summary-file"));
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("Missing --summary or --summary-file.");
        }

        var bus = CreateBus(args);
        var result = bus.WriteResult(
            args.Require("task-id"),
            summary,
            args.Value("status") ?? "completed",
            args.Value("from"),
            args.Value("to"),
            args.Value("commit"),
            SplitCsv(args.Value("tests")),
            SplitCsv(args.Value("open-questions")),
            SplitCsv(args.Value("changed-files")),
            SplitCsv(args.Value("artifacts")),
            args.Value("next"));
        Console.WriteLine($"result: {result.Id}");
        Console.WriteLine(Path.Combine(bus.ResultsDirectory, $"{result.Id}.json"));
        return 0;
    }

    private static int RunBusWait(Args args)
    {
        var result = CreateBus(args).WaitForResult(
            args.Require("task-id"),
            TimeSpan.FromSeconds(args.IntValue("timeout-seconds", 300)));
        if (result is null)
        {
            Console.Error.WriteLine("result wait timed out");
            return 2;
        }

        Console.WriteLine($"result: {result.Id}");
        Console.WriteLine(result.Summary);
        return 0;
    }

    private static int RunBusEvent(Args args)
    {
        var action = args.PositionalAt(2);
        if (action != "list")
        {
            return UsageError("Expected: ctu bus event list");
        }

        var bus = CreateBus(args);
        WriteEvents(bus.ListEvents(args.IntValue("limit", 100)));
        return 0;
    }

    private static async Task<int> RunDispatchAsync(Args args)
    {
        var bus = CreateBus(args);
        var taskId = args.Require("task-id");
        var threadId = ResolveTargetThreadId(bus, args, "to-agent", "to-thread");
        var task = bus.FindTask(taskId) ?? throw new FileNotFoundException($"Task not found: {taskId}");
        if (!string.Equals(task.Status, "open", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Task {task.Id} is {task.Status}; dispatch expects an open task.");
        }

        var message =
            $"You have a new AgentBus task: {task.Id}.\n" +
            $"Please read .codexteamup/agentbus/tasks/open/{task.Id}.json, work on it, write your result back as an AgentBus result, and then notify the architect.";

        await MaybePrintGitStatusAsync(args, task.Cwd).ConfigureAwait(false);
        await WarnIfThreadActiveAsync(args, threadId).ConfigureAwait(false);
        if (!await ConfirmAsync(args, $"Dispatch task {task.Id} to thread {threadId}?").ConfigureAwait(false))
        {
            return 4;
        }

        var result = await CreateAppServerClient(args)
            .SendTurnAsync(threadId, message, task.Cwd, RuntimeSettings(bus.FindAgent(args.Value("to-agent") ?? task.To)))
            .ConfigureAwait(false);
        if (result.Succeeded)
        {
            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = "task.dispatched",
                TaskId = task.Id,
                To = threadId,
                Message = "turn/start sent"
            });
        }

        if (result.Succeeded && args.Has("wait-result"))
        {
            var waited = bus.WaitForResult(task.Id, TimeSpan.FromSeconds(args.IntValue("timeout-seconds", 1800)));
            if (waited is null)
            {
                Console.Error.WriteLine($"Timed out waiting for AgentBus result for {task.Id}.");
                return 2;
            }

            Console.WriteLine($"result: {waited.Id}");
            if (args.Has("notify-return"))
            {
                var notifyArgs = args.WithOption("result-id", waited.Id).WithOption("to-agent", waited.To);
                return await RunNotifyAsync(notifyArgs).ConfigureAwait(false);
            }
        }

        return WriteAppServerMutationResult(result);
    }

    private static async Task<int> RunNotifyAsync(Args args)
    {
        var bus = CreateBus(args);
        var resultId = args.Require("result-id");
        var threadId = ResolveTargetThreadId(bus, args, "to-agent", "to-thread");
        var busResult = bus.FindResult(resultId) ?? throw new FileNotFoundException($"Result not found: {resultId}");
        var message =
            $"AgentBus result arrived: {busResult.Id}.\n" +
            $"Please read .codexteamup/agentbus/results/{busResult.Id}.json and review the result, scope, and next steps.";

        await WarnIfThreadActiveAsync(args, threadId).ConfigureAwait(false);
        if (!await ConfirmAsync(args, $"Notify thread {threadId} about result {busResult.Id}?").ConfigureAwait(false))
        {
            return 4;
        }

        var result = await CreateAppServerClient(args)
            .SendTurnAsync(threadId, message, null, RuntimeSettings(bus.FindAgent(args.Value("to-agent") ?? busResult.To)))
            .ConfigureAwait(false);
        if (result.Succeeded)
        {
            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = "result.notified",
                ResultId = busResult.Id,
                To = threadId,
                Message = "turn/start sent"
            });
        }

        return WriteAppServerMutationResult(result);
    }

    private static async Task<int> RunDelegateAsync(Args args)
    {
        var bus = CreateBus(args);
        var to = args.Require("to");
        var task = string.IsNullOrWhiteSpace(args.Value("prompt-file"))
            ? bus.CreateTask(
                args.Value("from") ?? "architect",
                to,
                args.Require("title"),
                args.Require("prompt"),
                args.Value("project") ?? new DirectoryInfo(Environment.CurrentDirectory).Name,
                args.Value("cwd") ?? Environment.CurrentDirectory,
                SplitCsv(args.Value("allowed-paths")),
                args.Value("return-to") ?? "architect")
            : bus.CreateTaskFromPromptFile(
                args.Value("from") ?? "architect",
                to,
                args.Require("title"),
                args.Require("prompt-file"),
                args.Value("project") ?? new DirectoryInfo(Environment.CurrentDirectory).Name,
                args.Value("cwd") ?? Environment.CurrentDirectory,
                SplitCsv(args.Value("allowed-paths")),
                args.Value("return-to") ?? "architect");

        Console.WriteLine($"created: {task.Id}");
        var dispatchArgs = args.WithOption("task-id", task.Id).WithOption("to-agent", to);
        if (args.Has("wait-result"))
        {
            dispatchArgs = dispatchArgs.WithOption("wait-result", "true").WithOption("notify-return", "true");
        }

        return await RunDispatchAsync(dispatchArgs).ConfigureAwait(false);
    }

    private static int RunDashboard(Args args)
    {
        var sub = args.PositionalAt(1);
        if (sub != "export")
        {
            return UsageError("Expected: ctu dashboard export");
        }

        var path = AgentBusDashboard.Export(CreateBus(args), args.Value("out"));
        Console.WriteLine(path);
        return 0;
    }

    private static int WriteAppServerMutationResult(AppServerCallResult result)
    {
        if (result.Succeeded)
        {
            Console.WriteLine("app-server: ok");
            Console.WriteLine(SafeText.Preview(result.ResultJson, 1000));
            return 0;
        }

        Console.Error.WriteLine("app-server call failed");
        Console.Error.WriteLine(SafeText.Preview(result.Error, 1000));
        return 2;
    }

    private static void WriteThreads(IReadOnlyList<CodexThreadRecord> threads)
    {
        Table.Write(Console.Out, ["id", "name/preview", "cwd", "source", "status", "updated", "origin"], threads.Select(t =>
            (IReadOnlyList<string?>)
            [
                t.Id,
                t.Name ?? t.Preview,
                t.Cwd,
                t.Source,
                t.Status,
                t.UpdatedAt?.ToString("u"),
                t.Origin
            ]));
    }

    private static void WriteThread(CodexThreadReadResult result)
    {
        Console.WriteLine($"id: {result.Thread.Id}");
        Console.WriteLine($"name: {SafeText.Preview(result.Thread.Name ?? result.Thread.Preview)}");
        Console.WriteLine($"cwd: {result.Thread.Cwd}");
        Console.WriteLine($"source: {result.Thread.Source}");
        Console.WriteLine($"status: {result.Thread.Status}");
        Console.WriteLine($"updatedAt: {result.Thread.UpdatedAt:u}");
        Console.WriteLine($"origin: {result.Thread.Origin}");
        Console.WriteLine($"path: {result.Thread.StoragePath}");

        if (result.Items.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Table.Write(Console.Out, ["timestamp", "type", "preview"], result.Items.Select(i =>
            (IReadOnlyList<string?>)[i.Timestamp, i.Type, i.Preview]));
    }

    private static void WriteTasks(IReadOnlyList<AgentBusTask> tasks)
    {
        Table.Write(Console.Out, ["id", "status", "from", "to", "title", "created"], tasks.Select(t =>
            (IReadOnlyList<string?>)[t.Id, t.Status, t.From, t.To, t.Title, t.CreatedAt.ToString("u")]));
    }

    private static void WriteAgents(IReadOnlyList<AgentDefinition> agents)
    {
        Table.Write(Console.Out, ["id", "role", "threadId", "cwd", "returnTo", "speed", "model", "effort", "status"], agents.Select(agent =>
            (IReadOnlyList<string?>)[agent.Id, agent.Role, agent.ThreadId, agent.Cwd, agent.ReturnTo, agent.Speed, agent.Model, agent.ReasoningEffort, agent.Status]));
    }

    private static void WriteEvents(IReadOnlyList<AgentBusEvent> events)
    {
        Table.Write(Console.Out, ["time", "type", "task", "result", "from", "to", "message"], events.Select(e =>
            (IReadOnlyList<string?>)[e.Timestamp.ToString("u"), e.Type, e.TaskId, e.ResultId, e.From, e.To, e.Message]));
    }

    private static AgentBusStore CreateBus(Args args)
    {
        return new AgentBusStore(args.Value("bus-root") ?? Path.Combine(Environment.CurrentDirectory, ".codexteamup/agentbus"));
    }

    private static CodexAppServerClient CreateAppServerClient(Args args)
    {
        return new CodexAppServerClient(
            args.Value("codex") ?? "codex",
            args.Value("pipe") ?? args.Value("sock"),
            args.Value("codex-home") ?? CodexHome.Resolve());
    }

    private static AppServerAgentSettings? RuntimeSettings(AgentDefinition? agent)
    {
        if (agent is null)
        {
            return null;
        }

        var speed = string.IsNullOrWhiteSpace(agent.Speed) ? "standard" : agent.Speed.Trim().ToLowerInvariant();
        var model = string.IsNullOrWhiteSpace(agent.Model)
            ? speed == "fast" ? "gpt-5.4-mini" : null
            : agent.Model;
        var effort = string.IsNullOrWhiteSpace(agent.ReasoningEffort)
            ? speed switch
            {
                "fast" => "low",
                "deep" => "high",
                "max" => "xhigh",
                _ => "medium"
            }
            : agent.ReasoningEffort;
        return string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(effort)
            ? null
            : new AppServerAgentSettings(model, effort);
    }

    private static string ResolveTargetThreadId(AgentBusStore bus, Args args, string agentOption, string threadOption)
    {
        var explicitThread = args.Value(threadOption);
        if (!string.IsNullOrWhiteSpace(explicitThread))
        {
            return explicitThread;
        }

        var agentId = args.Require(agentOption);
        var agent = bus.FindAgent(agentId) ?? throw new InvalidOperationException($"Agent is not registered: {agentId}");
        if (string.IsNullOrWhiteSpace(agent.ThreadId))
        {
            throw new InvalidOperationException($"Agent {agent.Id} has no threadId. Register it with ctu bus agent register --id {agent.Id} --thread-id <id>.");
        }

        return agent.ThreadId;
    }

    private static async Task<bool> ConfirmAsync(Args args, string prompt)
    {
        if (args.Has("yes"))
        {
            return true;
        }

        Console.Write($"{prompt} Type YES to continue: ");
        var answer = await Console.In.ReadLineAsync().ConfigureAwait(false);
        return string.Equals(answer, "YES", StringComparison.Ordinal);
    }

    private static async Task WarnIfThreadActiveAsync(Args args, string threadId)
    {
        var read = await CreateAppServerClient(args).ReadThreadAsync(threadId, includeTurns: false).ConfigureAwait(false);
        if (!read.Succeeded || read.ResultJson is null)
        {
            Console.Error.WriteLine($"Could not check live thread status before send: {SafeText.Preview(read.Error, 220)}");
            return;
        }

        var parsed = AppServerThreadMapper.ParseReadResult(read.ResultJson);
        if (parsed?.Thread.Status?.Contains("active", StringComparison.OrdinalIgnoreCase) == true)
        {
            Console.Error.WriteLine($"WARNING: target thread {threadId} appears active. Review before sending.");
        }
    }

    private static async Task MaybePrintGitStatusAsync(Args args, string? cwd)
    {
        if (!args.Has("check-git"))
        {
            return;
        }

        var workingDirectory = string.IsNullOrWhiteSpace(cwd) ? Environment.CurrentDirectory : cwd;
        var result = await new ProcessRunner()
            .RunAsync("git", "status --short", workingDirectory, timeout: TimeSpan.FromSeconds(10))
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"git status failed: {SafeText.Preview(result.StandardError, 220)}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            Console.Error.WriteLine("WARNING: git working tree has changes:");
            Console.Error.WriteLine(SafeText.Preview(result.StandardOutput, 1000));
        }
    }

    private static string? ReadPromptFile(Args args)
    {
        return ReadTextFile(args.Value("prompt-file"));
    }

    private static string? ReadTextFile(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : File.ReadAllText(path);
    }

    private static IReadOnlyList<string> SplitCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine(message);
        WriteUsage();
        return 1;
    }

    private static void WriteUsage()
    {
        Console.WriteLine("""
        CodexTeamUp PoC

        Usage:
          ctu codex info [--codex-home <path>]
          ctu codex schema [--out <dir>]
          ctu doctor
          ctu wrapper status
          ctu wrapper rpc --method <method> [--params-json <json>]
          ctu threads list [--cwd <path>] [--source auto|wrapper|state] [--limit <n>]
          ctu threads read --thread-id <id> [--include-turns] [--source auto|wrapper|state]
          ctu threads resume --thread-id <id>
          ctu threads start --cwd <path> --name <name> --role <role> [--model <model>] [--reasoning-effort <effort>] [--yes]
          ctu threads send --thread-id <id> --message <text> [--yes]
          ctu turns list --thread-id <id>
          ctu turns wait --thread-id <id> --turn-id <id>
          ctu bus init
          ctu bus agent list
          ctu bus agent register --id 01-web --thread-id <id> --role web [--speed fast|standard|deep|max] [--model <model>] [--reasoning-effort <effort>]
          ctu bus task create --from architect --to 01-web --title <title> --prompt-file <file>
          ctu bus task list [--to 01-web] [--status open|claimed|done]
          ctu bus task claim --task-id <id> [--owner 01-web]
          ctu bus result write --task-id <id> --summary <text>
          ctu bus wait --task-id <id>
          ctu bus event list
          ctu dispatch --task-id <id> --to-agent 01-web [--wait-result] [--yes]
          ctu notify --result-id <id> --to-agent architect [--yes]
          ctu delegate --to 01-web --title <title> --prompt-file <file> [--wait-result] [--yes]
          ctu dashboard export [--bus-root <path>] [--out <html>]
        """);
    }
}

internal sealed class Args
{
    private readonly Dictionary<string, List<string>> _options;

    private Args(IReadOnlyList<string> positionals, Dictionary<string, List<string>> options)
    {
        Positionals = positionals;
        _options = options;
    }

    public IReadOnlyList<string> Positionals { get; }

    public static Args Parse(string[] args)
    {
        var positionals = new List<string>();
        var options = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(token);
                continue;
            }

            var trimmed = token[2..];
            var equals = trimmed.IndexOf('=');
            string key;
            string value;
            if (equals >= 0)
            {
                key = trimmed[..equals];
                value = trimmed[(equals + 1)..];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                key = trimmed;
                value = args[++i];
            }
            else
            {
                key = trimmed;
                value = "true";
            }

            if (!options.TryGetValue(key, out var values))
            {
                values = [];
                options[key] = values;
            }

            values.Add(value);
        }

        return new Args(positionals, options);
    }

    public Args SkipFirstPositional()
    {
        return new Args(Positionals.Skip(1).ToList(), _options);
    }

    public Args WithOption(string key, string value)
    {
        var copy = _options.ToDictionary(entry => entry.Key, entry => entry.Value.ToList(), StringComparer.OrdinalIgnoreCase);
        copy[key] = [value];
        return new Args(Positionals.ToList(), copy);
    }

    public string? PositionalAt(int index)
    {
        return index >= 0 && index < Positionals.Count ? Positionals[index].ToLowerInvariant() : null;
    }

    public bool Has(string key)
    {
        return _options.ContainsKey(key);
    }

    public string? Value(string key)
    {
        return _options.TryGetValue(key, out var values) && values.Count > 0 ? values[^1] : null;
    }

    public string Require(string key)
    {
        var value = Value(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing --{key}.");
        }

        return value;
    }

    public int IntValue(string key, int defaultValue)
    {
        var value = Value(key);
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
