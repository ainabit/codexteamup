# CTU Logbook

Reverse chronological notes about the build journey, notable failures, redesigns, and why the current architecture looks the way it does.

## 2026-05-18

- Restart semantics were tightened around a CTU session manifest instead of best-effort process discovery. Startup now records launcher/Desktop/service/wrapper PIDs, and restart uses that source-session record so switching to `codexteamup.acceptance` can close the old startup console before launching the target checkout through the normal KISS PowerShell entrypoint. Automated restart starts are intentionally transient so test windows do not stay open after health is reached.
- Live restart testing exposed two concrete guardrail gaps: `codexteamup.acceptance` was initially on an older branch commit, and a PowerShell-written startup handoff used PascalCase JSON that the controller rejected.
- The fix made restart startup handoffs canonical camelCase, made exchange reads case-insensitive for compatibility, and deadletters malformed envelopes so one bad file cannot stall the controller sweep.
- The deterministic safety net was also isolated from live CTU environment variables after `CTU_CONTROLLER_PLUGIN_PATH` leaked from the running runtime into the test process.
- The intermediate global `ctu/projectlead` guardian heartbeat was removed from runtime code, controller policy, and binding docs. Carry-through is now intentionally agent-owned: an agent either finishes, hands off, asks a human, fails, or schedules its own deduplicated continuation.
- ADR-0006 replaced the global projectlead heartbeat as the normal carry-through mechanism with agent-owned continuations:
  - every result declares `done`, `handed_off`, `self_continue`, `human`, or `failed`;
  - only `self_continue` registers a deduplicated later wakeup for the same agent;
  - central recovery is future explicit stale-chain analysis, not a live heartbeat;
  - the dashboard must show continuation state centrally.
- A destructive `agentbus_clear_tasks` reset tool was added for disposable test phases, guarded by `confirm=DELETE`.
- Backlog and dailylog ideas can exchange directly without automatically routing every reply through `ctu/architect` when no architecture decision is needed.
- Local agent-path decisions should handle operational tips such as `.git/index.lock` cleanup or `git.exe` cleanup instead of escalating them upward by default.
- Interim ideas, ad-hoc notes, and quick context traps should be captured in `ctu/backlog` so the `ctu/architect` context stays cleaner.
- The backlog role can also hold operational hints such as Git lock behavior when they do not belong in the main planning thread.
- Execution continuity is now the top branch architecture initiative.
- The intended shape is a durable continuity state model plus a controller-owned guardian loop.
- The guardian target must stay configurable by display name or agent id.
- Restart and acceptance work are explicitly gated behind continuity proof.
- The reason is repeated stalls after partial reviews or deferred work even when no human decision was needed.
- The top priority shifted back from restart/acceptance to execution continuity itself.
- Repeated stalls after partial reviews or deferred dispatches are now treated as the main problem, and automation heartbeat alone was judged insufficient.
- The target state is a hot-reloadable controller-side guardian/execution-monitor loop with durable initiative and task state.
- The guardian target must be configurable by display name or agent id, not tied to a `ctu/*` prefix.
- Restart and acceptance roundtrip work resumes only after this mechanism is proven.
- `ctu/projectlead` was explicitly elevated into the interim execution monitor role for this initiative.
- The motivating issue was that partial reviews and deferred dispatches could leave the work feeling stalled.
- The package now explicitly includes durable carry-through for dispatch/notify, stale-task recovery, and no-passive-stop execution control.
- The merge gate remains real end-to-end carry-through, not just event logging.
- The restart branch expanded again to cover task carry-through reliability.
- The package now includes:
  - a controller-owned delivery retry loop;
  - stale-task and temporary-agent retirement;
  - durable execution monitoring so review-only stops are not terminal;
  - reliable return-target/result notification handling.
- This package was triggered by real stalls around `ctu/tester` and deferred delivery.
- Restart/orchestration moved one step closer to a durable handoff model:
  - the supervisor now writes a durable target-side handoff and verifies fallback health;
  - the runtime slice added exchange, checkpoint, and startup-sweep support.
- The remaining flaw is still on the source side: the source runtime can consume its own handoff before shutdown.
- The next correction pass is to move handoff authority fully to target-side import, then prove the roundtrip with deterministic and live tests.
- Restart stopped being treated as an operator habit and became an explicit CTU feature track.
- The current failure mode became clear: startup can succeed while post-start continuation over the wrapper still times out.
- That drove the shift from direct post-start continuation RPC to a durable startup handoff model.
- The architecture expanded from "restart records" to a broader external exchange boundary with inbox/outbox/correlation semantics.
- Known-good runtime checkpoints became a requirement because "rollback attempted" is weaker than "safe return to a verified CTU runtime".

## 2026-05-17

- Acceptance hardened into a real outside-user story instead of a local convenience story.
- `codexteamup.acceptance` was established as a separate real clone, not a second dev workspace.
- Fresh-clone acceptance was proven from the acceptance clone itself.
- Dashboard work landed, but also exposed how easy it is to focus on visibility while the transport/restart substrate still needs harder guarantees.

## 2026-05-16

- Codex Desktop instability after an update pushed the design toward reloadable runtime layers instead of hard restarts for every flow tweak.
- The reloadable controller direction became central because most breakage was in timing, orchestration, naming, and wakeup policy rather than raw API surface.
- Live multi-agent smoke tests matured into a real safety net:
  - create agents,
  - talk between them,
  - replace one,
  - clean up afterward.

## 2026-05-15

- CTU ideas became concrete around visible Codex Desktop threads, local coordination, and durable inter-thread files.
- App-server behavior and wrapper constraints were mapped out through direct investigation and bug-oriented experimentation.
- The project started leaning into "eat your own dogfood" by using CTU agents such as `ctu/github`, `ctu/reviewer`, and later broader role splits.

## Why this log exists

This project is not a polished release train yet. The path matters because a lot of the current architecture came from real failures in Codex Desktop, wrapper timing, restart gaps, and acceptance testing. The logbook preserves that context without forcing every architecture file to carry narrative baggage.
