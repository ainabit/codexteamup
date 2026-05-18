# CodexTeamUp Roadmap and Execution Plan (Persistent)

This file is the persistent long-form roadmap backing the initiative index in [initiatives/README.md](initiatives/README.md). It is no longer the only planning surface.

**Status date:** 2026-05-17
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
     - top-level: active agents, open/blocked/in-flight tasks, recent results, unresolved events.
     - drill-down: task details, linked result logs, thread/event history, and controller status.
   - Add explicit “stuck work” states and aging/timeout indicators by age and wait stage.
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
- Start with restart safety and exchange/handoff reliability because acceptance and cross-project work depend on it.
- Stand up acceptance workspace in parallel with small smoke harness updates once the restart handoff model is stable.
- Do dashboard model changes after restart/exchange telemetry exists so the UI can show the right states instead of guessing.
- Implement operational polish once the controller status surfaces exist from dashboards and runtime telemetry.
- Defer multi-project bus exploration until reliability and acceptance confidence are established.
- Gate GitHub/release hygiene changes to a stable release train (before merge freeze / after runtime changes stabilized).

## Initial decomposition (suggested)
- P0: Restart safety + known-good rollback + startup handoff/exchange boundary (architect + backend + reviewer).
- P1: Controller heartbeat/runtime loop for exchange import, outbox reconciliation, and stuck restart detection (backend + reviewer).
- P2: Acceptance workspace + deterministic clone/fetch/start/MCP coverage (architect + tester + backend).
- P3: Dashboard communication-first view + stuck-work surfacing (architect + web).
- P4: Controller/runtime observability + stale thread policy clarity surfaced in UI (backend + reviewer).
- P5: Visual thread-name verification and screenshot capture hooks in reporting (web + tester).
- P6: Release/governance hardening with security review and checklist updates (architect + security).
- P7: Multi-project bus exploration with security boundary prototype and read-path MVP (architect + backend).

## Suggested staffing by role (`ctu/*`)
- `ctu/architect`: scope decisions, sequencing, acceptance criteria ownership.
- `ctu/web`: dashboard UX, stuck-work visualization, drill-down surfaces, screenshot evidence outputs.
- `ctu/backend`: agent/task orchestration telemetry, stale-thread policy, controller/runtime state surfacing.
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
