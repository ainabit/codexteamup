# CodexTeamUp CLI API

## Diagnostics

```powershell
ctu doctor
ctu codex info
ctu codex schema --out .ctu/schemas
ctu wrapper status
ctu wrapper rpc --method thread/list --params-json '{"limit":3,"useStateDbOnly":true}'
```

## Threads

```powershell
ctu threads list --source wrapper --limit 20
ctu threads read --thread-id <id> --include-turns
ctu threads resume --thread-id <id>
ctu threads start --cwd <path> --name ctu/web --role web --yes
ctu threads send --thread-id <id> --message "hello" --yes
ctu turns list --thread-id <id>
ctu turns wait --thread-id <id> --turn-id <turn-id> --timeout-seconds 300
```

Mutating thread commands require confirmation unless `--yes` is set.

## AgentBus

```powershell
ctu bus init
ctu bus agent list
ctu bus agent register --id ctu/web --role web --thread-id <id> --cwd <path> --return-to ctu/architect --allowed-paths "web/,shared/,docs/" --speed standard --model gpt-5.4 --reasoning-effort medium
ctu bus task create --from ctu/architect --to ctu/web --title "S001" --prompt-file docs/coordination/ctu/web-S001.md
ctu bus task list --to ctu/web --status open
ctu bus task claim --task-id <id> --owner ctu/web
ctu bus result write --task-id <id> --summary "Done" --tests "dotnet build"
ctu bus wait --task-id <id> --timeout-seconds 1800
ctu bus event list
```

## Delegation

```powershell
ctu dispatch --task-id <id> --to-agent ctu/web --yes
ctu dispatch --task-id <id> --to-agent ctu/web --wait-result --notify-return --yes
ctu notify --result-id <id> --to-agent ctu/architect --yes
ctu delegate --to ctu/web --title "S001" --prompt-file docs/coordination/ctu/web-S001.md --wait-result --yes
```

`dispatch` and `notify` may also use `--to-thread <id>` for manual tests.

## Exit Codes

- `0`: success.
- `1`: usage or unexpected local error.
- `2`: app-server, wrapper or wait failure.
- `3`: requested thread not found in read-only state.
- `4`: user declined confirmation.
