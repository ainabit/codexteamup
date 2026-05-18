# Runtime Layers

CodexTeamUp runtime work is split into three layers.

## 1. API access layer

Owns:

- HTTP/MCP tool surface
- request/response adaptation
- wrapper transport and app-server clients
- thin ingress/egress channel adapters such as file exchange, public MCP, or REST/webhook adapters
- serialization, redaction, timeout plumbing
- defensive try/catch and project-local logging

Must stay thin.

## 2. Controller runtime layer

Owns:

- agent/thread orchestration
- naming and priming policy
- queue-first dispatch strategy
- retry/defer/wakeup timing
- controller-owned background loops and channel pump policy
- startup handoff import
- restart policy and durable operation steering

Must be hot-reloadable or scriptable where practical.

## 3. Durable coordination layer

Owns:

- AgentBus state
- external exchange state
- restart records
- checkpoints
- correlation metadata
- dead-letter evidence

## Canonical detailed reference

See [../ctu-runtime-architecture.md](../ctu-runtime-architecture.md) for the full rule text.
