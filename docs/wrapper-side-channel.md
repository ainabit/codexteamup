# Wrapper Side Channel

CodexTeamUp uses the observed `CODEX_CLI_PATH` Desktop hook. Desktop starts the wrapper instead of the real `codex.exe`; the wrapper starts the real CLI and proxies stdio.

## Data Path

```text
Codex Desktop stdin/stdout
  <-> CodexTeamUp.CodexWrapper
  <-> real codex.exe app-server stdio

ctu CLI / MCP
  <-> named pipe codexteamup-appserver
  <-> CodexTeamUp.CodexWrapper
  <-> real codex.exe app-server stdio
```

## Pipe Protocol

The pipe accepts one JSON line:

```json
{"method":"thread/list","params":{"limit":3,"useStateDbOnly":true}}
```

The wrapper assigns an internal request id with prefix `ctu:` and forwards the request to the real app-server. Responses with this id are intercepted and returned to the pipe client, not to Desktop.

## Special Method

```json
{"method":"ctu/status"}
```

Returns wrapper status without forwarding to Codex.

## Safety

- The wrapper does not patch Desktop.
- The wrapper should not write diagnostics to stdout.
- Logs are written as JSONL and redacted.
- Mutating sends are controlled by CLI/MCP confirmation policy.

## Known Caveat

`thread/turns/list` defaults to descending order in the observed app-server. The wrapper supports a mitigation via `CODEX_WRAPPER_FORCE_TURNS_ASC=1`, which rewrites Desktop `thread/turns/list` requests without explicit `sortDirection` to `asc`. `start-codexteamup.ps1` enables this by default; use `-NoForceTurnsAscending` to opt out if a future Desktop build changes pagination assumptions.

Live CTU wakeups also expose a Desktop-only ordering edge case: `turn/started` notifications can initially carry `startedAt=null`, while persisted history later has the correct timestamp. With `CODEX_WRAPPER_STAMP_TURN_STARTED_AT=1`, the wrapper stamps such live notifications with the current Unix time before forwarding them to Desktop. This does not change the persisted session JSONL; it only gives the live UI a stable chronological sort key. `start-codexteamup.ps1` enables this by default; use `-NoStampTurnStartedAt` to opt out.

The CTU wakeup path calls `thread/resume` before `turn/start` so persisted target threads are loaded into the live app-server registry. For these internal wakeups, CTU sends `excludeTurns=true` on `thread/resume`. That keeps Desktop from receiving a replay of historical turns as a new live block before the actual CTU message.
