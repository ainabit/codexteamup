# Task carry-through reliability

- Status: active
- Owner: `ctu/architect`
- Recovery monitor: `ctu/projectlead` (interim fallback only)

## Goal

Prevent stalls after partial reviews by making CTU execution continuity agent-owned, durable, observable, and controller-scheduled.

## Architecture decision update (2026-05-18)

- Normal carry-through is now driven by structured AgentBus result outcomes, not by a global projectlead heartbeat.
- Every result must declare `done`, `handed_off`, `self_continue`, `human`, or `failed`.
- Only `self_continue` registers a deduplicated later wakeup for the same agent.
- `ctu/projectlead` remains a fallback/recovery monitor for stale or malformed chains, not the normal progress driver.
- The dashboard must centrally show continuation state across agents.

## Priority shift (2026-05-18)

- Top branch initiative is now `execution-continuity-first`.
- Restart/acceptance runtime follow-ups remain valid but are deferred until continuity proof is green.

## Current state (2026-05-18)

- State: `in_progress_agent_owned`
- Current owner: `ctu/developer-continuity` (implementation)
- Blockers: result outcome contract, continuation dedupe registry, durable notify retry, attempt metadata, stale claimed-task recovery
- Runtime follow-up monitoring:
  - Claimed follow-up `task-2026-05-18-093829-77aab677cc734d9` (ctu/developer-runtime) is stale at monitor horizon and marked "stale claim candidate".
- Recycled with explicit next package: `task-2026-05-18-100119-a340925970f2483` (`ctu/backend`-targeted, currently stale/misaddressed and valid only if rebound automatically to a continuity developer agent, continuity-state terminalization).
- Escalation rule: gate `ctu/tester` until continuity and recovery execution are implemented.

## Canonical continuity state model

- `outcome` (`done`, `handed_off`, `self_continue`, `human`, `failed`) — required on every result.
- `nextAction` (`string`) — exact next actionable item.
- `correlationId` (`string`) — package-level correlation token.
- `chainId` (`string`) — links follow-up package lineage.
- `continuation` (`object`) — present only for `self_continue`; includes `notBefore`, `dedupeKey`, source `taskId`, source `resultId`, and same-agent target.
- `attemptMetadata` (`object`) — `attempt`, `maxAttempts`, `nextRetryAt`, `failureReason`, `lastAttemptAt`.
- `currentOwner` (`string`) — current owner agent.
- `lastDecision` (`object`) — `by`, `when`, `decision`, `evidence`, `nextDecisionTarget`.

## Reuse + stale-blocker cleanup

- Reuse today:
  - AgentBus durability and event logs.
  - Controller startup/retry orchestration patterns.
  - Hot-reloadable policy/runtime split.
  - Task result/claim lifecycle and audit surfaces.
- Stale/deferred blockers:
  - company-bus exploration,
  - broad dashboard UX polish,
  - long-term `ctu/cto` role activation.

## Minimum implementation slices (branch order)

1. Implement canonical continuity state persistence + `execution_monitor` loop in hot-reload controller runtime.
2. Add required result outcome capture to `agentbus_write_result` and result files.
3. Add deduplicated `self_continue` registry and due-time controller scheduling for the same agent.
4. Add durable notify retry path and attempt metadata persistence.
5. Add stale claimed-task recovery and fallback projectlead recovery for missing/malformed outcomes.
6. Add dashboard continuation visibility and explicit proof checks.
7. Run gated verification (then restart/acceptance resume unblocks).

## Immediate next work package (explicit)

- `ctu/developer-continuity`:
  - implement result outcome persistence;
  - implement deduplicated same-agent continuation registration and due-time dispatch;
  - implement durable notify retry + attempt metadata;
  - implement stale claimed-task recovery + recovery assertions.
- Active continuation package (explicitly created now): `task-2026-05-18-100119-a340925970f2483`.
- `ctu/tester`:
  - only after outcome/continuation proof is green and execution loop evidence is logged.

## Hard green gate: restart + acceptance resume

Resume/restart acceptance is permitted only when all are true:

- every result in the chain has a structured outcome.
- `done`, `handed_off`, `human`, and `failed` do not schedule automatic self-wakeup.
- `self_continue` creates at most one pending continuation per same-agent dedupe key.
- `attemptMetadata` shows bounded attempts with persisted outcomes.
- no open stale claimed-task recovery condition.
- continuation chain has an explicit `nextAction` / `lastDecision` and owner.

## Current phase and acceptance

- Current phase: P0 (result outcome + continuation registry)
- Acceptance criteria:
  - state transitions are durable and visible (AgentBus/events).
  - every result writes a structured outcome.
  - `self_continue` registers a deduplicated same-agent wakeup.
  - `handed_off` points to another owner or task without also scheduling self-wakeup.
  - `human` blocks automatic wakeup and is visible in the dashboard.
  - projectlead heartbeat is only a recovery surface for stale or malformed chains.
  - no passive review-only terminal state.
