# Contributing

CodexTeamUp is an early source-only proof of concept. Feedback is welcome, but the repository is intentionally maintained with a controlled review flow.

## Good Ways To Help

- Open a bug report if the clone/build/start path fails.
- Open a feature request if you have a concrete workflow that CTU should support.
- Open a documentation issue if the README or onboarding flow is unclear.
- Start a Discussion for design ideas, local setup questions, or experiments that are not yet actionable issues.

## Pull Requests

Pull requests are welcome, but public visibility does not imply commit access.

Before opening a PR:

1. Keep the change small and explain the user-facing workflow it improves.
2. Avoid unrelated refactors.
3. Prefix PR titles for fixes with `fix:` and new features with `feat:`.
4. Keep public docs in English.
5. Do not commit local runtime state, `.codexteamup`, `.ctu`, logs, generated binaries, local paths, or private project references.
6. Run:

```powershell
dotnet build
dotnet run --project tests\CodexTeamUp.Tests
```

If you cannot run the checks, say so in the PR.

## Project Boundaries

CodexTeamUp is currently Windows-first and Codex Desktop-focused. Cross-platform ideas are welcome, but PRs should be explicit about what they support and what remains experimental.
