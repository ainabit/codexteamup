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

It keeps `team_send_message` queue-first, caps Desktop wakeup at 8 seconds, caps inline waits at 10 seconds, names threads before prime, and starts prime prompts with the exact agent id.
