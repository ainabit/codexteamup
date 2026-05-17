# CodexTeamUp Roadmap and Execution Plan (Persistent)

**Status date:** 2026-05-17
**Owner:** ctu/architect (decision owner)
**Maintainer:** ctu/projectlead

## Recommended planning artifact path
- **`docs/ctu-roadmap-and-execution-plan.md`** (this file)

## Execution Plan (initial, sequenced)

1. Dashboard communication-first UX (visibility + stuck-work clarity)
   - Rework dashboard primary surface to emphasize agent/task communication graph:
     - top-level: active agents, open/blocked/in-flight tasks, recent results, unresolved events.
     - drill-down: task details, linked result logs, thread/event history, and controller status.
   - Add explicit “stuck work” states and aging/timeout indicators by age and wait stage.
   - Add lightweight operator actions from dashboard where safe.

2. Acceptance workspace and clone/fetch/start/mcp/live smoke hardening
   - Create `codexteamup.acceptance` workspace profile/directory.
   - Add deterministic acceptance scripts and evidence outputs for:
     - fresh clone/fetch flow,
     - wrapper and service startup path via `scripts/start-codexteamup.ps1`,
     - MCP discoverability and key tool paths,
     - dashboard loadability and communication traceability,
     - live smoke scenarios using dedicated CTU test namespace.

3. Multi-project / company bus exploration
   - Define bus anchor model per project with controlled endpoint metadata.
   - Add read-only federation model first (cross-project task visibility/query), then optional cross-dispatch with explicit allow-lists.
   - Keep scope limited to protocol, identity, and security controls before implementing cross-write defaults.

4. Operational polish and reliability
   - Expose hot-swap state for controller/runtime and adapter status in dashboard health view.
   - Surface redacted recent logs + failure summaries in dashboard for rapid operator diagnosis.
   - Add stale thread cleanup/retirement status and policy in one place.
   - Add thread-name verification with screenshot/visual evidence capture in live test/report outputs.

5. GitHub / release hygiene
   - Add a pre-commit/PR security review gate checklist for agent/PR owners.
   - Require ctu/github/security signoff in release PR template and release checklist.
   - Standardize changelog/notes to include:
     - controller runtime compatibility impact,
     - hot-swap path changes,
     - risk/recovery notes,
     - test evidence links.

## Dependencies and execution order
- Start with dashboard model changes to make acceptance/reporting loops observable.
- Stand up acceptance workspace in parallel with small smoke harness updates.
- Implement operational polish once the controller status surfaces exist from dashboards and runtime telemetry.
- Defer multi-project bus exploration until reliability and acceptance confidence are established.
- Gate GitHub/release hygiene changes to a stable release train (before merge freeze / after runtime changes stabilized).

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
- Should acceptance workspace live in-repo (`codexteamup.acceptance`) or as a versioned sibling directory by default?
- What is the minimum “stuck-work SLA” threshold (minutes/timeout) for operator escalation in dashboard?
- Should multi-project bus initially be “read-only bridge” only, or allow write with per-route allow-lists?
