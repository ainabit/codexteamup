# CodexTeamUp Architecture

CodexTeamUp coordinates visible Codex Desktop threads through a small Desktop wrapper, a local HTTP service, and a durable repo-local AgentBus.

## Main Pieces

```text
Codex Desktop
  -> CODEX_CLI_PATH
  -> CodexTeamUp.CodexWrapper
  -> real codex.exe app-server stdio

Codex agents
  -> CodexTeamUp.Service HTTP MCP
  -> .codexteamup/agentbus
  -> wrapper named pipe
  -> Codex Desktop app-server
```

## Responsibilities

- `CodexTeamUp.CodexWrapper`: transparent stdio proxy for Codex Desktop plus a controlled named-pipe bridge for JSON-RPC calls
- `CodexTeamUp.Service`: local HTTP service for MCP, AgentBus operations, dashboard data, and Desktop wakeups
- `CodexTeamUp.AppServer`: typed internal client for the Desktop app-server through the wrapper pipe
- `CodexTeamUp.AgentBus`: durable store for tasks, claims, results, and events under `.codexteamup/agentbus`
- `CodexTeamUp.Cli`: operational interface for build-time checks, diagnostics, and recovery
- Dashboard: local monitoring UI rendered by the service at `http://127.0.0.1:47319/`

## Startup Flow

1. A user runs `powershell -ExecutionPolicy Bypass -File .\scripts\start-codexteamup.ps1`.
2. The script publishes local tooling and refreshes the CTU service.
3. The service starts on `http://127.0.0.1:47319/`.
4. The script configures the Codex MCP entry for `http://127.0.0.1:47319/mcp`.
5. Codex Desktop starts through `CodexTeamUp.CodexWrapper`.
6. Agents can now use MCP tools against the local service, while the service can wake visible Desktop threads through the wrapper path.

## Task, Result, and Event Lifecycle

1. An architect or worker creates or receives a task.
2. The task is stored in `.codexteamup/agentbus/tasks/open`.
3. A target worker claims the task.
4. The service resolves the target agent and visible Desktop thread.
5. The service sends `thread/resume`, then `turn/start` through the wrapper-backed app-server client.
6. The worker reads the task file in its own thread context.
7. The worker writes exactly one result file.
8. The service records related events and can wake the return thread with `bridge_notify_result`.

The AgentBus remains the durable source of truth. Desktop wakeups are only the live delivery mechanism.

## Visible Threads and MCP

Visible Codex Desktop threads are the user-facing work surface. MCP is the normal machine-facing coordination surface. Agents should use MCP tools for daily task and result traffic instead of PowerShell.

## Transport Choice

The Desktop control socket expected under `%USERPROFILE%\.codex\app-server-control` is not available in the observed Desktop build. The currently reliable live path is therefore:

```text
CODEX_CLI_PATH -> wrapper -> named pipe -> Desktop app-server stdio
```

The wrapper is intentionally narrow so it can be replaced if Codex Desktop exposes an official local transport later.
