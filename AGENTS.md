# CodexTeamUp Target State

CodexTeamUp is a local team-bootstrap and inter-thread communication system for visible Codex Desktop chats.

The goal is not a PowerShell-centric daily workflow. PowerShell is only for bootstrap and recovery. MCP is the agent-facing interface so Codex threads can use tools instead of typing shell commands for normal coordination.

## Core Principle

CodexTeamUp does not decide which roles a project needs. That decision belongs to the initial architect thread or another AI working from the project material.

CodexTeamUp executes the coordination layer:

- discover visible Codex Desktop threads,
- create missing threads for requested `ctu/*` agents,
- register project agents,
- prime threads with role and file guidance,
- coordinate tasks, results, events, and wakeups.

## Agent Names

Agents use IDs in this format:

```text
ctu/<role>
```

Examples:

- `ctu/architect`
- `ctu/web`
- `ctu/backend`
- `ctu/designer`
- `ctu/reviewer`
- `ctu/schema`

## Intended Usage

A project can have visible Codex Desktop threads like:

- `ctu/architect`
- `ctu/web`
- `ctu/backend`
- `ctu/designer`

The user primarily talks to `ctu/architect`. The architect plans a feature with the user. When work belongs to another role, the architect creates a work package for that role and wakes the target thread through CodexTeamUp.

Worker threads operate in their own visible chat windows. They can later send results, questions, or status updates back to `ctu/architect`. The user can also switch into a worker thread and continue there directly at any time.

Worker chats should stay human-readable while they work. AgentBus is the durable task/result protocol, but visible threads should still show concise progress notes: what the agent is doing, meaningful decisions, blockers, handoffs to other agents, and final outcome. The user should not have to open raw AgentBus files to understand the current shape of the work.

## Team Bootstrap

The user starts an architect thread and can provide a prompt such as `docs/ai-team-bootstrap-prompt.md`.

The architect reads the project documentation and decides:

- which `ctu/*` agents are needed,
- what role each agent has,
- which directories each agent should mainly edit,
- which role or instruction markdown files each agent should read.

The architect then uses CodexTeamUp MCP, for example `team_ensure_agents`, and passes that exact agent list. CodexTeamUp does not invent roles on its own.

Implementation work should normally be delegated by `ctu/architect` to dedicated developer agents such as `ctu/service`, `ctu/wrapper`, or explicit `ctu/developer*` roles. The architect owns scope, architecture, sequencing, and final acceptance. The architect should only make implementation edits directly for unusually hard critical-path work or small final corrections.

Keep the active team small. When a temporary worker or developer agent is no longer needed, retire or archive it instead of letting stale threads accumulate. This is especially important for ad hoc `ctu/developer*` agents so they do not keep obsolete context from discarded designs, failed experiments, or already-finished slices.

## Project-Local State

Each project can keep its own CodexTeamUp directory:

```text
<repo-root>\.codexteamup
```

The durable exchange layer lives under:

```text
<repo-root>\.codexteamup\agentbus
```

Role and agent markdown files do not need to live inside `.codexteamup`. They can live in domain-appropriate project locations, for example:

- `docs/agents/web.md`
- `web/AGENTS.md`
- `docs/design/agent.md`
- `backend/AGENTS.md`

Project structure should follow the application and the project goals, not the CodexTeamUp implementation.

## Technical Core

- Agent interface: MCP
- Backend: `CodexTeamUp.Service` as a local HTTP service on `127.0.0.1`
- Desktop wakeup path: the service uses `thread/resume` plus `turn/start` against the visible Desktop app-server through the wrapper
- Durable truth: `.codexteamup/agentbus` stores tasks, results, events, messages, and audit history
- Desktop adapter: the named pipe is only an internal detail between the service and the wrapper
- PowerShell: bootstrap, publish, and recovery only
- UI: local HTTP dashboard for communication, content, runtimes, and status

## Desktop Adapter Boundary

Treat the Codex Desktop app-server as an unstable edge dependency. Keep AgentBus, MCP tool contracts, team orchestration, and agent bindings as the stable core. Put Desktop-specific lifecycle behavior behind the app-server adapter boundary.

The service uses a reloadable `IAppServerClient` facade. The default adapter is the built-in wrapper-pipe implementation, but adapter plugins can be loaded at runtime with `codex_appserver_adapter_reload` or `POST /api/appserver-adapter/reload`. Use `codex_appserver_adapter_status` or `GET /api/appserver-adapter` to inspect which adapter is active.

Adapter plugins are for fast fixes to Desktop-specific behavior such as thread readiness polling, missing-rollout retries, app-server timeout policy, and unsafe Desktop calls. If a plugin reload fails, the currently active adapter must remain active. See `docs/appserver-adapter-plugins.md`.

## Runtime Layers And Logging

Keep CTU split into two runtime layers:

- Fixed API adapter layer: MCP/HTTP service boundaries and the `IAppServerClient` app-server adapter. This layer owns transport, JSON-RPC, redaction, timeout plumbing, and defensive try/catch logging. It should change only for new hard API surface or transport contracts.
- Hot-reloadable controller logic layer: flow policy for agent/thread orchestration, including enqueue vs inline dispatch, wakeup timeout caps, wait caps, naming before prime, retry timing, and prompt fallback behavior. This layer must be reloadable at runtime where practical, for example through `codex_controller_reload`, `POST /api/controller/reload`, `codex_controller_policy_reload`, or `POST /api/controller-policy/reload`.

Do not put volatile Desktop timing and orchestration policy into the fixed API adapter layer. Prefer controller policy/script/plugin changes that can be reloaded without killing the CTU service.

The normal controller implementation is a loaded controller plugin DLL, not hardcoded service logic. The default controller may be shipped as `CodexTeamUp.Controller.Default.dll` and loaded automatically when no replacement is configured. If no controller plugin is loaded, CTU must report an explicit unloaded/error state and keep only reload/status controls available; it must not silently fall back to a non-hot-swappable built-in workflow controller.

Controller runtime files live under `.ctu/runtime/controllers/default` by default. Build and test outputs must stay isolated from the running CTU runtime; do not run the service from `src/**/bin` or depend on `src/**/obj`. `scripts/start-codexteamup.ps1` remains the KISS user entrypoint and must publish or refresh the runtime as needed. For fast controller-only development, publish the controller DLL into the runtime directory and call `codex_controller_reload` without restarting CTU. If an active controller DLL is locked, the publish script may place a versioned runtime under `.ctu/runtime/controllers/default/releases/...` and update `current-plugin.txt`; reload that published plugin path instead of killing CTU.

The service writes project-local JSONL and human `.log` files under `.codexteamup/logs` by default:

- `api-adapter-YYYYMMDD.jsonl` for hard app-server/API adapter calls and failures.
- `api-adapter-YYYYMMDD.log` for human-readable hard app-server/API adapter diagnostics.
- `controller-YYYYMMDD.jsonl` for MCP/controller tool calls, policy reloads, and orchestration failures.
- `controller-YYYYMMDD.log` for human-readable controller diagnostics.
- `wrapper-YYYYMMDD.jsonl` for Codex CLI wrapper invocations, parent/bridge decisions, and pipe diagnostics.
- `wrapper-YYYYMMDD.log` for human-readable wrapper diagnostics.

Logging is diagnostic only. Logging failures must never break CTU. Logs must redact obvious secrets and avoid dumping full prompts when a short preview, task id, result id, thread id, or error message is enough.

Codex Desktop may start multiple wrapped `app-server` processes. Only the visible Desktop app-server should expose the CTU bridge pipe by default; otherwise CTU can connect to a non-visible helper app-server and wakeups become nondeterministic. Use `CODEX_WRAPPER_BRIDGE_MODE=all` only for explicit diagnostics when every wrapper instance should expose the bridge pipe.

Visible Codex Desktop threads should be named with their agent id/display name. Normal controller policy should pass the display name at thread creation and call the explicit thread naming path before the first prime turn when a thread is created or rebound. For short ACK/NACK live smoke paths, the controller may use `prime=false setName=false` to avoid fragile Desktop rename or prime calls before the AgentBus registration is usable; those paths must still pass the intended display name into thread creation and include the exact agent id in the first dispatched task or prime fallback. The first line of every prime prompt should also be the exact agent id as a fallback for Desktop title generation.

`docs/architecture/README.md` is the binding architecture entrypoint. Read it before changing CTU runtime, MCP, app-server adapter, controller, AgentBus, exchange, logging, acceptance flow, or orchestration code.

Architecture governance is split deliberately:

- `docs/architecture/**`: binding current-state architecture rules
- `docs/adr/**`: durable decision history and rationale
- `docs/initiatives/**`: active execution tracks and definition of done
- `docs/operations/**`: startup, testing, recovery, and runtime inspection guidance
- `docs/logbook.md`: reverse-chronological journal of notable CTU milestones, failures, and redesigns

New runtime changes must preserve the architecture split: MCP and app-server adapters stay thin; workflow lives in the hot-reloadable controller runtime; AgentBus remains CTU's internal durable truth; external ingress/egress and restart startup handoffs use the exchange boundary.

## Reactivity And ACK/NACK

CodexTeamUp coordination must keep every visible thread reactive. Do not design agent-to-agent calls that block a thread for minutes.

Use a short ACK/NACK pattern:

- direct MCP/HTTP calls should return within about 10 seconds whenever possible;
- a call should acknowledge that a task/message was accepted, deferred, or failed to enqueue;
- long work must continue asynchronously through AgentBus task/result files;
- prefer queue-first communication: create AgentBus tasks first, then dispatch/wake threads as a separate best-effort step;
- do not fan out Desktop `turn/start` calls in parallel; the controller should serialize wakeups or otherwise throttle them because the Desktop app-server can cancel otherwise valid calls under bursty load;
- callers that need completion should poll or call `agentbus_wait_result` in short chunks instead of holding one long tool call;
- `team_send_message` defaults to enqueue-only. Use `bridge_dispatch_task` to wake the target thread, or `dispatchMode=inline` only for narrow compatibility/debug cases;
- `team_send_message` with `waitResult=true` is only for quick acknowledgements or very short answers and should not be used as a cross-agent RPC primitive. Longer work should use asynchronous enqueue, explicit dispatch, and result polling.

Live tests and adapters should prefer short per-call timeouts and explicit polling. A Desktop app-server timeout is not by itself proof that an agent did not receive work; AgentBus events/results are the durable truth.

## Operating Model

The normal way to start Codex Desktop with CodexTeamUp enabled is:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-codexteamup.ps1
```

The startup script is responsible for a fresh CTU Desktop session. It detects existing Codex Desktop, CTU service, CTU wrapper, and repo-local CTU test/helper processes, asks before stopping them, and aborts if it cannot prove they were stopped. Use `-ForceStopExisting` only when an unattended recovery start should stop those existing processes without prompting.

If Codex Desktop is started normally, inter-thread communication is not active. If Desktop is started through the script, CodexTeamUp is active for that Desktop session. Each project still chooses its own workspace through `cwd`.

## Test Safety Net

Before a PR, run the deterministic safety net:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1
```

The script restores and builds into an isolated temp artifact directory so repo-local `bin/obj` locks do not break routine test runs.

For coverage evidence, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -Coverage
```

Coverage uses the repo-local .NET tool manifest and `coverlet.console`. The default line threshold is 80 percent. If the current baseline is below that threshold, add focused tests rather than lowering the architecture target.

New production behavior must come with focused deterministic tests. Live smoke tests are evidence for runtime integration; they do not replace unit or deterministic controller/service tests. When you add or change controller flow, restart behavior, AgentBus semantics, exchange/channel logic, or service/API behavior, write or extend tests in the same branch before treating the slice as done.

For changes to CTU service, wrapper, MCP tools, AgentBus, thread binding, wakeups, runtime settings, or agent orchestration, also run at least the relevant live Codex Desktop smoke test:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -UseTestWorkspace -Live -LiveScenario basic
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -UseTestWorkspace -Live -LiveScenario peer
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -UseTestWorkspace -Live -LiveScenario replacement
```

Use `-UseTestWorkspace -LiveAll` before merging broad orchestration or wrapper changes. The default test workspace is the sibling directory `codexteamup.test`. Live smoke agents use the `ctu-test/<run-id>/...` prefix and should be cleaned up by the runner unless manual inspection is needed.

For a clean-checkout onboarding proof after CTU Desktop startup, use:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-fresh-clone-acceptance.ps1
```

Run that inside a real fresh acceptance clone, for example `S:/_work/_development/codexteamup.acceptance`. It verifies service, MCP, dashboard snapshot, the deterministic suite, and the minimal live `basic` scenario.

Controller-style live smoke tests may assume the test workspace has one manually created initial controller chat named `ctu-test/architect`. That controller is the in-Desktop starting point for run-scoped worker creation and coordination inside `codexteamup.test`; the runner should bind that existing chat and enqueue one controller task instead of inventing a new controlling role.

After changing CTU service, wrapper, or MCP tool registration code, restart Codex Desktop through `scripts/start-codexteamup.ps1` before live smoke tests. Otherwise the running service may not expose the new tool surface.

When the user asks for the CTU tests or smoke tests, notify `ctu/tester` through CodexTeamUp and ask it to run or coordinate the smallest relevant test set:

- normal fixes: deterministic safety net
- service, wrapper, MCP, AgentBus, wakeup, runtime, or binding changes: deterministic safety net plus `basic`, `peer`, or `replacement`
- broad orchestration changes: deterministic safety net plus `-LiveAll`

The tester should report exact commands, pass/fail status, run id for live tests, cleanup status, and any blockers.

## Code Documentation

Keep source code lightly documented for human maintainers.

- Add short English summary comments for important classes, records, and interfaces.
- The summary should say what the type owns or is responsible for, not restate the type name.
- Keep the comments brief. One or two short sentences is enough.
- Add or update the summary when a class changes responsibility in a meaningful way.
- Prefer type-level documentation over noisy inline comments. Use inline comments only where local control flow is genuinely non-obvious.

## Codex Desktop Git Directives

Codex Desktop currently has a renderer issue when a Git app directive contains a Windows path with backslashes in its `cwd` value. Such responses can trigger the Desktop Oops screen when old thread history is loaded.

Always use forward-slash paths in Git app directives, for example `X:/repo/codexteamup`. Do not quote or reproduce the broken backslash form as a raw app directive in final answers, documentation, or agent instructions.

The CTU wrapper can sanitize newly generated answers, but historical session history remains a separate problem and may still need cleanup or avoidance.

## Pull Requests

Pull request titles use a short conventional prefix:

- Fixes start with `fix: ` followed by a short title.
- Features start with `feat: ` followed by a short title.

## Non-Goals

- No manual copy and paste between chats
- No manual thread-id maintenance by the user
- No CTU-owned role selection
- No app patching as the standard solution
- No blind production sends without a traceable AgentBus entry
- No PowerShell-centric daily workflow

## Acceptance

CodexTeamUp is only meaningfully usable when this flow works:

1. The user starts Codex Desktop through the startup script.
2. The user talks to `ctu/architect`.
3. `ctu/architect` reads the project material and decides the team.
4. `ctu/architect` calls CodexTeamUp to create or bind the requested `ctu/*` agents.
5. Worker threads are visibly woken and primed with role and file guidance.
6. Workers operate in their own chat context.
7. Workers send a result or question back to `ctu/architect`.
8. `ctu/architect` evaluates the result, replans, or commits.
9. The user can see in a UI which communication happened, from whom to whom, with content, status, and runtimes.
