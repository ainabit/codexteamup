# Acceptance Hardening

- Status: active
- Owner: `ctu/architect`

## Goal

Prove that another user can freshly clone CTU, start it, discover MCP, see the dashboard, and run the minimum live smoke path.

## Definition of done

1. acceptance clone can be recreated cleanly;
2. `scripts/start-codexteamup.ps1` works from the acceptance checkout;
3. deterministic safety net passes there;
4. `scripts/test-fresh-clone-acceptance.ps1` passes there;
5. minimal live smoke passes there;
6. CTU can return from acceptance back to the normal checkout.

## Detailed references

- [../fresh-clone-acceptance.md](../fresh-clone-acceptance.md)
- [restart-orchestration.md](restart-orchestration.md)
