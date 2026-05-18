# ADR-0001: Hot-reloadable controller runtime

- Status: accepted
- Date: 2026-05-17

## Context

Desktop timing, wait caps, wakeup policy, naming behavior, and orchestration flow changed frequently while Codex Desktop behavior was unstable. Requiring a full CTU process restart for every controller policy fix made iteration slow and brittle.

## Decision

Workflow logic lives in a hot-reloadable controller runtime. The fixed service/API layer stays thin and does not contain hidden non-reloadable orchestration fallback logic.

## Consequences

- controller DLLs/scripts can be replaced faster than service bootstrap code;
- runtime state and controller status must be observable;
- tests must detect accidental workflow leakage into the fixed service/API layer.
