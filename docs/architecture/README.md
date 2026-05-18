# CTU Architecture Hub

This directory is the binding entrypoint for CodexTeamUp architecture work.

Use it when you need to answer:

- what is non-negotiable;
- which runtime layer owns which behavior;
- which decisions are already fixed;
- which detailed reference documents back those rules.

## Read order

1. [principles.md](principles.md)
2. [runtime-layers.md](runtime-layers.md)
3. [channel-model.md](channel-model.md)
4. [exchange-boundary.md](exchange-boundary.md)
5. [acceptance-model.md](acceptance-model.md)
5. relevant ADRs under [../adr](../adr)

## Document classes

- `docs/architecture/**`: binding current-state architecture rules
- `docs/adr/**`: why a decision was made, alternatives, and consequences
- `docs/initiatives/**`: active execution tracks and definitions of done
- `docs/operations/**`: how to start, test, recover, and inspect CTU
- `docs/logbook.md`: reverse-chronological build journal for notable milestones, failures, and redesigns

## Detailed reference documents

These remain the detailed technical references behind the architecture hub:

- [../ctu-runtime-architecture.md](../ctu-runtime-architecture.md)
- [../architecture.md](../architecture.md)
- [../agentbus-protocol.md](../agentbus-protocol.md)
- [../restart-orchestration-plan.md](../restart-orchestration-plan.md)
- [../fresh-clone-acceptance.md](../fresh-clone-acceptance.md)

## Change discipline

If a change alters architecture rather than only implementation detail:

1. update the relevant file under `docs/architecture/**`;
2. add or update an ADR under `docs/adr/**`;
3. update the relevant initiative plan under `docs/initiatives/**`;
4. update operations/test guidance if execution behavior changed.

Architecture changes are incomplete until the documentation reflects the new decision.
