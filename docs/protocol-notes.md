# Protocol Notes

Historical internal note: these notes summarize observed app-server behavior from local schema generation, manual probes, and wrapper experiments. They are not a guarantee of stable upstream Codex behavior.

Status date: 2026-05-14.

## Status

The app-server protocol is marked as experimental in the CLI. These notes are derived from local schema generation and observed behavior.

Schema source:

```powershell
codex app-server generate-json-schema --experimental --out <dir>
```

## JSON-RPC Shape

Requests are JSON-RPC-like objects with `id`, `method`, and optional `params`:

```json
{"id":1,"method":"initialize","params":{"clientInfo":{"name":"CodexTeamUp","version":"0.1.0"},"capabilities":{"experimentalApi":true}}}
```

After `initialize`, a client notification exists:

```json
{"method":"initialized"}
```

Example `thread/list`:

```json
{"id":2,"method":"thread/list","params":{"limit":20,"useStateDbOnly":true}}
```

Example `thread/read`:

```json
{"id":3,"method":"thread/read","params":{"threadId":"<id>","includeTurns":true}}
```

Example `turn/start`:

```json
{"id":4,"method":"turn/start","params":{"threadId":"<id>","input":[{"type":"text","text":"Hello"}],"approvalPolicy":"on-request"}}
```

## Formally Present Methods

The generated schema included:

- `thread/list`
- `thread/read`
- `thread/start`
- `thread/name/set`
- `thread/resume`
- `thread/fork`
- `turn/start`
- `turn/steer`
- `thread/inject_items`
- `thread/turns/list`
- `thread/turns/items/list`

## Relevant Parameters

`thread/list`:

- `archived`
- `cursor`
- `cwd`
- `limit`
- `modelProviders`
- `searchTerm`
- `sortDirection`
- `sortKey`
- `sourceKinds`
- `useStateDbOnly`

`thread/read`:

- `threadId`
- `includeTurns`

`thread/start`:

- `cwd`
- `model`
- `effort`
- `approvalPolicy`
- `sandbox`
- `permissions`
- `developerInstructions`
- `threadSource`

`turn/start`:

- `threadId`
- `input`
- `cwd`
- `approvalPolicy`
- `sandboxPolicy`
- `permissions`
- `model`
- `effort`

CodexTeamUp maps persisted agent settings to these fields. `AgentDefinition.Model` becomes `model`, and `AgentDefinition.ReasoningEffort` becomes `effort`.

## Transport Notes

`codex app-server proxy` describes itself as:

```text
Proxy stdio bytes to the running app-server control socket
```

Observed option:

```text
--sock <SOCKET_PATH>
```

Observed default path:

```text
%CODEX_HOME%\app-server-control\app-server-control.sock
```

On the observed machine, that socket was not reachable.

Important behavior:

- `codex app-server proxy` is not a JSONL client.
- The official `codex-rs/app-server` material indicates that it proxies raw bytes into a WebSocket transport.
- Direct JSONL lines that work with `stdio://` are therefore wrong for `proxy`.
- A local test against a separately started `codex app-server --listen unix://<workspace-sock>` produced the expected HTTP parse failure when JSONL was written directly into that WebSocket transport.

`codex app-server --listen stdio://` can start its own app-server process and accepts newline-delimited JSON directly over stdin and stdout. That is useful for protocol testing, but it is not the same as a visible existing Desktop thread.

`codex app-server --listen unix://` can create a control socket for a separately started app-server. An external client needs a real WebSocket-over-UDS implementation for that path.

`codex app-server --listen ws://127.0.0.1:<port>` can expose a local WebSocket app-server. According to the official docs, that transport is experimental and unsupported. The listener also exposes `GET /readyz` and `GET /healthz`.

`codex app` itself does not expose a documented endpoint or port parameter, but the installed Desktop app was observed to read `CODEX_APP_SERVER_WS_URL`:

```text
process.env.CODEX_APP_SERVER_WS_URL ?? e.websocket_url
```

That is not a documented contract. The safe local test path was:

1. start an external `ws://127.0.0.1:<port>` app-server,
2. start Desktop with `CODEX_APP_SERVER_WS_URL`,
3. test the same server with a dedicated WebSocket JSON-RPC client.

Observed result:

- direct WebSocket JSON-RPC against the external app-server worked,
- Desktop still showed `Sign-in failed: Codex app-server is not available`,
- therefore the WebSocket transport is realistic for a dedicated client but not automatically a visible Desktop control transport.

## Safety Rules in the PoC

- app-server access is still experimental
- the wrapper side-channel uses bridge-owned request ids with the `ctu:` prefix and intercepts those responses before Desktop sees them
- mutating PowerShell helper scripts such as `send-codex-wrapper-turn.ps1` require explicit confirmation unless `-Yes` is supplied
- mutating commands require confirmation unless `--yes` is supplied
- `threads send`, `dispatch`, and `notify` try to inspect live thread state first and warn when the target appears active
- `dispatch --check-git` can show a Git status hint for the task working directory before sending
- obvious token and secret patterns are redacted
- `danger-full-access` is not the default
- `.codexteamup/agentbus` events remain append-only JSONL
