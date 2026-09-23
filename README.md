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
