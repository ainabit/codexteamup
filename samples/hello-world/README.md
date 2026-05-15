# Hello World Team Sample

This sample is a small CodexTeamUp walkthrough.

It demonstrates the core idea of CodexTeamUp: visible AI agents inside the Codex Desktop app coordinate like a small team. The sample uses three agents:

- `ctu/architect`: plans the tiny feature, creates or binds the team, delegates work, and reviews the outcome.
- `ctu/designer`: gives a compact visual direction and may answer design questions from other agents.
- `ctu/developer`: implements the small Hello World artifact and may ask `ctu/designer` directly if useful.

The intended artifact is a single static page at `app/index.html`.

## What This Sample Should Show

- A human steering the work from the top.
- A visible architect agent creating or binding a small team.
- A designer agent providing direction in its own visible chat.
- A developer agent doing bounded implementation work in its own visible chat.
- Agent-to-agent communication that does not have to pass through the architect.
- AgentBus tasks, results, and communication flow appearing in the dashboard.

## Suggested Runtime Settings

The included [agents.json](agents.json) demonstrates differentiated runtime settings:

- `ctu/architect`: model omitted to use the local Codex default, `reasoningEffort` `medium`, `speed` `standard`
- `ctu/designer`: `model` `gpt-5.5`, `reasoningEffort` `high`, `speed` `standard`
- `ctu/developer`: `model` `gpt-5.3-codex-spark`, `reasoningEffort` `medium`, `speed` `standard`

If your local installation expects different model identifiers, use the closest supported names and keep the same role intent.

## Start Prompt for `ctu/architect`

Open a visible Codex Desktop chat for `ctu/architect` in this sample folder and give it this prompt:

```text
You are ctu/architect for the CodexTeamUp Hello World sample.

Read AGENTS.md, docs/project-brief.md, docs/agents/architect.md, docs/agents/developer.md, docs/agents/designer.md, and agents.json.

Use CodexTeamUp MCP tools only for team communication. Do not use shell commands for CTU messaging.
You own this walkthrough. Start immediately after reading the required files.
Do not wait for a pre-existing open AgentBus task for this initial human-started run.

Goal:
1. Ensure the visible team ctu/architect, ctu/developer, and ctu/designer exists for this sample.
2. Ask ctu/designer for a concrete mini design spec covering layout, spacing, color palette, type hierarchy, agent chips, accessibility/contrast notes, and one optional review pass.
3. Ask ctu/developer to implement app/index.html using that direction.
4. Allow ctu/developer to contact ctu/designer directly if it needs a design clarification.
5. Require ctu/developer to write an AgentBus result with summary, accurate `changedFiles` (must include `app/index.html` when edited), `tests` or `checks` describing verification performed, whether ctu/designer was contacted, and open questions.
6. Review the final result and report what happened in a short visible summary for the human steering lead.

Keep the demo small and focused. Use AgentBus tasks and results so the dashboard shows the communication flow.
```

## Suggested Walkthrough Sequence

1. `ctu/architect` after reading the sample and planning the team.
2. CodexTeamUp dashboard showing the new agents and first task.
3. `ctu/designer` receiving and answering the design direction request.
4. `ctu/developer` receiving the implementation task.
5. Optional: `ctu/developer` asking `ctu/designer` a direct clarification.
6. Dashboard showing the full communication flow.
7. Final `app/index.html` rendered in a browser.

## Screenshot Walkthrough Script

Use this checklist while capturing screenshots for GitHub:

1. Show `ctu/architect` right after file-read and planning, starting without waiting for a pre-existing task.
2. Show dashboard evidence of a design task sent to `ctu/designer`.
3. Show `ctu/designer` returning a concrete mini design spec.
4. Show dashboard evidence of the implementation task sent to `ctu/developer`.
5. Show `ctu/developer` result including accurate `changedFiles` with `app/index.html`.
6. Show final `ctu/architect` summary to the human steering lead.

## Expected Result

The final page should be intentionally small:

- a clear "Hello World" headline,
- a short line explaining that the page was made by a visible CTU agent team,
- a compact visual treatment,
- no build system, no dependencies, and no large framework.
