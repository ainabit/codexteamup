# Interested User Onboarding

CodexTeamUp is currently a source-only proof of concept with no binary releases. The expected entry path for an interested user is: clone the repository, build it locally, run the local tests, and follow the startup path through the repo scripts and docs.

## Human Quickstart

From the repository root:

```powershell
dotnet build
dotnet run --project tests\CodexTeamUp.Tests
powershell -ExecutionPolicy Bypass -File .\scripts\start-codexteamup.ps1
```

The startup path publishes local tools, starts the CTU service at `http://127.0.0.1:47319/`, registers the MCP endpoint `http://127.0.0.1:47319/mcp` for Codex, and launches Codex Desktop through the wrapper.

## Expected Local Codex Agent Behavior

If an interested user creates a local Codex agent in this repository, that agent should read:

- `AGENTS.md`
- `README.md`
- `docs/agent-thread-usage.md`
- this file

After that, the local agent should:

1. build the repository locally,
2. run the test project,
3. explain or run the repo-relative startup command `powershell -ExecutionPolicy Bypass -File .\scripts\start-codexteamup.ps1`,
4. verify the service, MCP endpoint, and dashboard after startup,
5. clearly communicate that this is still an experimental source-only PoC without a binary release workflow.

## Verification

After startup, these checks are useful:

```powershell
Invoke-RestMethod http://127.0.0.1:47319/health
dotnet run --project src\CodexTeamUp.Cli -- wrapper status
dotnet run --project src\CodexTeamUp.Cli -- threads list --source wrapper --limit 5
```

The local dashboard is available at `http://127.0.0.1:47319/`.

For a single-command acceptance path after Desktop is running through CTU, use:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-fresh-clone-acceptance.ps1
```

Run that inside a real fresh acceptance clone, for example `S:/_work/_development/codexteamup.acceptance`. It runs health, MCP, dashboard, deterministic, and minimal live smoke checks. See [fresh-clone-acceptance.md](fresh-clone-acceptance.md).

## PoC Boundaries

- No binary release or installer path
- Windows-first workflow with PowerShell startup scripts
- MCP is the normal agent path; PowerShell remains bootstrap and recovery only
- The Desktop app-server transport is still experimental
- `.codexteamup/agentbus` remains the durable truth for team communication
