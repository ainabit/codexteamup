# CTU Runtime Architecture

This is the detailed runtime reference behind the architecture hub in [architecture/README.md](architecture/README.md). Read the hub first for document class and governance, then use this file for the full runtime-layer rule text.

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
- structured result outcome handling;
- deduplicated self-continuation scheduling;
- result notification policy;
- ACK/NACK behavior;
- polling strategy;
- controller operation state.

This layer must be hot-reloadable or scriptable where practical. Changes to volatile Desktop timing, waits, naming, retries, dispatch strategy, and orchestration flow should be made in this layer instead of requiring a CTU service restart.

The active controller must be a loaded controller module. The standard implementation is the shipped `CodexTeamUp.Controller.Default.dll` plugin. It is acceptable for CTU to ship that default DLL, but it must be loaded through the same reloadable controller path as replacement controllers. There must be no hidden hardcoded workflow fallback in the service or API access layer. If no controller plugin is loaded, CTU reports an explicit unloaded/error state and exposes only status/reload controls.

The running CTU runtime must be separate from source build folders. Use `.ctu/runtime` for reloadable runtime DLLs and `.ctu/tools` for published service/CLI/wrapper tools. Do not point CTU at `src/**/bin` or `src/**/obj`; those directories are for builds and tests and may be cleaned, rebuilt, or locked independently. Controller hot-publish may create versioned runtime directories under `.ctu/runtime/controllers/default/releases/` when the active DLL is locked; the startup path may read `.ctu/runtime/controllers/default/current-plugin.txt` to choose the last published plugin.

The controller runtime can expose a diagnostic/control tool surface of its own. This may look like MCP-in-controller or a controller command API, but it is for observability and runtime control. It must not move workflow logic back into the MCP API access layer.

### Controller-owned runtime loops

If CTU needs background progress while no human is actively calling a tool, that loop belongs to the controller runtime layer.

Examples:

- restart continuation pickup after a target checkout boots;
- startup handoff processing;
- external inbox import into AgentBus or system operations;
- outbox export completion and correlation reconciliation;
- stuck operation requeue, retry, or dead-letter policy;
- controller heartbeat checks that watch for pending durable work;
- fallback/recovery heartbeats that detect stranded plans or stale continuation registrations.

The fixed service/API layer may host the process and start/stop the loop, but it must not own the workflow policy. The policy, timings, retry caps, sweep interval, and routing rules belong to the hot-reloadable controller runtime or controller policy.

### Agent-owned continuations

CTU execution continuity is driven by each agent's own result, not by a normal global projectlead heartbeat.

Every AgentBus result must declare one structured outcome:

- `done`: this task is complete and no automatic follow-up is required;
- `handed_off`: this agent created or identified a follow-up for another owner;
- `self_continue`: this same agent needs a later wakeup to keep working;
- `human`: progress requires a human decision or input;
- `failed`: the task failed and should be visible as failed, retryable, or recoverable by policy.

Only `self_continue` may register a scheduled continuation. That continuation targets the same `agentId` that wrote the result, carries the originating `taskId` and `resultId`, includes a `notBefore` time or retry policy, and uses a deterministic dedupe key such as `agentId + chainId + nextAction`. Rewriting the same outcome should refresh or preserve the existing pending continuation instead of creating duplicate wakeups.

The controller runtime owns continuation scheduling policy: when a continuation becomes due and the same agent has no open or claimed work for that chain, the controller creates a durable AgentBus task and then performs best-effort dispatch through normal wakeup policy. It must not call Desktop directly outside the controller delivery policy.

A central `ctu/projectlead` or guardian heartbeat is no longer the normal carry-through mechanism. It may exist only as fallback/recovery: detect stale plans, missing outcomes, expired continuations, or stranded chains; surface them in the dashboard; and, if policy allows, enqueue a recovery task through AgentBus. The fallback must not manufacture routine progress when the owning agent should have written `self_continue`, `handed_off`, `human`, `failed`, or `done`.

The dashboard must show continuations centrally: pending, due, dispatched, expired, deduped, and blocked-by-human entries should be visible by agent, chain, source task/result, next action, and next wakeup time.

### 3. Durable Coordination Layer

AgentBus is the durable truth.

It owns:

- agents;
- tasks;
- results;
- result outcomes and continuation registrations;
- events;
- audit history;
- operation state when controller flows need to survive uncertain Desktop wakeups.

Desktop wakeup is best-effort. A Desktop timeout is not proof that a task was not received. AgentBus tasks/results/events are the source of truth.

The controller must not burst multiple Desktop `turn/start` calls in parallel. Queue task creation first, then serialize or throttle wakeups in the controller runtime. Burst wakeups can make otherwise valid Desktop calls return cancellation errors while the same target threads accept sequential retries.

### External exchange boundary

CTU may also expose an adjacent durable file-based exchange for inputs and outputs that originate outside the normal MCP/visible-thread flow.

That exchange lives outside the internal AgentBus layout, for example:

```text
<repo>/.codexteamup/exchange/
  inbox/
  outbox/
  deadletter/
  leases/
  payloads/
  correlations/
```

Use this boundary for:

- restart/startup handoff envelopes that must survive a session swap;
- externally dropped requests for a project or a specific agent;
- exported work packets for humans or external AI systems;
- imported responses that need correlation back into CTU;
- project-scoped system commands that should not be encoded as raw chat text.

The exchange is still durable state, not workflow. It owns message envelopes, payload manifests, correlation ids, leases, retry metadata, and dead-letter evidence. The controller heartbeat/runtime loop decides how to import an exchange message into AgentBus tasks, restart operations, or other controller-owned flows.

Do not overload `.codexteamup/agentbus/tasks/*` with foreign producer semantics. AgentBus remains the CTU-native coordination store; the exchange is the controlled ingress/egress boundary.

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
- `wrapper-YYYYMMDD.jsonl`
- `wrapper-YYYYMMDD.log`

JSONL is for machines. `.log` is for humans. Both are diagnostic only and must never break CTU.

Do not log full prompts unless explicitly needed for a narrow diagnostic. Prefer task ids, result ids, thread ids, operation ids, latency, status, and redacted error messages.

## Implementation Rule

When adding or changing runtime behavior, ask:

1. Is this transport/API adaptation? Put it in API access.
2. Is this workflow/timing/naming/retry/dispatch logic? Put it in the controller runtime.
3. Is this durable state or audit history? Put it in AgentBus.

If code violates this split, refactor before extending it.

## Wrapper Bridge Selection

Codex Desktop can start more than one wrapped `app-server` process. CTU must not expose the same named pipe from every wrapper process by default, because the service could connect to a helper app-server that is not the visible Desktop context.

The default wrapper bridge policy is selective: expose the CTU bridge pipe only for the Desktop app-server invocation that carries the Desktop analytics app-server argument. Other wrapped app-server invocations remain transparent proxies. For diagnostics, set `CODEX_WRAPPER_BRIDGE_MODE=all` before starting Desktop to restore the old behavior where every wrapper exposes the bridge. Set `CODEX_WRAPPER_BRIDGE_MODE=none` to disable the wrapper bridge entirely.
