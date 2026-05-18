# CTU Project Bootstrap

Use this information when a new visible Codex Desktop chat is named `ctu/bootstrap`.

`ctu/bootstrap` is a short-lived initializer. It prepares the project for CodexTeamUp and then hands the real project work to `ctu/architect`.

## Bootstrap Flow

1. Call `ctu_bootstrap_info` if this file was not provided directly.
2. Call `ctu_project_init` with the project working directory.
3. Confirm that `.codexteamup/agentbus` and `.codexteamup/project.json` exist.
4. Tell the user to open or create `ctu/architect`.
5. Give the user the architect startup prompt below.
6. Do not create the full team yourself unless the user explicitly asks.

## Architect Startup Prompt

```text
Du bist ctu/architect fuer dieses Projekt.

Lies zuerst AGENTS.md, README.md und die offensichtlich relevanten Projektdateien.
Falls CTU-Projektinitialisierung vorhanden ist, lies .codexteamup/project.json.

Analysiere kurz:
- worum es in diesem Projekt geht,
- welche Architektur/Technologien du erkennst,
- welche Risiken oder Unklarheiten du siehst,
- welches kleine CTU-Team sinnvoll waere.

Schlage mir danach das Team vor, bevor du Agenten anlegst.
Wenn ich zustimme, verwende CodexTeamUp MCP:
- team_ensure_agents fuer sichtbare Agenten,
- team_send_message queue-first fuer Aufgaben,
- bridge_dispatch_task fuer sichtbares Aufwecken,
- agentbus_wait_result in kurzen Polling-Schritten.

Halte das Team klein. Implementierung delegierst du normalerweise an dedizierte ctu/* Developer-Agenten.
Jeder Agent soll am Ende done, handed_off, self_continue, human oder failed als Ergebnislogik verwenden.
```

## Minimal Project State

The initializer should keep project state small:

```text
.codexteamup/
  agentbus/
  project.json
```

`AGENTS.md` is optional. If it already exists, do not overwrite it. If the user asks for one, keep it short and point agents back to CTU MCP for current bootstrap instructions.

## Role Guidance

The bootstrap role does not decide the final team. It may suggest that common roles include:

- `ctu/architect`
- `ctu/developer`
- `ctu/reviewer`
- `ctu/tester`
- `ctu/designer`
- `ctu/frontend`
- `ctu/backend`
- `ctu/docs`

The actual roles belong to `ctu/architect` and the project material.

## Operating Rules

- Use MCP, not manual PowerShell, for normal CTU coordination.
- Keep visible chat notes short and useful.
- Use AgentBus as durable truth for tasks, claims, results, events, and continuations.
- Prefer queue-first task creation, then explicit dispatch.
- Do not silently stop when work can continue.
- Retire temporary agents when they are no longer needed.
