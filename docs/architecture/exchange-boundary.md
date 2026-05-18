# External Exchange Boundary

CTU needs a durable ingress/egress boundary for messages that do not originate from a live MCP call in the currently running visible thread flow.

This document describes the first concrete filesystem-backed exchange channel. It is not a claim that all external channels must be file-based.

That includes:

- restart/startup handoffs;
- project-scoped system requests;
- agent-targeted external tasks;
- exported packets for human or external AI review;
- imported responses that need correlation back into CTU.

## Layout

```text
.codexteamup/exchange/
  inbox/system/
  inbox/project/
  inbox/agent/
  startup/system/
  startup/project/
  startup/agent/
  outbox/
  deadletter/
  leases/
  payloads/
  correlations/
```

## Envelope model

Each filesystem-backed envelope should support:

- `messageId`
- `kind`
- `targetScope`
- `targetProject`
- `targetAgentId`
- `targetThreadName`
- `correlationId`
- `causationId`
- `responseTo`
- `createdAt`
- `notBefore`
- `expiresAt`
- `payloadType`
- `payloadPath`
- `attemptCount`
- `leaseOwner`
- `leaseExpiresAt`
- `lastError`

## Channel pump behavior

The controller runtime owns the sweep/import policy for this channel:

1. scan inboxes;
2. lease envelopes;
3. validate target scope and correlation;
4. translate into AgentBus tasks or controller system actions;
5. emit success, outbox, or dead-letter evidence.

`startup` follows the same schema as `inbox` but is reserved for restart/bootstrap handoffs and read with higher priority by the controller startup sweep loop. The service currently keeps that loop running after boot, so this is a dedicated startup lane rather than a single one-shot import phase.

If future channels exist, they should still land on this same envelope/correlation model before workflow routing.

## Restart usage

Restart currently uses:

```text
.codexteamup/exchange/startup/system/restart/<message-id>.json
```

That durable envelope is what the target CTU runtime reads through the startup-sweep loop before it internally dispatches the continuation.
