# CTU Logbook

Reverse chronological notes about the build journey, notable failures, redesigns, and why the current architecture looks the way it does.

## 2026-05-18

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
