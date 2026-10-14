# Jobbby

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)

Jobbby collects job postings, normalizes them with an LLM, scores their fit against your CV, and keeps a ledger of outcomes. It works for any profession and for the countries supported by Adzuna.

An Italian version of the original README is kept in [docs/README.it.md](docs/README.it.md).

## What it does

1. Builds searches from `settings.json` and, optionally, from roles derived from your CV by the LLM.
2. Fetches postings from Adzuna.
3. Normalizes each posting with the LLM and applies a first-stage filter (including a minimum yearly salary when the salary is known).
4. Judges the fit against the CV and assigns an outcome.
5. Records outcomes in a ledger and writes one report per run.

Postings already in the ledger are not re-evaluated and cost no LLM calls.

## Capabilities

- CLI and local web UI over the same `DataDir`.
- Dry run: uses the real Adzuna and LLM but writes neither ledger nor cursors, and evaluates at most `dryRun.maxPostingsPerQuery` postings per query.
- CV as PDF (extracted by the LLM) or JSON; YAML is accepted by the loader.
- LLM providers: `claude-cli` (Claude Code CLI) or `openai` (an OpenAI-compatible chat/completions endpoint).
- Configurable search area: country, location, radius in km, remote sweep.
- Profession-neutral skill matching with optional `skill-aliases.json`.
- Interruptible runs: `Ctrl+C` stops in-flight LLM calls and the run is marked `Interrupted`.

## Architecture

The solution (`GraphEngine.sln`) is split into projects under `src/`:

| Project | Role |
|---|---|
| `GraphEngine` | Graph execution engine and LLM clients |
| `JobPostings` | Job source abstraction and Adzuna client |
| `CvExtraction` | CV loading and LLM-based extraction |
| `Matching` | Two-stage matching (filter, then LLM judgment) |
| `Host` | CLI, run orchestration, ledger and run storage |
| `Web` | ASP.NET web UI and API |
| `Config`, `ApplicationLedger`, `Reporting` | Settings, outcome ledger, run reports |
| `Discovery`, `Notifications` | Source discovery and Telegram gateway; present in the code but not used by runs |

Tests are under `tests/`, one project per source project.

## Quick start

Requires the .NET 8 SDK.

```bash
git clone https://github.com/ragePolpette/Jobbby.git
cd Jobbby

cd src/Host
dotnet user-secrets set "Adzuna:AppId" "..."
dotnet user-secrets set "Adzuna:AppKey" "..."
cd ../..

export Jobbby__DataDir=/path/to/data
Jobbby__DryRun=true dotnet run --project src/Host/Host.csproj
```

The first start creates `settings.json` in the `DataDir` with neutral values. Set `area.country` and put your CV in the `DataDir` before a real run.

## Configuration

State lives in a single directory set with `Jobbby__DataDir` (required). Settings are in `settings.json`; secrets are in `dotnet user-secrets` or environment variables. The full reference (settings, secrets, LLM providers, outcomes) is in [docs/configuration.md](docs/configuration.md).

## Usage

Normal run (records outcomes and cursors):

```bash
Jobbby__DataDir=/path/to/data dotnet run --project src/Host/Host.csproj
```

Dry run:

```bash
Jobbby__DataDir=/path/to/data Jobbby__DryRun=true dotnet run --project src/Host/Host.csproj
```

Each run is written to `runs/<runId>.json`. Decide on pending postings from the command line:

```bash
dotnet run --project src/Host/Host.csproj -- pending
dotnet run --project src/Host/Host.csproj -- decide <postingId> approve|reject|applied
```

Web UI:

```bash
Jobbby__DataDir=/path/to/data dotnet run --project src/Web
```

The UI listens on `http://0.0.0.0:5080` by default. See [docs/configuration.md](docs/configuration.md#web-ui).

## Security

- The web UI has no login. It accepts only `localhost` and `127.0.0.1` hosts, and state-changing requests require the `X-Jobbby-Request: 1` header. Do not expose it to untrusted networks.
- Secrets belong in `dotnet user-secrets` or environment variables, never in the repository or the `DataDir`.
- With `claude-cli`, the CLI is launched without tools, MCP servers, or user/project settings.

See [SECURITY.md](SECURITY.md) for reporting vulnerabilities.

## Development

```bash
dotnet build GraphEngine.sln
dotnet test GraphEngine.sln
```

See [CONTRIBUTING.md](CONTRIBUTING.md). No linter or formatter is configured in the repository.

## Project status

Under active development. Telegram approvals and Discovery remain in the code but are not wired into runs. Design notes are in `docs/superpowers/` (in Italian).

## License

MIT. See [LICENSE](LICENSE).
