# Recovery

## Principle

Recovery should return CTU to a known-good runtime, not merely retry an arbitrary source tree.

## When restart fails

Check:

1. current `/health`
2. active `defaultBusRoot`
3. restart operation record under `.codexteamup/restart/operations`
4. wrapper/controller/service logs under `.codexteamup/logs`
5. pending startup handoff or exchange messages

## Goal state

Recovery is complete only when:

- CTU is healthy on the intended or fallback checkout,
- the durable restart/startup handoff is preserved,
- the architect thread can continue from durable state.
