# Acceptance Model

Acceptance exists to prove outside-user behavior, not just internal development behavior.

## Core rule

`S:/_work/_development/codexteamup.acceptance` is a real separate clone of the repository.

It is:

- disposable,
- re-cloneable,
- startable through `scripts/start-codexteamup.ps1`,
- usable for fresh-clone smoke validation.

It is not:

- a side-by-side development worktree,
- a place to patch source to make tests pass,
- a substitute for the main `codexteamup` development checkout.

## Expected flow

1. fix and push from the main repo;
2. refresh or recreate the acceptance clone;
3. start CTU from the acceptance clone;
4. run deterministic plus minimal live acceptance proof;
5. return to the normal CTU checkout.

## Detailed reference

See:

- [../fresh-clone-acceptance.md](../fresh-clone-acceptance.md)
- [../restart-orchestration-plan.md](../restart-orchestration-plan.md)
