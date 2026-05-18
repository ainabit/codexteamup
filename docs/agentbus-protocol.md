# AgentBus Protocol

`.codexteamup/agentbus` is the durable truth for inter-thread coordination. Desktop app-server calls only wake threads; task content and results live here.

AgentBus is the internal CTU-native coordination store. If CTU needs file-based ingress/egress for outside producers or restart startup handoffs, that lives in an adjacent exchange boundary under `.codexteamup/exchange`, not by inventing parallel semantics inside `tasks/open`.

## Layout

```text
.codexteamup/agentbus/
  agents.json
  tasks/open/*.json
  tasks/claimed/*.json
  tasks/done/*.json
  tasks/failed/*.json
  prompts/*.md
  results/*.json
  continuations/pending/*.json
  continuations/done/*.json
  events.jsonl
  locks/*.lock
  inbox/<agent-id>/
```

Separate external exchange layouts may live alongside AgentBus, for example:

```text
.codexteamup/exchange/
  inbox/
  outbox/
  deadletter/
  payloads/
  correlations/
```

Those files are imported or exported by controller-owned runtime loops. They are not a second ad hoc task format for visible workers to manually poll.

## Lifecycle

1. `task.created`: task file is written to `tasks/open`.
2. `task.dispatched`: target thread was woken via `turn/start`.
3. `task.claimed`: worker atomically moves the task to `tasks/claimed`.
4. `result.written`: worker writes a result file with a structured outcome.
5. Task moves to `tasks/done` or `tasks/failed`.
6. If the outcome is `self_continue`, CTU registers or refreshes one deduplicated pending continuation for the same agent.
7. `result.notified`: Architect thread was woken with the result id. The event message includes wakeup latency, turn id when available, and the target thread status observed before sending. If the app-server call fails, CTU writes `result.notify_failed` instead.
8. Optional synchronous wait: if the caller used `team_send_message` with `waitResult=true`, CTU writes `team.wait.completed` as soon as the result file is observed, or `team.wait.timeout` when the configured timeout expires.
9. When a pending continuation becomes due, the controller creates a normal AgentBus task for the same agent and dispatches it through the usual wakeup path.

## Claims

Claiming uses a lock file and an atomic move from `open` to `claimed`. A task should only be worked on by the claim owner. If a worker gets interrupted, the task remains visible in `claimed` and can be recovered manually.

## Results

Results contain:

- task id
- sender and receiver
- status
- outcome (`done`, `handed_off`, `self_continue`, `human`, or `failed`)
- summary
- changed files
- commits
- tests
- artifacts
- open questions
- next suggested action

`done`, `handed_off`, `human`, and `failed` are terminal for automatic self-wakeup. They may still notify a return target or be recovered by explicit policy, but they do not schedule routine continuation work.

`self_continue` means the same agent needs a later wakeup. The result should provide enough continuation metadata for the controller to register the follow-up:

- source task id
- source result id
- agent id
- chain id or correlation id
- next action
- not-before time or retry policy
- attempt metadata when retrying

The continuation dedupe key must be deterministic. Rewriting or retrying the same `self_continue` outcome should not create multiple pending wakeups for the same agent and next action.

## Continuations

Continuations are durable scheduling records derived from `self_continue` results. They are not a second task queue and are not manually polled by worker agents.

A pending continuation records:

- continuation id
- dedupe key
- agent id
- source task id
- source result id
- chain id or correlation id
- next action
- created time
- not-before time
- attempt metadata
- status (`pending`, `due`, `dispatched`, `expired`, `blocked`)

The controller runtime owns due-time evaluation. When dispatching a due continuation, it creates a normal AgentBus task and writes events that tie the new task back to the continuation record.

The dashboard should show continuation records centrally by status, agent, chain, next action, source task/result, and next wakeup time.

## Events

`events.jsonl` is append-only. Consumers should tolerate partially written or corrupt lines and continue reading later lines.

Continuation-related events should be explicit enough for operators and the dashboard to reconstruct the chain:

- `continuation.registered`
- `continuation.deduped`
- `continuation.due`
- `continuation.dispatched`
- `continuation.expired`
- `continuation.blocked`
