"use strict";

// Jobbby UI. Everything that comes from postings, CVs or the LLM is untrusted text: it only ever
// reaches the page through textContent (see el()), never as HTML.

const GUARD = { "X-Jobbby-Request": "1" };

const OUTCOME_LABELS = {
  Pending: "Da decidere",
  Shortlisted: "Selezionato",
  AutoRejected: "Scartato",
  Approved: "Approvato",
  Rejected: "Rifiutato",
  Applied: "Inviato",
  Discovered: "Scaduto (Telegram)",
  Skipped: "Già visto",
  Failed: "Errore",
  Interrupted: "Interrotto",
};

const RUN_STATUS_LABELS = {
  Running: "In corso",
  Completed: "Completata",
  Failed: "Fallita",
  Interrupted: "Interrotta",
  running: "In corso",
  completed: "Completata",
  failed: "Fallita",
  interrupted: "Interrotta",
};

const COUNTRIES = [
  ["at", "Austria"], ["au", "Australia"], ["be", "Belgio"], ["br", "Brasile"], ["ca", "Canada"],
  ["ch", "Svizzera"], ["de", "Germania"], ["es", "Spagna"], ["fr", "Francia"], ["gb", "Regno Unito"],
  ["in", "India"], ["it", "Italia"], ["mx", "Messico"], ["nl", "Paesi Bassi"], ["nz", "Nuova Zelanda"],
  ["pl", "Polonia"], ["sg", "Singapore"], ["us", "Stati Uniti"], ["za", "Sudafrica"],
];

// ---------- small helpers

function el(tag, attributes, ...children) {
  const node = document.createElement(tag);
  for (const [name, value] of Object.entries(attributes || {})) {
    if (value === undefined || value === null || value === false) continue;
    if (name === "class") node.className = value;
    else if (name === "text") node.textContent = value;
    else if (name.startsWith("on")) node.addEventListener(name.slice(2), value);
    else if (value === true) node.setAttribute(name, "");
    else node.setAttribute(name, String(value));
  }
  for (const child of children.flat()) {
    if (child === null || child === undefined || child === false) continue;
    node.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
  return node;
}

function clear(node) {
  while (node.firstChild) node.removeChild(node.firstChild);
  return node;
}

async function api(method, url, body) {
  const options = { method, headers: { ...GUARD } };
  if (body !== undefined) {
    options.headers["Content-Type"] = "application/json";
    options.body = JSON.stringify(body);
  }
  const response = await fetch(url, options);
  const text = await response.text();
  const data = text ? JSON.parse(text) : null;
  if (!response.ok) {
    const error = new Error((data && (data.error || (data.errors || []).map(e => e.message).join(" "))) || `Errore ${response.status}`);
    error.status = response.status;
    error.data = data;
    throw error;
  }
  return data;
}

const get = url => api("GET", url);

function formatDate(value) {
  if (!value) return "";
  return new Date(value).toLocaleString("it-IT", { dateStyle: "short", timeStyle: "short" });
}

function formatConfidence(value) {
  return typeof value === "number" ? value.toFixed(2).replace(".", ",") : "";
}

function formatSalary(value) {
  return typeof value === "number" ? value.toLocaleString("it-IT") : "";
}

function outcomeBadge(outcome) {
  return el("span", { class: `outcome ${outcome || ""}`, text: OUTCOME_LABELS[outcome] || outcome || "" });
}

function notice(container, message, kind) {
  const node = el("div", { class: `notice ${kind || ""}`, role: kind === "error" ? "alert" : "status", text: message });
  container.prepend(node);
  setTimeout(() => node.remove(), kind === "error" ? 8000 : 4000);
}

function safeLink(url, label) {
  // Only http(s) links from postings are made clickable.
  if (typeof url === "string" && /^https?:\/\//i.test(url))
    return el("a", { href: url, target: "_blank", rel: "noopener noreferrer", text: label });
  return el("span", { class: "muted", text: url || "" });
}

// ---------- tabs

const loaders = {};

function showTab(name) {
  for (const tab of document.querySelectorAll(".tab")) {
    if (tab.dataset.tab === name) tab.setAttribute("aria-current", "page");
    else tab.removeAttribute("aria-current");
  }
  for (const page of document.querySelectorAll(".page"))
    page.hidden = page.id !== `tab-${name}`;
  history.replaceState(null, "", `#${name}`);
  loaders[name] && loaders[name]();
}

document.addEventListener("click", event => {
  const target = event.target.closest("[data-tab]");
  if (target) showTab(target.dataset.tab);
});

// ---------- pending

async function refreshPendingCount() {
  try {
    const pending = await get("/api/pending");
    const badge = document.getElementById("pending-count");
    badge.hidden = pending.length === 0;
    badge.textContent = pending.length === 1 ? "1 da decidere" : `${pending.length} da decidere`;
    return pending;
  } catch {
    return [];
  }
}

function postingCard(record, onDecided) {
  const container = document.createElement("div");
  const decide = async decision => {
    try {
      await api("POST", `/api/postings/${encodeURIComponent(record.postingId)}/decision`, { decision });
      onDecided();
    } catch (error) {
      notice(container, error.message, "error");
    }
  };

  const facts = [record.company, record.location, record.workMode && record.workMode !== "Unknown" ? workModeLabel(record.workMode) : null,
    record.salaryMaximum ? `retribuzione fino a ${formatSalary(record.salaryMaximum)}` : null]
    .filter(Boolean).join(", ");

  container.append(el("article", { class: "posting" },
    el("h3", { text: record.title }),
    el("div", { class: "meta", text: facts }),
    record.confidence !== undefined && record.confidence !== null
      ? el("p", {}, el("strong", { text: "Confidenza " }), formatConfidence(record.confidence), record.category ? ` (${record.category})` : "")
      : null,
    record.reasoning ? el("p", { text: record.reasoning }) : null,
    record.reason ? el("p", { class: "muted", text: record.reason }) : null,
    (record.requiredSkills || []).length ? el("p", { class: "muted", text: `Competenze richieste: ${record.requiredSkills.join(", ")}` }) : null,
    el("div", { class: "actions" },
      el("button", { type: "button", class: "primary", onclick: () => decide("approve"), text: "Approva" }),
      el("button", { type: "button", onclick: () => decide("reject"), text: "Rifiuta" }),
      safeLink(record.applyUrl || record.sourceUrl, "Apri annuncio"))));
  return container;
}

function workModeLabel(mode) {
  return { Onsite: "in sede", Hybrid: "ibrido", Remote: "da remoto" }[mode] || "";
}

async function renderPending() {
  const list = clear(document.getElementById("pending-list"));
  const pending = await refreshPendingCount();
  if (pending.length === 0) {
    list.append(el("p", { class: "muted", text: "Niente da decidere. Gli annunci sotto la soglia di selezione compaiono qui dopo una run." }));
    return;
  }
  for (const record of pending)
    list.append(postingCard(record, renderPending));
}

// ---------- run page

let eventSource = null;

async function renderRunPlan() {
  const plan = clear(document.getElementById("run-plan"));
  try {
    const [settings, cv] = await Promise.all([get("/api/settings"), get("/api/cv")]);
    const country = COUNTRIES.find(([code]) => code === settings.area.country);
    const where = settings.area.where
      ? `${settings.area.where}${settings.area.distanceKm ? ` (+${settings.area.distanceKm} km)` : ""}`
      : settings.area.whereFromCv ? "dal CV" : "tutto il paese";
    const queries = settings.searches.queries.length ? settings.searches.queries.join(", ") : "nessuna";
    plan.append(el("dl", {},
      el("dt", { text: "Paese" }), el("dd", { text: country ? country[1] : "non impostato" }),
      el("dt", { text: "Zona" }), el("dd", { text: where }),
      el("dt", { text: "Remoto" }), el("dd", { text: settings.area.acceptsRemote ? "accettato" : "no" }),
      el("dt", { text: "Ricerche" }), el("dd", { text: `${queries}${settings.searches.deriveFromCv ? `, più fino a ${settings.searches.maxDerivedQueries} dal CV` : ""}` }),
      el("dt", { text: "Retribuzione minima" }), el("dd", { text: settings.salary.minimumYearly ? formatSalary(settings.salary.minimumYearly) : "nessuna" }),
      el("dt", { text: "LLM" }), el("dd", { text: `${settings.llm.provider}${settings.llm.model ? ` (${settings.llm.model})` : ""}` }),
      el("dt", { text: "CV" }), el("dd", { text: cv.file || cv.message })));
  } catch (error) {
    notice(plan, error.message, "error");
  }
}

function setRunning(running) {
  document.getElementById("start-dry").disabled = running;
  document.getElementById("start-normal").disabled = running;
  document.getElementById("cancel-run").hidden = !running;
}

function showRunStatus(run) {
  const status = document.getElementById("run-status");
  if (!run) {
    status.textContent = "";
    return;
  }
  const label = `${run.mode === "dry" ? "Dry run" : "Run"} ${RUN_STATUS_LABELS[run.status] || run.status}`;
  status.textContent = run.error ? `${label}: ${run.error}` : label;
}

function appendEvent(runEvent) {
  const list = document.getElementById("run-events");
  list.append(el("li", { class: runEvent.kind, text: runEvent.message }));
  list.scrollTop = list.scrollHeight;
}

function followRun() {
  eventSource && eventSource.close();
  clear(document.getElementById("run-events"));
  eventSource = new EventSource("/api/runs/current/events");
  eventSource.onmessage = message => appendEvent(JSON.parse(message.data));
  eventSource.addEventListener("end", message => {
    eventSource.close();
    eventSource = null;
    const run = JSON.parse(message.data);
    showRunStatus(run);
    setRunning(false);
    renderPending();
  });
  eventSource.onerror = () => {
    // The stream closes at the end of a run; a broken one is re-read from the current state.
    if (eventSource) {
      eventSource.close();
      eventSource = null;
      loadRunPage();
    }
  };
}

async function startRun(mode) {
  if (mode === "normal" && !confirm("La run registra gli esiti e aggiorna i cursori. Avviarla?")) return;
  const page = document.getElementById("tab-run");
  try {
    const run = await api("POST", "/api/runs", { mode });
    showRunStatus(run);
    setRunning(true);
    followRun();
  } catch (error) {
    const messages = error.data && error.data.errors ? error.data.errors.map(e => e.message).join(" ") : error.message;
    notice(page, messages, "error");
  }
}

async function loadRunPage() {
  renderRunPlan();
  renderPending();
  const response = await fetch("/api/runs/current");
  if (response.status === 204) {
    showRunStatus(null);
    setRunning(false);
    return;
  }
  const run = await response.json();
  showRunStatus(run);
  const running = run.status === "running";
  setRunning(running);
  if (running) followRun();
  else {
    const list = clear(document.getElementById("run-events"));
    for (const runEvent of run.events || []) list.append(el("li", { class: runEvent.kind, text: runEvent.message }));
  }
}

document.getElementById("start-dry").addEventListener("click", () => startRun("dry"));
document.getElementById("start-normal").addEventListener("click", () => startRun("normal"));
document.getElementById("cancel-run").addEventListener("click", async () => {
  try {
    await api("POST", "/api/runs/current/cancel", {});
  } catch (error) {
    notice(document.getElementById("tab-run"), error.message, "error");
  }
});
loaders.run = loadRunPage;

// ---------- settings page

const lines = value => value.split("\n").map(line => line.trim()).filter(Boolean);
const numberOrNull = value => (value.trim() === "" ? null : Number(value));

function field(id, label, input, help) {
  return el("div", { class: "field", "data-field": id },
    el("label", { for: `f-${id}`, text: label }),
    input,
    help ? el("span", { class: "help", text: help }) : null,
    el("span", { class: "error", role: "alert" }));
}

function textInput(id, value, attributes) {
  return el("input", { id: `f-${id}`, type: "text", value: value ?? "", ...(attributes || {}) });
}

function numberInput(id, value, attributes) {
  return el("input", { id: `f-${id}`, type: "number", value: value ?? "", ...(attributes || {}) });
}

function checkbox(id, label, checked, help) {
  return el("div", { class: "field inline", "data-field": id },
    el("input", { id: `f-${id}`, type: "checkbox", checked: checked ? true : null }),
    el("label", { for: `f-${id}`, text: label }),
    help ? el("span", { class: "help", text: help }) : null,
    el("span", { class: "error", role: "alert" }));
}

function textArea(id, value) {
  const area = el("textarea", { id: `f-${id}` });
  area.value = value ?? "";
  return area;
}

function select(id, options, value) {
  const node = el("select", { id: `f-${id}` });
  for (const [optionValue, optionLabel] of options)
    node.append(el("option", { value: optionValue, text: optionLabel, selected: optionValue === value ? true : null }));
  return node;
}

function keywordsToText(keywords) {
  return Object.entries(keywords || {}).map(([language, words]) => `${language}: ${words.join(", ")}`).join("\n");
}

function textToKeywords(text) {
  const result = {};
  for (const line of lines(text)) {
    const [language, words] = line.includes(":") ? line.split(/:(.*)/s) : ["*", line];
    result[language.trim() || "*"] = words.split(",").map(word => word.trim()).filter(Boolean);
  }
  return result;
}

async function renderSettings() {
  const form = clear(document.getElementById("settings-form"));
  let settings;
  let secrets;
  try {
    [settings, secrets] = await Promise.all([get("/api/settings"), get("/api/secrets/status")]);
  } catch (error) {
    notice(form, error.message, "error");
    return;
  }
  const value = id => document.getElementById(`f-${id}`).value;
  const checked = id => document.getElementById(`f-${id}`).checked;

  form.append(
    el("fieldset", {}, el("legend", { text: "Ricerche" }),
      field("searches.queries", "Ricerche", textArea("searches.queries", settings.searches.queries.join("\n")), "Una per riga, come titoli di ruolo. Evita le sigle di due lettere: i motori di ricerca le ignorano."),
      checkbox("searches.deriveFromCv", "Aggiungi ricerche ricavate dal CV", settings.searches.deriveFromCv),
      field("searches.maxDerivedQueries", "Massimo ricerche dal CV", numberInput("searches.maxDerivedQueries", settings.searches.maxDerivedQueries, { min: 1, max: 10 }))),
    el("fieldset", {}, el("legend", { text: "Zona" }),
      field("area.country", "Paese", select("area.country", [["", "Scegli il paese"], ...COUNTRIES], settings.area.country || "")),
      field("area.where", "Località", textInput("area.where", settings.area.where), "Vuota: si usa la località del CV, se l'opzione qui sotto è attiva, altrimenti tutto il paese."),
      checkbox("area.whereFromCv", "Usa la località del CV se la località è vuota", settings.area.whereFromCv),
      field("area.distanceKm", "Raggio (km)", numberInput("area.distanceKm", settings.area.distanceKm, { min: 0 })),
      checkbox("area.acceptsRemote", "Accetto lavori da remoto", settings.area.acceptsRemote),
      field("remoteSweep.keywords", "Parole chiave per la ricerca remota", textArea("remoteSweep.keywords", keywordsToText(settings.remoteSweep.keywords)),
        "Una riga per lingua, per esempio \"it: da remoto, smart working\". Senza parole chiave la ricerca remota fuori zona resta spenta.")),
    el("fieldset", {}, el("legend", { text: "Retribuzione" }),
      field("salary.minimumYearly", "Retribuzione annua minima", numberInput("salary.minimumYearly", settings.salary.minimumYearly, { min: 0 }), "Esclude solo gli annunci con retribuzione indicata e più bassa. Vuota: nessun filtro."),
      field("salary.minimumPlausible", "Soglia di plausibilità", numberInput("salary.minimumPlausible", settings.salary.minimumPlausible, { min: 0 }), "Sotto questo valore una retribuzione non è considerata annua ed è trattata come sconosciuta.")),
    el("fieldset", {}, el("legend", { text: "Valutazione" }),
      field("evaluation.autoApproveThreshold", "Soglia di selezione automatica", numberInput("evaluation.autoApproveThreshold", settings.evaluation.autoApproveThreshold, { min: 0, max: 1, step: 0.05 }), "Da 0 a 1. Sotto la soglia gli annunci compatibili restano da decidere."),
      field("dryRun.maxPostingsPerQuery", "Annunci per ricerca nella dry run", numberInput("dryRun.maxPostingsPerQuery", settings.dryRun.maxPostingsPerQuery, { min: 1, max: 20 })),
      field("dedupe.extraCompanySuffixes", "Suffissi aziendali aggiuntivi", textArea("dedupe.extraCompanySuffixes", settings.dedupe.extraCompanySuffixes.join("\n")), "Uno per riga, per riconoscere la stessa azienda scritta in modi diversi.")),
    el("fieldset", {}, el("legend", { text: "LLM" }),
      field("llm.provider", "Provider", select("llm.provider", [["claude-cli", "Claude Code (claude -p)"], ["openai", "Endpoint compatibile OpenAI"]], settings.llm.provider)),
      field("llm.model", "Modello", textInput("llm.model", settings.llm.model))),
    el("fieldset", {}, el("legend", { text: "Segreti" }),
      el("p", { class: "hint", text: "I segreti non passano dalla UI. Si impostano nel container con dotnet user-secrets, dalla cartella src/Host." }),
      el("ul", { class: "secrets" }, Object.entries(secrets).map(([key, present]) =>
        el("li", {}, el("code", { text: key }), " ", el("span", { class: present ? "ok" : "missing", text: present ? "impostato" : "mancante" }))))),
    el("div", { class: "actions" }, el("button", { type: "submit", class: "primary", text: "Salva impostazioni" })));

  form.onsubmit = async event => {
    event.preventDefault();
    for (const node of form.querySelectorAll(".error")) node.textContent = "";
    for (const node of form.querySelectorAll("[aria-invalid]")) node.removeAttribute("aria-invalid");
    const updated = {
      ...settings,
      searches: { queries: lines(value("searches.queries")), deriveFromCv: checked("searches.deriveFromCv"), maxDerivedQueries: Number(value("searches.maxDerivedQueries")) },
      area: {
        country: value("area.country") || null,
        where: value("area.where").trim(),
        whereFromCv: checked("area.whereFromCv"),
        distanceKm: numberOrNull(value("area.distanceKm")),
        acceptsRemote: checked("area.acceptsRemote"),
      },
      salary: { minimumYearly: numberOrNull(value("salary.minimumYearly")), minimumPlausible: Number(value("salary.minimumPlausible")) },
      evaluation: { autoApproveThreshold: Number(value("evaluation.autoApproveThreshold")) },
      llm: { provider: value("llm.provider"), model: value("llm.model").trim() || null },
      dryRun: { maxPostingsPerQuery: Number(value("dryRun.maxPostingsPerQuery")) },
      remoteSweep: { keywords: textToKeywords(value("remoteSweep.keywords")) },
      dedupe: { extraCompanySuffixes: lines(value("dedupe.extraCompanySuffixes")) },
    };
    try {
      settings = await api("PUT", "/api/settings", updated);
      notice(form, "Impostazioni salvate.", "success");
    } catch (error) {
      const errors = (error.data && error.data.errors) || [];
      for (const { field: name, message } of errors) {
        const container = form.querySelector(`[data-field="${CSS.escape(name)}"]`);
        if (container) {
          container.querySelector(".error").textContent = message;
          const input = container.querySelector("input, select, textarea");
          input && input.setAttribute("aria-invalid", "true");
        }
      }
      notice(form, errors.length ? "Controlla i campi evidenziati." : error.message, "error");
    }
  };
}
loaders.settings = renderSettings;

// ---------- CV page (read only until CV editing arrives)

async function renderCv() {
  const view = clear(document.getElementById("cv-view"));
  try {
    const cv = await get("/api/cv");
    view.append(cv.file
      ? el("p", {}, "Le run usano ", el("code", { text: cv.file }), `, aggiornato il ${formatDate(cv.updatedAt)}.`)
      : el("p", { text: cv.message }));
    view.append(el("p", { class: "hint", text: "Il caricamento del CV e la modifica dei dati estratti arrivano con la prossima versione." }));
  } catch (error) {
    notice(view, error.message, "error");
  }
}
loaders.cv = renderCv;

// ---------- history page

async function renderHistory() {
  const list = clear(document.getElementById("history-list"));
  clear(document.getElementById("history-detail"));
  let runs;
  try {
    runs = await get("/api/runs");
  } catch (error) {
    notice(list, error.message, "error");
    return;
  }
  if (runs.length === 0) {
    list.append(el("p", { class: "muted", text: "Nessuna run ancora. Avviane una dalla sezione Run." }));
    return;
  }

  const table = el("table", {},
    el("thead", {}, el("tr", {},
      el("th", { text: "Data" }), el("th", { text: "Tipo" }), el("th", { text: "Stato" }), el("th", { class: "hide-narrow", text: "Zona" }),
      el("th", { class: "num", text: "Trovati" }), el("th", { class: "num", text: "Selezionati" }), el("th", { class: "num", text: "Da decidere" }),
      el("th", { class: "num", text: "Scartati" }), el("th", { class: "num hide-narrow", text: "Chiamate" }))));
  const body = el("tbody");
  for (const run of runs) {
    const report = run.report || {};
    body.append(el("tr", { class: "clickable", tabindex: 0, onclick: () => renderRunDetail(run.runId), onkeydown: e => e.key === "Enter" && renderRunDetail(run.runId) },
      el("td", { text: formatDate(run.startedAt) }),
      el("td", { text: { dry: "Dry run", normal: "Run", legacy: "Storica" }[run.mode] || run.mode }),
      el("td", {}, el("span", { class: `outcome ${run.status}`, text: RUN_STATUS_LABELS[run.status] || run.status })),
      el("td", { class: "hide-narrow", text: run.where || "" }),
      el("td", { class: "num", text: report.totalFetched ?? "" }),
      el("td", { class: "num", text: report.autoApproved ?? "" }),
      el("td", { class: "num", text: report.pending ?? "" }),
      el("td", { class: "num", text: report.autoRejected ?? "" }),
      el("td", { class: "num hide-narrow", text: run.adzunaCalls ?? "" })));
  }
  table.append(body);
  list.append(table);
}

async function renderRunDetail(runId) {
  const detail = clear(document.getElementById("history-detail"));
  let data;
  try {
    data = await get(`/api/runs/${encodeURIComponent(runId)}`);
  } catch (error) {
    notice(detail, error.message, "error");
    return;
  }
  const { run, postings } = data;
  const outcomeOf = item => item.currentOutcome || (item.posting.record && item.posting.record.outcome) || item.posting.status;

  detail.append(el("h2", { text: `${run.mode === "dry" ? "Dry run" : "Run"} del ${formatDate(run.startedAt)}` }));
  if (run.error) detail.append(el("div", { class: "notice error", text: run.error }));
  for (const warning of run.warnings || []) detail.append(el("p", { class: "muted", text: warning }));
  if ((run.queries || []).length) detail.append(el("p", { class: "muted", text: `Ricerche: ${run.queries.join(", ")}` }));

  const outcomes = [...new Set(postings.map(outcomeOf))];
  const filter = select("history-filter", [["", "Tutti gli esiti"], ...outcomes.map(o => [o, OUTCOME_LABELS[o] || o])], "");
  detail.append(el("div", { class: "toolbar" }, el("label", { for: "f-history-filter", text: "Mostra" }), filter));

  const table = el("table", {},
    el("thead", {}, el("tr", {},
      el("th", { text: "Esito" }), el("th", { class: "num", text: "Conf." }), el("th", { text: "Annuncio" }),
      el("th", { class: "hide-narrow", text: "Località" }), el("th", { class: "num hide-narrow", text: "Retribuzione" }), el("th", { text: "Motivazione" }))));
  const body = el("tbody");
  table.append(body);
  detail.append(table);

  const draw = () => {
    clear(body);
    const shown = postings.filter(item => !filter.value || outcomeOf(item) === filter.value);
    if (shown.length === 0) body.append(el("tr", {}, el("td", { colspan: 6, class: "muted", text: "Nessun annuncio." })));
    for (const item of shown) {
      const record = item.currentRecord || item.posting.record || {};
      const row = el("tr", { class: "clickable", tabindex: 0 },
        el("td", {}, outcomeBadge(outcomeOf(item))),
        el("td", { class: "num", text: formatConfidence(record.confidence) }),
        el("td", {}, el("strong", { text: item.posting.title }), el("div", { class: "muted", text: item.posting.company })),
        el("td", { class: "hide-narrow", text: record.location || "" }),
        el("td", { class: "num hide-narrow", text: formatSalary(record.salaryMaximum) }),
        el("td", { text: record.reasoning || item.posting.summary || item.posting.error || "" }));
      const toggle = () => {
        const next = row.nextElementSibling;
        if (next && next.classList.contains("detail-row")) {
          next.remove();
          row.classList.remove("expanded");
          return;
        }
        row.classList.add("expanded");
        row.after(postingDetailRow(item, record, () => renderRunDetail(runId)));
      };
      row.addEventListener("click", toggle);
      row.addEventListener("keydown", e => e.key === "Enter" && toggle());
      body.append(row);
    }
  };
  filter.addEventListener("change", draw);
  draw();
  detail.scrollIntoView({ behavior: "smooth", block: "start" });
}

function postingDetailRow(item, record, onDecided) {
  const facts = [
    ["Competenze richieste", (record.requiredSkills || []).join(", ")],
    ["Requisiti mancanti", (record.missingRequirements || []).join("; ")],
    ["Avvisi", (record.warnings || []).join("; ")],
    ["Modalità", workModeLabel(record.workMode)],
    ["Anni richiesti", record.minYearsExperience ?? ""],
    ["Motivo dell'esito", record.reason || item.posting.summary || ""],
    ["Estratto", record.excerpt || ""],
  ].filter(([, text]) => text !== "" && text !== undefined && text !== null);

  const cell = el("td", { colspan: 6 },
    el("dl", {}, facts.flatMap(([label, text]) => [el("dt", { text: label }), el("dd", { text: String(text) })])),
    el("div", { class: "actions" }, safeLink(item.posting.applyUrl, "Apri annuncio")));

  if (item.currentOutcome === "Pending" && item.currentPostingId) {
    const decide = async decision => {
      try {
        await api("POST", `/api/postings/${encodeURIComponent(item.currentPostingId)}/decision`, { decision });
        refreshPendingCount();
        onDecided();
      } catch (error) {
        notice(cell, error.message, "error");
      }
    };
    cell.querySelector(".actions").prepend(
      el("button", { type: "button", class: "primary", onclick: () => decide("approve"), text: "Approva" }),
      el("button", { type: "button", onclick: () => decide("reject"), text: "Rifiuta" }));
  }
  return el("tr", { class: "detail-row" }, cell);
}
loaders.history = renderHistory;

// ---------- start

showTab((location.hash || "#run").slice(1) in loaders ? (location.hash || "#run").slice(1) : "run");
refreshPendingCount();
