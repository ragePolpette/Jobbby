# Configuration reference

Italian version of the project README: [README.it.md](README.it.md).

## DataDir

All state of an installation lives in one directory, set with `Jobbby__DataDir` (required).

| File | Content |
|---|---|
| `settings.json` | Settings (created with neutral values on first start) |
| `cv.pdf`, `cv.json` or `cv.extracted.json` | The CV; alternatively set `Jobbby__CvPath` |
| `applications.json`, `cursors.json` | Outcome ledger (with decision history) and search cursors |
| `skill-aliases.json` | Optional: skill equivalences for your profession |
| `runs/` | One file per run, dry or normal: search and evaluation settings used (not the presentation texts), area, searches, calls, outcome of each posting |

The CLI and the web UI run only inside the container, one process at a time per `DataDir`: a second process exits immediately with a clear message. On first start, `applications.json` and `run-reports.json` are copied from the executable's directory if present; `cursors.json` is not, because cursor keys now include country and area. `run-reports.json` is then imported once into `runs/` as a summary-only run (`legacy`) and renamed `.imported`.

## Settings (`settings.json`)

```json
{
  "searches": { "queries": [], "deriveFromCv": true, "maxDerivedQueries": 3 },
  "area": { "country": null, "where": "", "whereFromCv": true, "distanceKm": null, "acceptsRemote": false },
  "salary": { "minimumYearly": null, "minimumPlausible": 5000 },
  "evaluation": { "autoApproveThreshold": 0.7 },
  "llm": { "provider": "claude-cli", "model": "sonnet" },
  "dryRun": { "maxPostingsPerQuery": 3 },
  "remoteSweep": { "keywords": {} }
}
```

- `area.country` is required for a run: an Adzuna country code (`at`, `au`, `be`, `br`, `ca`, `ch`, `de`, `es`, `fr`, `gb`, `in`, `it`, `mx`, `nl`, `nz`, `pl`, `sg`, `us`, `za`).
- `area.where` and `distanceKm` restrict the Adzuna search; the location reported by Adzuna is authoritative, with no further text checks. With an empty `where` and `whereFromCv` enabled (default), the CV `location` is used; a filled `where` always wins; `whereFromCv: false` with an empty `where` searches the whole country.
- With `acceptsRemote` and a location, each query also runs a country-wide search: results pass only if the title or excerpt contains one of the `remoteSweep.keywords` (per language, for example `{ "it": ["da remoto"], "en": ["remote"] }`) and the LLM classifies them as fully remote. Without keywords the remote search stays off. Cost: one extra Adzuna call per query.
- `salary.minimumYearly` excludes only postings with a known, lower salary; postings without salary are kept. Below `minimumPlausible` a salary is not treated as yearly and is considered unknown. No salary filter is sent to Adzuna.
- With `deriveFromCv`, the LLM adds up to `maxDerivedQueries` roles derived from the CV. Adzuna ignores two-letter abbreviations ("AI engineer" becomes "engineer"): prefer the long form.
- Postings found by several queries or republished under a new link are evaluated once. A posting is identified by what Adzuna reports: company and title, or, when Adzuna has no company, title plus the Adzuna ad id (or the normalized link). A company guessed by the LLM is shown but never used to recognize a posting.
- Skills are compared with neutral rules (case, whitespace, trailing version, more specific variant). Profession-specific equivalences go in `skill-aliases.json`: `{ "aliases": { "rcp": "rianimazione cardiopolmonare" }, "implies": { "bls-d": ["bls"] } }`.
- An existing `searches.json` can seed the searches of the first `settings.json` via `Jobbby__SearchesConfig`.

## Secrets

From the `src/Host` directory:

```bash
dotnet user-secrets set "Adzuna:AppId" "..."
dotnet user-secrets set "Adzuna:AppKey" "..."
```

With `llm.provider` = `openai`, `Llm:Endpoint` (full chat/completions URL) and `Llm:ApiKey` are also required.

Secrets are read from the configuration of the process that runs (user-secrets and environment variables), falling back to a plain environment variable with the same name (`Adzuna:AppKey`).

### LLM options

| Key | Default | Meaning |
|---|---|---|
| `Llm:Language` | `en` | Language of the prompts: `en` or `it`. Use `it` when CV and postings are in Italian. |
| `Llm:MaxConcurrency` | `4` (`openai`), `2` (`claude-cli`) | LLM calls in flight at the same time. |

With `openai`, transient failures (408, 429, 5xx, timeouts) are retried with exponential backoff and jitter; a `Retry-After` from the endpoint is honoured up to 60 seconds, a longer one fails the call. Markdown code fences around a reply are removed for every provider, and replies that are not valid JSON, or carry an unknown match category, fail the posting with an LLM error instead of being guessed.

### Adzuna requests

Each Adzuna request has a 20-second timeout. 408, 429, 5xx, timeouts and network errors are retried up to 3 attempts with exponential backoff (honouring `Retry-After` up to 60 seconds). A query that still fails is reported in the run with a message that never contains the request URL, and its cursor is left unchanged. Adzuna takes `app_id`/`app_key` in the query string, so the outgoing HTTP client of the web UI does not log requests at all, the `System.Net.Http.HttpClient` log category is capped at `Warning`, and request errors are rethrown with credentials masked.

### LLM through Claude Code (`claude -p`)

With `llm.provider` = `claude-cli`, the already authenticated Claude Code CLI is used, with no endpoint or API key. The CLI is launched without tools, MCP servers, or user/project settings, with a 120-second timeout and at most 2 parallel processes. Options: `Llm:ClaudePath` (default `claude`), `Llm:TimeoutSeconds`, `Llm:MaxConcurrency`, `Llm:Language`. Each call starts a process, so it is slower than a direct API.

## Web UI

```bash
Jobbby__DataDir=/path/to/data dotnet run --project src/Web
```

The server listens on `http://0.0.0.0:5080` by default. From the UI you edit settings, start dry runs and runs (one at a time, with progress and an Interrupt button), decide pending postings, and browse run history.

- No login: the UI accepts only the `localhost` and `127.0.0.1` hosts (DNS rebinding protection), and every request that modifies data must carry the `X-Jobbby-Request: 1` header and, when present, the app's `Origin`.
- The UI and the CLI do not run together on the same `DataDir`: the second process exits with a message.
- Secrets stay in user-secrets (the same store as the CLI): the UI only shows whether they are set.

## Outcomes

| Outcome | When |
|---|---|
| `AutoRejected` | Stage 1 filter failed, or `Weak` judgment |
| `Pending` | `Strong`/`Borderline` below `evaluation.autoApproveThreshold`, or no requirements could be extracted from the excerpt |
| `Shortlisted` | `Strong`/`Borderline` at or above the threshold |
| `Approved` / `Rejected` / `Applied` | User decision |

A posting already in the ledger is not re-evaluated (not even in a dry run) and costs no LLM calls. During a run the ledger is written every 20 outcomes and at the end (also when the run is interrupted or fails); decisions are written at once. Every write replaces the file atomically.
