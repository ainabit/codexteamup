# Restart Orchestration

- Status: active
- Owner: `ctu/architect`

## Goal

Switch CTU reliably between `codexteamup` and `codexteamup.acceptance`, carry forward intent durably, and recover to a known-good runtime when the target path fails.

## Definition of done

1. restart request writes durable restart record and startup handoff;
2. target checkout boots through `scripts/start-codexteamup.ps1`;
3. target CTU startup sweep imports the restart handoff;
4. target architect continues work without relying on old chat history;
5. acceptance roundtrip back to normal CTU works;
6. rollback returns to verified healthy known-good CTU.

## Phases

1. known-good checkpoint + safe rollback
2. exchange inbox/outbox + correlation model
3. controller startup sweep / heartbeat
4. end-to-end acceptance roundtrip proof

## Detailed plan

See [../restart-orchestration-plan.md](../restart-orchestration-plan.md).
