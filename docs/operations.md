# Operations

## Normal Desktop Start

To return to normal Codex Desktop behavior, close Desktop and start it normally without the wrapper script. No installed Desktop files are modified.

## Wrapper Start

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-codexteamup.ps1
```

That is the normal and preferred start path. It starts the backend service, registers the HTTP MCP URL, and launches Codex Desktop through the wrapper.

The startup path enables chronological UI mitigations by default. The wrapper sets `CODEX_WRAPPER_FORCE_TURNS_ASC=1` so Desktop `thread/turns/list` requests without an explicit `sortDirection` are rewritten to `asc`. It also sets `CODEX_WRAPPER_STAMP_TURN_STARTED_AT=1` so live `turn/started` notifications with `startedAt=null` get a current timestamp before they reach Desktop. The second mitigation affects only live UI sorting; persisted thread history remains authoritative.

Internal CTU wakeups resume target threads with `excludeTurns=true` before `turn/start`. This loads the target thread for the app-server without replaying old turns into Desktop as a fresh live block.

Chronological mitigation opt-out:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-codexteamup.ps1 -NoForceTurnsAscending
```

Live turn timestamp mitigation opt-out:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-codexteamup.ps1 -NoStampTurnStartedAt
```

## Verify

```powershell
Invoke-RestMethod http://127.0.0.1:47319/health
dotnet run --project src\CodexTeamUp.Cli -- wrapper status
dotnet run --project src\CodexTeamUp.Cli -- threads list --source wrapper --limit 5
```

The dashboard is available at `http://127.0.0.1:47319/`.

## Recovery

If the wrapper build output is locked, close Codex Desktop and rebuild. The longer-term plan is to use versioned wrapper publish directories to avoid this.

If a task is stuck in `claimed`, inspect `.codexteamup/agentbus/events.jsonl`, the task file, and the target thread. Move it manually only after ownership is clear.

## Logging

Wrapper logs default to:

```text
%USERPROFILE%\.codex\codexteamup-wrapper
```

Logs are JSONL and should be reviewed before sharing.
