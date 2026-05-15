# Findings

Historical internal note: this document captures local observations from manual PoC validation on a real Windows machine. Local installation paths and user-specific locations below are observations, not recommended setup instructions.

Status date: 2026-05-14.

## Local Environment

- .NET SDK: `10.0.204`
- Codex CLI observed at `%LOCALAPPDATA%\OpenAI\Codex\bin\codex.exe`
- Codex CLI version from Windows metadata: `0.130.0.0`
- Codex Desktop process path observed at `C:\Program Files\WindowsApps\OpenAI.Codex_26.506.3741.0_x64__2p2nqsd0c76g0\app\Codex.exe`

The following commands were present and returned help output:

- `codex --help`
- `codex app-server --help`
- `codex app-server proxy --help`
- `codex remote-control --help`
- `codex app-server generate-json-schema --help`

`codex app-server generate-json-schema --experimental --out <dir>` worked and generated schemas.

## Manual Probe Under the Real Windows User

On 2026-05-14, `scripts/probe-codex-app-server.ps1 -GenerateSchemas` was run from a normal PowerShell window under the real Windows user account.

Observed result:

- Codex CLI was found at the known local install path.
- `codex` was not on `PATH` in that PowerShell session, but the known install path worked.
- The help commands listed above returned exit code `0`.
- `CODEX_HOME=%USERPROFILE%\.codex` existed.
- `session_index.jsonl`, `state_5.sqlite`, and `logs_2.sqlite` existed.
- `%USERPROFILE%\.codex\app-server-control` did not exist.
- `%USERPROFILE%\.codex\app-server-control\app-server-control.sock` did not exist.
- `codex app-server proxy` failed with a socket error.
- `codex app-server --listen stdio://` started as a separate standalone app-server.
- Schema generation with `--experimental` worked under the real user account.

Assessment: even with real Windows user rights, the visible Codex Desktop process did not expose a reachable control socket at the expected path.

## Notes From the Open Codex Repository

The open `openai/codex` app-server documentation describes four transport types:

- `stdio://`: newline-delimited JSON over stdin and stdout
- `ws://IP:PORT`: WebSocket, experimental and unsupported
- `unix://` or `unix://PATH`: WebSocket over a control socket
- `off`: no local transport

`codex app-server proxy` only opens a raw stream to the control socket and proxies bytes. The proxied connection contains an HTTP upgrade and then WebSocket frames, so `proxy` is not a simple JSONL RPC client.

An open GitHub issue (`openai/codex#21743`, 2026-05-08) reported the same core behavior for Windows Desktop: Desktop started the app-server separately over stdio, exposed no `app-server-control.sock`, and an external app-server client could append persisted turns without producing deterministic live refresh in an already-open Desktop thread view.

Local additional test:

- A separately started `codex app-server --listen unix://<workspace-sock>` did create a socket.
- Direct JSONL requests sent through `codex app-server proxy --sock <sock>` were rejected with an HTTP parse error, as expected for a WebSocket transport.
- A real WebSocket-over-UDS client would be required for that transport.

Assessment: the control socket transport is technically usable for a separately started app-server, but that is not the same thing as controlling the already-visible Desktop instance.

## Desktop Start With an External WebSocket App-Server

`codex app --help` exposed no documented port, endpoint, or app-server transport parameter. The observed Desktop bundle did contain an undocumented hook:

```text
process.env.CODEX_APP_SERVER_WS_URL ?? e.websocket_url
```

The same code path also appeared to honor `CODEX_APP_SERVER_FORCE_CLI=1`. `CODEX_ELECTRON_USER_DATA_PATH` also appeared to exist for isolated-user-data testing.

Assessment:

- The official, documented part is that `codex app-server --listen ws://IP:PORT` can expose an experimental WebSocket transport.
- The locally observed part is that Codex Desktop reads `CODEX_APP_SERVER_WS_URL`.
- That is not a stable public contract and may break with app updates.

Historical local test harness:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-codex-desktop-with-ws-appserver.ps1 -ProbeOnly -Port 8765
```

Observed result:

- A separate `codex app-server --listen ws://127.0.0.1:8766` started successfully.
- `http://127.0.0.1:8766/readyz` returned ready.

Historical local Desktop docking test:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-codex-desktop-with-ws-appserver.ps1 -Port 8765
```

Observed result:

- Codex Desktop started visibly.
- The sign-in flow showed `Sign-in failed: Codex app-server is not available`.
- The external app-server stayed reachable during the test.
- A direct WebSocket JSON-RPC probe against `ws://127.0.0.1:8765` succeeded for `initialize` and `thread/list`.

Assessment: the external WebSocket app-server was technically usable, but Desktop did not attach to it well enough for a normal local login and UI path.

Historical stop command:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-codex-desktop-with-ws-appserver.ps1 -Stop
```

## `CODEX_CLI_PATH` as the Strongest Local Hook

The installed Desktop app also appeared to read `CODEX_CLI_PATH` before falling back to the bundled or default `codex.exe` path.

Assessment: this was the first realistic non-invasive hook close to Desktop behavior. If Desktop starts its local app-server CLI from that path, a wrapped or patched CLI and app-server can sit under the same visible Desktop UI without modifying the installed app itself.

To validate that path safely, `CodexTeamUp.CodexWrapper` was built to:

- delegate all arguments to the real `codex.exe`,
- proxy stdin, stdout, and stderr transparently,
- write only JSONL file logs,
- redact known sensitive environment variable names.

Historical local smoke test:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-codex-desktop-with-cli-wrapper.ps1 -NoLaunch
```

Historical local log review:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\show-codex-cli-wrapper-logs.ps1
```

Decision point:

- If the wrapper log shows `AppServer=True`, Desktop is using `CODEX_CLI_PATH` for the local app-server.
- If no Desktop launch appears in the wrapper log, that hook is not usable on the normal local Desktop path.

Observed local result on 2026-05-14:

```text
Args: app-server --analytics-default-enabled
AppServer: True
cwd: %USERPROFILE%
```

Assessment: Codex Desktop did start its local app-server through `CODEX_CLI_PATH`.

## Live Side-Channel Result

Historical local breakthrough on 2026-05-14:

- The release wrapper was started by Codex Desktop through `CODEX_CLI_PATH`.
- `ctu/status` over the named pipe returned `ok`.
- `thread/list` over the named pipe returned Desktop threads including the target thread.
- A direct `turn/start` against the unloaded target thread first returned `thread not found`.
- `thread/read` worked, and `thread/resume` was needed to load the thread into the live registry.
- After `thread/resume`, `turn/start` succeeded.
- The target thread replied with `Hello from the CodexTeamUp wrapper bridge.`

Assessment: the goal of triggering one visible or persisted Desktop thread from another is technically reachable when Desktop is started through the release wrapper. The target thread may need `thread/resume` before `turn/start`.

## UI Ordering Behavior

Observed behavior:

- `thread/read` returned turns in chronological order.
- `thread/turns/list` defaulted to `sortDirection=desc`.
- Desktop therefore displayed the newer turn above the older turn after the side-channel test.
- `thread/turns/list` with `sortDirection=asc` returned the expected order.

Mitigations adopted in the PoC:

- `CODEX_WRAPPER_FORCE_TURNS_ASC=1` rewrites Desktop `thread/turns/list` requests without explicit sort direction to `asc`.
- `CODEX_WRAPPER_STAMP_TURN_STARTED_AT=1` stamps live `turn/started` notifications that arrive with `startedAt=null`.
- CTU wakeups now use `thread/resume` with `excludeTurns=true` so older turns are not replayed into Desktop as a new live block.

## App-Server Control Socket

`codex app-server proxy` expects a control socket under:

```text
%CODEX_HOME%\app-server-control\app-server-control.sock
```

In the sandbox, a default sandbox home was used until `CODEX_HOME` was pointed to the real user home. At the real path, the expected control-socket directory still did not exist, and the proxy handshake failed.

Process metadata checks against running Codex processes showed no obvious command-line hints for `app-server-control`, `.sock`, `--listen`, `ws://`, `wss://`, or `remote`.

Assessment: on this machine, the visible Desktop app-server was not reachable through the expected local proxy path.

## Local Codex State Files

Relevant observed paths under `%USERPROFILE%\.codex` included:

- `session_index.jsonl`
- `sessions\2026\...\rollout-*.jsonl`
- `state_5.sqlite`
- `logs_2.sqlite`
- `auth.json` and config files that the PoC does not read or print

The PoC only reads:

- `session_index.jsonl`
- `sessions/**/*.jsonl`

SQLite support was intentionally not implemented because no `sqlite3` CLI was available and the PoC was kept free of extra NuGet dependencies.

## Read-Only Result

Confirmed possible:

- list persisted threads locally,
- filter by `cwd` when `session_meta.cwd` is present,
- print thread id, thread name from `session_index.jsonl`, cwd, source, status, updated time, and rollout path,
- read a specific thread from rollout JSONL and return redacted previews.

Not guaranteed:

- whether a thread is currently open in Desktop,
- live UI state from Desktop,
- canonical full thread data from SQLite.

## Controlled Trigger Result

The live side-channel path is implemented with guarded CLI commands such as:

- `ctu threads start --cwd <path> --name <name> --role <role>`
- `ctu threads send --thread-id <id> --message <text>`
- `ctu dispatch --task-id <id> --to-thread <thread-id>`
- `ctu notify --result-id <id> --to-thread <thread-id>`

All mutating app-server calls require explicit confirmation unless `--yes` is supplied.

## Main Blocker

The formal protocol methods are not enough on their own while visible Codex Desktop still does not expose a stable, reachable control socket or documented WebSocket endpoint for external local clients. A separate app-server process can work against shared state, but reliable live Desktop UI synchronization remains undocumented and incomplete.
