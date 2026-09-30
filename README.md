# Jobbby

Jobbby raccoglie annunci, li normalizza con un LLM, valuta la compatibilità con il CV e conserva gli esiti operativi. Funziona per qualsiasi professione e per i paesi supportati da Adzuna.

## DataDir

Tutto lo stato di un'installazione sta in una cartella, indicata con `Jobbby__DataDir` (obbligatoria):

| File | Contenuto |
|---|---|
| `settings.json` | impostazioni (creato con valori neutri al primo avvio) |
| `cv.pdf`, `cv.json` o `cv.extracted.json` | il CV; in alternativa `Jobbby__CvPath` |
| `applications.json`, `cursors.json`, `run-reports.json` | registro degli esiti, cursori delle ricerche, riepiloghi |
| `skill-aliases.json` | facoltativo: equivalenze tra competenze della propria professione |
| `runs/` | log delle dry run |

CLI e (in seguito) UI web girano solo nel container, un processo alla volta per `DataDir`: un secondo processo esce subito con un messaggio chiaro. Al primo avvio `applications.json` e `run-reports.json` vengono copiati dalla directory dell'eseguibile, se presenti; `cursors.json` no, perché le chiavi ora includono paese e zona.

## Impostazioni (`settings.json`)

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

- `area.country` è obbligatorio per una run: codice Adzuna (`at`, `au`, `be`, `br`, `ca`, `ch`, `de`, `es`, `fr`, `gb`, `in`, `it`, `mx`, `nl`, `nz`, `pl`, `sg`, `us`, `za`).
- `area.where` e `distanceKm` restringono la ricerca ad Adzuna; la località indicata da Adzuna fa fede, senza ulteriori controlli testuali. Con `where` vuoto e `whereFromCv` attivo (default) si usa la località del CV (`location`); `where` compilato vince sempre; `whereFromCv: false` con `where` vuoto cerca in tutto il paese.
- Con `acceptsRemote` e una località, ogni query fa anche una ricerca in tutto il paese: i risultati passano solo se titolo o estratto contengono una delle parole di `remoteSweep.keywords` (per lingua, es. `{ "it": ["da remoto"], "en": ["remote"] }`) e se l'LLM li classifica come interamente da remoto. Senza parole chiave la ricerca remota resta spenta. Costo: una chiamata Adzuna in più per query.
- `salary.minimumYearly` esclude solo gli annunci con retribuzione nota e inferiore; gli annunci senza retribuzione restano. Sotto `minimumPlausible` una retribuzione non è considerata annua ed è trattata come sconosciuta. Adzuna non riceve filtri di retribuzione.
- Con `deriveFromCv` l'LLM aggiunge fino a `maxDerivedQueries` ruoli ricavati dal CV. Adzuna ignora le sigle di due lettere ("AI engineer" diventa "engineer"): meglio la forma estesa.
- Gli annunci trovati da più query o ripubblicati con un nuovo link vengono valutati una volta sola.
- Le competenze si confrontano con regole neutre (maiuscole, spazi, versione finale, variante più specifica). Le equivalenze della propria professione vanno in `skill-aliases.json`: `{ "aliases": { "rcp": "rianimazione cardiopolmonare" }, "implies": { "bls-d": ["bls"] } }`.
- Un `searches.json` esistente può inizializzare le ricerche del primo `settings.json` con `Jobbby__SearchesConfig`.

## Segreti

Dalla directory `src/Host`:

```bash
dotnet user-secrets set "Adzuna:AppId" "..."
dotnet user-secrets set "Adzuna:AppKey" "..."
```

Con `llm.provider` = `openai` servono anche `Llm:Endpoint` (URL completo di chat/completions) e `Llm:ApiKey`.

### LLM tramite Claude Code (`claude -p`)

Con `llm.provider` = `claude-cli` si usa il CLI di Claude Code già autenticato, senza endpoint né API key. Il CLI viene lanciato senza strumenti, server MCP né impostazioni utente/progetto, con un timeout di 120 secondi e al massimo 2 processi in parallelo. Opzioni di configurazione: `Llm:ClaudePath` (default `claude`), `Llm:TimeoutSeconds`, `Llm:MaxConcurrency`. Ogni chiamata avvia un processo, quindi è più lento dell'API diretta.

## Esecuzione

```bash
Jobbby__DataDir=/percorso/dati Jobbby__DryRun=true dotnet run --project src/Host/Host.csproj
```

La dry run usa Adzuna e l'LLM reali ma non scrive registro, cursori né riepiloghi e non invia messaggi; valuta al massimo `dryRun.maxPostingsPerQuery` annunci per query. Il log dettagliato va in `runs/dry-run-YYYYMMDD-HHMMSS.json` (oppure `Jobbby__DryRunLogPath`). `Ctrl+C` interrompe la run fermando anche le chiamate LLM in corso; cursori e riepilogo non vengono salvati.

Senza `Jobbby__DryRun` la run è normale: registra esiti e cursori e, finché le approvazioni non passano alla UI, chiede su Telegram gli annunci sotto la soglia (`Telegram:BotToken`, `Telegram:ChatId`). Discovery non fa parte delle run: se `Jobbby__DiscoveryConfig` è impostato viene ignorato con un avviso.
