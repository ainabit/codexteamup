# Bug Ledger

Durable local record for concrete CTU bugs, operator footguns, and guardrails that should not get lost between implementation loops.

Use this file when:

- a bug was observed in live CTU behavior,
- the lesson should shape controller or UX guardrails,
- the issue is still open or only partially mitigated,
- the item is too concrete to live only in architecture notes or the daily log.

This ledger is local and immediate. Later, selected items can be mirrored into GitHub issues when they become externally relevant, need discussion, or should survive beyond the current branch/workstream.

## Fixed

### 2026-05-18 - Restart left the old startup PowerShell session behind

- **Observed in:** live restart from `codexteamup` to `codexteamup.acceptance`.
- **Symptom:** the old user-started PowerShell window stayed open while the restart helper opened another window, making it unclear which CTU session was actually active.
- **Cause:** cleanup relied mainly on command-line matching for `scripts/start-codexteamup.ps1`. If CTU was started from an already-open interactive PowerShell, the command line could later look like plain `pwsh`/`powershell` and no longer identify the original startup window.
- **Fix:** startup now writes `.ctu/sessions/current.json` with launcher, Desktop, service, and wrapper PIDs. The restart supervisor reads that manifest, stops the source session by PID with process-name checks, starts the target checkout through the normal startup script in a visible PowerShell, and keeps the supervisor console transient.
- **Regression:** deterministic script tests assert session manifest recording, source-session cleanup, visible target startup, and no persistent supervisor `-NoExit`.

### 2026-05-18 - Restart supervisor wrote non-canonical startup handoff JSON

- **Observed in:** live restart from `codexteamup.acceptance` back to `codexteamup`.
- **Symptom:** the target runtime became healthy, but the startup handoff stayed pending while the controller logged repeated `ExchangeEnvelope` deserialization failures.
- **Cause:** the PowerShell supervisor emitted exchange envelope fields as `MessageId`, `Kind`, and other PascalCase names while the canonical C# exchange model writes camelCase JSON.
- **Fix:** the supervisor now writes canonical camelCase exchange envelopes and correlations. `JsonFile` also reads properties case-insensitively so older handoffs can still be imported, and malformed envelopes are isolated into deadletter instead of poisoning the startup sweep.
- **Regression:** deterministic tests cover PowerShell-cased restart handoffs and malformed startup envelopes.

### 2026-05-18 - Deterministic tests inherited live controller plugin path

- **Observed in:** safety-net run after switching between regular and acceptance runtimes.
- **Symptom:** MCP registry tests reported `No CTU controller plugin is loaded` because the test process inherited `CTU_CONTROLLER_PLUGIN_PATH` from a live runtime.
- **Fix:** `scripts/test-codexteamup.ps1` now points deterministic tests at the freshly built test controller plugin inside the isolated artifacts directory and restores the previous environment afterward.

## Open

### 2026-05-18 - Queue-first messages can be silently left undispatched

- **Observed in:** restart-orchestration work while coordinating `ctu/developer-runtime`, `ctu/tester`, and `ctu/dailylog`
- **Symptom:** `team_send_message` successfully enqueued tasks, but no visible agent activity happened because `bridge_dispatch_task` was not called afterward.
- **Why it matters:** CTU is intentionally queue-first, but visible work depends on a second explicit wake/disptach step. Forgetting that step makes the system look stalled even though AgentBus contains valid open tasks.
- **Current rule:** `team_send_message` is queue-first. For visible work, follow it with `bridge_dispatch_task` unless the controller/tool path explicitly guarantees dispatch.
- **Required hardening:** add a UX/controller guardrail so normal visible delegation does not silently stop at enqueue-only. Options include a combined helper, an explicit warning on enqueue-only visible work, or controller policy that can auto-dispatch in safe cases.
- **Status:** open
- **Owner:** `ctu/architect` / `ctu/projectlead`
