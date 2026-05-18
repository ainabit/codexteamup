# ADR-0006: Agent-owned continuations

- Status: accepted
- Date: 2026-05-18

## Context

The controller-side guardian heartbeat was introduced to prevent CTU work from silently stalling after partial reviews, deferred dispatches, or uncertain wakeups. That solved the symptom but made routine progress depend on a central monitor, usually `ctu/projectlead`, deciding that work should continue.

That is too coarse for normal coordination. The agent that finishes a task has the best local knowledge of whether the work is done, handed to someone else, needs a later self-wakeup, requires a human, or failed.

## Decision

Execution continuity is agent-owned.

Every AgentBus result declares one structured outcome:

- `done`
- `handed_off`
- `self_continue`
- `human`
- `failed`

Only `self_continue` registers a later wakeup. The scheduled continuation targets the same agent that wrote the result, includes the source task/result and next action, and is deduplicated by a deterministic key such as `agentId + chainId + nextAction`.

The controller runtime owns the due-time policy and dispatches the continuation through normal AgentBus task creation plus best-effort Desktop wakeup. The fixed API layer does not own continuation policy.

The global `ctu/projectlead` heartbeat is removed from the runtime path. Central recovery may be introduced later as explicit stale-chain analysis that surfaces missing outcomes, expired continuations, or stranded plans, but it must not act as routine carry-through.

The dashboard must show continuations centrally so humans can see pending, due, dispatched, expired, deduped, and human-blocked follow-ups across agents.

## Consequences

- Result-writing agents must be explicit about terminal and non-terminal outcomes.
- Normal continuation ownership remains with the worker that has context.
- Duplicate wakeups are controlled at the continuation-registration layer instead of by chat convention.
- Dashboard visibility becomes part of the architecture contract, not a later UX nice-to-have.
- Recovery logic can still exist as explicit analysis, but it must not hide missing or malformed agent outcomes.
