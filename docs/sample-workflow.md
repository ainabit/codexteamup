# Sample Workflow

This workflow is a generic sample for a downstream multi-agent project that uses CodexTeamUp.

## Agents

- `ctu/architect`: slices work, owns scope and final decisions.
- `ctu/web`: implements web UI and interaction slices.
- `02-design`: checks responsive layout, visual fit and design consistency.
- `03-reviewer`: runs tests and reviews regressions.
- `04-contract`: owns schemas, API contracts and data model boundaries.

## Setup

```powershell
ctu bus init --bus-root .\.codexteamup\agentbus
ctu bus agent register --id ctu/architect --role ctu/architect --thread-id <architect-thread> --cwd <project-root>
ctu bus agent register --id ctu/web --role web --thread-id <web-thread> --cwd <project-root> --return-to ctu/architect --allowed-paths "web/,shared/,docs/"
ctu bus agent register --id 03-reviewer --role reviewer --thread-id <review-thread> --cwd <project-root> --return-to ctu/architect
```

## Delegate a Web Slice

```powershell
ctu delegate --bus-root .\.codexteamup\agentbus --to ctu/web --title "Frontend Slice" --prompt-file docs\coordination\ctu/web-S001.md --wait-result --yes
```

## Worker Contract

The worker should:

1. Read the task file.
2. Claim the task.
3. Work only in allowed paths.
4. Run focused tests.
5. Write a result.
6. Notify the Architect.

## Result Review

The Architect should verify:

- scope was respected
- changed files match allowed paths
- tests are credible
- open questions are captured
- a follow-up reviewer task is needed or not
