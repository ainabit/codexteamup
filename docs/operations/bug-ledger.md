# Bug Ledger

Durable local record for concrete CTU bugs, operator footguns, and guardrails that should not get lost between implementation loops.

Use this file when:

- a bug was observed in live CTU behavior,
- the lesson should shape controller or UX guardrails,
- the issue is still open or only partially mitigated,
- the item is too concrete to live only in architecture notes or the daily log.

This ledger is local and immediate. Later, selected items can be mirrored into GitHub issues when they become externally relevant, need discussion, or should survive beyond the current branch/workstream.

## Open

### 2026-05-18 - Queue-first messages can be silently left undispatched

- **Observed in:** restart-orchestration work while coordinating `ctu/developer-runtime`, `ctu/tester`, and `ctu/dailylog`
- **Symptom:** `team_send_message` successfully enqueued tasks, but no visible agent activity happened because `bridge_dispatch_task` was not called afterward.
- **Why it matters:** CTU is intentionally queue-first, but visible work depends on a second explicit wake/disptach step. Forgetting that step makes the system look stalled even though AgentBus contains valid open tasks.
- **Current rule:** `team_send_message` is queue-first. For visible work, follow it with `bridge_dispatch_task` unless the controller/tool path explicitly guarantees dispatch.
- **Required hardening:** add a UX/controller guardrail so normal visible delegation does not silently stop at enqueue-only. Options include a combined helper, an explicit warning on enqueue-only visible work, or controller policy that can auto-dispatch in safe cases.
- **Status:** open
- **Owner:** `ctu/architect` / `ctu/projectlead`

