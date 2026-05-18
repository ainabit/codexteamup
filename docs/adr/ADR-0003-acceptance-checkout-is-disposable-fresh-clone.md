# ADR-0003: Acceptance checkout is a disposable fresh clone

- Status: accepted
- Date: 2026-05-17

## Context

Testing in the main development checkout does not prove outside-user startup, clone, fetch, and bootstrap behavior. A side-by-side state directory is also weaker than a real clone.

## Decision

`codexteamup.acceptance` is a real sibling clone used only for fresh-clone acceptance and live startup proof. Fixes happen in the main repo, get pushed, and are then re-tested from the acceptance clone.

## Consequences

- acceptance can be deleted and recreated freely;
- startup and restart must work from either checkout;
- documentation and tests must clearly distinguish development from acceptance.
