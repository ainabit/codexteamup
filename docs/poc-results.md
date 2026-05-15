# PoC Results

Status date: 2026-05-14.

## Key Questions

Can we list Codex threads locally?

- Yes, read-only from persisted JSONL rollouts and `session_index.jsonl`.
- No, not live from the visible Desktop app-server, because the expected control socket is not reachable.

Can we read a specific thread?

- Yes, from local rollout JSONL with metadata and redacted previews.
- Not yet verified through `thread/read` against the live visible Desktop app-server path in the general case.

Can we start a new thread?

- Formally yes: `thread/start` exists in the schema and the CLI command is implemented.
- Practically not verified end to end against a visible Desktop transport.
- A separate `ws://127.0.0.1:<port>` app-server can be started and queried directly, but Desktop does not dock to it reliably enough for the normal login and UI path.

Can we trigger an existing visible thread with `turn/start`?

- Formally yes: `turn/start` exists in the schema and is implemented.
- On the originally observed Desktop path, not through the missing control-socket route.
- On the wrapper side-channel path, yes: a resumed visible thread can be triggered when Desktop is started through the release wrapper.

Will that show up in Codex Desktop?

- The direct side-channel proof did show a visible reply from the target thread.
- UI ordering still needed mitigation because Desktop defaulted to descending turn order and could replay old history after `thread/resume`.

Can a worker thread trigger the architect thread after completion?

- Yes at the workflow level through `.codexteamup/agentbus` plus notify and wakeup paths.
- Live Desktop behavior still depends on the wrapper-backed transport rather than an official public Desktop control transport.

Is MCP realistic for this?

- Yes, as the agent-facing layer for AgentBus, state reads, and optional app-server calls.
- MCP does not solve Desktop transport by itself; it still needs a working local control path.

## Recommended Minimal Architecture

- `.codexteamup/agentbus` as the durable source of truth
- `CodexTeamUp.AppServer` as an optional experimental adapter
- CLI first, then MCP using the same core services
- no Codex app modification and no app patching as the default solution

## What Works

- .NET 10 solution with CLI, Core, AppServer adapter, AgentBus, and test runner
- Codex CLI discovery
- JSON schema generation
- manual schema-generation probes under the real Windows user
- extraction of formal methods from the schema
- separate `codex app-server --listen unix://<workspace-sock>` can create a control socket
- separate `codex app-server --listen ws://127.0.0.1:<port>` can start and answer ready checks
- direct WebSocket JSON-RPC against the separate app-server works for `initialize` and `thread/list`
- `CodexTeamUp.CodexWrapper` can transparently delegate to the real CLI
- local manual validation showed that Codex Desktop starts its local app-server through `CODEX_CLI_PATH`
- the named-pipe side-channel can inject `initialize` and `thread/list` into a stdio app-server
- live wrapper breakthrough: `thread/resume` plus `turn/start` worked against a visible Desktop thread and received a visible reply
- ordering mitigations are implemented for turn sorting and live timestamp stamping
- wakeups now use `excludeTurns=true` on resume to avoid replaying old history as a fresh live block
- persisted thread listing from local Codex state files
- thread read from JSONL rollouts
- `.codexteamup/agentbus` init, create, list, claim, result, and event flows
- dispatch and notify commands with confirmation and error paths

## What Does Not Work Yet or Remains Unclear

- the expected Desktop app-server control socket is not reachable
- standalone `codex app-server --listen stdio://` is not the same as the visible Desktop app-server
- `codex app-server proxy` cannot be used as a simple JSONL client
- several live Desktop methods still need broader manual confirmation beyond the demonstrated path
- `CODEX_APP_SERVER_WS_URL` does not currently provide a stable normal Desktop login and UI flow
- request injection into the real running Desktop app-server still needs more hardening for approvals, conflicts, and additional methods
- UI live update behavior, agent start flows, and active-thread conflict handling need more validation
- SQLite state reads are still intentionally unimplemented

## Verification Performed

```powershell
dotnet build
dotnet run --project tests\CodexTeamUp.Tests
dotnet run --project src\CodexTeamUp.Cli -- codex info
dotnet run --project src\CodexTeamUp.Cli -- codex schema --out .\.ctu\schemas\verify
dotnet run --project src\CodexTeamUp.Cli -- threads list --source state --limit 5
dotnet run --project src\CodexTeamUp.Cli -- threads read --source state --thread-id <test-id> --include-turns
dotnet run --project src\CodexTeamUp.Cli -- bus init --bus-root .\.ctu\verify-agentbus
powershell -ExecutionPolicy Bypass -File .\scripts\probe-codex-app-server.ps1 -GenerateSchemas
powershell -ExecutionPolicy Bypass -File .\scripts\start-codex-desktop-with-ws-appserver.ps1 -ProbeOnly -Port 8766
powershell -ExecutionPolicy Bypass -File .\scripts\start-codex-desktop-with-ws-appserver.ps1 -Stop
powershell -ExecutionPolicy Bypass -File .\scripts\start-codex-desktop-with-cli-wrapper.ps1 -NoLaunch
powershell -ExecutionPolicy Bypass -File .\scripts\invoke-codex-wrapper-rpc.ps1 -Method "thread/list" -ParamsJson '{"limit":1,"useStateDbOnly":true}'
```

## Next Steps

- build a real `ws://127.0.0.1:<port>` client in `CodexTeamUp.AppServer`
- with explicit confirmation, test a dedicated thread through the separate app-server and verify whether Desktop sees it after restart
- keep looking for an official or stable local Desktop control transport
- extend the wrapper into a more robust JSON-RPC stdio proxy where appropriate
- add an optional SQLite reader only if a dependency strategy is agreed
- continue using the existing services as the basis for the MCP surface

## Implementation Update 2026-05-14

The production-facing implementation pass added:

- wrapper protocol helpers for bridge request ids, Desktop request rewriting, and JSON-RPC summaries
- a real `WrapperPipeAppServerClient` for the existing named-pipe side-channel
- CLI commands for wrapper status, RPC, thread resume, turn listing and waiting, AgentBus registry, result wait, delegate, dispatch, and notify
- AgentBus registry, failed-task handling, prompt file tracking, richer result metadata, and file-watcher-based result waiting
- a lightweight `CodexTeamUp.Mcp` JSONL tool host that exposes AgentBus and app-server tools through the same services as the CLI
- sample-project examples and convenience commands for agent initialization and web delegation

Automated verification:

- `dotnet build` passes
- `dotnet run --project tests\CodexTeamUp.Tests` passes
- CLI smoke checks for help, AgentBus init, task creation, and sample agent initialization pass

Manual Desktop verification remains intentionally explicit: use a dedicated test thread, start Desktop through the wrapper, then run `ctu wrapper status`, `ctu threads list --source wrapper`, and a confirmed `ctu dispatch` or `ctu threads send`.
