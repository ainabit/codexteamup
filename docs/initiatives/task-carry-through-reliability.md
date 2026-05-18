# Task carry-through reliability

- Status: active
- Owner: `ctu/architect`
- Monitor: `ctu/projectlead` (interim)

## Goal

Prevent stalls after partial reviews by making CTU execution continuity durable, observable, and controller-owned.

## Priority shift (2026-05-18)

- Top branch initiative is now `execution-continuity-first`.
- Restart/acceptance runtime follow-ups remain valid but are deferred until continuity proof is green.

## Current state (2026-05-18)

- State: `in_progress_guarded`
- Current owner: `ctu/developer-continuity` (implementation)
- Blockers: durable notify retry, attempt metadata, stale claimed-task recovery
- Runtime follow-up monitoring:
  - Claimed follow-up `task-2026-05-18-093829-77aab677cc734d9` (ctu/developer-runtime) is stale at monitor horizon and marked "stale claim candidate".
- Recycled with explicit next package: `task-2026-05-18-100119-a340925970f2483` (`ctu/backend`-targeted, currently stale/misaddressed and valid only if rebound automatically to a continuity developer agent, continuity-state terminalization).
- Escalation rule: gate `ctu/tester` until continuity and recovery execution are implemented.

## Canonical continuity state model

- `shouldContinue` (`bool`) — whether more execution is required.
- `terminalState` (`in_progress`, `completed`, `blocked-with-owner`, `delegated-next-task`, `verification-started`)
- `nextAction` (`string`) — exact next actionable item.
- `lastOutcome` (`string`) — last outcome summary (`success`, `retrying`, `blocked`, `failed`, `expired`).
- `correlationId` (`string`) — package-level correlation token.
- `chainId` (`string`) — links follow-up package lineage.
- `guardianDisplayName` (`string`) — visible guardian target.
- `guardianAgentId` (`string`) — routable guardian target (not bound to `ctu/`).
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
2. Add durable notify retry path and attempt metadata persistence.
3. Add stale claimed-task recovery and supersede semantics.
4. Add configurable guardian target dispatch (`guardianDisplayName` / `guardianAgentId`).
5. Add explicit proof checks + hard gate.
6. Run gated verification (then restart/acceptance resume unblocks).

## Immediate next work package (explicit)

- `ctu/developer-continuity`:
  - implement `ExecutionState` persistence and monitor loop;
  - implement durable notify retry + attempt metadata;
  - implement stale claimed-task recovery + recovery assertions.
- `ctu/developer-continuity` (follow-up):
  - implement guardian target config + continuation dispatch.
- Active continuation package (explicitly created now): `task-2026-05-18-100119-a340925970f2483`.
- `ctu/tester`:
  - only after continuity proof is green and execution loop evidence is logged.

## Hard green gate: restart + acceptance resume

Resume/restart acceptance is permitted only when all are true:

- `terminalState` is `completed` or explicit `blocked-with-owner`.
- `shouldContinue` is `false`.
- `attemptMetadata` shows bounded attempts with persisted outcomes.
- no open stale claimed-task recovery condition.
- continuation chain has an explicit `nextAction` / `lastDecision` and owner.

## Current phase and acceptance

- Current phase: P0 (state + monitor bootstrap)
- Acceptance criteria:
  - state transitions are durable and visible (AgentBus/events).
  - guardian loop continues until terminal state or explicit block.
  - every partial execution dispatches the next concrete package.
  - no passive review-only terminal state.
