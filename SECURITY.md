# Security Policy

## Scope

This policy covers the code in this repository: the CLI (`src/Host`), the local web UI and API (`src/Web`), and the handling of the `DataDir`, settings, CV files and secrets.

## Threat model

Jobbby is a single-user tool meant to run locally.

- The web UI has no authentication. It accepts only the `localhost` and `127.0.0.1` hosts (protection against DNS rebinding), and every request that modifies data must carry the `X-Jobbby-Request: 1` header and, when present, the app's `Origin`.
- Secrets (Adzuna credentials, LLM endpoint and key) are read from `dotnet user-secrets` or environment variables. The UI only reports whether they are set.
- The CV and the ledger contain personal data and are stored as plain files in the `DataDir`. Protecting that directory is the responsibility of the user.
- Posting text from third parties is sent to the configured LLM. With `claude-cli`, the Claude Code CLI is launched without tools, MCP servers, or user/project settings.

## Reporting a vulnerability

Please do not open a public issue for a security problem. Use GitHub Security Advisories ("Report a vulnerability" in the Security tab of the repository). If that is not available, open an issue asking for a private contact channel, without including details.

Include the affected version or commit, steps to reproduce, and the expected impact.

## Not covered

- Exposing the web UI to untrusted networks or the internet, or running it behind a proxy that rewrites the `Host` header.
- Compromise of the machine, the `DataDir`, or the user account running Jobbby.
- Vulnerabilities in third parties: Adzuna, LLM providers, the Claude Code CLI, .NET and NuGet packages. Report those upstream.
- Content quality of LLM output, including incorrect scoring of a posting.
