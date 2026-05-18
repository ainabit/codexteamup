# Controller Runtime

CodexTeamUp keeps hard API adapters separate from volatile orchestration policy.

## Layers

- Fixed API adapter layer: service HTTP/MCP boundary and `IAppServerClient` app-server transport. This layer owns transport calls, JSON-RPC, redaction, defensive try/catch behavior, and API adapter logs.
- Controller runtime layer: workflow and timing decisions such as enqueue vs inline dispatch, wakeup timeout caps, wait caps, thread naming before prime, prime prompt title fallback, dispatch strategy, and retry/defer behavior.

The controller runtime can be inspected and reloaded without restarting CTU:

```powershell
Invoke-RestMethod -Uri http://127.0.0.1:47319/api/controller
```

```powershell
Invoke-RestMethod -Uri http://127.0.0.1:47319/api/controller/reload `
  -Method Post `
  -ContentType "application/json" `
  -Body '{"policyPath":"S:/_work/_development/codexteamup/config/ctu-controller-policy.json"}'
```

The policy-only path remains available:

```powershell
Invoke-RestMethod -Uri http://127.0.0.1:47319/api/controller-policy/reload `
  -Method Post `
  -ContentType "application/json" `
  -Body '{"policyPath":"S:/_work/_development/codexteamup/config/ctu-controller-policy.json"}'
```

Agents can use the MCP tools:

- `codex_controller_status`
- `codex_controller_reload`
- `codex_controller_policy_status`
- `codex_controller_policy_reload`

Reload failures keep the active controller and policy.

## Default Controller Plugin

The default workflow controller is deployed as a normal controller plugin DLL:

```text
CodexTeamUp.Controller.Default.dll
```

On startup, CTU loads `CTU_CONTROLLER_PLUGIN_PATH` when it is set; otherwise it loads `CodexTeamUp.Controller.Default.dll` from the service base directory. Passing an empty plugin path to `codex_controller_reload` or `POST /api/controller/reload` reloads that same default plugin DLL.

There is no hidden built-in workflow fallback. If no controller plugin can be loaded, the controller status is `unloaded`, workflow tools are unavailable, and only status/reload controls remain usable. If a reload fails while a controller plugin is already active, the active plugin stays active and the error is reported in controller status/logs.

The normal repo runtime location is:

```text
.ctu/runtime/controllers/default/CodexTeamUp.Controller.Default.dll
```

`scripts/start-codexteamup.ps1` sets `CTU_CONTROLLER_PLUGIN_PATH` to the plugin recorded in `.ctu/runtime/controllers/default/current-plugin.txt` when that pointer exists and is valid; otherwise it uses the default runtime DLL. The user-facing startup path stays one command; the script publishes or refreshes local runtime files before starting CTU. For controller-only development, build and copy the controller plugin without touching the running service binaries:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-controller-runtime.ps1
```

If the active default plugin DLL is locked by a running CTU process, `publish-controller-runtime.ps1` publishes a versioned runtime under:

```text
.ctu/runtime/controllers/default/releases/<timestamp>/
```

It also updates `current-plugin.txt` to the published plugin path. Reload the path reported by the script, or call controller reload with an empty body when the process environment already points at that DLL.

Then reload the running controller through MCP or HTTP:

```powershell
Invoke-RestMethod -Uri http://127.0.0.1:47319/api/controller/reload -Method Post -ContentType "application/json" -Body "{}"
```

This keeps `src/**/bin` and `src/**/obj` out of the running CTU runtime so routine builds and tests are not blocked by CTU using its own DLLs.

## Logs

By default the service writes JSONL and human `.log` files to:

```text
<repo>/.codexteamup/logs/
```

Files:

- `api-adapter-YYYYMMDD.jsonl`
- `api-adapter-YYYYMMDD.log`
- `controller-YYYYMMDD.jsonl`
- `controller-YYYYMMDD.log`

Set `CTU_LOG_ROOT` to override the directory. Logging is best-effort and must not break CTU.

## Default Policy

The checked-in default policy is:

```text
config/ctu-controller-policy.json
```

It keeps `team_send_message` queue-first, serializes Desktop wakeups, caps Desktop wakeup at 8 seconds, caps inline waits at 10 seconds, names threads before prime, and starts prime prompts with the exact agent id. Agent prime turns use the same short wakeup/defer policy as task dispatch so a stalled Desktop app-server call records `agent.prime_deferred` instead of blocking the controller for the wrapper response timeout. Short live-smoke or recovery flows may explicitly pass `prime=false setName=false` so AgentBus registration and dispatch can be tested without first depending on volatile Desktop rename or prime calls.

## Guardian Heartbeat

The controller can run a lightweight guardian heartbeat from the startup/sweep loop. This is not an API-layer feature; it is controller workflow policy and must remain reloadable.

Relevant policy fields:

- `guardianHeartbeatEnabled`
- `guardianHeartbeatAgentId`
- `guardianHeartbeatDisplayName`
- `guardianHeartbeatPlanFile`
- `guardianHeartbeatStatusDirectory`
- `guardianHeartbeatIntervalSeconds`

If `guardianHeartbeatPlanFile` is unset, the default is:

```text
.codexteamup/guardian/plan.md
```

If `guardianHeartbeatStatusDirectory` is unset, the default is:

```text
.codexteamup/guardian/status
```

The status directory contains one marker file named after the current state. `pending`, `open`, and `running` mean the plan still needs movement. `closed`, `done`, `failed`, and `human` stop automatic wakeups. When the plan is active and the configured guardian has no open or claimed AgentBus work, CTU enqueues a short task for the guardian and dispatches it through the normal delivery path.
