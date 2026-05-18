# Testing Matrix

## Product safety net

Always run before a meaningful PR:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-codexteamup.ps1
```

For service, wrapper, MCP, AgentBus, controller, binding, startup, or restart changes, also run the relevant live smoke or `-LiveAll`.

## Outside-user safety net

Run inside the real acceptance clone:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test-fresh-clone-acceptance.ps1
```

## Architecture-heavy changes

For restart, acceptance, bootstrap, wrapper, or orchestration redesign:

1. deterministic safety net
2. relevant live smokes
3. acceptance clone proof
4. when needed, full roundtrip `codexteamup -> acceptance -> codexteamup`
