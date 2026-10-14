# Jobbby: UI web, impostazioni unificate e messaggio di presentazione

Data: 2026-09-30 · Revisione 3 · Stato: approvata dal proprietario il 2026-09-30

## Obiettivo

Un solo posto, semplice e funzionale, per usare Jobbby senza toccare file o variabili d'ambiente:
modificare tutte le impostazioni, caricare il CV (PDF o JSON), lanciare una dry run o una run
normale, vedere e decidere i risultati, e ottenere per gli annunci scelti un messaggio di
presentazione proposto dall'LLM.

Utente unico, in locale, senza login. Un futuro deploy su server (app o accesso web) è previsto ma
fuori scope: il design non deve impedirlo.

**Nessun profilo specifico nel codice.** Jobbby deve funzionare per qualsiasi professione
(sviluppatore, infermiere, commercialista, magazziniere…) e per qualsiasi paese supportato da
Adzuna. Codice, default e prompt non contengono riferimenti a una professione, a un paese o alla
ricerca del proprietario: quei valori vivono solo nel `DataDir` di chi usa l'applicazione.

## Decisioni prese

| Tema | Decisione |
|---|---|
| Approvazioni | Solo dalla UI. Telegram resta nel codice ma non viene collegato; la rimozione è una decisione separata |
| Flusso di approvazione | Asincrono: la run non attende; Strong/Borderline sotto la soglia di auto-approvazione diventano `Pending` senza scadenza |
| Annunci deboli | Scartati automaticamente con esito `AutoRejected` (solo stage 1 fallito o categoria `Weak`), distinto dal rifiuto umano |
| RAL minima | Configurabile, applicata solo lato client (stage 1). Annuncio con retribuzione nota e inferiore: escluso. Senza retribuzione: sempre mostrato |
| Zona di ricerca | Configurabile: paese, località, raggio in km, accetta remoto |
| Testo completo degli annunci | Nessun recupero automatico (vedi "Vincoli verificati"). L'utente può incollarlo nel dettaglio annuncio: rivalutazione e messaggio usano quel testo |
| Formato CV | JSON o PDF. YAML resta accettato dal loader; TOML non è supportato e non viene aggiunto |
| Accesso alla UI | `http://127.0.0.1:5080`, pubblicata da un override di compose usato solo per il progetto jobbby |
| Stack UI | ASP.NET Core minimal API + HTML/JS statici, nessun framework frontend né build step |

## Vincoli verificati

### API Adzuna

Verifica del 2026-09-30 sulla specifica ufficiale (`https://developer.adzuna.com/swagger/spec/test2.json`),
su `/docs/search`, `/docs/terms_of_service` e su chiamate reali a `/jobs/it/search`.

- **Il testo completo non è ottenibile dall'API.** Esistono solo 7 metodi (`search`, `categories`,
  `histogram`, `top_companies`, `geodata`, `history`, `version`); `/jobs/it/ad/{id}` risponde 404.
  La specifica definisce `description` come *"truncated to 500 characters"*. Su 20 risultati reali
  tutte le descrizioni erano di 500 caratteri esatti, 8 senza alcun requisito.
- `where` + `distance` (km) funzionano (Milano 31, +30 km 35, Lombardia 40 risultati).
- `salary_min` lato server **non viene usato**: senza `salary_include_unknown=1` esclude gli annunci
  senza RAL (170 → 14), e in ogni caso il filtro server confronta anche valori che Jobbby tratta di
  proposito come sconosciuti (RAL stimate `salary_is_predicted = 1`, valori implausibili come `38`, `70`).
- Nessun filtro per il remoto.
- Termini API: quota predefinita 25 chiamate/minuto, 250/giorno, 1000/settimana, 2500/mese; uso
  "personal research" consentito; gli annunci mostrati vanno etichettati "Jobs by Adzuna".
- Lo stesso annuncio è pubblicato una volta per città (fino a 7 copie).

### Sito adzuna.it (testo completo)

- `robots.txt` (`User-agent: *`) consente `/details/` ma vieta `/land/ad/`, `/goto/ad/` e `?aztt=`,
  cioè il passaggio verso il sito dell'azienda; `ClaudeBot`, `anthropic-ai`, `GPTBot` sono bloccati del tutto.
- Condizioni d'uso generali: nessun divieto esplicito di lettura automatica, ma *"You will not access
  the Services for the purpose of building a similar or competitive product"* e *"Unauthorised use of
  this website may give rise to a claim for damages"*.
- Dal 2026-09-30 CloudFront risponde 403 a qualsiasi client non browser (curl e Python, da container
  e da Windows, con qualsiasi User-Agent), anche su `/details/`.

Decisione: nessun recupero automatico del testo completo. Aggirare il blocco richiederebbe un
browser automatizzato che eluda una protezione anti-bot. Le valutazioni automatiche si basano
sull'estratto e la UI lo dichiara. Il testo completo entra solo se l'utente lo incolla (vedi
"Testo completo fornito dall'utente").

## Architettura

### Progetti

- **`src/Web` (nuovo)**: ASP.NET Core minimal API. Serve `wwwroot/` (HTML, CSS, JS) e le API JSON.
  Referenzia `Host`.
- **`src/Host`**: la logica oggi in `Program.cs` si sposta in `JobbbyRunner`:
  `Task<RunRecord> RunAsync(JobbbySettings settings, RunMode mode, IProgress<RunEvent> progress, CancellationToken ct)`.
  `Program.cs` resta l'entry point da riga di comando e chiama `JobbbyRunner`.
- **Approvazioni**: `HostGraph` riceve un `IApprovalPolicy`. L'implementazione usata da CLI e web,
  `DeferredApprovalPolicy`, registra `Pending` e termina il grafo senza attendere. `AskApprovalNode` e
  le classi Telegram restano compilate e testate, ma non vengono istanziate né da `Program.cs` né da `Web`.
- **Discovery**: `JobbbyRunner` non esegue Discovery (resta fuori scope). Oggi, in una run normale,
  `DiscoveryReviewService` riceverebbe un gateway Telegram `null`. Se `Jobbby:DiscoveryConfig` è
  impostato, viene ignorato con un avviso nella run.

### Cancellazione

`INode.ExecuteAsync(GraphState)` non riceve un `CancellationToken`, quindi oggi "Interrompi" non
fermerebbe le chiamate LLM in corso. Il contratto diventa `ExecuteAsync(GraphState, CancellationToken)`;
`GraphRun.RunAsync` riceve il token, lo passa ai nodi e lo controlla tra un nodo e l'altro. I nodi
lo propagano a `ILlmClient.CompleteAsync` e `IJobSource.FetchAsync`, che lo accettano già.
`ClaudeCliLlmClient` termina il processo `claude` alla cancellazione (già implementato). Gli
annunci non completati risultano `Interrupted` nella run e non vengono scritti nel registro.

È una modifica al `GraphEngine`, ma resta agnostica rispetto al dominio: aggiunge soltanto la
cancellazione al motore.

### Dati

Tutti sotto `Jobbby:DataDir` (default `/home/dev/jobbby-data` nel container, configurabile), fuori
da git. Nessun percorso del container è scritto nel codice.

| File | Contenuto |
|---|---|
| `settings.json` | tutte le impostazioni non segrete |
| `cv.pdf` o `cv.json` | CV caricato (uno solo alla volta) |
| `cv.extracted.json` | CV strutturato: estratto dal PDF al caricamento, modificabile dalla UI; è quello usato dalle run |
| `skill-aliases.json` | facoltativo: equivalenze tra competenze (vedi "Confronto delle competenze") |
| `runs/<runId>.json` | una run: modalità, stato, impostazioni usate, ricerche, chiamate Adzuna e LLM, eventi per ricerca, e per ogni annuncio dati normalizzati, stage 1, confidenza, motivazione, esito |
| `applications.json` | registro (`ApplicationLedger`), esteso come descritto sotto |
| `cursors.json` | cursori per fonte e ricerca |
| `.jobbby.lock` | lock del processo che possiede il `DataDir` |

Scrittura di `applications.json`, `runs/<runId>.json`, `settings.json` e `cursors.json`: file
temporaneo + rename, così un arresto a metà non corrompe nulla.

### Un solo scrittore del registro

- `ApplicationLedger` oggi carica il file nel costruttore e lo riscrive per intero: due istanze si
  sovrascrivono. Nel processo Web ne esiste una sola istanza singleton, condivisa tra la run e le API,
  e protetta dal proprio lock interno.
- `.jobbby.lock` è un file aperto in modo esclusivo (`FileShare.None`) per tutta la vita del
  processo, sia dal Web sia dalla CLI. Una CLI lanciata con il Web attivo fallisce subito con:
  *"DataDir già in uso da un altro processo Jobbby (probabilmente la UI web): chiudilo o usa la UI"*.
  Il lock è tenuto dal sistema operativo, quindi un crash lo rilascia senza file orfani.
- **CLI e Web girano solo nel container.** Verificato il 2026-09-30 su bind mount da Windows: tra
  processi nel container il lock funziona anche su bind mount (il secondo processo lo trova
  occupato); tra un processo Windows e uno del container i due si bloccano a vicenda, ma il
  container riceve un `Permission denied` generico invece di un lock riconoscibile. Un processo
  Jobbby nativo su Windows non è quindi supportato. Il `DataDir` resta sul volume del container,
  come previsto anche dalle regole dell'ambiente (nessuna cartella Windows montata).

### Impostazioni (`settings.json`)

Default per una nuova installazione, neutri:

```json
{
  "searches": { "queries": [], "deriveFromCv": true, "maxDerivedQueries": 3 },
  "area": { "country": null, "where": "", "distanceKm": null, "acceptsRemote": false },
  "salary": { "minimumYearly": null, "minimumPlausible": 5000 },
  "evaluation": { "autoApproveThreshold": 0.7 },
  "llm": { "provider": "claude-cli", "model": "sonnet" },
  "dryRun": { "maxPostingsPerQuery": 3 },
  "remoteSweep": { "keywords": {} },
  "dedupe": { "extraCompanySuffixes": [] },
  "presentation": {
    "enabled": true,
    "opening": "",
    "closing": "",
    "tone": "formale",
    "length": "breve",
    "language": "annuncio",
    "extraInstructions": ""
  }
}
```

- `area.country` è obbligatorio prima della prima run: la UI chiede di sceglierlo tra i paesi
  supportati da Adzuna. La valuta e le etichette della retribuzione derivano dal paese.
- `salary.minimumPlausible` sostituisce la costante 5000 di `AdzunaJobSource`: sotto questa soglia
  una retribuzione riportata non viene considerata annua ed è trattata come sconosciuta. È espressa
  nella valuta del paese.
- Primo avvio con `settings.json` mancante: il file viene creato dai default, poi valorizzato con ciò
  che esiste già nel `DataDir` o accanto all'eseguibile (`searches.json` del proprietario). Il paese
  resta da scegliere nella UI, a meno che un file esistente lo indichi.
- `minimumSalary`, `desiredLocations` e `acceptsRemote` escono dal modello del CV. Se un `cv.json`
  esistente li contiene, vengono ignorati con un avviso.
- I segreti (`Adzuna:AppId`, `Adzuna:AppKey`, eventuale `Llm:ApiKey`) restano in user-secrets o in
  variabili d'ambiente. La UI mostra solo se sono presenti.

**Località dal CV** (richiesta del proprietario, 2026-09-30): il CV strutturato ha `location` (città di residenza, estratta anche dal PDF). `area.whereFromCv` (default `true`): con `where` vuoto la ricerca usa la località del CV; `where` compilato vince sempre; `whereFromCv: false` e `where` vuoto = tutto il paese. Un raggio senza località risolta viene ignorato con un avviso.

Esempio, non default, del `settings.json` del proprietario:
`queries: [".NET developer", "Artificial Intelligence engineer", "LLM engineer"]`, `country: "it"`,
`where: ""` con `whereFromCv: true` (dal CV: Bologna), `acceptsRemote: true`, `minimumYearly: 33000`.

### Migrazione dei file esistenti

Oggi `applications.json`, `cursors.json` e `run-reports.json` stanno in `AppContext.BaseDirectory`
(dentro `bin/`). Al primo avvio con un `DataDir` vuoto:

- `applications.json` viene **copiato** in `DataDir` (l'originale resta). Le chiavi di
  deduplica dei record vengono ricalcolate con la normalizzazione unificata.
- `cursors.json` **non** viene migrato: le chiavi nuove includono paese e zona, quindi quelle
  vecchie non corrispondono. Effetto: la prima run cerca senza `max_days_old`; le ripetizioni sono
  comunque evitate dalla deduplica del registro.
- `run-reports.json` viene importato come run storiche di sola sintesi (`runs/legacy-<data>.json`,
  modalità `legacy`, senza annunci) e poi non viene più scritto. Lo sostituisce `runs/`.
- Le directory di origine considerate sono `Jobbby:LegacyDir`, se impostato, e l'output di `src/Host`.
  La migrazione viene registrata nella prima run.

## Ricerca

### Adzuna

- Il paese diventa `area.country`: `https://api.adzuna.com/v1/api/jobs/{country}/search/1`.
- Ricerca principale per ogni query: `what`, più `where` e `distance` se la località è impostata.
  Niente parametri di retribuzione.
- **Remoto fuori zona**: se `acceptsRemote` è attivo e `where` è impostato, per ogni query si fa una
  seconda ricerca senza `where`. Dei suoi risultati si tengono solo gli annunci con `workMode = remote`.
- **Prefiltro della ricerca remota**: prima della normalizzazione LLM, un risultato della ricerca
  remota passa solo se titolo o estratto contengono una delle parole chiave configurate
  (`remoteSweep.keywords`, per lingua, es. `{ "it": ["remoto", "smart working"], "en": ["remote"] }`).
  Il confronto ignora maiuscole, minuscole e accenti. Si usano le parole di tutte le lingue
  configurate, perché la lingua dell'annuncio non è nota a priori. Il default è nessuna parola:
  la ricerca remota resta disattivata finché non se ne configura almeno una, e la UI lo spiega.
  Le parole del proprietario vivono nel suo `settings.json`. Il prefiltro è solo un risparmio:
  chi lo supera va comunque in normalizzazione, e resta solo `workMode = remote`.
- Costo: una chiamata Adzuna in più per query, più una chiamata LLM per ogni risultato che supera il
  prefiltro. Il riepilogo prima della run mostra le chiamate previste, quello finale quelle
  effettive, compresi i risultati scartati dal prefiltro.
- **Cursori**: la chiave diventa `source|country|where|distanceKm|sweep|query`, con `sweep` pari a
  `local` o `remote`. Cambiando zona non si riusa il `max_days_old` di un'altra zona.

### Modalità di lavoro

`NormalizeJobPostingNode` estrae `workMode: onsite | hybrid | remote | unknown` al posto di
`remoteAvailable`. `hybrid` è trattato come `onsite` ai fini della località.

### Stage 1

Tutte le soglie arrivano dalle impostazioni.

- **Località**:
  - annuncio della ricerca principale con `where`: la località è garantita dall'API, nessun
    controllo testuale;
  - annuncio della ricerca remota: passa solo se `workMode = remote`;
  - fonti senza filtro geografico, o `where` vuoto: controllo testuale "contiene `where`" solo per
    `onsite`/`hybrid`, e solo se `where` è impostato. `remote` passa se `acceptsRemote`; `unknown` passa.
  - `remote` con `acceptsRemote` spento non viene escluso: passa con un avviso, come fa oggi. Il
    remoto è un'opzione in più, non un difetto dell'annuncio.
- **Retribuzione**: esclude solo se la retribuzione è nota, plausibile e inferiore a `minimumYearly`.
- **Esperienza**: niente più scala di seniority. Le fasce Junior/Mid/Senior/Staff e le etichette
  "staff/lead/principal" sono tipiche del software. La normalizzazione estrae
  `minYearsExperience: number | null`; lo stage 1 esclude solo se è indicato ed è superiore agli
  anni del CV. L'etichetta di seniority dell'annuncio va solo allo stage 2, che la valuta con l'LLM.
  Un'etichetta non riconosciuta non scarta mai l'annuncio.
- **Competenze**: vedi sotto.

### Confronto delle competenze

- "Stack" diventa "competenze/requisiti" ovunque: `RequiredSkills`, `MustHaveSkills`,
  `PreferredSkills`, `CvRole.Skills`, e "Competenze richieste" nei prompt. La lettura di `cv.json`
  accetta ancora `stack` come alias di `skills` nei ruoli.
- `SkillMatcher` tiene nel codice solo regole generiche: maiuscole/minuscole, spazi, numero di
  versione finale, e "una variante più specifica copre la generica" (`X Y` copre `X`).
- Alias ed equivalenze (oggi `dotnet`, `asp.net`, `entity framework`, `wcf`…) escono dal codice e
  vanno in `DataDir/skill-aliases.json` (`{ "aliases": { "k8s": "kubernetes" }, "implies": { "asp.net core": [".net"] } }`).
  Il default è nessun file, cioè nessun alias. Il file del proprietario viene creato nel suo `DataDir`
  con gli alias attuali, come dato e non come codice.

## Esiti e identità degli annunci

| Esito | Quando |
|---|---|
| `AutoRejected` | stage 1 fallito, oppure categoria `Weak` |
| `Pending` | categoria `Strong` o `Borderline` con confidenza < `autoApproveThreshold`, qualunque sia la confidenza; oppure informazioni insufficienti (vedi sotto) |
| `Shortlisted` | categoria `Strong` o `Borderline` con confidenza ≥ `autoApproveThreshold` |
| `Approved` / `Rejected` | decisione dalla UI (o dalla CLI, vedi PR 2) |
| `Applied` | "Segna come inviata" |
| `Interrupted` | annuncio non completato per una cancellazione (solo nella run, non nel registro) |

- La confidenza misura la certezza del giudizio, non l'aderenza, quindi non decide mai uno scarto.
  Scartano solo lo stage 1 e la categoria `Weak`. `MatchConfidenceMapper` smette di azzerare la
  confidenza dei `Weak`: l'esito lo decide la categoria, la confidenza resta quella dell'LLM.
- **Informazioni insufficienti**: se la normalizzazione non estrae alcun requisito dall'estratto
  (nessuna competenza richiesta e nessun anno di esperienza), l'annuncio salta il controllo delle
  competenze dello stage 1 e lo scarto `Weak`. Va in `Pending` con motivo *"informazioni
  insufficienti nell'estratto"*, e la UI suggerisce di incollare il testo completo. Non diventa mai
  `AutoRejected`, perché quell'esito è terminale per la deduplica e l'annuncio non tornerebbe più.
  Gli altri controlli dello stage 1 (località, retribuzione) restano validi.
- `AutoRejected`, `Pending`, `Shortlisted`, `Approved`, `Rejected` e `Applied` sono tutti terminali
  per la deduplica: una run successiva non ripropone l'annuncio.
- `Discovered`, il vecchio timeout di Telegram, resta leggibile nei record esistenti ma non viene più prodotto.
- Ogni decisione aggiunge un record con la stessa chiave: lo stato corrente di un annuncio è il suo
  record più recente, e la storia resta.
- In dry run il registro non viene scritto: gli esiti compaiono solo nella run, senza pulsanti di decisione.

### Una sola normalizzazione

`DedupeKey.Normalize` (tra run) e `MultiQueryFetcher.TitleCompanyKey` (dentro una run) usano oggi
regole diverse: "Acme S.r.l." e "ACME SRL" coincidono dentro una run ma non tra run diverse.
Diventano un solo `PostingIdentity.Normalize(company, title)`:

- minuscole;
- punteggiatura trasformata in spazi (tenendo `#` e `+`);
- spazi compressi;
- rimozione dei suffissi societari finali da una lista internazionale neutra (srl, spa, sas, snc, sa,
  gmbh, ag, kg, bv, nv, ltd, plc, llc, inc, corp, co, oy, ab, as, sl, sp zoo, kft…) più
  `dedupe.extraCompanySuffixes`. Niente parole geografiche come "italia".

### Id opaco

`PostingId` è la codifica esadecimale dei primi 16 byte dello SHA-256 della chiave normalizzata:
stabile tra le run e sicuro nelle URL, dove `%2F` nei titoli non verrebbe decodificato nei route values.

### Il record basta per decidere

`ApplicationRecord` contiene tutto ciò che serve per decidere e generare il messaggio senza leggere
`runs/<runId>.json`:

- `PostingId`, `DedupeKey`, `RunId`, `RecordedAt`, `Outcome`;
- `Title`, `Company`, `ApplyUrl`, `SourceName`, `Excerpt`;
- campi normalizzati: competenze richieste, obbligatorie e preferenziali, `workMode`, `Location`,
  `Salary`, `minYearsExperience`, seniority;
- `Confidence`, `Category`, `Reasoning`, requisiti mancanti, avvisi;
- `Presentation`: testo, data, se è stato modificato a mano.

Viene corretto anche `SourceUrl`: oggi contiene `source.BaseUrl`, non il link dell'annuncio.
Diventa `ApplyUrl` (link dell'annuncio), più `SourceName`.

## UI

Una pagina con una barra in alto (**Run · Impostazioni · CV · Storico**) e un contatore
"N da decidere". Stile sobrio: tabelle e form, niente grafici. In fondo a ogni elenco di annunci:
"Jobs by Adzuna".

### Run (pagina iniziale)

- Pulsanti **Dry run** e **Run**, il secondo con conferma.
- Riepilogo: ricerche, zona, remoto, retribuzione minima, LLM, chiamate Adzuna previste (ricerca
  principale ed eventuale ricerca remota) su quota giornaliera 250.
- Avanzamento in tempo reale via server-sent events. Pulsante **Interrompi**, con cancellazione vera.
- Una sola run alla volta: un secondo avvio risponde 409.
- Elenco **Da decidere** con Approva, Rifiuta e link all'annuncio.

### Impostazioni

- Form a gruppi con tutti i campi di `settings.json`.
- Il paese si sceglie da un elenco; la valuta si aggiorna di conseguenza.
- Validazione al salvataggio, con gli errori accanto ai campi.
- Segreti in sola lettura, con il comando per impostarli.

### CV

- Caricamento `.pdf` o `.json`, massimo 5 MB. Il PDF viene estratto al caricamento.
- Form modificabile del CV strutturato, salvato in `cv.extracted.json`.
- Anteprima delle ricerche derivate.

### Storico

- Tabella delle run: data, modalità, stato, esiti per categoria, chiamate Adzuna e LLM.
- Dettaglio run: tabella degli annunci filtrabile per esito. Espandendo una riga compaiono
  competenze richieste e mancanti, avvisi, modalità di lavoro, link e messaggio. Approva e Rifiuta
  sui `Pending`.

## Messaggio di presentazione

- **Quando**: solo con Approva, oppure a richiesta con "Genera messaggio" su qualsiasi annuncio.
  Mai automatico per gli `Shortlisted`: ogni messaggio è una chiamata LLM in più.
- **Regole del prompt**:
  - fino a 3 punti concreti presenti **sia** nel CV **sia** nell'annuncio, anche nessuno se il testo
    non ne contiene;
  - se non ce ne sono, il messaggio resta generico e la UI lo segnala ("nessuna corrispondenza
    specifica trovata nell'estratto");
  - non dichiarare competenze assenti dal CV e non inventare numeri, aziende o contatti.
- **Configurabile**:
  - `opening`: vuota di default, e allora l'LLM scrive un'apertura neutra nella lingua scelta. Se
    compilata, viene usata con i segnaposti `{nome}`, `{ruolo}`, `{azienda}`;
  - `closing`: inserita letteralmente, perché l'LLM non genera mai contatti;
  - `tone` (formale, cordiale), `length` (breve ~100 parole, media ~180), `language` (quella
    dell'annuncio, oppure un codice lingua), `extraInstructions`;
  - anteprima su un annuncio dello storico.
- **In UI**: riquadro modificabile con Rigenera, Copia e Segna come inviata (`Applied`). Avviso fisso:
  *"basato sull'estratto dell'annuncio (500 caratteri): verificalo prima di inviarlo"*, oppure
  *"basato sul testo che hai incollato"* quando c'è il testo completo.
- **Salvataggio**: nel record del registro. Per gli annunci di una dry run, in `runs/<runId>.json`.
- **Invio**: nessuno, resta manuale.

## Testo completo fornito dall'utente

- Nel dettaglio di un annuncio (storico, `Pending`, dry run) il campo **"Incolla testo completo"**
  accetta il testo copiato dal browser, fino a 20.000 caratteri.
- Al salvataggio parte una **rivalutazione** su quel testo: normalizzazione, stage 1, giudizio.
  L'esito si aggiorna con le regole normali. Un annuncio già deciso dall'utente (`Approved`,
  `Rejected`, `Applied`) mostra la nuova valutazione ma non cambia esito.
- Il messaggio di presentazione generato o rigenerato dopo usa il testo completo.
- **Salvataggio** nel record dell'annuncio (nella run, per una dry run): testo, data e le due
  valutazioni (estratto e testo completo), marcato come **testo fornito dall'utente**.
- **In UI**: badge "testo completo fornito da te" sull'annuncio. L'avviso del messaggio diventa
  *"basato sul testo che hai incollato"*.
- Il testo incollato è non fidato come quello di Adzuna: va nel DOM solo come testo e nei prompt è
  delimitato.

## API web

| Metodo | Percorso | Scopo |
|---|---|---|
| GET/PUT | `/api/settings` | leggere/salvare le impostazioni (400 con errori per campo) |
| GET | `/api/secrets/status` | quali segreti sono presenti |
| GET/POST/PUT | `/api/cv` | leggere, caricare (multipart), salvare il CV strutturato |
| GET | `/api/cv/derived-queries` | anteprima delle ricerche dal CV |
| POST | `/api/runs` | avviare una run (`{ "mode": "dry" \| "normal" }`), 409 se ne esiste una in corso |
| GET | `/api/runs`, `/api/runs/{runId}` | storico e dettaglio |
| GET | `/api/runs/current/events` | server-sent events della run in corso |
| POST | `/api/runs/current/cancel` | interrompere |
| GET | `/api/pending` | annunci da decidere |
| POST | `/api/postings/{postingId}/decision` | `approve` \| `reject` \| `applied` |
| POST/PUT | `/api/postings/{postingId}/presentation` | generare o rigenerare / salvare una modifica |
| POST/PUT | `/api/runs/{runId}/postings/{postingId}/presentation` | idem, per annunci di dry run |
| POST | `/api/presentation/preview` | anteprima con impostazioni non ancora salvate |
| PUT | `/api/postings/{postingId}/full-text` | salvare il testo incollato e rivalutare |
| PUT | `/api/runs/{runId}/postings/{postingId}/full-text` | idem, per annunci di dry run |

## Sicurezza

- **Rete**: nel container Kestrel ascolta su `0.0.0.0:5080`; compose lo pubblica solo su
  `127.0.0.1:5080`. Nessun login in questa fase.
- **DNS rebinding**: `AllowedHosts: "localhost;127.0.0.1"`, quindi un `Host` diverso riceve 400.
- **Richieste da altri siti**: ogni richiesta che modifica dati (POST, PUT, DELETE, compreso il
  caricamento multipart) deve avere l'header `X-Jobbby-Request: 1`. Se ha un `Origin`, deve
  coincidere con quello dell'app, altrimenti 403. L'header custom forza il preflight CORS, che
  l'app non autorizza per altre origini: un form esterno "simple request" non può impostarlo.
- **Testo non fidato**: il testo degli annunci (e tutto ciò che arriva da Adzuna o dall'LLM) va
  nel DOM solo con `textContent`, mai con `innerHTML`.
- **Prompt**: il testo dell'annuncio è sempre delimitato (`<annuncio>…</annuncio>`), con
  l'istruzione di trattarlo come dati e non come istruzioni. Lo stesso vale per il CV. `claude -p`
  resta con `--tools ""`.
- **Caricamenti**: solo `.pdf` e `.json`, massimo 5 MB, nome file ignorato.
- I segreti non transitano mai per la UI o per le API.

## Gestione errori

- Ricerca Adzuna fallita (rete, 4xx/5xx, quota): registrata nella run, le altre proseguono.
- LLM fallito su un annuncio: l'annuncio risulta "errore" con il messaggio, gli altri proseguono.
- Estrazione del CV o messaggio falliti: errore mostrato in UI, nessun file sovrascritto.
- Arresto del processo durante una run: all'avvio successivo la run risulta `Interrupted`; registro
  e cursori contengono solo il lavoro completato.
- CLI con il `DataDir` bloccato: esce subito con codice diverso da 0 e il messaggio del lock.
- Nessun errore viene assorbito in silenzio.

## Ambiente

In `Tools\PersonalContainers` arriva un nuovo `compose.jobbby.yaml` con `127.0.0.1:5080:5080`, che
`Personal.ps1` aggiunge solo con `-Project jobbby`. Il `compose.yaml` condiviso non cambia.
L'avvio avviene con `dotnet run --project src/Web` nel container, con un'eventuale azione `-Action Web`.

## Test

- **Unitari**:
  - impostazioni: validazione e default neutri;
  - `PostingIdentity`: normalizzazione e id;
  - `SkillMatcher` senza alias e con `skill-aliases.json`;
  - stage 1: località con e senza `where`, `workMode`, retribuzione plausibile, anni di esperienza;
  - esiti: `AutoRejected` solo per stage 1 o `Weak`; Borderline con confidenza bassa → `Pending`;
    nessun requisito estratto → `Pending` "informazioni insufficienti";
  - prefiltro della ricerca remota (più lingue, accenti, nessuna parola configurata);
  - rivalutazione su testo incollato, compreso un annuncio già deciso;
  - chiave dei cursori e ricerca remota;
  - lock del `DataDir`;
  - migrazione;
  - cancellazione nel `GraphEngine`;
  - generatore del messaggio: nessun punto inventato, apertura vuota o con segnaposti, chiusura
    letterale.
- **Integrazione API** con `WebApplicationFactory`, fonte e LLM finti:
  - impostazioni, CV, run completa;
  - 409 su una run concorrente, cancellazione;
  - decisione, messaggio;
  - rifiuto senza header custom o con un `Origin` estraneo;
  - `Host` non consentito.
- **Profilo non tecnico**: fixture con un CV da infermiere, in un paese diverso dall'Italia, e un
  test end-to-end con fonte e LLM finti. Ricerche derivate, stage 1, valutazione e messaggio
  devono funzionare senza nessun concetto da sviluppatore.
- `src/Host/cv.json`, il placeholder C#, viene spostato tra le fixture dei test.
- **Verifica finale dal browser**: dry run reale con Adzuna e `claude-cli`, poi una run normale con
  decisione e messaggio.

## Consegna

Sei PR incrementali, dopo #8 (già su `main`):

1. **Impostazioni e runner**: `JobbbySettings`, `settings.json`, migrazione, `JobbbyRunner`, lock del
   `DataDir`. Adzuna con paese, zona, ricerca remota e cursori nuovi. Stage 1 con impostazioni,
   `workMode`, anni di esperienza, retribuzione solo lato client. Cancellazione nel `GraphEngine`.
2. **Neutralità di profilo**: competenze generiche al posto di "stack", alias in `DataDir`, prompt
   senza esempi di settore, suffissi societari neutri, fixture non tecnica e test end-to-end.
3. **Persistenza ed esiti**: `runs/`, `PostingIdentity` unificata, `PostingId`, record completo,
   `AutoRejected`/`Pending`. Include un comando CLI minimo per decidere
   (`dotnet run --project src/Host -- pending`, `-- decide <postingId> approve|reject|applied`),
   così la PR è utilizzabile da sola.
4. **UI web**: `src/Web` con Run, Impostazioni e Storico, sicurezza, override di compose.
5. **Pagina CV** con estrazione modificabile.
6. **Messaggio di presentazione e testo incollato**: generazione e configurazione del messaggio,
   campo "Incolla testo completo" con rivalutazione.

## Fuori scope

- Rimozione di Telegram.
- Run pianificate.
- Recupero automatico del testo completo (vedi "Sito adzuna.it").
- Altre fonti e crawler.
- Discovery nel runner.
- Login e deploy su server.
- Invio automatico delle candidature.
- Traduzione della UI: resta in italiano.
