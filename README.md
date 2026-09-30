# Jobbby

Jobbby raccoglie annunci, li normalizza con un LLM, valuta la compatibilità con il CV e conserva gli esiti operativi. Funziona per qualsiasi professione e per i paesi supportati da Adzuna.

## DataDir

Tutto lo stato di un'installazione sta in una cartella, indicata con `Jobbby__DataDir` (obbligatoria):

| File | Contenuto |
|---|---|
| `settings.json` | impostazioni (creato con valori neutri al primo avvio) |
| `cv.pdf`, `cv.json` o `cv.extracted.json` | il CV; in alternativa `Jobbby__CvPath` |
| `applications.json`, `cursors.json` | registro degli esiti (con lo storico delle decisioni), cursori delle ricerche |
| `skill-aliases.json` | facoltativo: equivalenze tra competenze della propria professione |
| `runs/` | una file per run, dry o normale: impostazioni usate, zona, ricerche, chiamate, esito di ogni annuncio |

CLI e (in seguito) UI web girano solo nel container, un processo alla volta per `DataDir`: un secondo processo esce subito con un messaggio chiaro. Al primo avvio `applications.json` e `run-reports.json` vengono copiati dalla directory dell'eseguibile, se presenti; `cursors.json` no, perché le chiavi ora includono paese e zona. `run-reports.json` viene poi importato una volta in `runs/` come run di sola sintesi (`legacy`) e rinominato `.imported`.

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

## UI web

```bash
Jobbby__DataDir=/percorso/dati dotnet run --project src/Web
```

Ascolta su `http://0.0.0.0:5080` nel container; `Personal.cmd -Project jobbby` pubblica la porta solo su `127.0.0.1:5080` del PC (override `compose.jobbby.yaml`), e `-Action Web` / `-Action WebStop` avviano e fermano la UI. Dalla UI si modificano le impostazioni, si lanciano dry run e run (una alla volta, con avanzamento e Interrompi), si decidono gli annunci in attesa e si consulta lo storico delle run.

- Nessun login: la UI accetta solo gli host `localhost` e `127.0.0.1` (protezione dal DNS rebinding), e ogni richiesta che modifica dati deve avere l'header `X-Jobbby-Request: 1` e, se presente, l'`Origin` dell'app.
- UI e CLI non girano insieme sullo stesso `DataDir`: il secondo processo esce con un messaggio.
- I segreti restano in user-secrets (lo stesso store della CLI): la UI mostra solo se sono impostati.

## Esecuzione

```bash
Jobbby__DataDir=/percorso/dati Jobbby__DryRun=true dotnet run --project src/Host/Host.csproj
```

La dry run usa Adzuna e l'LLM reali ma non scrive registro né cursori; valuta al massimo `dryRun.maxPostingsPerQuery` annunci per query. Senza `Jobbby__DryRun` la run è normale e registra esiti e cursori. Ogni run finisce in `runs/<runId>.json`. `Ctrl+C` interrompe la run fermando anche le chiamate LLM in corso: la run risulta `Interrupted` e i cursori non vengono salvati.

### Esiti

| Esito | Quando |
|---|---|
| `AutoRejected` | filtro stage 1 non superato, oppure giudizio `Weak` |
| `Pending` | `Strong`/`Borderline` sotto `evaluation.autoApproveThreshold`, oppure nessun requisito estraibile dall'estratto |
| `Shortlisted` | `Strong`/`Borderline` dalla soglia in su |
| `Approved` / `Rejected` / `Applied` | decisione dell'utente |

Un annuncio già presente nel registro non viene rivalutato (nemmeno in dry run) e non costa chiamate LLM. Le decisioni si prendono, finché non c'è la UI, dalla riga di comando:

```bash
dotnet run --project src/Host/Host.csproj -- pending
dotnet run --project src/Host/Host.csproj -- decide <postingId> approve|reject|applied
```

Discovery non fa parte delle run: se `Jobbby__DiscoveryConfig` è impostato viene ignorato con un avviso. L'integrazione Telegram resta nel codice ma le run non la usano.
