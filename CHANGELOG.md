# Changelog

All notable CodexTeamUp changes are tracked here by date. The project does not use formal version numbers yet.

## 2026-05-17

### Dashboard Flow Visibility

- Added a dashboard flow overview band for high-level situation awareness.
- Surfaced open, claimed, and stuck work counts before the detailed communication list.
- Added route-map and latest-handoff summaries so users can see agent-to-agent movement before drilling into task/result/event payloads.
- Sorted stuck and active flows ahead of completed flows in the dashboard model.
- Added `docs/ctu-roadmap-and-execution-plan.md` as the persistent planning artifact maintained by `ctu/projectlead`.

### Runtime Architecture Redesign

CodexTeamUp now has a sharper split between stable API access and volatile orchestration logic.

The fixed layer is the service/API boundary: HTTP, MCP tool registration, app-server adapter plumbing, wrapper transport, JSON-RPC shape, redaction, timeout plumbing, and defensive logging. This layer should change only when CTU needs new hard API surface or a transport contract changes. Those changes may still require a full CTU restart because they affect the service process or wrapper process itself.

The workflow layer is the controller runtime. Agent and thread orchestration now lives in a reloadable controller plugin rather than in hardcoded service logic. This includes queue-vs-inline dispatch policy, wakeup caps, wait caps, naming before prime, prompt fallback behavior, retry/defer behavior, and live smoke orchestration. The default implementation is shipped as `CodexTeamUp.Controller.Default.dll` and loaded through the same controller reload path as any replacement controller. If no controller plugin is loaded, CTU reports an explicit unloaded/error state instead of silently falling back to hidden workflow logic.

This redesign exists because Codex Desktop behavior around visible threads, wakeups, readiness, naming, and timing can change or become temporarily fragile. CTU should not need a full process restart for every policy/timing fix in that volatile area. The intended development loop is: change the controller, publish it into `.ctu/runtime/controllers/default`, call controller reload through MCP or HTTP, and immediately test again.

### Hot Reload And Runtime Isolation

- Added `src/CodexTeamUp.Controller.Default` as the default reloadable controller plugin project.
- Moved the default workflow controller out of the core controller project into the plugin project.
- Added `scripts/publish-controller-runtime.ps1` for controller-only publish and reload workflows.
- Updated `scripts/publish-ctu.ps1` and `scripts/start-codexteamup.ps1` so the running controller runtime lives under `.ctu/runtime/controllers/default`.
- Kept source build outputs under `src/**/bin` and `src/**/obj` separate from the runtime used by a running CTU instance.
- Added support for `current-plugin.txt` so startup can use the last published controller plugin path.

### Controller Policy And Reactivity

- Kept `team_send_message` queue-first by default.
- Added short ACK/deferred behavior for agent creation and task dispatch paths.
- Added short result polling behavior for `agentbus_wait_result`.
- Serialized Desktop wakeups in the controller to avoid bursty `turn/start` cancellations from the Desktop app-server.
- Added support for `prime=false` and `setName=false` on agent creation/ensure flows for short recovery and smoke-test paths.
- Treated Desktop wakeup as best-effort delivery while keeping AgentBus tasks/results/events as the durable source of truth.

### Logging And Diagnostics

- Added project-local human `.log` files beside JSONL diagnostics under `.codexteamup/logs`.
- Added wrapper diagnostics:
  - `wrapper-YYYYMMDD.jsonl`
  - `wrapper-YYYYMMDD.log`
- Kept controller and API adapter diagnostics split so failures can be traced to the fixed adapter layer or the reloadable controller layer.
- Logging remains diagnostic-only and must not break CTU behavior.

### Wrapper Bridge Selection

- Changed wrapper bridge exposure so only the visible Desktop app-server exposes the CTU bridge pipe by default.
- Added `CODEX_WRAPPER_BRIDGE_MODE=all` for diagnostics when every wrapped app-server should expose the bridge.
- Added `CODEX_WRAPPER_BRIDGE_MODE=none` to disable bridge exposure.
- Documented why exposing every wrapped app-server can make wakeups nondeterministic when helper app-server processes exist.

### Live Smoke Tests

- Hardened `scripts/test-live-multi-agent-orchestration.ps1`.
- Added live Codex Desktop smoke coverage for:
  - creating and waking one visible test agent,
  - having one agent create peer agents,
  - replacing a stale agent binding with a new visible thread,
  - verifying model/speed/reasoning settings on test agents.
- Live smoke tests use the `ctu-test/<run-id>/...` prefix and may assume a manually created initial `ctu-test/architect` chat in the test workspace.

### Documentation

- Added the controller/runtime split to `AGENTS.md`.
- Added `docs/ctu-runtime-architecture.md` as the binding runtime architecture reference.
- Expanded `docs/controller-policy-runtime.md`, `docs/mcp-tools.md`, and `docs/wrapper-side-channel.md`.
- Added a README "What's New" section that summarizes the runtime redesign for new readers.

### Verification

- Deterministic safety net passed.
- Coverage gate passed with total line coverage above 80 percent.
- Live Codex Desktop smoke tests passed for `basic`, `peer`, and `replacement`.
