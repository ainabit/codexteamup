# ADR-0002: Restart uses durable startup handoff

- Status: accepted
- Date: 2026-05-18

## Context

Direct continuation delivery immediately after `target_healthy` proved fragile. The target CTU runtime could already be booted while wrapper/app-server continuation delivery still timed out, leaving the operation half-complete.

## Decision

Restart continuation is delivered as a durable startup handoff message under `.codexteamup/exchange/startup/system/restart/` in the target checkout. The newly started CTU runtime reads that message during startup sweep and performs the internal dispatch itself.

## Rejected alternative

Immediate live wrapper/app-server continuation RPC after target health check.

## Consequences

- restart no longer depends on a single post-start live RPC succeeding;
- restart and external ingress share a common exchange/correlation model;
- controller startup sweep becomes part of the product architecture.
