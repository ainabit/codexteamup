# ADR-0005: Channel adapter model

- Status: accepted
- Date: 2026-05-18

## Context

The filesystem exchange is useful for restart handoffs and external drop-in requests, but CTU should not hardcode its future around one transport. Future ingress/egress may include public MCP, REST/webhooks, or cross-project bridges.

## Decision

CTU uses a channel model:

- channel adapters are thin transport-specific connectors;
- all channels translate to a common durable exchange envelope/correlation contract;
- workflow routing stays in the controller runtime;
- a controller-owned channel pump/heartbeat distributes messages from channels into AgentBus or system actions.

## Consequences

- the filesystem inbox/outbox remains valid without becoming a permanent architectural bottleneck;
- restart can reuse the same channel model as future integrations;
- public MCP or HTTP ingress can be added later without moving orchestration logic into transport handlers.
