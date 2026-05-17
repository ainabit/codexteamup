# CTU Runtime Architecture

This file is the mandatory architecture rule for CodexTeamUp runtime work.

## Non-Negotiable Layering

CodexTeamUp has three separate runtime layers.

### 1. API Access Layer

The API access layer is fixed, thin, and defensive.

It includes:

- the HTTP/MCP service surface;
- MCP tool schemas and request/response adaptation;
- `IAppServerClient` implementations that speak to Codex Desktop/app-server;
- JSON-RPC, named-pipe, HTTP, serialization, redaction, and timeout plumbing.

It must not contain orchestration workflow logic. It may validate requests, parse arguments, call the controller, serialize responses, catch/log exceptions, and return errors.

Hard API adapter boundaries must use defensive try/catch and project-local logging. Exceptions from Desktop, pipe, plugin, HTTP, or JSON boundaries must not crash CTU.

### 2. Controller Runtime Layer

The controller runtime owns workflow and coordination.

It includes:

- agent/thread creation flow;
- binding and rebinding;
- thread naming and prime prompt strategy;
- queue-first task creation;
- dispatch/wakeup timing;
- retry/defer policy;
- result notification policy;
- ACK/NACK behavior;
- polling strategy;
- controller operation state.

This layer must be hot-reloadable or scriptable where practical. Changes to volatile Desktop timing, waits, naming, retries, dispatch strategy, and orchestration flow should be made in this layer instead of requiring a CTU service restart.

The controller runtime can expose a diagnostic/control tool surface of its own. This may look like MCP-in-controller or a controller command API, but it is for observability and runtime control. It must not move workflow logic back into the MCP API access layer.

### 3. Durable Coordination Layer

AgentBus is the durable truth.

It owns:

- agents;
- tasks;
- results;
- events;
- audit history;
- operation state when controller flows need to survive uncertain Desktop wakeups.

Desktop wakeup is best-effort. A Desktop timeout is not proof that a task was not received. AgentBus tasks/results/events are the source of truth.

## Reload Rules

CTU should need a process restart only for hard API surface changes, binary contract changes, or service bootstrap changes.

The following should be reloadable without killing CTU:

- controller policy;
- wakeup and wait caps;
- naming and priming strategy;
- enqueue/dispatch strategy;
- controller retry/defer rules;
- controller plugin or script implementation.

Reload failures must keep the currently active controller.

## Logging Rules

Logs live in the project directory by default:

```text
<repo>/.codexteamup/logs/
```

Required log streams:

- `api-adapter-YYYYMMDD.jsonl`
- `api-adapter-YYYYMMDD.log`
- `controller-YYYYMMDD.jsonl`
- `controller-YYYYMMDD.log`

JSONL is for machines. `.log` is for humans. Both are diagnostic only and must never break CTU.

Do not log full prompts unless explicitly needed for a narrow diagnostic. Prefer task ids, result ids, thread ids, operation ids, latency, status, and redacted error messages.

## Implementation Rule

When adding or changing runtime behavior, ask:

1. Is this transport/API adaptation? Put it in API access.
2. Is this workflow/timing/naming/retry/dispatch logic? Put it in the controller runtime.
3. Is this durable state or audit history? Put it in AgentBus.

If code violates this split, refactor before extending it.
