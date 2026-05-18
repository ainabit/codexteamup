# CodexTeamUp MCP Tools

CodexTeamUp exposes MCP directly from `CodexTeamUp.Service` over local HTTP. The agent-facing endpoint is:

```text
http://127.0.0.1:47319/mcp
```

`CodexTeamUp.Mcp` is only a .NET library for tool metadata and registration. There is intentionally no standalone `CodexTeamUp.Mcp.exe` stdio server in the normal architecture.

## Daily Start

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-codexteamup.ps1
```

The user does not start a separate MCP process. Codex Desktop connects to the registered HTTP MCP URL:

```text
http://127.0.0.1:47319/mcp
```

`start-codexteamup.ps1` registers the global Codex MCP entry `[mcp_servers.ctu]` with `url = "http://127.0.0.1:47319/mcp"`. It sets `approval_mode = "approve"` for CodexTeamUp communication tools so agent threads can use MCP without falling back to PowerShell approvals. Use `-NoConfigureMcp` only for diagnostics.

## MCP Methods

The server supports:

- `initialize`
- `notifications/initialized`
- `tools/list`
- `tools/call`

## Manual Request Format

```json
{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"agentbus_init","arguments":{}}}
```

## Tools

- `agentbus_init`
- `agentbus_list_agents`
- `agentbus_register_agent`
- `agentbus_create_task`
- `agentbus_list_tasks`
- `agentbus_clear_tasks`
- `agentbus_claim_task`
- `agentbus_write_result`
- `agentbus_wait_result`
- `codex_thread_list`
- `codex_thread_read`
- `codex_thread_archive`
- `codex_turn_start`
- `codex_appserver_adapter_status`
- `codex_appserver_adapter_reload`
- `codex_controller_status`
- `codex_controller_reload`
- `codex_controller_policy_status`
- `codex_controller_policy_reload`
- `bridge_dispatch_task`
- `bridge_notify_result`
- `team_create_agent`
- `team_ensure_agents`
- `team_discover_agents`
- `team_send_message`
- `team_dashboard_export`

## Intended Use

- Architect thread: decide the explicit `ctu/*` team, call `team_ensure_agents`, create tasks, dispatch work, and review results
- Worker thread: claim a task, do the work, write a result, and notify the return thread
- Reviewer thread: inspect results and create follow-up tasks

Use `team_send_message` for asynchronous delegation. By default it only creates the durable AgentBus task and returns an accepted/enqueued response. Wake the target thread separately with `bridge_dispatch_task`. The worker later writes a result and calls `bridge_notify_result`.

Use `team_send_message` with `waitResult=true` only for quick acknowledgements or very short answers. It is capped to short waits so visible threads stay reactive, and it is only meaningful with `dispatchMode=inline` or after a separate dispatch path. For longer work, enqueue the task, call `bridge_dispatch_task`, and poll with `agentbus_wait_result` in short chunks. `agentbus_wait_result` is the lower-level variant when a task already exists.

`team_send_message` supports `dispatchMode=inline` for compatibility and diagnostics. Inline dispatch also caps the Desktop wakeup attempt itself. The optional `wakeupTimeoutSeconds` argument is clamped to 1-10 seconds. If Desktop does not answer in time, the tool returns a deferred/uncertain wakeup status while keeping the AgentBus task as the durable handoff.

All CTU coordination should follow ACK/NACK semantics: a direct call should quickly report whether work was accepted, deferred, or failed to enqueue. Completion is a separate AgentBus result. Avoid one long blocking tool call when a loop of short polls gives the same evidence and keeps the thread responsive.

Any registered `ctu/*` agent can use these paths. Communication is not architect-only. For example, `ctu/dashboard` can ask `ctu/ux` directly and set `returnTo` to `ctu/dashboard`. If `returnTo` is omitted, `team_send_message` uses the sender as the return target.

`bridge_notify_result` is still the visible callback path. It wakes the return thread and writes audit events, but it cannot interrupt a thread that is already busy in another turn.

Large task prompts should live in Markdown files and be referenced by path instead of being inlined.

`agentbus_write_result` accepts comma-separated `changedFiles`, `tests`, `checks`, `artifacts`, and `openQuestions` values. `checks` is accepted as an alias for `tests`. Agents that edit files should set `changedFiles` explicitly so the dashboard and return agent can see what changed without parsing prose.

Every result must also carry a structured outcome: `done`, `handed_off`, `self_continue`, `human`, or `failed`. Only `self_continue` schedules a deduplicated later wakeup for the same agent. Other outcomes may notify a return target or remain visible for recovery, but they do not create routine self-wakeup work.

`agentbus_clear_tasks` is a destructive test-reset tool. It deletes AgentBus task queue files and, when `includeResults=true`, result files too. It requires `confirm=DELETE` so agents do not wipe active coordination by accident. Use it only for disposable test phases or deliberate local recovery; the normal production path should close work with results instead of erasing it.

CodexTeamUp does not infer project roles. The caller provides the desired team. `team_ensure_agents` accepts `agentsJson` as a JSON array string, for example:

```json
[
  {
    "id": "ctu/web",
    "role": "Frontend and editor implementation",
    "speed": "standard",
    "model": "gpt-5.4",
    "reasoningEffort": "medium",
    "allowedPaths": ["web/", "shared/"],
    "instructionFiles": ["web/AGENTS.md", "docs/agents/web.md"]
  }
]
```

`team_create_agent` and `team_ensure_agents` also accept short-control flags:

- `defer=true`, `ackOnly=true`, or `background=true` returns an ACK with an operation id and continues create/bind work asynchronously.
- `prime=false` registers or creates the agent without sending an initial prime turn.
- `setName=false` skips the explicit Desktop thread rename call. The controller still passes `displayName` to thread creation; this flag exists for short live-smoke and recovery flows where Desktop rename/prime calls are known to be fragile.

## Agent Runtime Settings

Agent runtime settings are stored in `.codexteamup/agentbus/agents.json`:

- `speed`: CTU profile; supported values are `fast`, `standard`, `deep`, and `max`
- `model`: optional Codex model override such as `gpt-5.4` or `gpt-5.4-mini`
- `reasoningEffort`: optional thinking level such as `low`, `medium`, `high`, or `xhigh`

If no runtime values are supplied, CTU defaults new agents to `speed=standard` and `reasoningEffort=medium`. Explicit `model` and `reasoningEffort` values always override the speed profile. `speed=fast` defaults to `gpt-5.4-mini` with `low` effort, while `deep` and `max` keep the default model and use `high` or `xhigh` effort.

## Role in the System

MCP is the normal path for Codex agents. Scripts remain for bootstrap and recovery only:

- start Desktop through the wrapper
- publish local tools
- perform emergency or diagnostic CLI operations

Daily architect and worker coordination should use MCP tools. Agent threads should not call `ctu.ps1` or PowerShell for normal task and result communication.

The named pipe is not an MCP or agent interface. It is only an internal transport from `CodexTeamUp.Service` to the Desktop wrapper.

## App-Server Adapter Reload

Desktop-specific lifecycle behavior is behind a reloadable app-server adapter. Use `codex_appserver_adapter_status` to inspect the active adapter and `codex_appserver_adapter_reload` to load a replacement plugin DLL without restarting Codex Desktop. Passing an empty plugin path switches back to the built-in wrapper-pipe adapter. See `docs/appserver-adapter-plugins.md`.

## Controller Policy Reload

Volatile orchestration lives behind a reloadable controller runtime. Use `codex_controller_status` to inspect the active controller and `codex_controller_reload` to reload the controller or its policy. The policy-only aliases `codex_controller_policy_status` and `codex_controller_policy_reload` remain available for settings such as queue-first dispatch, wakeup timeout caps, inline wait caps, thread naming before prime, and prime prompt title fallback. See `docs/controller-policy-runtime.md`.
