# Startup

## Normal CTU startup

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-codexteamup.ps1
```

## Acceptance startup

Run the same command inside the real acceptance clone:

```powershell
cd S:\_work\_development\codexteamup.acceptance
powershell -ExecutionPolicy Bypass -File .\scripts\start-codexteamup.ps1
```

## Rule

`start-codexteamup.ps1` is the KISS entrypoint. Users should not have to manually stage runtime folders, environment wiring, or wrapper commands.

## Session Manifest

Every startup writes `.ctu/sessions/current.json` in the checkout that started CTU. The manifest records the launcher PowerShell PID, Codex Desktop PID, CTU service PID, wrapper PIDs, service URL, pipe name, and controller runtime path.

Restart orchestration uses that manifest before command-line fallback cleanup. That matters when the user starts CTU from an already-open PowerShell window: after the startup script returns, the PowerShell command line may no longer contain `scripts/start-codexteamup.ps1`, so PID-based session cleanup is the durable way to close the old startup console and replace the whole CTU session.

Target restarts launch the target checkout through the same `scripts/start-codexteamup.ps1` entrypoint in a visible PowerShell window. The supervisor window is transient; the target startup window is the one that should remain visible after the switch.
