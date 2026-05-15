# Agent Thread Usage

In daily use, the human user should not need to type `dotnet run` commands or start a separate MCP process. The normal human path is a single bootstrap command. The normal agent path is MCP.

## One-Time User Start

If live Desktop wakeups are required, start Codex Desktop through CodexTeamUp:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-codexteamup.ps1
```

To return to normal Codex behavior:

1. Close Codex Desktop.
2. Start Codex Desktop normally from the Start menu.

No installed Codex Desktop files are modified.

## What Architect and Worker Threads Should Use

Use MCP tools exposed through the CTU service.

The `scripts\ctu.ps1` shim is for diagnostics, recovery, or local development. Agent threads should not use it for normal communication because shell calls trigger approvals and slow down the team workflow.

## MCP Server

The MCP server is the local CTU HTTP service:

```text
http://127.0.0.1:47319/mcp
```

There is no separate MCP stdio process to start. `CodexTeamUp.Mcp` is only a .NET library for tool registry and metadata; Codex Desktop talks directly to the HTTP MCP endpoint exposed by the service.

The service chooses the correct project AgentBus from `cwd` or `busRoot`. If `cwd` is present, the service uses `<cwd>/.codexteamup/agentbus`.

The startup script registers the MCP server globally as `[mcp_servers.ctu]` with `url = "http://127.0.0.1:47319/mcp"` in the local Codex config under the user's home directory, for example `%USERPROFILE%\.codex\config.toml` on Windows. It also sets `approval_mode = "approve"` for CTU communication tools so worker threads do not fall back into shell approvals such as `ctu.ps1 notify`.

## Example Architect Prompt

You can hand `docs/sample-architect-prompt.md` to `ctu/architect` as a starting instruction file.

## Architect Workflow Through MCP

1. Use `team_discover_agents` for `ctu/architect,ctu/web,ctu/backend,ctu/designer`.
2. Use `team_send_message` for direct questions or lightweight delegation.
3. Use `agentbus_create_task` plus `bridge_dispatch_task` for larger work packages.
4. Use `team_dashboard_export` for monitoring.
5. After a result arrives, review it or create a follow-up task.

If a target agent is not registered yet, `team_send_message` tries to find the visible Codex thread from the agent name automatically.

For low-latency questions, `team_send_message` can be called with `waitResult=true` and a suitable `timeoutSeconds`. That waits for the result file in AgentBus and returns it directly. Without `waitResult`, the call remains asynchronous and the sender is woken later through `bridge_notify_result`.

Any registered `ctu/*` agent can use this pattern, not only `ctu/architect`. A worker can ask another worker directly, for example `ctu/dashboard` to `ctu/ux`, and set `returnTo` to itself.

## Agent Runtime

CTU can store `speed`, `model`, and `reasoningEffort` per agent in `.codexteamup/agentbus/agents.json`. Those values are passed to the Codex app-server when CTU creates or wakes threads through `turn/start`.

If nothing is configured explicitly, CTU defaults new agents to `speed=standard` and `reasoningEffort=medium`. `speed=fast` defaults to `model=gpt-5.4-mini` with `low` effort. `speed=deep` uses `high`, and `speed=max` uses `xhigh`. Explicit `model` or `reasoningEffort` values always win over the speed profile.

## Worker Workflow Through MCP

1. `agentbus_list_tasks`
2. `agentbus_claim_task`
3. Write a short visible-chat note about what you are doing now
4. Read the task file and do the work
5. Leave short visible-chat notes for meaningful decisions, blockers, handoffs, or completed steps
6. `agentbus_write_result`
7. `bridge_notify_result` with the result id so the return agent is woken

The visible chat should not be a black box. AgentBus is the durable audit trail, but each worker chat should still show enough human-readable progress that the user can switch into that thread and understand what the agent is doing without opening raw task or result files. Keep those notes concise: current action, key decision, blocker, handoff, or final outcome.

Workers must not reconstruct replacement tasks from chat text. If a task file mentioned in a wakeup is missing or not addressed to that worker, the worker should only reply with a short visible-chat diagnosis and must not write a result.

## Recovery

If an agent suggests PowerShell such as `ctu.ps1 notify`, that is a configuration or instruction error. Cancel the shell approval and tell the agent to use `agentbus_write_result` plus `bridge_notify_result` through MCP instead.
