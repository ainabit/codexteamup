# Hello World Project Brief

Build a tiny static Hello World page that demonstrates a visible CodexTeamUp agent workflow.

The page should communicate:

- CodexTeamUp coordinates visible AI agents inside Codex Desktop.
- The human remains the steering lead.
- The sample was created by a small agent team.

## Constraints

- Keep the result to `app/index.html`.
- Use plain HTML and CSS only.
- Do not introduce npm, build tools, images, or external dependencies.
- Keep text short and readable.
- Prefer a polished but restrained visual style.

## Acceptance Criteria

- `app/index.html` exists.
- The page can be opened directly in a browser.
- The design has a clear title, short supporting copy, and visible mention of the participating agents.
- The final AgentBus result explains who contributed and what changed.

## Suggested Team Flow

1. `ctu/architect` starts immediately after reading required files and does not wait for a pre-existing open AgentBus task in the initial human-started walkthrough.
2. `ctu/architect` asks `ctu/designer` for a concrete mini design spec (layout, spacing, palette, type hierarchy, chips, accessibility notes, optional review pass).
3. `ctu/architect` gives `ctu/developer` the implementation task and includes the approved design spec.
4. `ctu/developer` may ask `ctu/designer` one direct clarification if useful.
5. `ctu/developer` writes the result to AgentBus with accurate `changedFiles`, verification in `tests` or `checks`, and notifies `ctu/architect`.
6. `ctu/architect` reviews and summarizes the flow for the human.
