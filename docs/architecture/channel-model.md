# Channel Model

CTU should treat external message transport as a channel abstraction, not as a single filesystem trick.

## Purpose

Channels let CTU receive or emit durable messages beyond the live visible-thread/MCP flow while keeping a common internal contract.

Examples:

- filesystem inbox/outbox
- restart startup handoff
- public MCP-facing ingress
- REST/webhook ingress or egress
- future company-bus bridges between projects

## Rule

Every channel must map to the same logical envelope model and correlation rules before CTU workflow touches it.

Channels may differ in transport, but not in coordination semantics.

## Channel responsibilities

A channel adapter is thin. It may:

- read or write transport-specific payloads;
- translate them to or from the canonical exchange envelope;
- own transport-specific diagnostics and retries at the transport edge.

A channel adapter must not:

- decide workflow routing;
- decide task ownership;
- decide restart policy;
- decide cross-agent orchestration behavior.

Those belong to the controller runtime.

## Channel pump / heartbeat

CTU needs a runtime distribution loop for channel traffic.

This should be treated as a channel pump with a short heartbeat:

1. poll or receive channel work,
2. lease pending envelopes,
3. validate correlation and target scope,
4. hand off to controller workflow,
5. persist outbox/dead-letter evidence,
6. retry or escalate according to policy.

The heartbeat/sweep executor may be hosted by the service process, but its routing/timing policy belongs to the hot-reloadable controller runtime.

## First channel implementation

The first concrete channel is the filesystem exchange under:

```text
.codexteamup/exchange/
```

Restart uses that filesystem exchange first, but the architecture must not assume that all future ingress/egress is file-based.
