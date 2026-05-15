# CodexTeamUp Backend Service

`CodexTeamUp.Service` is the local backend for agent-facing communication, dashboard rendering, and Desktop wakeups.

Agents do not call PowerShell or the named pipe directly. They call MCP tools through the CTU HTTP service at `http://127.0.0.1:47319/mcp`.

## Default URL

```text
http://127.0.0.1:47319/
```

## Runtime Chain

```text
Codex agent
  -> CodexTeamUp.Service HTTP MCP
  -> .codexteamup/agentbus files
  -> internal WrapperPipeAppServerClient
  -> CodexTeamUp.CodexWrapper
  -> Codex Desktop app-server
```

## Endpoints

- `GET /health`
- `GET /`
- `GET /dashboard?busRoot=<path>`
- `GET /api/snapshot?busRoot=<path>`
- `GET /api/agents?busRoot=<path>`
- `GET /api/tasks?busRoot=<path>`
- `GET /api/results?busRoot=<path>`
- `GET /api/events?busRoot=<path>`
- `POST /mcp`
- `POST /mcp/tools/<toolName>`

`POST /mcp` is the Codex-facing JSON-RPC MCP endpoint. `/mcp/tools/<toolName>` is a diagnostic HTTP shortcut that accepts the tool arguments as JSON.

Tools may pass `cwd` or `busRoot`. If `cwd` is present, the service uses `<cwd>/.codexteamup/agentbus` for that project.

`GET /` and `GET /dashboard` both render the monitoring dashboard.

`GET /api/snapshot` returns one combined response:

```json
{"busRoot":"...","generatedAt":"...","stats":{},"agents":[],"tasks":[],"results":[],"events":[]}
```

## Start

The normal startup script is the daily user workflow. It publishes local tools, refreshes the service, starts the service, and launches Codex Desktop with the wrapper and service environment:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-codexteamup.ps1
```

## MCP Configuration

MCP clients should connect to:

```text
http://127.0.0.1:47319/mcp
```

The startup script writes this Codex configuration:

```text
[mcp_servers.ctu]
url = "http://127.0.0.1:47319/mcp"
```

`CODEX_WRAPPER_PIPE_NAME` is only needed by the backend service, not by normal MCP clients or human users.
