# Jobbby

Jobbby raccoglie annunci, li normalizza con un LLM, valuta la compatibilità con il CV e conserva gli esiti operativi.

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

Limiti predefiniti: 3 annunci per fonte e 3 candidati Discovery. I massimi accettati sono rispettivamente 20 e 10.
