# Jobbby: UI web, impostazioni unificate e messaggio di presentazione

Data: 2026-09-30 · Stato: approvata in conversazione, in attesa di revisione scritta

## Obiettivo

Un solo posto, semplice e funzionale, per usare Jobbby senza toccare file o variabili d'ambiente:
modificare tutte le impostazioni, caricare il CV (PDF o JSON), lanciare una dry run o una run
normale, vedere e decidere i risultati, e ottenere per gli annunci scelti un messaggio di
presentazione proposto dall'LLM.

Utente unico: il proprietario del progetto, in locale, senza login. Un futuro deploy su server
(come app o con accesso web) è previsto ma fuori scope: il design non deve impedirlo.

## Decisioni prese

| Tema | Decisione |
|---|---|
| Approvazioni | Solo dalla UI. Telegram resta nel codice ma non viene più collegato: la sua rimozione è una decisione separata del proprietario |
| Flusso di approvazione | Asincrono: la run non attende; gli annunci sotto soglia diventano `Pending` senza scadenza |
| RAL minima | Configurabile (es. 33000). Annuncio con RAL nota e inferiore: escluso. Annuncio senza RAL: sempre mostrato |
| Zona di ricerca | Configurabile: località e raggio in km, più "accetta remoto" |
| Formato CV | JSON (come `cv.json` attuale) o PDF. YAML resta accettato dal loader; TOML non è supportato e non viene aggiunto |
| Accesso alla UI | `http://127.0.0.1:5080`, pubblicata da un override di compose usato solo per il progetto jobbby |
| Stack UI | ASP.NET Core minimal API + HTML/JS statici, nessun framework frontend né build step |

## Vincoli verificati su Adzuna

Verifica del 2026-09-30 sulla specifica ufficiale (`https://developer.adzuna.com/swagger/spec/test2.json`),
sulla documentazione (`/docs/search`, `/docs/terms_of_service`) e su chiamate reali a `/jobs/it/search`.

- **Il testo completo di un annuncio non è ottenibile dall'API.** Esistono solo 7 metodi
  (`search`, `categories`, `histogram`, `top_companies`, `geodata`, `history`, `version`), nessuno
  restituisce il singolo annuncio (`/jobs/it/ad/{id}` risponde 404). La specifica definisce
  `description` come *"truncated to 500 characters"*; la documentazione: *"we currently only provide
  a snippet of the job description in the response"*. Su 20 risultati reali tutte le descrizioni
  erano di 500 caratteri esatti, 8 senza alcun requisito (solo presentazione dell'azienda).
- `redirect_url` porta alla pagina dell'annuncio sul sito Adzuna; il testo completo lì c'è
  (JSON-LD `JobPosting`), ma è scraping del sito, non API: fuori scope.
- Parametri usati: `where` e `distance` (km) funzionano su `/it` (Milano 31 risultati, +30 km 35,
  Lombardia 40). `salary_min` da solo esclude gli annunci senza RAL (170 → 14);
  con `salary_include_unknown=1` li mantiene (158). Nessun filtro per il remoto.
- La RAL è quasi sempre assente (0 su 20 nel campione); alcuni valori non sono annui (`38`, `70`).
- Termini: quota predefinita 25 chiamate/minuto, 250/giorno, 1000/settimana, 2500/mese; uso
  "personal research" consentito; gli annunci mostrati vanno etichettati "Jobs by Adzuna".
- Lo stesso annuncio è pubblicato una volta per città (fino a 7 copie): la deduplica su
  titolo + azienda della PR #8 le riduce a una.

Conseguenza: tutte le valutazioni LLM e il messaggio di presentazione si basano sull'estratto di
500 caratteri, e la UI lo dichiara.

## Architettura

### Progetti

- **`src/Web` (nuovo)**: ASP.NET Core minimal API. Serve `wwwroot/` (HTML, CSS, JS) e le API JSON.
  Referenzia `Host`.
- **`src/Host`**: la logica oggi in `Program.cs` si sposta in `JobbbyRunner`:
  `Task<RunRecord> RunAsync(JobbbySettings settings, RunMode mode, IProgress<RunEvent> progress, CancellationToken ct)`.
  `Program.cs` resta come entry point da riga di comando e chiama `JobbbyRunner`.
- **Approvazioni**: `HostGraph` riceve un `IApprovalPolicy`. L'implementazione usata da CLI e web,
  `DeferredApprovalPolicy`, registra `Pending` e termina il grafo senza attendere. `AskApprovalNode`
  e le classi Telegram restano compilate e testate ma non vengono istanziate da `Program.cs` né da `Web`.

### Dati

Tutti sotto `Jobbby:DataDir` (default `/home/dev/jobbby-data`), fuori da git. Nessun percorso del
container è scritto nel codice.

| File | Contenuto |
|---|---|
| `settings.json` | tutte le impostazioni non segrete (vedi sotto) |
| `cv.pdf` o `cv.json` | CV caricato dall'utente (uno solo alla volta) |
| `cv.extracted.json` | CV strutturato: estratto dal PDF al caricamento, modificabile dalla UI; è quello usato dalle run |
| `runs/<runId>.json` | una run: modalità, stato, impostazioni usate, ricerche, chiamate Adzuna, eventi per ricerca, e per ogni annuncio dati normalizzati, esito del primo filtro, confidenza, motivazione, esito finale |
| `applications.json` | registro esistente (`ApplicationLedger`), esteso con `Pending`, il messaggio e il `RunId` |
| `cursors.json` | cursori per fonte/ricerca, invariati |

`settings.json`:

```json
{
  "searches": { "queries": [".NET developer", "Artificial Intelligence engineer", "LLM engineer"], "deriveFromCv": true, "maxDerivedQueries": 3 },
  "area": { "where": "Milano", "distanceKm": 30, "acceptsRemote": true },
  "salary": { "minimumYearly": 33000 },
  "evaluation": { "autoApproveThreshold": 0.7 },
  "llm": { "provider": "claude-cli", "model": "sonnet" },
  "dryRun": { "maxPostingsPerQuery": 3 },
  "presentation": {
    "enabled": true,
    "opening": "Salve, sono l'agente AI di {nome}. Ho letto il vostro annuncio per {ruolo} e credo che il profilo di {nome} corrisponda a ciò che cercate.",
    "closing": "",
    "tone": "formale",
    "length": "breve",
    "language": "annuncio",
    "extraInstructions": ""
  }
}
```

- Se `settings.json` manca, al primo avvio viene creato dai valori attuali (`searches.json`,
  provider `claude-cli`, modello `sonnet`, soglia 0.7). `searches.json` resta solo come sorgente
  di questo primo avvio.
- `minimumSalary`, `desiredLocations` e `acceptsRemote` escono dal modello del CV e vengono da
  `settings.json`. Se un `cv.json` esistente li contiene ancora, vengono ignorati con un avviso.
- I segreti (`Adzuna:AppId`, `Adzuna:AppKey`, eventuale `Llm:ApiKey`) restano in user-secrets o
  variabili d'ambiente. La UI mostra solo se sono presenti.

### Adzuna

`AdzunaJobSource` riceve zona e RAL dalle impostazioni e aggiunge alla ricerca `where`, `distance`,
`salary_min` e, sempre insieme a `salary_min`, `salary_include_unknown=1`. Con zona vuota cerca in
tutta Italia, come oggi.

Il primo filtro (`MatchStageOneFilter`) legge RAL minima, località e remoto dalle impostazioni:
esclude solo se la RAL è nota e inferiore; una località è accettata se contiene la località
desiderata, o se l'annuncio è remoto e il remoto è accettato.

## Esiti

| Esito | Quando |
|---|---|
| `Shortlisted` | confidenza ≥ soglia (approvato automaticamente) |
| `Pending` | primo filtro superato, confidenza < soglia, in attesa di decisione |
| `Approved` / `Rejected` | decisione dalla UI |
| `Applied` | "Segna come inviata" dalla UI |

`Pending` non è terminale per il ciclo di vita ma lo è per la deduplica: una run successiva non
ripropone come nuovo un annuncio già `Pending` (`ApplicationOutcomes.IsTerminal` va esteso, o la
deduplica usa un controllo dedicato). Una decisione aggiunge un nuovo record con lo stesso
`DedupeKey`; lo stato corrente di un annuncio è il suo record più recente. `Discovered` (timeout Telegram) resta leggibile per
i record esistenti ma non viene più prodotto.

In dry run non si scrive il registro, quindi gli annunci sotto soglia compaiono nella run come
"sarebbe da decidere" senza pulsanti di decisione.

## UI

Una pagina, barra in alto: **Run · Impostazioni · CV · Storico**, più un contatore "N da decidere".
Stile sobrio: tabelle e form, nessun grafico. In fondo a ogni elenco di annunci: "Jobs by Adzuna".

### Run (pagina iniziale)

- Pulsanti **Dry run** e **Run** (quest'ultimo con conferma). Riepilogo di cosa verrà fatto:
  ricerche, zona, RAL minima, LLM, chiamate Adzuna previste.
- Avanzamento in tempo reale via server-sent events: ricerca in corso, annunci trovati, annuncio
  in valutazione N di M, errori. Pulsante **Interrompi**. Una sola run alla volta: il secondo
  avvio risponde 409 e la UI mostra la run in corso.
- Elenco **Da decidere**: annunci `Pending` di tutte le run, con Approva, Rifiuta e link all'annuncio.

### Impostazioni

Form a gruppi con tutti i campi di `settings.json`. Validazione al salvataggio con errori accanto
ai campi (es. raggio negativo, soglia fuori da 0–1, nessuna ricerca e derivazione dal CV spenta).
Sezione segreti in sola lettura, con il comando per impostarli.

### CV

- Caricamento `.pdf` o `.json`, massimo 5 MB. Il PDF viene estratto con l'LLM al caricamento; il
  risultato va in `cv.extracted.json`.
- Form modificabile del CV strutturato (nome, anni, seniority, competenze, ruoli, lingue),
  salvato in `cv.extracted.json`.
- Anteprima delle ricerche che verrebbero derivate dal CV.

### Storico

- Tabella delle run: data, modalità, stato (completata, fallita, interrotta), annunci trovati,
  forti, borderline, deboli, scartati, errori, chiamate Adzuna.
- Dettaglio run: tabella annunci (esito, confidenza, titolo · azienda · località, RAL,
  motivazione), filtrabile per esito. Espandendo una riga: stack richiesto, requisiti mancanti,
  avvisi, link, messaggio di presentazione. Approva e Rifiuta sugli annunci `Pending`.

## Messaggio di presentazione

- **Generato** automaticamente per gli annunci `Shortlisted` e al momento di Approva; a richiesta
  con "Genera messaggio" su qualsiasi annuncio dello storico. Mai per gli annunci scartati al
  primo filtro, salvo richiesta esplicita.
- **Prompt** con regole fisse: usare solo fatti presenti nel CV strutturato e nell'annuncio; citare
  2–3 punti concreti di corrispondenza; non dichiarare competenze assenti dal CV; non inventare
  numeri, aziende o contatti.
- **Configurabile** (sezione `presentation`): attivo, apertura con segnaposti `{nome}`, `{ruolo}`,
  `{azienda}`, chiusura/firma inserita letteralmente (l'LLM non genera mai contatti), tono
  (formale, cordiale), lunghezza (breve ~100 parole, media ~180), lingua (quella dell'annuncio,
  italiano, inglese), istruzioni aggiuntive. **Anteprima** su un annuncio dello storico prima di
  salvare.
- **In UI**: riquadro modificabile con Rigenera, Copia, Segna come inviata (`Applied`). Avviso fisso:
  "basato sull'estratto dell'annuncio (500 caratteri): verificalo prima di inviarlo".
- **Salvataggio** nel record del registro, quindi persiste tra le run. Per gli annunci di una dry
  run, che non scrive il registro, il messaggio generato a richiesta viene salvato in
  `runs/<runId>.json` accanto all'annuncio.
- **Non invia nulla**: l'invio resta manuale.

## API web

| Metodo | Percorso | Scopo |
|---|---|---|
| GET/PUT | `/api/settings` | leggere/salvare le impostazioni (validazione, 400 con errori per campo) |
| GET | `/api/secrets/status` | quali segreti sono presenti (mai i valori) |
| GET/POST/PUT | `/api/cv` | leggere il CV strutturato, caricare PDF/JSON, salvare modifiche |
| GET | `/api/cv/derived-queries` | anteprima delle ricerche dal CV |
| POST | `/api/runs` | avviare una run (`{ "mode": "dry" \| "normal" }`), 409 se ne esiste una in corso |
| GET | `/api/runs`, `/api/runs/{id}` | storico e dettaglio |
| GET | `/api/runs/current/events` | server-sent events della run in corso |
| POST | `/api/runs/current/cancel` | interrompere |
| GET | `/api/pending` | annunci da decidere |
| POST | `/api/postings/{key}/decision` | `approve` \| `reject` \| `applied` |
| POST | `/api/postings/{key}/presentation` | generare/rigenerare il messaggio; PUT per salvarne la modifica |
| POST | `/api/presentation/preview` | anteprima con impostazioni non ancora salvate |

`{key}` è la chiave di deduplica del registro (`azienda::titolo` normalizzati), URL-encoded; per un
annuncio di dry run si usa `/api/runs/{id}/postings/{index}/presentation`.

## Gestione errori

- Ricerca Adzuna fallita (rete, 4xx/5xx, quota): registrata nella run, le altre proseguono.
- LLM fallito su un annuncio: l'annuncio risulta "errore" con il messaggio, gli altri proseguono.
- Estrazione CV o messaggio falliti: errore mostrato in UI, nessun file sovrascritto.
- Arresto del processo durante una run: all'avvio successivo la run risulta "interrotta";
  registro e cursori contengono solo il lavoro completato. Oggi `ApplicationLedger` riscrive il file
  con `File.WriteAllText`: diventa scrittura su file temporaneo + rename, così un arresto a metà
  non corrompe il registro. Stesso schema per `runs/<runId>.json` e `settings.json`.
- Nessun errore viene assorbito in silenzio: ogni fallimento compare nella run o nella risposta API.

## Sicurezza

- Ascolto solo su `127.0.0.1` (Kestrel nel container su `0.0.0.0:5080`, pubblicato da compose su
  `127.0.0.1:5080`). Nessun login in questa fase.
- Upload: solo `.pdf` e `.json`, massimo 5 MB, nome file ignorato (salvato come `cv.pdf`/`cv.json`).
- I segreti non transitano mai per la UI o le API.

## Ambiente

`Tools\PersonalContainers`: nuovo `compose.jobbby.yaml` con la porta `127.0.0.1:5080:5080`;
`Personal.ps1` lo aggiunge solo quando `-Project jobbby`. Il `compose.yaml` condiviso non cambia.
Il servizio web si avvia con `dotnet run --project src/Web` nel container (eventuale azione
`-Action Web` in `Personal.ps1`).

## Test

- Unitari: validazione e default di `JobbbySettings`, parametri Adzuna (zona, RAL con
  `salary_include_unknown`), primo filtro con impostazioni, `DeferredApprovalPolicy`, deduplica
  con `Pending`, generatore del messaggio (prompt, segnaposti, chiusura letterale, errori LLM).
- Integrazione API con `WebApplicationFactory`, fonte e LLM finti: salvataggio impostazioni,
  caricamento CV, run completa, 409 su run concorrente, decisione su un `Pending`, generazione messaggio.
- Verifica finale reale dal browser: dry run con Adzuna e `claude-cli`, poi una run normale con
  decisione e messaggio.

## Consegna

Cinque PR incrementali, ciascuna utilizzabile da sola, dopo #6 → #7 → #8:

1. `JobbbySettings` + `settings.json` + `JobbbyRunner` + Adzuna con zona e RAL (CLI invariata nell'uso).
2. Persistenza delle run (`runs/`) e approvazioni asincrone `Pending`.
3. `src/Web` con Run, Impostazioni, Storico e override di compose.
4. Pagina CV con estrazione modificabile.
5. Messaggio di presentazione.

## Fuori scope

Rimozione di Telegram; run pianificate; testo completo degli annunci; altre fonti e crawler;
login e deploy su server; invio automatico delle candidature; estrazione di competenze
obbligatorie/preferenziali e lingue dall'annuncio.
