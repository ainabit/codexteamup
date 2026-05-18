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
