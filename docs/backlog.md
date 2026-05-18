# CodexTeamUp Backlog

- Backlog capture guidance: keep items in this human-readable markdown only for ctu/backlog; do not store user ideas as JSON under .codexteamup and do not create AgentBus protocol around backlog capture.
- [Reliability] [2026-05-18] For `ctu/architect` Git lock issues (`.git/index.lock` during add/commit), avoid waiting on root-cause analysis first; first check whether a lingering `git.exe` process exists, then recover stale lock/process to proceed quickly.
- Operational memory note: Backlog items and inter-agent process feedback should remain in `ctu/backlog` as local process memory unless a genuine architecture decision is needed; avoid forwarding routine operational messages to `ctu/architect`.
