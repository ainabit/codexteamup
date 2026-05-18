# Multi-Agent Orchestration Test Strategy

This feature needs a real Codex Desktop smoke path because the risky behavior lives in the Desktop app context: visible threads, wrapper wakeups, thread binding, model settings, and AgentBus handoffs. The deterministic test suite still covers protocol logic, but it is not enough by itself.

The target live scenario is:

1. assume a manually created first controller agent such as `ctu-test/architect` exists in the test project,
2. optionally ask that controller to create or bind run-scoped workers such as `ctu-test/<run-id>/agent-a`,
3. have `agent-a` or the controller create or wake `agent-b` and `agent-c`,
3. verify `agent-b` and `agent-c` can communicate through AgentBus,
4. take the `agent-b` binding out of service and create or bind a replacement,
5. verify `model`, `speed`, and `reasoningEffort` are persisted and passed into wakeups,
6. verify agent-owned continuation and terminal outcome behavior when the controller-suite scenario is used,
7. archive the temporary test chats and mark their AgentBus bindings as retired when cleanup is requested.

## Test Agent Prefix

Live tests must not spam normal project roles. Use a run-scoped test prefix:

```text
ctu-test/<run-id>/agent-a
ctu-test/<run-id>/agent-b
ctu-test/<run-id>/agent-c
```

The run id should be timestamp-like, for example `20260516-143000`. Normal project agents keep the `ctu/*` prefix. The smoke runner refuses cleanup for non-test prefixes unless explicitly forced.

## Live Desktop Smoke

Run only when Desktop was started through CodexTeamUp:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -UseTestWorkspace -Live -LiveScenario basic
```

After changing CTU service, wrapper, or MCP tool registration code, restart Desktop through `scripts/start-codexteamup.ps1` before live smoke tests. The running service must expose the branch's current MCP tools.

The repeatable smoke tests are:

- `controller`: send one task to the manually provided `ctu-test/architect`; the controller performs the run-scoped orchestration from inside Codex Desktop.
- `controller-suite`: send one packaged safety-net task to `ctu-test/architect`; the controller creates workers with distinct display names, roles, models, and reasoning depths, then verifies terminal outcomes and self-continuation evidence.
- `surface`: exercise live CTU MCP and AgentBus surfaces in `codexteamup.test`, including status tools, create/claim/write/wait result flow, result notification to `ctu-test/architect`, and dashboard export.
- `queue-first`: prove `team_send_message dispatchMode=enqueue` returns a pure queue ACK with no inline wakeup, then prove explicit `bridge_dispatch_task` wakes a worker and produces a result.
- `continuation`: prove a real worker writes `self_continue`, CTU waits until the continuation is due, creates a self-continuation task, wakes the same worker, and receives a follow-up `done` result.
- `error-paths`: exercise live negative paths such as missing task dispatch, retired target dispatch, and stale/non-visible thread binding.
- `stale-claimed`: deliberately claim a worker task without a result, wait for controller stale-claim recovery, and verify the recovered task is delivered and completed.
- `basic`: create or bind `agent-a`, wake it, and wait for one AgentBus result.
- `peer`: run `basic`, have `agent-a` create `agent-b` and `agent-c`, and verify `agent-b -> agent-c` communication.
- `replacement`: run `peer`, mark `agent-b` stale, create or bind a replacement, and verify the replacement handles a task.

Useful commands:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -UseTestWorkspace -Live -LiveScenario controller
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -UseTestWorkspace -Live -LiveScenario controller-suite
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -UseTestWorkspace -Live -LiveScenario surface
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -UseTestWorkspace -Live -LiveScenario queue-first
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -UseTestWorkspace -Live -LiveScenario continuation
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -UseTestWorkspace -Live -LiveScenario error-paths
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -UseTestWorkspace -Live -LiveScenario stale-claimed
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -UseTestWorkspace -Live -LiveScenario basic
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -UseTestWorkspace -Live -LiveScenario peer
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -UseTestWorkspace -Live -LiveScenario replacement
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1 -UseTestWorkspace -LiveAll
powershell -ExecutionPolicy Bypass -File .\scripts\test-live-multi-agent-orchestration.ps1 -Workspace S:/_work/_development/codexteamup.test -RunId 20260516-143000 -CleanupOnly
```

`test-codexteamup.ps1` always runs `dotnet build` and the deterministic suite first. With `-UseTestWorkspace`, it creates or validates the sibling `codexteamup.test` workspace and keeps live AgentBus state out of the main repo. Live smoke runs clean up temporary test chats by default. Use `-KeepLiveAgents` only when the visible test threads need manual inspection.

Every `test-codexteamup.ps1` run writes a Markdown safety report under `.codexteamup/reports` unless `-ReportPath` is provided. The report lists each high-level testcase with category, status, failure reason, and details so the run is inspectable without reading the full console log. Use `-LiveAll` as the repeatable "all current CTU features except fresh-clone acceptance" package.

At the end of `-LiveAll`, the runner archives and retires all `ctu-test/*` smoke agents except the manually maintained `ctu-test/architect`, unless `-KeepLiveAgents` is set. This keeps `codexteamup.test` small after full safety-net runs while preserving AgentBus audit files.

During execution, deterministic tests print progress as `RUN <current>/<total> <testcase>` before each testcase and live smoke scenarios print `Running live smoke <current>/<total>: <scenario>`. The goal is that a human can see which capability is being checked even while the run is still active.

Live smoke tool calls use a short per-call timeout, defaulting to 10 seconds, so Desktop/app-server stalls fail with a clear phase instead of blocking the runner for many minutes. Override with `-ToolTimeoutSeconds` only for diagnostics.

The live runner follows ACK/NACK semantics. A direct CTU tool call should quickly enqueue or reject work. Desktop wakeup is a separate dispatch step through `bridge_dispatch_task`. Long completion checks must use explicit AgentBus polling in short chunks, not one long blocking HTTP/MCP call. If Desktop returns an uncertain wakeup response, the runner should still inspect AgentBus for task claims and results before declaring failure.

The daily safety-net path restores and builds into an isolated temp artifact directory so repo-local `bin/obj` locks do not break routine test runs.

Deterministic tests write temporary data under the OS temp directory by default. Set `CTU_TEST_RUN_ROOT` only when a specific test artifact location is needed.

The live smoke runner checks:

- CTU service health,
- wrapper reachability through `codex_thread_list`,
- live status checks for the app-server adapter, controller runtime, and controller policy,
- direct AgentBus create/claim/write/wait flow,
- result notification to the manually provided `ctu-test/architect`,
- dashboard export against the live `codexteamup.test` AgentBus,
- queue-first enqueue acknowledgement versus explicit task dispatch,
- optional controller orchestration through a manually provided `ctu-test/architect`,
- controller-driven agent creation with display names and roles that are intentionally not tied to the agent id,
- creation and priming of `agent-a`,
- `agent-a` creating agents, enqueuing tasks for `agent-b` and `agent-c`, and dispatching those tasks separately,
- runtime settings on `agent-b` and `agent-c`,
- controller-suite evidence markers for `done`, `human`, `failed`, `handed_off`, and agent-owned `self_continue`,
- delayed agent-owned `self_continue` wakeup and follow-up `done` result,
- missing-task, retired-agent, and non-visible-thread negative dispatch paths,
- stale claimed-task recovery through the controller delivery loop,
- peer communication from `agent-b` to `agent-c`,
- replacement of a stale `agent-b` binding,
- replacement wakeup and result.

Cleanup uses `codex_thread_archive` for visible test chats and then re-registers matching AgentBus agents with `status=retired`. It does not delete AgentBus tasks, results, or events; those are audit history for the smoke run.

## Optional SDK Probe

`scripts/test-codex-sdk-probe.ps1` is an optional diagnostic for the experimental Codex Python SDK. It checks whether `openai_codex` is installed and can initialize a headless Codex app-server session. This is not a replacement for visible Desktop smokes because CodexTeamUp acceptance still requires visible Codex Desktop chats, but it can separate SDK/runtime availability from Desktop-wrapper issues.

## Tester Handoff

`ctu/tester` owns the repeatable safety-net workflow. When the user asks for CTU tests, smoke tests, or proof that agent spawning works, the architect should notify `ctu/tester` through AgentBus.

The tester should choose the smallest useful set:

- normal fixes: `test-codexteamup.ps1`
- wakeup, binding, runtime, MCP, AgentBus, service, or wrapper changes: deterministic safety net plus one live scenario
- broad orchestration changes: deterministic safety net plus `-LiveAll`

Use `controller-suite` as the frequent packaged CTU-in-Desktop proof when the goal is to verify the whole coordination loop through `ctu-test/architect` rather than through the architect chat that is currently developing CTU.

The tester result must include the commands used, pass/fail status, live run id, cleanup status, and any blockers.

## Deterministic Local Tests

These are covered by:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1
```

They use a fake app-server and a temporary `.codexteamup/agentbus`. They must not depend on Codex Desktop, real LLM output, wall-clock chat timing, or user interaction.

### 1. Team Creation And Priming

Use `team_ensure_agents` with `agentsJson` for:

- `ctu-test/run/agent-a`: `speed=deep`, `reasoningEffort=high`
- `ctu-test/run/agent-b`: `speed=fast`, no explicit model or effort
- `ctu-test/run/agent-c`: explicit `model=gpt-5.5`, `reasoningEffort=xhigh`, `speed=max`

Assertions:

- `agents.json` contains exactly the requested test agents with expected `threadId` values,
- default runtime resolution is persisted,
- explicit runtime values win over speed defaults,
- each prime prompt includes role, allowed paths, instruction files, runtime settings, and strict AgentBus task handling rules,
- events include `agent.created` and `agent.primed`.

### 6. Restart Orchestration Slice 1 (Deterministic)

Use `ctu_restart_request` and `ctu_restart_status` checks through deterministic fake adapters, exercising the production restart model in
`src/CodexTeamUp.AgentBus/RestartOperations.cs` directly.

Required assertions for the first slice:

- restart operation records are written to `.codexteamup/restart/operations/<operationId>.json`.
- required fields are present for `ctu.desktop-restart` records (`requestedByAgentId`, `sourceCwd`, `targetCwd`, `targetAgentId`, `continueTitle`, `continuePrompt`).
- invalid target checkout requests fail with deterministic errors (`targetCwd` missing, same as source).
- status transitions are durable and idempotent for legal paths:
  `prepared -> helper_started -> stopping_source -> starting_target -> target_healthy -> continuation_enqueued -> continuation_dispatched -> completed`.
- terminal states (`completed`, `rolled_back`, `failed`) are stable on repeated transition writes and set completion metadata.
- fallback config is preserved in the record (`fallbackCwd`, `fallbackBusRoot`).

### 7. Restart Live Proof Contract (Not in deterministic suite)

The first live proof remains manual and should be limited in scope:

1. start source orchestration checkout on `codexteamup`,
2. request restart into a prepared target checkout (`codexteamup.acceptance`),
3. verify `ctu/architect` receives `Continue after restart` result/task with `ctu_continue` intent,
4. run `scripts/test-codexteamup.ps1` smoke for the target to verify continuation health,
5. request restart back into `codexteamup`,
6. verify architect receives and can continue the run from the previous continuation point.

Evidence bundle expected in the tester result:

- commands run,
- operation ids and their final status,
- continuation task id,
- target assistant confirmation message captured in AgentBus events,
- pass/fail + cleanup status.

### 2. Agent A Enqueues Work For B And C

Invoke `team_send_message` twice:

- from `ctu-test/run/agent-a` to `ctu-test/run/agent-b`,
- from `ctu-test/run/agent-a` to `ctu-test/run/agent-c`.

Assertions:

- two task files are created under `tasks/open`,
- each task has the expected `from`, `to`, `returnTo`, and cwd,
- no Desktop wakeup is attempted by default,
- events include `team.message.enqueued`.

### 3. Dispatch And Waited Result

Invoke `bridge_dispatch_task` for a queued task to `agent-c`. The fake app-server callback should parse the task id from the wakeup message, claim the task as `agent-c`, and write a result to `agent-b`. Then use `agentbus_wait_result` in a short chunk.

Assertions:

- the wait response completes with the synthetic result,
- the task moves from `open` to `claimed` to `done`,
- exactly one result exists for the task,
- dashboard snapshot shows the `agent-b -> agent-c` communication edge.

### 4. Agent B Replacement Or Rebind

Seed `agents.json` with `agent-b` bound to an old or stale thread id. Then run one of two deterministic variants:

- replacement: fake app-server list omits the old thread, `createMissing=true`, and `StartThreadAsync` returns a new thread,
- rebind: fake app-server list contains a visible thread with the requested display name and `createMissing=false`.

Assertions:

- registry now points `agent-b` to the new thread,
- role, allowed paths, instruction files, return target, and runtime settings remain as requested,
- subsequent `team_send_message` enqueues for the replacement agent and `bridge_dispatch_task` wakes the new thread, not the old thread.

### 5. Runtime Settings On Every Wakeup Path

Cover all paths that call `turn/start`:

- priming from `team_ensure_agents`,
- task dispatch from `bridge_dispatch_task`,
- result notification from `bridge_notify_result`,
- direct `codex_turn_start`.

Assertions:

- fake app-server receives `AppServerAgentSettings` matching the registered or explicitly supplied runtime,
- `speed=fast` resolves to `model=gpt-5.4-mini` and `effort=low`,
- `speed=deep` resolves to `effort=high`,
- `speed=max` resolves to `effort=xhigh`,
- explicit `model` and `reasoningEffort` override speed defaults,
- invalid speed or effort values fail fast with a clear error.

## Risks And Boundaries

- Assert protocol facts, not prose quality.
- Keep default CI independent from Codex Desktop, wrapper named pipe availability, Desktop UI timing, and real model behavior.
- Do not automate Desktop UI clicking unless there is a stable first-party automation surface.
- Do not assert exact timestamps or latency beyond presence and non-negative shape.
- Do not delete live thread history during cleanup; archive temporary chats and retire test bindings.

## PR Guidance

Use `feat:` for PRs that add the orchestration coverage. Use `fix:` only when correcting broken runtime propagation, stale binding behavior, or AgentBus event persistence found while adding the tests.
