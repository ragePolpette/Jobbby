# Contributing

## Workflow

1. Fork the repository or create a branch from `main` (`feature/<topic>`, `fix/<topic>`, `chore/<topic>`).
2. Keep changes focused: one concern per pull request.
3. Add or update tests for behavior changes.
4. Open a pull request against `main` and fill in the template.

## Build and test

Requires the .NET 8 SDK.

```bash
dotnet build Jobbby.sln
dotnet test Jobbby.sln
dotnet format Jobbby.sln --verify-no-changes
```

Tests use mock LLM and job source implementations and do not need real credentials. `Directory.Build.props` enables nullable reference types, the `latest-recommended` analyzers and the code style rules of `.editorconfig`, and treats every warning as an error. CI (`.github/workflows/ci.yml`) runs restore, build, format check and tests on pushes to `main` and on pull requests.

## Security rules

- Never commit secrets, API keys, real CVs, or a real `DataDir` (`applications.json`, `cursors.json`, `run-reports.json`, `dry-run-*.json`, `secrets.json` are git-ignored).
- Use `dotnet user-secrets` or environment variables for credentials.
- Do not hard-code a profession, country, or personal search in code, defaults, or prompts. Such values belong in the user's `DataDir`.
- Report vulnerabilities as described in [SECURITY.md](SECURITY.md), not in public issues.
