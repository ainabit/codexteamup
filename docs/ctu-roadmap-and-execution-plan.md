# CodexTeamUp Roadmap and Execution Plan (Persistent)

This file is the persistent long-form roadmap backing the initiative index in [initiatives/README.md](initiatives/README.md). It is no longer the only planning surface.

**Status date:** 2026-05-18
**Owner:** ctu/architect (decision owner)
**Maintainer:** ctu/projectlead

## Recommended planning artifact path
- **`docs/ctu-roadmap-and-execution-plan.md`** (this file)

## Execution Plan (initial, sequenced)

1. Restart safety, durable startup handoff, and channel/exchange boundary
   - Replace direct post-start continuation RPC with durable startup handoff envelopes.
   - Add known-good runtime checkpointing so rollback means "back to a verified CTU runtime", not just "try the old repo path".
   - Add `.codexteamup/exchange` as the first concrete channel implementation with inbox/outbox/deadletter/correlation semantics for restart handoffs and future external producer/consumer flows.
   - Add a controller-owned channel pump/heartbeat to import startup handoffs, process exchange messages, and reconcile stuck restart operations.

2. Dashboard communication-first UX (visibility + stuck-work clarity)
   - Rework dashboard primary surface to emphasize agent/task communication graph:
     - top-level: active agents, open/blocked/in-flight tasks, pending continuations, recent results, unresolved events.
     - drill-down: task details, linked result logs, continuation records, thread/event history, and controller status.
   - Add explicit “stuck work” states and aging/timeout indicators by age and wait stage.
   - Show agent-owned continuations centrally by status, agent, chain, source task/result, next action, and next wakeup time.
   - Add lightweight operator actions from dashboard where safe.

3. Acceptance workspace and clone/fetch/start/mcp/live smoke hardening
   - Create `codexteamup.acceptance` as a real separate clone/check-out used only for outside-user acceptance.
   - Add deterministic acceptance scripts and evidence outputs for:
     - fresh clone/fetch flow,
     - wrapper and service startup path via `scripts/start-codexteamup.ps1`,
     - MCP discoverability and key tool paths,
     - dashboard loadability and communication traceability,
     - live smoke scenarios using dedicated CTU test namespace.

4. Multi-project / company bus exploration
   - Define bus anchor model per project with controlled endpoint metadata.
   - Add read-only federation model first (cross-project task visibility/query), then optional cross-dispatch with explicit allow-lists.
   - Keep scope limited to protocol, identity, and security controls before implementing cross-write defaults.

5. Operational polish and reliability
   - Expose hot-swap state for controller/runtime and adapter status in dashboard health view.
   - Surface redacted recent logs + failure summaries in dashboard for rapid operator diagnosis.
   - Add stale thread cleanup/retirement status and policy in one place.
   - Add explicit agent lifecycle controls so temporary worker threads, especially ad hoc `ctu/developer*` agents, can be retired once their slice is done and do not accumulate stale context.
   - Add a queue-first dispatch guardrail so visible work is not silently left at `team_send_message` enqueue-only when `bridge_dispatch_task` was expected afterward.
   - Add thread-name verification with screenshot/visual evidence capture in live test/report outputs.

6. GitHub / release hygiene
   - Add a pre-commit/PR security review gate checklist for agent/PR owners.
   - Require ctu/github/security signoff in release PR template and release checklist.
   - Standardize changelog/notes to include:
     - controller runtime compatibility impact,
     - hot-swap path changes,
     - risk/recovery notes,
     - test evidence links.

## Dependencies and execution order
- Start with execution continuity first (structured result outcomes + same-agent self-continuations + fallback recovery), because it prevents partial-review stalls across all slices.
- Then execute restart safety and exchange/handoff reliability.
- Stand up acceptance workspace in parallel with small smoke harness updates once the restart handoff model is stable.
- Do dashboard model changes after restart/exchange telemetry exists so the UI can show the right states instead of guessing.
- Implement operational polish once the controller status surfaces exist from dashboards and runtime telemetry.
- Defer multi-project bus exploration until reliability and acceptance confidence are established.
- Gate GitHub/release hygiene changes to a stable release train (before merge freeze / after runtime changes stabilized).

## Initial decomposition (suggested)
- P0: Agent-owned execution continuity (hot-reload controller/runtime): result outcomes, deduplicated same-agent continuations, fallback recovery, and dashboard visibility (architect + backend + reviewer).
- P1: Restart safety + known-good rollback + startup handoff/exchange boundary (architect + backend + reviewer).
- P2: Controller heartbeat/runtime loop for exchange import, outbox reconciliation, and stuck restart detection (backend + reviewer).
- P3: Acceptance workspace + deterministic clone/fetch/start/MCP coverage (architect + tester + backend).
- P4: Dashboard communication-first view + stuck-work surfacing (architect + web).
- P5: Controller/runtime observability + stale thread policy clarity surfaced in UI (backend + reviewer).
- P6: Visual thread-name verification and screenshot capture hooks in reporting (web + tester).
- P7: Release/governance hardening with security review and checklist updates (architect + security).
- P8: Multi-project bus exploration with security boundary prototype and read-path MVP (architect + backend).

## Suggested staffing by role (`ctu/*`)
- `ctu/architect`: scope decisions, sequencing, acceptance criteria ownership.
- `ctu/web`: dashboard UX, stuck-work visualization, drill-down surfaces, screenshot evidence outputs.
- `ctu/developer-continuity`: agent/task orchestration telemetry, stale-task recovery policy, controller/runtime state surfacing.
- `ctu/reviewer`: cross-flows audit, acceptance criteria validation, rollback and risk review.
- `ctu/tester`: deterministic safety net + live smoke scenarios and evidence capture.
- `ctu/security`: GitHub/release security checklist and threat/risk control design.

## Risks and blockers
- Desktop wrapper/AppServer behavior drift can block dashboard or wakeup observability assumptions.
- Live acceptance environments can be flaky; requires robust cleanup and retry policy.
- Cross-project bus introduces security and tenancy boundaries that affect trust and ACL modeling.
- Visual verification depends on stable environment support for screenshot capture and deterministic thread naming.
- Release hygiene can stall without clear role ownership and explicit signoff criteria.

## Open questions
- What is the minimum “stuck-work SLA” threshold (minutes/timeout) for operator escalation in dashboard?
- Should multi-project bus initially be “read-only bridge” only, or allow write with per-route allow-lists?

## Operational guardrail (No-passive-review)

- Architect/result-review turns must end in exactly one terminal outcome:
  - `done`
  - `handed_off`
  - `self_continue`
  - `human`
  - `failed`
- A task is invalid in planning if it ends in passive "review-only" or equivalent non-terminating state while acceptance criteria are unmet.
- `handed_off` must include: target owner or task id, expected deliverable, and return target.
- `self_continue` must include: same-agent target, next action, not-before or retry policy, and dedupe key.
- `human` must include: required decision/input and owner.
- `failed` must include: failure reason, evidence, and retry/recovery recommendation.

## Reliability carry-through package (current branch, now priority)

- Scope: execution continuity package for branch `codex/restart-orchestration`; projectlead acts as interim fallback recovery monitor until/if `ctu/cto` is introduced.
- Goal: tasks do not stop at review-only states unless a real blocker requires explicit human input.
- Current canonical continuity state to enforce in controller/runtime artifacts:
  - `outcome`, `nextAction`, `correlationId`, `chainId`,
  - `continuation.dedupeKey`, `continuation.notBefore`, `continuation.sourceTaskId`, `continuation.sourceResultId`,
  - `attemptMetadata`, `currentOwner`, `lastDecision`.

### 1) Controller-owned delivery loop
- Add controller-owned retry loop for deferred dispatch and notify paths (no silent terminal enqueue).
- Correlate attempts and outcomes in AgentBus events; every deferred path must either complete, escalate as `blocked-with-owner`, or continue with explicit `verification-started`.

### 2) Stale-task supersede + temporary agent retirement
- Define clear supersede/override policy: older tasks can be marked superseded only by newer intended intent with evidence.
- Mark temporary `ctu/developer*`/helper agents as retired via explicit state update and dashboard visibility once handoff slice completes.

### 3) Agent-owned continuation loop
- Keep execution state in AgentBus results and continuation records until a structured outcome is recorded.
- Only `self_continue` creates automatic same-agent follow-up.
- Do not treat review-only states as terminal unless the result records `done`, `handed_off`, `human`, or `failed` with the required owner/evidence.

### 4) Future `ctu/cto` role
- Add a short note and handoff plan for future visible `ctu/cto` control-oversight role.
- Current branch uses `ctu/projectlead` as recovery monitor with explicit handoff criteria to `ctu/cto` when approved by architect.

### Current tracker (2026-05-18)

- State: `in_progress_agent_owned`; structured outcome and self-continuation gate is active.
- Blockers:
  - result outcome contract not implemented,
  - continuation registry/dedupe not implemented,
  - durable notify retry not implemented,
  - retry state/attempt metadata not persisted,
  - stale claimed-task recovery not implemented.
- Runtime follow-up recycling:
  - `task-2026-05-18-093829-77aab677cc734d9` (ctu/developer-runtime) stalled; marked stale claim candidate.
  - Next explicit package created: `task-2026-05-18-100119-a340925970f2483` (`ctu/backend`-targeted; treat as stale/misaddressed unless rebound automatically to a continuity developer agent, continuity-state terminal disposition).
- Current next owner: `ctu/developer-continuity` (with `ctu/architect` for policy confirmation).
- Next concrete next steps:
  - define/apply result outcome model + durable continuation registry,
  - implement same-agent `self_continue` due-time dispatch,
  - implement controller-owned delivery loop retry metadata,
  - wire stale claimed-task recovery path,
  - gate `ctu/tester` wakeups until runtime paths are in place, then run deterministic/live verification.
- Terminal rule enforced: no review-only final state; each result must record `done`, `handed_off`, `self_continue`, `human`, or `failed`.

### Minimal slices and explicit gate (2026-05-18)

- Minimal slices, in order:
  1. Implement required result outcome persistence.
  2. Add deduplicated `self_continue` continuation registry.
  3. Add controller due-time dispatch for same-agent continuations.
  4. Implement notify retry + attempt metadata.
  5. Implement stale claimed-task recovery and fallback projectlead recovery.
  6. Add dashboard continuation visibility.
  7. Add hard proof gate for restart/acceptance resume.
- Hard gate:
  - restart + acceptance continuation only if continuity proof is green (structured outcomes on every result, deduplicated self-continuations, bounded retry, and no stale recovery condition).
- Reuse/stale blockers:
  - Reuse: AgentBus state/events, controller loop/policy split, startup/retry orchestration model.
  - Defer: company-bus work and broad dashboard polish until continuity proof green.
