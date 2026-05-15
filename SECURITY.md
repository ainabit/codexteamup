# Security Policy

CodexTeamUp is an early proof of concept and should not be treated as production infrastructure.

## Supported Versions

Only the current `main` branch is considered for security fixes.

## Reporting A Vulnerability

Please do not publish exploit details in a public issue.

Use GitHub private vulnerability reporting if it is enabled for this repository. If that is not available, open a minimal public issue that says you have a security concern and avoid sharing secrets, exploit steps, or private environment details in the issue body.

## Sensitive Data

Do not commit:

- Codex auth/config secrets,
- API keys or tokens,
- `.codexteamup` runtime state,
- `.ctu` published tools or logs,
- local machine paths that identify a private setup,
- private project names or screenshots containing private project data.

If a secret is accidentally committed, rotate or revoke it first. History cleanup is useful, but it does not replace secret rotation.
