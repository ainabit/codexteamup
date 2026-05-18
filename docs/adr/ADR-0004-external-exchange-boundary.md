# ADR-0004: External exchange boundary

- Status: accepted
- Date: 2026-05-18

## Context

CTU needs durable ingress/egress beyond internal AgentBus semantics:

- restart handoffs,
- externally dropped tasks or requests,
- outbox packets for human or external AI workflows,
- correlated imported responses.

Overloading AgentBus task folders with these foreign semantics would blur ownership and make controller behavior harder to reason about.

## Decision

CTU gets an adjacent external exchange boundary under `.codexteamup/exchange/**`, with inbox/outbox/deadletter/correlation semantics. The controller runtime imports/export messages between exchange and AgentBus or system actions.

## Consequences

- AgentBus remains the CTU-native internal bus;
- restart and future external integrations share one durable ingress/egress model;
- controller heartbeat/background sweep becomes a first-class orchestration concern.
