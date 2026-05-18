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
4. `result.written`: worker writes a result file.
5. Task moves to `tasks/done` or `tasks/failed`.
6. `result.notified`: Architect thread was woken with the result id. The event message includes wakeup latency, turn id when available, and the target thread status observed before sending. If the app-server call fails, CTU writes `result.notify_failed` instead.
7. Optional synchronous wait: if the caller used `team_send_message` with `waitResult=true`, CTU writes `team.wait.completed` as soon as the result file is observed, or `team.wait.timeout` when the configured timeout expires.

## Claims

Claiming uses a lock file and an atomic move from `open` to `claimed`. A task should only be worked on by the claim owner. If a worker gets interrupted, the task remains visible in `claimed` and can be recovered manually.

## Results

Results contain:

- task id
- sender and receiver
- status
- summary
- changed files
- commits
- tests
- artifacts
- open questions
- next suggested action

## Events

`events.jsonl` is append-only. Consumers should tolerate partially written or corrupt lines and continue reading later lines.
