# CodexTeamUp Rename and Team Bootstrap Plan

Historical internal note: this document summarizes the rename and bootstrap direction that moved the project toward the `CodexTeamUp` and `ctu/*` naming model. It is kept for repository history and GitHub readers who want to understand the naming decisions.

## Goal

CodexTeamUp is the local execution and communication layer for visible Codex Desktop teams.

CodexTeamUp does not decide which roles a project needs. That decision belongs to the initial architect thread or another AI reading the project material. CodexTeamUp only creates the requested agents, binds visible threads, registers them, and wakes them with the provided role and file guidance.

## Naming Rules

- Product name: `CodexTeamUp`
- Short CLI, MCP, and script context: `ctu`
- Agent ids: `ctu/<role>`, for example `ctu/architect`, `ctu/web`, `ctu/designer`
- Project state directory: `.codexteamup/`
- Durable exchange layer: `.codexteamup/agentbus/`

Role or agent markdown files do not need to live inside `.codexteamup`. They can live in any project-specific location, for example `docs/agents/web.md`, `web/AGENTS.md`, or `design/briefing.md`.

## Target Structure in Downstream Projects

```text
project/
  AGENTS.md
  docs/
    agents/
      architect.md
      web.md
      designer.md
  web/
    AGENTS.md
  .codexteamup/
    project.json
    agentbus/
      agents.json
      tasks/
      results/
      events.jsonl
```

## Intended Behavior

1. The user starts Codex Desktop once through CodexTeamUp.
2. The user starts or enters an initial architect thread.
3. The architect reads the project documentation and decides which `ctu/*` agents are needed.
4. The architect calls CodexTeamUp MCP and passes that exact agent list, including roles, allowed paths, and instruction files.
5. CodexTeamUp finds existing visible threads or creates the missing ones.
6. CodexTeamUp registers the agents in `.codexteamup/agentbus/agents.json`.
7. CodexTeamUp sends initial guidance to new or newly bound threads.
8. Ongoing team communication uses MCP plus `.codexteamup/agentbus`.

## Implementation Steps

1. Rename the product across code, namespaces, project files, solution files, scripts, and docs.
2. Keep `.codexteamup/agentbus` as the default AgentBus path.
3. Preserve compatibility for older bus layouts where needed, but create new projects with the current `.codexteamup/agentbus` structure.
4. Move pipe, log, and tool paths to `codexteamup` and `ctu`.
5. Move MCP and service environment variables to `CTU_*`.
6. Keep matching for `ctu/<role>` robust.
7. Provide bootstrap prompt files that explain that the architect defines roles and CodexTeamUp only executes the plan.
8. Update tests to the new paths and `ctu/*` ids.
