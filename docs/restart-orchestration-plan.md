# Restart Orchestration Plan

## Goal

CodexTeamUp should be able to switch the active Desktop/CTU session between checkouts such as:

- `S:/_work/_development/codexteamup`
- `S:/_work/_development/codexteamup.acceptance`

without relying on the dying source thread to "remember" the handoff in chat history.

The operator-level experience should become:

1. `ctu/architect` decides it needs to move to another checkout.
2. `ctu/architect` requests `restart me`.
3. CTU writes a durable restart record and a continuation intent.
4. An external helper restarts Desktop/CTU into the target checkout.
5. The target architect thread receives a concrete continue task and resumes work.
6. If target startup fails, the helper can return to the last stable checkout.

## Core Design

### 1. No direct in-process self-restart

The requesting agent may ask for restart, but it must not be the only process that owns shutdown/startup progression.

Reason:

- the current Desktop thread disappears during restart,
- the current CTU service/wrapper disappear during restart,
- visible chat text is not a durable control channel.

Restart therefore uses a supervisor pattern:

- controller writes durable intent,
- external helper executes stop/start,
- target session resumes from durable state.

### 2. AgentBus remains durable truth

Restart intent must not live only in chat text or in controller memory.

The restart subsystem gets its own durable records under project-local state, for example:

```text
.codexteamup/restart/operations/<operation-id>.json
```

The continuation itself is also durable:

- either encoded in the restart operation record,
- or materialized as an AgentBus task for the target architect after the target runtime is healthy.

### 3. Thin MCP surface, workflow in controller/helper

MCP should only expose a thin request/status API. The workflow belongs in:

- the hot-reloadable controller runtime for request creation,
- the external restart supervisor/helper for process orchestration,
- the durable restart store for state transitions.

This preserves the existing architecture split:

- MCP/API stays thin,
- orchestration stays out of the fixed adapter boundary.

### 4. Restart supervisor is a runtime layer, not an app-server adapter

The restart executor is allowed to live out of process because the source session is intentionally being replaced.

Treat that executor as its own runtime concern:

- it may be hosted in `CodexTeamUp.Cli` for slice 1,
- it must consume the shared durable restart model,
- it must not embed ad hoc restart state semantics that differ from the shared store,
- it must remain independent from Desktop app-server adapter internals.

This keeps the hard adapter boundary narrow while still allowing a supervised process handoff.

### 5. Slice 1 stays CTU-specific; generic app restart is a later layer

For slice 1 the MCP contract should stay explicit and CTU-specific:

- `ctu_restart_request`
- `ctu_restart_status`

Reason:

- the first real problem is switching CTU between known local checkouts,
- the continuation contract is CTU-specific because it resumes through AgentBus and target architect roles,
- generic restart abstractions too early would hide critical CTU invariants such as `targetBusRoot`, target agent identity, and continuation task dispatch.

Future generalization can happen one layer lower as a reusable supervisor contract, for example:

- `app_restart_request`
- `app_restart_status`

or a restart plugin/supervisor abstraction that CTU can call. That should happen only after the CTU-specific slice is stable and proven.

## First Implementation Slice

### Durable restart record

Add a .NET 10 restart operation store and record model.

Suggested fields:

- `id`
- `kind` (`ctu.desktop-restart`)
- `status`
- `requestedAt`
- `completedAt`
- `requestedByAgentId`
- `sourceCwd`
- `sourceBusRoot`
- `targetCwd`
- `targetBusRoot`
- `targetAgentId`
- `fallbackCwd`
- `fallbackBusRoot`
- `continueTitle`
- `continuePrompt`
- `expectedTargetBranch` (optional)
- `helperPid` (optional)
- `lastError` (optional)
- `continuationTaskId` (optional)

Suggested statuses:

- `prepared`
- `helper_started`
- `stopping_source`
- `starting_target`
- `target_healthy`
- `continuation_enqueued`
- `continuation_dispatched`
- `completed`
- `rollback_starting`
- `rolled_back`
- `failed`

The record must be idempotent and safe to retry by operation id.

### MCP-facing tools

First slice should add:

- `ctu_restart_request`
- `ctu_restart_status`

`ctu_restart_request` should:

1. validate the target checkout,
2. reject source/target equality,
3. constrain target and fallback to known allowed local checkouts, initially the current checkout and explicit sibling clones such as `codexteamup.acceptance`,
4. derive source/target bus roots,
5. write the restart operation record,
6. launch the external helper,
7. return an ACK with `operationId`.

Validation should be production-enforced, not test-only. The durable record is the source of truth for retry and resume.

`ctu_restart_status` should read the durable record and report current state.

### Continuation contract

The restart continuation must be explicit and durable.

The operation record should carry:

- the intended target agent id,
- the continuation title,
- the continuation prompt,
- the source checkout identity,
- the fallback checkout identity,
- the last known operation status.

The helper should materialize that as a concrete AgentBus task in the target checkout once the target runtime is healthy.

The wakeup message should point to the exact task file and operation id so the target thread can continue deterministically instead of inferring intent from historic chat state.

### External helper contract

The external helper should be implemented in .NET 10 via `CodexTeamUp.Cli`, launched through a visible PowerShell console so the operator can inspect it.

Suggested launch shape:

```powershell
pwsh -NoExit -ExecutionPolicy Bypass -Command "& '<repo>/.ctu/tools/cli/CodexTeamUp.Cli.exe' restart execute --operation-path '<path>'"
```

The helper should:

1. load the restart record,
2. mark `helper_started`,
3. run the target checkout's `scripts/start-codexteamup.ps1 -ForceStopExisting`,
4. poll target service health until:
   - service is healthy,
   - `defaultBusRoot` matches the target checkout,
5. enqueue a continuation task for `targetAgentId`,
6. dispatch the continuation task,
7. mark `completed`.

If target startup or continuation dispatch fails:

1. mark failure details,
2. if `fallbackCwd` is set, start the fallback checkout,
3. mark `rolled_back` or `failed`.

### Continuation behavior

The helper should not assume chat history is enough.

It should create a concrete continuation task such as:

- title: `Continue after restart`
- prompt: what the source architect intended to do next

That continuation task is the durable resume point.

## Scope boundaries for slice 1

In scope:

- restart between two existing CTU checkouts,
- visible helper console,
- non-interactive helper path,
- durable status inspection,
- continuation task dispatch,
- fallback to previous stable checkout.

Out of scope for slice 1:

- arbitrary external applications,
- full generic restart framework,
- automatic creation of missing target architect roles,
- background invisible restart helper,
- UI dashboard control surface.

## Suggested ownership

### `ctu/service`

- add restart operation models/store,
- add MCP tool metadata,
- add controller handlers,
- add CLI `restart execute` command.

### `ctu/wrapper`

- update startup/helper invocation path,
- keep the visible PowerShell console open,
- verify non-interactive restart behavior around `start-codexteamup.ps1`.

### `ctu/tester`

- add deterministic tests for record lifecycle/idempotency/validation,
- define the minimum live proof,
- document mandatory evidence.

## Minimum proof for slice 1

1. Deterministic tests:
   - restart record write/read/update,
   - invalid target checkout rejection,
   - helper state transition logic,
   - fallback/rollback state transition.

2. Live proof:
   - running on `codexteamup`,
   - request restart into `codexteamup.acceptance`,
   - target architect receives continue task,
   - acceptance smoke runs,
   - request restart back into `codexteamup`,
   - target architect resumes there.

This is the first slice that turns "restart me" from an operator habit into a CTU feature.
