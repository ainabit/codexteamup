# Restart Orchestration Plan

This is the detailed implementation plan behind [initiatives/restart-orchestration.md](initiatives/restart-orchestration.md) and the architecture hub in [architecture/README.md](architecture/README.md).

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
5. The target checkout reads a durable startup handoff and the target architect thread resumes work.
6. If target startup or startup handoff fails, the helper can return to the last known good CTU runtime.

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

### 2. AgentBus remains durable truth; startup handoff is durable too

Restart intent must not live only in chat text or in controller memory.

The restart subsystem gets its own durable records under project-local state, for example:

```text
.codexteamup/restart/operations/<operation-id>.json
```

The continuation itself is also durable:

- encoded in the restart operation record,
- materialized as a startup handoff envelope in the target checkout,
- and then imported by the target controller runtime into AgentBus or a system operation after boot.

The helper must not depend on a direct live wrapper/app-server call immediately after `target_healthy`. The startup handoff is the source of truth for what the target runtime should do next.

### 3. Thin MCP surface, workflow in controller/helper/runtime loop

MCP should only expose a thin request/status API. The workflow belongs in:

- the hot-reloadable controller runtime for request creation,
- the external restart supervisor/helper for process orchestration,
- the controller-owned background loop for startup handoff pickup and post-start routing,
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

### 6. Known-good runtime checkpoint is required

Rollback must not mean only "start the old checkout path and hope it boots".

CTU should keep a durable known-good runtime checkpoint, for example:

```text
.codexteamup/runtime/checkpoints/known-good.json
```

Suggested contents:

- checkout path;
- branch/head evidence when available;
- published runtime root under `.ctu/runtime` and `.ctu/tools`;
- active controller plugin path;
- active app-server adapter identity;
- timestamp of last verified healthy state;
- optional last verified architect/target thread evidence.

Rollback should prefer this checkpointed runtime context over an unverified source tree. A rollback is only successful when CTU is healthy again on the known-good side and the return handoff is durable.

### 7. Restart uses the channel boundary; filesystem exchange is the first durable channel

Restart should reuse a general external channel boundary instead of inventing restart-only hidden magic.

Suggested layout:

```text
.codexteamup/exchange/
  inbox/system/
  inbox/project/
  inbox/agent/
  outbox/
  deadletter/
  leases/
  payloads/
  correlations/
```

Use cases for the first filesystem-backed channel:

- startup handoff for `ctu/architect` or `ctu-acceptance/architect`;
- project-scoped acceptance requests;
- exported packets for external review or human/manual workflows;
- imported responses correlated back to CTU.

The exchange message envelope should support:

- `messageId`
- `kind`
- `targetScope` (`system`, `project`, `agent`)
- `targetProject`
- `targetAgentId` (optional)
- `targetThreadName` (optional)
- `correlationId`
- `causationId` (optional)
- `responseTo` (optional)
- `createdAt`
- `notBefore` (optional)
- `expiresAt` (optional)
- `payloadType`
- `payloadPath` or inline payload reference
- `attemptCount`
- `leaseOwner` / `leaseExpiresAt`
- `lastError`

The controller loop imports these envelopes into AgentBus tasks or restart/system actions. The outbox uses the same correlation model for emitted responses.

Future channels such as public MCP ingress or REST/webhook ingress should translate into the same durable envelope/correlation contract rather than bypassing it.

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
- `startupHandoffMessageId` (optional)
- `knownGoodCheckpointId` (optional)

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
- the startup handoff message id,
- the last known operation status.

The helper should materialize that as a startup handoff envelope in the target checkout before or during target startup. The target controller loop should then import that durable handoff into a concrete AgentBus task or system action once the target runtime is healthy.

For restart specifically, use the exchange channel itself as the durable message:

```text
.codexteamup/exchange/startup/system/restart/<message-id>.json
```

That file is the restart continuation contract. When CTU starts and the controller runtime is up, the first startup sweep should read pending `system/restart` envelopes, lease them, validate the linked restart operation, and only then perform the internal dispatch into AgentBus or the controller's system action flow.

The wakeup message, if used at all, should point to the exact task file, handoff id, and operation id so the target thread can continue deterministically instead of inferring intent from historic chat state.

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
5. ensure the startup handoff envelope exists under `.codexteamup/exchange/startup/system/restart/` in the target checkout,
6. let the target controller/runtime loop import and route that handoff,
7. mark `completed`.

If target startup or startup handoff routing fails:

1. mark failure details,
2. if `fallbackCwd` is set, start the checkpointed or explicit fallback runtime,
3. mark `rolled_back` or `failed`.

### Continuation behavior

The helper should not assume chat history or immediate live wrapper RPC is enough.

It should create a durable startup handoff envelope and the target runtime should then create a concrete continuation task such as:

- title: `Continue after restart`
- prompt: what the source architect intended to do next

That continuation task is the visible resume point. The durable resume point is the startup handoff plus the restart operation record.

### Controller heartbeat / channel pump runtime loop

Restart completion depends on a controller-owned runtime loop.

That loop should:

1. scan startup handoffs under `.codexteamup/exchange/startup/**` and runtime ingress under `.codexteamup/exchange/inbox/**`,
2. lease pending envelopes,
3. import startup handoffs and external requests, with `system/restart` processed first during startup,
4. translate them into AgentBus tasks or restart/system actions,
5. emit outbox or dead-letter evidence,
6. reconcile correlations and retry metadata,
7. detect stuck restart operations and request retry or rollback according to policy.

This loop must use short, bounded iterations. It should never block the visible thread experience for long waits.

## Scope boundaries for slice 1

In scope:

- restart between two existing CTU checkouts,
- visible helper console,
- non-interactive helper path,
- durable status inspection,
- startup handoff routing,
- fallback to previous stable checkout.

Out of scope for slice 1:

- arbitrary external applications,
- full generic restart framework,
- automatic creation of missing target architect roles,
- background invisible restart helper,
- UI dashboard control surface.

## Implementation phases

### Phase 1: restart safety hardening

- add known-good checkpoint record and update rules;
- stop treating rollback as successful before target/fallback health is proven;
- replace direct post-start continuation RPC with durable startup handoff records;
- keep visible supervisor console.

### Phase 2: exchange boundary and correlation model

- add `.codexteamup/exchange` layout and message envelope schema;
- add correlation ids, response ids, leases, and dead-letter handling;
- support `system`, `project`, and `agent` target scopes;
- support payload references for text, json, zip, and file bundles.

### Phase 3: controller-owned heartbeat/runtime loop

- add a controller runtime background loop contract;
- implement startup handoff import, exchange inbox sweep, outbox reconciliation, and stuck-operation detection;
- keep timing/retry policy hot-reloadable.

### Phase 4: acceptance roundtrip

- from `codexteamup`, request acceptance run;
- prepare or refresh the disposable `codexteamup.acceptance` checkout;
- restart into acceptance;
- `ctu-acceptance/architect` receives the startup handoff and runs fresh-clone acceptance;
- restart back into known-good `codexteamup`;
- `ctu/architect` receives the return handoff and continues.

### Phase 5: external producer/consumer workflows

- allow manual or automated drop-in requests through exchange inbox;
- allow CTU to emit outbox packets for external review systems or human workflows;
- correlate imported responses back into AgentBus results or follow-up tasks.

## Suggested ownership

### `ctu/service`

- add restart operation models/store,
- add MCP tool metadata,
- add controller handlers,
- add CLI `restart execute` command,
- add exchange envelope models/store and controller background-loop hosting contract.

### `ctu/wrapper`

- update startup/helper invocation path,
- keep the visible PowerShell console open,
- verify non-interactive restart behavior around `start-codexteamup.ps1`,
- keep startup/bootstrap behavior clean when moving between normal and acceptance checkouts.

### `ctu/tester`

- add deterministic tests for record lifecycle/idempotency/validation,
- add deterministic tests for exchange inbox/outbox/correlation and checkpoint rollback,
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
