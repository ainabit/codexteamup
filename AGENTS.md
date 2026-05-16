# CodexTeamUp Target State

CodexTeamUp is a local team-bootstrap and inter-thread communication system for visible Codex Desktop chats.

The goal is not a PowerShell-centric daily workflow. PowerShell is only for bootstrap and recovery. MCP is the agent-facing interface so Codex threads can use tools instead of typing shell commands for normal coordination.

## Core Principle

CodexTeamUp does not decide which roles a project needs. That decision belongs to the initial architect thread or another AI working from the project material.

CodexTeamUp executes the coordination layer:

- discover visible Codex Desktop threads,
- create missing threads for requested `ctu/*` agents,
- register project agents,
- prime threads with role and file guidance,
- coordinate tasks, results, events, and wakeups.

## Agent Names

Agents use IDs in this format:

```text
ctu/<role>
```

Examples:

- `ctu/architect`
- `ctu/web`
- `ctu/backend`
- `ctu/designer`
- `ctu/reviewer`
- `ctu/schema`

## Intended Usage

A project can have visible Codex Desktop threads like:

- `ctu/architect`
- `ctu/web`
- `ctu/backend`
- `ctu/designer`

The user primarily talks to `ctu/architect`. The architect plans a feature with the user. When work belongs to another role, the architect creates a work package for that role and wakes the target thread through CodexTeamUp.

Worker threads operate in their own visible chat windows. They can later send results, questions, or status updates back to `ctu/architect`. The user can also switch into a worker thread and continue there directly at any time.

Worker chats should stay human-readable while they work. AgentBus is the durable task/result protocol, but visible threads should still show concise progress notes: what the agent is doing, meaningful decisions, blockers, handoffs to other agents, and final outcome. The user should not have to open raw AgentBus files to understand the current shape of the work.

## Team Bootstrap

The user starts an architect thread and can provide a prompt such as `docs/ai-team-bootstrap-prompt.md`.

The architect reads the project documentation and decides:

- which `ctu/*` agents are needed,
- what role each agent has,
- which directories each agent should mainly edit,
- which role or instruction markdown files each agent should read.

The architect then uses CodexTeamUp MCP, for example `team_ensure_agents`, and passes that exact agent list. CodexTeamUp does not invent roles on its own.

## Project-Local State

Each project can keep its own CodexTeamUp directory:

```text
<repo-root>\.codexteamup
```

The durable exchange layer lives under:

```text
<repo-root>\.codexteamup\agentbus
```

Role and agent markdown files do not need to live inside `.codexteamup`. They can live in domain-appropriate project locations, for example:

- `docs/agents/web.md`
- `web/AGENTS.md`
- `docs/design/agent.md`
- `backend/AGENTS.md`

Project structure should follow the application and the project goals, not the CodexTeamUp implementation.

## Technical Core

- Agent interface: MCP
- Backend: `CodexTeamUp.Service` as a local HTTP service on `127.0.0.1`
- Desktop wakeup path: the service uses `thread/resume` plus `turn/start` against the visible Desktop app-server through the wrapper
- Durable truth: `.codexteamup/agentbus` stores tasks, results, events, messages, and audit history
- Desktop adapter: the named pipe is only an internal detail between the service and the wrapper
- PowerShell: bootstrap, publish, and recovery only
- UI: local HTTP dashboard for communication, content, runtimes, and status

## Operating Model

The normal way to start Codex Desktop with CodexTeamUp enabled is:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\start-codexteamup.ps1
```

If Codex Desktop is started normally, inter-thread communication is not active. If Desktop is started through the script, CodexTeamUp is active for that Desktop session. Each project still chooses its own workspace through `cwd`.

## Codex Desktop Git Directives

Codex Desktop currently has a renderer issue when a Git app directive contains a Windows path with backslashes in its `cwd` value. Such responses can trigger the Desktop Oops screen when old thread history is loaded.

Always use forward-slash paths in Git app directives, for example `X:/repo/codexteamup`. Do not quote or reproduce the broken backslash form as a raw app directive in final answers, documentation, or agent instructions.

The CTU wrapper can sanitize newly generated answers, but historical session history remains a separate problem and may still need cleanup or avoidance.

## Pull Requests

Pull request titles use a short conventional prefix:

- Fixes start with `fix: ` followed by a short title.
- Features start with `feat: ` followed by a short title.

## Non-Goals

- No manual copy and paste between chats
- No manual thread-id maintenance by the user
- No CTU-owned role selection
- No app patching as the standard solution
- No blind production sends without a traceable AgentBus entry
- No PowerShell-centric daily workflow

## Acceptance

CodexTeamUp is only meaningfully usable when this flow works:

1. The user starts Codex Desktop through the startup script.
2. The user talks to `ctu/architect`.
3. `ctu/architect` reads the project material and decides the team.
4. `ctu/architect` calls CodexTeamUp to create or bind the requested `ctu/*` agents.
5. Worker threads are visibly woken and primed with role and file guidance.
6. Workers operate in their own chat context.
7. Workers send a result or question back to `ctu/architect`.
8. `ctu/architect` evaluates the result, replans, or commits.
9. The user can see in a UI which communication happened, from whom to whom, with content, status, and runtimes.
