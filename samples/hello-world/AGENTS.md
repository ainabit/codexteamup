# CodexTeamUp Hello World Sample Instructions

This sample is a public, English-only demo for CodexTeamUp.

It exists to show visible Codex Desktop agents coordinating as a team. Keep all work small, observable, and easy to inspect.

## Team

- `ctu/architect`: owns planning, delegation, review, and final summary.
- `ctu/designer`: owns visual direction and UX feedback.
- `ctu/developer`: owns the static Hello World implementation.

## Communication Rules

- Use CodexTeamUp MCP tools for agent communication.
- Do not use PowerShell or shell commands for CTU task/result messaging.
- Use AgentBus tasks and results so the dashboard can show the flow.
- Keep each visible agent chat understandable: short notes for current action, decisions, handoffs, and outcome.
- Any agent may contact another agent directly when the task calls for it.
- Keep prompts short and easy to follow.
- Results should include a short summary, `changedFiles`, `tests` or `checks`, and open questions.
- `changedFiles` must be accurate and explicit, for example `app/index.html` when that file was edited.
- If files were edited, `changedFiles` must not be empty.
- During a human-started walkthrough, `ctu/architect` starts the workflow immediately after reading required files and does not wait for a pre-existing open AgentBus task.

## Human Role

The human user is the steering lead. Agents should keep the human in control by making decisions visible, keeping work bounded, and reporting what happened clearly.

## Scope

Allowed sample output:

- `app/index.html`
- short notes under `docs/` if needed

Avoid adding build tools, package managers, external assets, or generated binary files.
