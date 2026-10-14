# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

### Security

- Adzuna credentials (`app_id`, `app_key`) no longer reach the logs: the web UI's outgoing HTTP client has its request loggers removed, the `System.Net.Http.HttpClient` log category is capped at `Warning` (in code and in `src/Web/appsettings.json`), and a `SecretRedactingHandler` masks query-string credentials in request errors. Adzuna failures are reported as `JobSourceException` with a message that never contains the URL.
- LLM output is validated: the match category must be `Strong`, `Borderline` or `Weak` (any case), confidence is clamped to 0-1, reasoning, company, seniority and skills are length-capped, and malformed JSON is reported as `LlmException` instead of `JsonException`.
- The posting identity (dedupe key and `PostingId`) is built only from what the source reports: company and title, or, without a company, title plus the Adzuna ad id or the normalized link. A company guessed by the LLM is still shown but no longer used to recognize a posting. Ledger records now store `SourceCompany` and `SourcePostingId`.
- Run files (`runs/*.json`) record only an allowlist of settings (searches, area, salary, evaluation, LLM provider and model, dry run, remote sweep, dedupe); the presentation texts are no longer copied.

### Added

- `Llm:MaxConcurrency` now applies to the `openai` provider too (default 4; `claude-cli` keeps 2).
- `Llm:Language` (`en` or `it`, default `en`) selects the language of the prompts.
- The OpenAI-compatible client retries transient failures with exponential backoff and jitter and honours `Retry-After` up to 60 seconds.
- Adzuna requests have a 20-second timeout and are retried up to 3 attempts on 408, 429, 5xx, timeouts and network errors, with backoff and `Retry-After`. A failed query leaves its cursor unchanged.
- `Directory.Build.props` (nullable, warnings as errors, `latest-recommended` analyzers, code style enforced in the build), `.editorconfig`, `global.json` (SDK 8.0) and a CI workflow running restore, build, format check and tests.

### Changed

- **Prompts are in English by default.** Installations with Italian CVs and postings should set `Llm:Language=it` (for example `Llm__Language=it`) to keep the previous prompts. The stage-two reasoning follows the prompt language.
- CLI messages, the web request guard messages and the Claude CLI system prompt are in English.
- The ledger is written in batches during a run (every 20 outcomes and when the run ends, is interrupted or fails) instead of after every outcome, always with an atomic replace; user decisions are still written at once. Lookups use an in-memory index.
- A markdown code fence around any LLM reply is removed by a `CodeFenceStrippingLlmClient` decorator for every provider, not only for `claude-cli`.
- Postings without a company in Adzuna are now recognized across runs before normalization, so they cost no LLM call when already decided. Ledger entries written before this change whose company had been guessed by the LLM are recognized by that company only, so such a posting may be evaluated once more.
- Secrets are resolved from the configuration passed in by the host (`SourceWhitelist.ResolveSecret(configuration, key)`, `AdzunaJobSource.FromConfiguration`) instead of a static `SourceWhitelist.Configuration`.
- The solution is now `Jobbby.sln` (was `GraphEngine.sln`). `Config` and `ApplicationLedger` moved to a new `Jobbby.Core` library; `CvExtraction` is a library without its own entry point. Test package versions are centralized in `tests/Directory.Build.props` and updated (xunit 2.9.3, Microsoft.NET.Test.Sdk 17.12.0).
- Internal design notes moved from `docs/superpowers/` to `docs/archive/`.

### Removed

- The Telegram approval flow (`Notifications`, `AskApprovalNode`, `RecordIfApprovedNode`) and the Discovery project with its `ApprovedSourceStore`, which runs no longer used. `Jobbby:DiscoveryConfig` and `Jobbby:DryRunMaxDiscoveryCandidates` are no longer read.
- The `src/CvExtraction` console entry point, which only ran a mock LLM.
- `src/Example` is no longer part of the solution.
