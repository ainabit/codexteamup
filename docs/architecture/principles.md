# CTU Architecture Principles

These principles are binding.

## 1. Visible threads are the product surface

CodexTeamUp exists to coordinate visible Codex Desktop chats. Hidden automation is a support mechanism, not the primary user experience.

## 2. MCP is the normal agent interface

PowerShell is for bootstrap, recovery, publish, and diagnostics. Normal coordination should happen through MCP tools and durable files.

## 3. AgentBus is the internal durable truth

`.codexteamup/agentbus` is the CTU-native store for agents, tasks, results, events, and audit history. Desktop wakeups are best-effort live delivery, not the source of truth.

## 4. Agent outcomes drive execution continuity

Every AgentBus result must declare the agent's structured outcome: `done`, `handed_off`, `self_continue`, `human`, or `failed`. Only `self_continue` schedules a later deduplicated wakeup for the same agent. There is no global `ctu/projectlead` heartbeat in the runtime path; central recovery must be an explicit stale-chain analysis flow, not hidden routine progress.

## 5. The service/API layer stays thin

HTTP, MCP, JSON-RPC, wrapper transport, app-server adapters, redaction, and defensive logging live in the fixed API access layer. Workflow logic does not.

## 6. Workflow lives in the hot-reloadable controller layer

Thread creation, naming, priming, retry policy, dispatch timing, wakeup throttling, ACK/NACK behavior, startup sweeps, and background controller loops belong to the controller runtime, not the fixed service/API layer.

## 7. External ingress/egress uses channel adapters and a durable exchange boundary

External requests or responses from outside the normal CTU flow must use a channel model with a common durable envelope/correlation contract. A filesystem in/outbox is one channel implementation, not the only one. File-based requests, including restart handoffs, belong in `.codexteamup/exchange/**`, not in ad hoc AgentBus task files.

## 8. Restart is a CTU feature, not an operator ritual

Restart must be durable, supervised, and resumable. A new CTU runtime should be able to continue from a durable restart message without relying on chat history or a live post-start RPC.

## 9. Acceptance is a real outside-user path

`codexteamup.acceptance` is a disposable fresh clone used to prove clone/fetch/start/test behavior as another user would experience it. It is not a second development workspace.

## 10. Known-good recovery matters more than optimistic rollback

Rollback is only meaningful when CTU can return to a verified healthy runtime, not merely attempt to start an older checkout path.

## 11. Reactivity is mandatory

Tool calls should ACK/NACK quickly. Long work continues asynchronously through durable files plus short polling or later notifications.
