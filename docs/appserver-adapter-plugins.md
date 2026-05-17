# Codex Desktop App-Server Adapter Plugins

CodexTeamUp treats the Codex Desktop app-server integration as an unstable edge adapter.
AgentBus, MCP tools, team orchestration, and role bindings are the stable core. Desktop
thread lifecycle details are isolated behind `IAppServerClient`.

## Stable Core

- MCP tool contracts such as `team_ensure_agents`, `team_send_message`, and `bridge_notify_result`
- AgentBus files for agents, tasks, results, events, and audit state
- Agent role bindings to visible Desktop thread IDs
- Dashboard and deterministic tests

## Swappable Edge

The Codex Desktop app-server behavior can change between Desktop releases. Adapter-specific
logic belongs behind `IAppServerClient`, including:

- `thread/start` readiness waits
- `thread/resume` and `turn/start` retry policy
- temporary error classification such as missing thread or missing rollout
- app-server timeout handling
- unsafe Desktop calls such as synchronous thread naming

## Runtime Reload

The service creates a `ReloadableAppServerClient`. By default it wraps the built-in
`WrapperPipeAppServerClient`. A plugin DLL can be loaded without restarting Codex Desktop.

Initial load can be configured with environment variables before starting the service:

```text
CTU_APP_SERVER_PLUGIN_PATH=<absolute plugin dll path>
CTU_APP_SERVER_PLUGIN_TYPE=<optional full type name>
```

Runtime inspection and reload are available through HTTP:

```text
GET  http://127.0.0.1:47319/api/appserver-adapter
POST http://127.0.0.1:47319/api/appserver-adapter/reload
```

Reload request body:

```json
{
  "pluginPath": "S:/path/to/CodexTeamUp.DesktopAdapter.dll",
  "pluginType": "Example.DesktopAdapterPlugin",
  "options": {
    "threadReadyTimeoutMs": "15000"
  }
}
```

The same controls are exposed as MCP tools:

- `codex_appserver_adapter_status`
- `codex_appserver_adapter_reload`

Passing an empty `pluginPath` switches back to the built-in adapter.

## Plugin Contract

A plugin references `CodexTeamUp.AppServer` and implements `IAppServerClientPlugin`:

```csharp
using CodexTeamUp.AppServer;

public sealed class DesktopAdapterPlugin : IAppServerClientPlugin
{
    public string Name => "desktop-adapter";
    public string Version => "1.0.0";

    public IAppServerClient Create(AppServerClientPluginContext context)
    {
        return new MyDesktopAdapter(context.PipeName, context.Options);
    }
}
```

The host shadow-copies the plugin directory before loading it, so the original DLL can be
overwritten by a newer build and reloaded again. If a reload fails, the currently active
adapter stays active and the failure is reported in adapter status.
