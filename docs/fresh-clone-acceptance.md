# Fresh Clone Acceptance

This path is the smallest end-to-end proof that a real fresh checkout can start CodexTeamUp, expose MCP, load the dashboard, and complete one real Desktop smoke run.

## Goal

A new user should be able to:

1. clone the repository,
2. start Codex Desktop through the CTU startup script,
3. verify service, MCP, and dashboard reachability,
4. run one minimal live smoke scenario in that checkout.

This is intentionally narrower than the full development safety net. It is an onboarding and operator-confidence path, not a full regression sweep.

## Preconditions

- Windows with PowerShell
- Git
- .NET 10 SDK
- local Codex Desktop installation
- Codex Desktop started through:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-codexteamup.ps1
```

## Minimal Acceptance Flow

In a separate acceptance project such as `S:/_work/_development/codexteamup.acceptance`, first create a real clone:

```powershell
git clone https://github.com/ainabit/codexteamup.git S:\_work\_development\codexteamup.acceptance
cd S:\_work\_development\codexteamup.acceptance
powershell -ExecutionPolicy Bypass -File .\scripts\start-codexteamup.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\test-fresh-clone-acceptance.ps1
```

The acceptance runner assumes it is running inside that real checkout and then runs:

- service health verification,
- MCP tool reachability checks,
- dashboard snapshot reachability,
- deterministic safety net,
- one live smoke scenario: `basic`.

## What Counts As Pass

- `GET /health` succeeds.
- MCP tool calls for controller/app-server status and `agentbus_list_agents` succeed.
- `GET /api/snapshot` succeeds for the acceptance checkout.
- `scripts/test-codexteamup.ps1` passes.
- the live `basic` scenario passes in the acceptance checkout.

## What This Does Not Prove

- full `peer` or `replacement` orchestration coverage,
- company-bus or multi-project federation,
- dashboard UX quality beyond basic reachability,
- release readiness on every machine.

Those stay in the broader test and roadmap tracks.

## Important Boundary

`codexteamup.acceptance` is not a second development workspace. Treat it as a synthetic outside-user machine:

- pull or clone from remote,
- start CTU,
- run acceptance,
- report evidence.

Do not patch source there to "make the test pass". Fixes belong in the main development checkout, then get pushed, then the acceptance checkout pulls again and retries.
