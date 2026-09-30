# Jobbby

Jobbby raccoglie annunci, li normalizza con un LLM, valuta la compatibilità con il CV e conserva gli esiti operativi.

## Ricerche e CV

Le fonti (`src/Config/sources.json`) dicono *dove* cercare, le ricerche (`src/Config/searches.json`) *cosa*: ogni query viene eseguita su ogni fonte.

```json
{
  "queries": [".NET developer", "AI engineer"],
  "deriveFromCv": true,
  "maxDerivedQueries": 3
}
```

Adzuna ignora le sigle di due lettere: "AI engineer" viene cercato come "engineer" e restituisce annunci di ogni tipo. Usare la forma estesa ("Artificial Intelligence engineer") o sigle più lunghe ("LLM engineer").

Con `deriveFromCv` l'LLM aggiunge fino a `maxDerivedQueries` (massimo 10) ruoli ricavati dal CV, senza ripetere quelli già configurati. Se la derivazione fallisce si usano solo le query configurate. Gli annunci restituiti da più query, o ripubblicati con un nuovo link e l'azienda scritta diversamente ("Acme S.r.l" / "ACME SRL"), vengono valutati una volta sola, e ogni coppia fonte/query ha il proprio cursore. Un file diverso può essere indicato con `Jobbby__SearchesConfig`.

Il CV predefinito è `src/Host/cv.json`. Per usare un PDF, tenuto fuori dal repository, indicarne il percorso con `Jobbby__CvPath=/percorso/cv.pdf`: il testo viene estratto e strutturato dall'LLM a ogni esecuzione.

## Dry run sicura

La dry run usa Adzuna e il provider LLM reali, ma non:

- invia messaggi Telegram;
- scrive ledger, cursori, report o fonti approvate;
- seleziona, approva o rifiuta annunci;
- attiva fonti trovate tramite Discovery.

Impostare i segreti dalla directory `src/Host`:

```bash
dotnet user-secrets set "Adzuna:AppId" "..."
dotnet user-secrets set "Adzuna:AppKey" "..."
dotnet user-secrets set "Llm:Endpoint" "..."
dotnet user-secrets set "Llm:ApiKey" "..."
dotnet user-secrets set "Llm:Model" "..."
```

### LLM tramite Claude Code (`claude -p`)

In alternativa a un endpoint OpenAI-compatibile, soprattutto per i test, si può usare il CLI di Claude Code già autenticato. In questo caso `Llm:Endpoint` e `Llm:ApiKey` non servono:

```bash
dotnet user-secrets set "Llm:Provider" "claude-cli"
dotnet user-secrets set "Llm:Model" "haiku"    # facoltativo, default sonnet
```

Il CLI viene lanciato senza strumenti, server MCP né impostazioni utente/progetto, con un timeout di 120 secondi e al massimo 2 processi in parallelo. Opzioni: `Llm:ClaudePath` (default `claude`), `Llm:TimeoutSeconds`, `Llm:MaxConcurrency`. L'autenticazione è quella del CLI (`claude` interattivo e `/login`, oppure `ANTHROPIC_API_KEY`). Ogni chiamata avvia un processo, quindi è più lento dell'API diretta.

Eseguire con limiti espliciti:

```bash
Jobbby__DryRun=true \
Jobbby__DryRunMaxPostingsPerSource=3 \
Jobbby__DryRunMaxDiscoveryCandidates=2 \
dotnet run --project src/Host/Host.csproj
```

La ricerca fonti resta disabilitata se `Jobbby__DiscoveryConfig` non è impostato. Per abilitarla durante la simulazione:

```bash
Jobbby__DiscoveryConfig=/percorso/discovery.json
```

Il log JSON dettagliato viene creato nella directory di output del programma con nome `dry-run-YYYYMMDD-HHMMSS.json`. Un percorso diverso può essere indicato tramite `Jobbby__DryRunLogPath`. Il log include configurazione non segreta, fonti interrogate, quantità restituite e processate, normalizzazione, requisiti mancanti, avvisi, confidenza, motivazione LLM, errori e conferma che nessuna azione è stata eseguita.

Limiti predefiniti: 3 annunci per fonte e per query, 3 candidati Discovery. I massimi accettati sono rispettivamente 20 e 10.
