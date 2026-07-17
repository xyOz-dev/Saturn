/* Saturn web UI */
"use strict";

/* ---------- helpers ---------- */

const $ = (sel) => document.querySelector(sel);
const $$ = (sel) => Array.from(document.querySelectorAll(sel));

// Injected by the server when it renders index.html; every API call must carry it.
const API_TOKEN = document.querySelector('meta[name="saturn-token"]')?.content ?? "";

const api = {
  async request(method, path, body) {
    let res;
    try {
      res = await fetch(`/api${path}`, {
        method,
        headers: {
          "X-Saturn-Token": API_TOKEN,
          ...(body ? { "Content-Type": "application/json" } : {}),
        },
        body: body ? JSON.stringify(body) : undefined,
      });
    } catch {
      throw new Error("can't reach the Saturn server — it may be restarting");
    }
    if (res.status === 401) {
      // The server restarted and minted a new token; reload to pick it up.
      window.location.reload();
      throw new Error("session expired — reloading");
    }
    if (!res.ok) {
      let message = `${res.status} ${res.statusText}`;
      try {
        const data = await res.json();
        message = data.error || data.detail || message;
      } catch { /* not json */ }
      const err = new Error(message);
      err.status = res.status;
      throw err;
    }
    if (res.status === 204 || res.status === 202) return null;
    const text = await res.text();
    return text ? JSON.parse(text) : null;
  },
  get: (p) => api.request("GET", p),
  post: (p, b) => api.request("POST", p, b ?? {}),
  patch: (p, b) => api.request("PATCH", p, b),
  put: (p, b) => api.request("PUT", p, b),
  del: (p) => api.request("DELETE", p),
};

function esc(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function fmtDuration(totalSeconds) {
  const s = Math.max(0, Math.floor(totalSeconds));
  if (s < 60) return `${s}s`;
  if (s < 3600) return `${Math.floor(s / 60)}m ${s % 60}s`;
  return `${Math.floor(s / 3600)}h ${Math.floor((s % 3600) / 60)}m`;
}

function fmtTime(iso) {
  return new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
}

/* ---------- markdown + math ---------- */

marked.use({ gfm: true, breaks: true });

// Math is pulled out before markdown parsing (so `_`, `\` and `*` inside
// formulas are not treated as emphasis), skipping code spans/fences, then
// rendered by KaTeX into private-use-area placeholders after sanitization.
function protectMath(segment, store) {
  const pattern = /\$\$([\s\S]+?)\$\$|\\\[([\s\S]+?)\\\]|\\\((.+?)\\\)|\$([^$\n]+?)\$/g;
  return segment.replace(pattern, (match, dollars, brackets, parens, inline) => {
    const body = dollars ?? brackets ?? parens ?? inline;
    const display = dollars !== undefined || brackets !== undefined;
    if (inline !== undefined) {
      // Guard against currency and loose dollar signs: "$5 and $10".
      if (/^\s|\s$/.test(inline) || /^[\d.,]+$/.test(inline.trim())) return match;
    }
    store.push({ body, display });
    return `\uE000${store.length - 1}\uE001`;
  });
}

function extractMath(src, store) {
  const codePattern = /(```[\s\S]*?(?:```|$)|~~~[\s\S]*?(?:~~~|$)|`[^`\n]*`)/g;
  return src
    .split(codePattern)
    .map((seg, i) => (i % 2 === 1 ? seg : protectMath(seg, store)))
    .join("");
}

function renderMarkdown(target, text) {
  const math = [];
  const src = extractMath(text ?? "", math);
  let html = DOMPurify.sanitize(marked.parse(src));

  html = html.replace(/\uE000(\d+)\uE001/g, (m, i) => {
    const item = math[Number(i)];
    if (!item) return "";
    try {
      return katex.renderToString(item.body, { displayMode: item.display, throwOnError: false });
    } catch {
      return esc(item.body);
    }
  });

  target.innerHTML = html;

  target.querySelectorAll("a").forEach((a) => {
    a.target = "_blank";
    a.rel = "noopener";
  });

  target.querySelectorAll("pre").forEach((pre) => {
    const code = pre.querySelector("code");
    const lang = [...(code?.classList || [])].find((c) => c.startsWith("language-"))?.slice(9) || "code";
    const wrap = document.createElement("div");
    wrap.className = "codeblock";
    pre.replaceWith(wrap);
    const head = document.createElement("div");
    head.className = "codeblock-head";
    const label = document.createElement("span");
    label.textContent = lang;
    const copy = document.createElement("button");
    copy.className = "codeblock-copy";
    copy.textContent = "copy";
    copy.addEventListener("click", () => {
      navigator.clipboard.writeText(code?.textContent || "").then(() => {
        copy.textContent = "copied";
        setTimeout(() => (copy.textContent = "copy"), 1200);
      });
    });
    head.append(label, copy);
    wrap.append(head, pre);
  });

  return target;
}

function toast(html, ms = 3500) {
  const el = document.createElement("div");
  el.className = "toast";
  el.innerHTML = html;
  $("#toasts").appendChild(el);
  setTimeout(() => {
    el.classList.add("out");
    setTimeout(() => el.remove(), 350);
  }, ms);
}

// Approval toasts persist until the request is resolved (here, in the
// Approvals view, from another client, or by timeout) — never auto-dismiss.
function approvalToast(a) {
  removeApprovalToast(a.id);
  const el = document.createElement("div");
  el.className = "toast approval-toast";
  el.dataset.approvalId = a.id;
  el.innerHTML = `
    <div class="toast-title">⚑ Approval needed${a.agentName ? ` · ${esc(a.agentName)}` : ""}</div>
    <div class="toast-body">${esc(a.title)}</div>
    ${a.command ? `<div class="toast-command">${esc(a.command.slice(0, 160))}</div>` : ""}
    <div class="toast-actions">
      <button class="btn sm primary" data-act="approve">Approve</button>
      <button class="btn sm danger" data-act="deny">Deny</button>
      <button class="btn ghost sm" data-act="view">View</button>
    </div>`;
  el.querySelector('[data-act="approve"]').addEventListener("click", () => resolveApproval(a.id, true));
  el.querySelector('[data-act="deny"]').addEventListener("click", () => resolveApproval(a.id, false));
  el.querySelector('[data-act="view"]').addEventListener("click", () => {
    removeApprovalToast(a.id);
    showView("approvals");
  });
  $("#toasts").appendChild(el);
}

function removeApprovalToast(id) {
  $$(`.approval-toast[data-approval-id="${CSS.escape(id)}"]`).forEach((el) => {
    el.classList.add("out");
    setTimeout(() => el.remove(), 350);
  });
}

/* ---------- state ---------- */

const state = {
  view: "orchestrator",
  workTab: "queue",
  chatWindow: 20,
  overview: null,
  agents: [],
  tasks: { running: [], completed: [] },
  todos: [],
  todoFilter: "all",
  todoScope: "all",
  todoBoard: null,
  sessions: [],
  approvals: [],
  transcript: [],
  orchestratorBusy: false,
  models: [],
  activity: [],
  agentSearch: "",
  settings: null,
};

/* ---------- view switching ---------- */

const VIEW_TITLES = {
  overview: "Overview",
  agents: "Agents",
  work: "Work",
  orchestrator: "Orchestrator",
  sessions: "Sessions",
  approvals: "Approvals",
  settings: "Settings",
};

const WORK_TABS = ["queue", "running", "history", "schedule"];

function showView(name) {
  state.view = name;
  $$(".nav-item").forEach((b) => b.classList.toggle("active", b.dataset.view === name));
  $$(".view").forEach((v) => v.classList.toggle("active", v.id === `view-${name}`));
  $("#view-title").textContent = VIEW_TITLES[name] || name;
  updateApprovalBanner();
  syncHash();
  refreshView(name);
}

async function refreshView(name) {
  try {
    if (name === "overview") await loadOverview();
    else if (name === "agents") await loadAgents();
    else if (name === "work") await Promise.all([loadTodos(), loadTasks(), loadWakes()]);
    else if (name === "orchestrator") await Promise.all([loadTranscript(), loadAgents(), loadTasks(), loadTodos()]);
    else if (name === "sessions") await loadSessions();
    else if (name === "approvals") await loadApprovals();
    else if (name === "settings") await loadSettings();
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
}

/* ---------- routing ---------- */

// The view (and work sub-tab) live in the URL hash — #/agents, #/work/running —
// so refresh keeps your place and views are linkable.
function currentRoute() {
  const parts = location.hash.replace(/^#\/?/, "").split("/");
  const view = VIEW_TITLES[parts[0]] ? parts[0] : "orchestrator";
  const sub = view === "work" && WORK_TABS.includes(parts[1]) ? parts[1] : null;
  return { view, sub };
}

function syncHash() {
  const path = state.view === "work" ? `work/${state.workTab}` : state.view;
  if (location.hash !== `#/${path}`) location.hash = `/${path}`;
}

window.addEventListener("hashchange", () => {
  const { view, sub } = currentRoute();
  if (view !== state.view) showView(view);
  if (sub && sub !== state.workTab) showWorkTab(sub);
});

/* ---------- keyboard shortcuts ---------- */

// 1-7 jump between views (nav order); "/" focuses the view's main input.
// Disabled while typing or while a modal/drawer is open.
const VIEW_ORDER = ["orchestrator", "overview", "agents", "work", "sessions", "approvals", "settings"];

document.addEventListener("keydown", (e) => {
  if (e.ctrlKey || e.metaKey || e.altKey) return;
  const t = e.target;
  if (t.matches?.("input, textarea, select") || t.isContentEditable) return;
  if (!$("#modal-backdrop").hidden || !$("#drawer-backdrop").hidden) return;

  const idx = Number(e.key) - 1;
  if (e.key >= "1" && e.key <= "7" && VIEW_ORDER[idx]) {
    showView(VIEW_ORDER[idx]);
    return;
  }
  if (e.key === "/") {
    const target =
      state.view === "orchestrator" ? $("#chat-text")
      : state.view === "agents" ? $("#agent-search")
      : state.view === "work" ? $("#todo-title")
      : null;
    // offsetParent is null for inputs inside a hidden work pane.
    if (target && target.offsetParent !== null) {
      e.preventDefault();
      target.focus();
    }
  }
});

$$(".nav-item").forEach((b) => b.addEventListener("click", () => showView(b.dataset.view)));
$$("[data-goto]").forEach((el) =>
  el.addEventListener("click", () => {
    showView(el.dataset.goto);
    if (el.dataset.gotoTab) showWorkTab(el.dataset.gotoTab);
  })
);

/* ---------- activity feed ---------- */

function logActivity(html) {
  state.activity.unshift({ time: new Date(), html });
  state.activity = state.activity.slice(0, 120);
  renderActivity();
  renderRail();
}

function renderActivity() {
  $("#activity-feed").innerHTML = state.activity
    .map((a) => `<li><span class="activity-time">${a.time.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" })}</span><span class="activity-text">${a.html}</span></li>`)
    .join("");
}

$("#clear-activity").addEventListener("click", () => {
  state.activity = [];
  renderActivity();
});

const decisionLog = [];
function logDecision(html) {
  decisionLog.unshift({ time: new Date(), html });
  if (decisionLog.length > 50) decisionLog.pop();
  const el = $("#decision-log");
  if (el) {
    el.innerHTML = decisionLog
      .map((d) => `<li><span class="activity-time">${d.time.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" })}</span><span class="activity-text">${d.html}</span></li>`)
      .join("");
  }
}

/* ---------- overview ---------- */

async function loadOverview() {
  const o = await api.get("/overview");
  state.overview = o;

  $("#stat-agents").textContent = o.agents.total;
  $("#stat-agents-sub").textContent = `${o.agents.working} working · max ${o.agents.max}`;
  $("#stat-running").textContent = o.tasks.running;
  $("#stat-tasks-sub").textContent = `${o.tasks.completed} done · ${o.tasks.failed} failed`;
  $("#stat-todos").textContent = o.todos.open;
  $("#stat-todos-sub").textContent = `${o.todos.done} completed`;
  $("#stat-approvals").textContent = o.pendingApprovals;
  $("#stat-orch-sub").textContent = o.orchestratorBusy ? "orchestrator working" : "orchestrator idle";

  const pct = o.agents.max > 0 ? (o.agents.total / o.agents.max) * 100 : 0;
  $("#capacity-fill").style.width = `${Math.min(100, pct)}%`;
  $("#capacity-label").textContent = `${o.agents.total} / ${o.agents.max} agents`;

  $("#provider-chip").textContent = `${o.provider} · ${o.model}`;
  $("#model-chip").textContent = o.model || "no model";
  state.uptimeBase = o.uptimeSeconds;
  state.uptimeAt = Date.now();

  updateBadges();
}

function updateBadges() {
  const o = state.overview;
  if (!o) return;
  setBadge("#badge-agents", o.agents.total);
  setBadge("#badge-work", o.todos.open);
  setBadge("#badge-approvals", o.pendingApprovals);
  $("#workcount-queue").textContent = o.todos.open || "";
  $("#workcount-running").textContent = o.tasks.running || "";
  updateApprovalBanner();
  renderRail();
}

// Live status rail beside the orchestrator chat: enough situational awareness
// to command without tabbing away. Each section links to its full view.
function renderRail() {
  if (state.view !== "orchestrator") return;
  const o = state.overview;
  if (o) {
    $("#rail-agents").textContent = o.agents.total;
    $("#rail-running").textContent = o.tasks.running;
    const approvals = $("#rail-approvals");
    approvals.textContent = o.pendingApprovals;
    approvals.classList.toggle("warn", o.pendingApprovals > 0);
  }
  $("#rail-agent-list").innerHTML = state.agents
    .slice(0, 8)
    .map((a) => `<li title="${esc(a.purpose)}"><span class="status-dot ${a.currentTask ? "working" : ""}"></span><span>${esc(a.name)}</span></li>`)
    .join("") || '<li class="rail-empty">no agents</li>';
  $("#rail-task-list").innerHTML = state.tasks.running
    .slice(0, 6)
    .map((t) => `<li title="${esc(t.description)}"><span class="status-dot working"></span><span>${esc(t.description)}</span></li>`)
    .join("") || '<li class="rail-empty">idle</li>';
  const openTodos = state.todos.filter((t) => !isTerminal(t.status));
  $("#rail-queue").textContent = state.overview?.todos.open ?? openTodos.length;
  $("#rail-queue-list").innerHTML = openTodos
    .slice(0, 6)
    .map((t) => `<li title="${esc(t.title)}"><span class="rail-pri ${esc(t.priority)}"></span><span>${esc(t.title)}</span></li>`)
    .join("") || '<li class="rail-empty">nothing queued</li>';
  $("#rail-activity").innerHTML = state.activity
    .slice(0, 10)
    .map((a) => `<li>${a.html}</li>`)
    .join("") || '<li class="rail-empty">quiet so far</li>';
}

// Pending approvals block agents mid-run, so they get a persistent banner
// on every view except Approvals itself (where the cards are already visible).
function updateApprovalBanner() {
  const n = state.overview?.pendingApprovals || 0;
  const show = n > 0 && state.view !== "approvals";
  $("#approval-banner").hidden = !show;
  if (show) {
    $("#approval-banner-text").textContent = `${n} ${n === 1 ? "request is" : "requests are"} waiting for your approval`;
  }
}

$("#approval-banner-btn").addEventListener("click", () => showView("approvals"));

function setBadge(sel, value) {
  const el = $(sel);
  el.textContent = value;
  el.classList.toggle("hidden", !value);
}

setInterval(() => {
  if (state.uptimeBase != null) {
    const up = state.uptimeBase + (Date.now() - state.uptimeAt) / 1000;
    $("#uptime-chip").textContent = `up ${fmtDuration(up)}`;
  }
}, 1000);

async function loadWakes() {
  try {
    const wakes = await api.get("/wake");
    $("#wake-list").innerHTML = wakes.length === 0
      ? `<li class="hint" style="padding:6px 2px;border:none">Nothing queued — the orchestrator is caught up.</li>`
      : wakes
          .map(
            (w) => `
            <li>
              <span class="activity-time">${esc(w.kind)}</span>
              <span class="activity-text">${esc(w.prompt.slice(0, 90))}</span>
              <button class="todo-del" data-wake-del="${esc(w.id)}" title="drop this wake">✕</button>
            </li>`
          )
          .join("");
    $$("[data-wake-del]").forEach((b) =>
      b.addEventListener("click", async () => {
        await api.del(`/wake/${b.dataset.wakeDel}`);
        await loadWakes();
      })
    );
  } catch { /* panel is best-effort */ }
}

/* ---------- agents ---------- */

async function loadAgents() {
  state.agents = await api.get("/agents");
  renderAgents();
  renderRail();
}

function renderAgents() {
  const filter = state.agentSearch.toLowerCase();
  const agents = state.agents.filter(
    (a) =>
      !filter ||
      a.name.toLowerCase().includes(filter) ||
      a.purpose.toLowerCase().includes(filter) ||
      a.agentId.toLowerCase().includes(filter)
  );

  $("#agents-empty").classList.toggle("show", state.agents.length === 0);

  $("#agent-grid").innerHTML = agents
    .map((a, i) => {
      const working = !!a.currentTask;
      const statusClass = working ? "working" : a.status === "Error" ? "error" : "";
      return `
      <div class="agent-card ${working ? "working" : ""}" style="animation-delay:${Math.min(i * 0.03, 0.4)}s">
        <div class="agent-head">
          <span class="status-dot ${statusClass}"></span>
          <span class="agent-name" title="${esc(a.name)}">${esc(a.name)}</span>
          <span class="agent-id">${esc(a.agentId)}</span>
        </div>
        <div class="agent-purpose" title="${esc(a.purpose)}">${esc(a.purpose)}</div>
        <div class="agent-meta">
          <span>${esc(a.status)}</span>
          <span>${esc(a.model)}</span>
          <span data-created="${esc(a.createdAt)}">${fmtDuration(a.runningSeconds)}</span>
        </div>
        ${a.currentTask ? `<div class="agent-task" title="${esc(a.currentTask.description)}">${esc(a.currentTask.description)}</div>` : ""}
        <div class="agent-actions">
          <button class="btn sm" data-handoff="${esc(a.agentId)}" ${working ? "disabled" : ""}>Hand off</button>
          <button class="btn sm" data-log="${esc(a.agentId)}" title="open this agent's transcript">Log</button>
          <button class="btn sm danger" data-terminate="${esc(a.agentId)}">Terminate</button>
        </div>
      </div>`;
    })
    .join("");

  $$("#agent-grid [data-handoff]").forEach((b) =>
    b.addEventListener("click", () => openHandoffModal(b.dataset.handoff))
  );
  $$("#agent-grid [data-log]").forEach((b) =>
    b.addEventListener("click", () => openAgentTranscript(b.dataset.log))
  );
  $$("#agent-grid [data-terminate]").forEach((b) =>
    b.addEventListener("click", async () => {
      try {
        await api.del(`/agents/${b.dataset.terminate}`);
        toast(`Terminated <b>${esc(b.dataset.terminate)}</b>`);
        await Promise.all([loadAgents(), loadOverview()]);
      } catch (err) {
        toast(`<b>Error:</b> ${esc(err.message)}`);
      }
    })
  );
}

$("#agent-search").addEventListener("input", (e) => {
  state.agentSearch = e.target.value;
  renderAgents();
});

// Sessions come back newest-first, so the first match is the agent's
// latest conversation.
async function openAgentTranscript(agentId) {
  const agent = state.agents.find((a) => a.agentId === agentId);
  if (!agent) return;
  try {
    const sessions = await api.get("/sessions?limit=100");
    const session = sessions.find((s) => s.agentName === agent.name);
    if (!session) {
      toast(`No transcript for <b>${esc(agent.name)}</b> yet — it hasn't exchanged any messages`);
      return;
    }
    await openSessionDrawer(session);
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
}

async function terminateAll() {
  const ok = await confirmModal(
    "Terminate all agents",
    "Terminates every agent and clears completed tasks. Anything still running is lost.",
    "Terminate all"
  );
  if (!ok) return;
  try {
    const r = await api.post("/agents/terminate-all");
    toast(`Terminated <b>${r.terminated}</b> agents`);
    await Promise.all([loadAgents(), loadOverview()]);
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
}

$("#btn-terminate-all").addEventListener("click", terminateAll);
$("#qa-terminate-all").addEventListener("click", terminateAll);

/* ---------- modals ---------- */

function openModal(title, bodyHtml) {
  $("#modal-title").textContent = title;
  $("#modal-body").innerHTML = bodyHtml;
  $("#modal-backdrop").hidden = false;
}

// Set by confirmModal so dismissing any way (✕, backdrop, Escape) counts as "no".
let onModalDismiss = null;

function closeModal() {
  $("#modal-backdrop").hidden = true;
  $("#modal-body").innerHTML = "";
  const cb = onModalDismiss;
  onModalDismiss = null;
  if (cb) cb();
}

function confirmModal(title, body, confirmLabel = "Confirm", danger = true) {
  return new Promise((resolve) => {
    openModal(
      title,
      `
      <p style="font-size:13px;color:var(--muted);margin:0">${body}</p>
      <div class="modal-actions">
        <button class="btn" id="cf-cancel">Cancel</button>
        <button class="btn ${danger ? "danger" : "primary"}" id="cf-ok">${esc(confirmLabel)}</button>
      </div>`
    );
    onModalDismiss = () => resolve(false);
    $("#cf-cancel").addEventListener("click", closeModal);
    $("#cf-ok").addEventListener("click", () => {
      resolve(true);
      closeModal();
    });
  });
}

$("#modal-close").addEventListener("click", closeModal);
$("#modal-backdrop").addEventListener("mousedown", (e) => {
  if (e.target === $("#modal-backdrop")) closeModal();
});
document.addEventListener("keydown", (e) => {
  if (e.key !== "Escape") return;
  if (!$("#modal-backdrop").hidden) closeModal();
  else if (!$("#drawer-backdrop").hidden) closeDrawer();
});

/* ---------- drawer (transcripts and other long reads) ---------- */

function openDrawer(title, sub) {
  $("#drawer-title").textContent = title;
  $("#drawer-sub").textContent = sub || "";
  $("#drawer-body").innerHTML = "";
  $("#drawer-backdrop").hidden = false;
  return $("#drawer-body");
}

function closeDrawer() {
  $("#drawer-backdrop").hidden = true;
  $("#drawer-body").innerHTML = "";
}

$("#drawer-close").addEventListener("click", closeDrawer);
$("#drawer-backdrop").addEventListener("mousedown", (e) => {
  if (e.target === $("#drawer-backdrop")) closeDrawer();
});

async function ensureModels() {
  if (state.models.length) return;
  try {
    state.models = await api.get("/models");
  } catch {
    state.models = [];
  }
}

function modelDatalist() {
  return `<datalist id="model-list">${state.models
    .map((m) => `<option value="${esc(m.id)}">${esc(m.displayName)}</option>`)
    .join("")}</datalist>`;
}

async function openCreateAgentModal() {
  await ensureModels();
  const defaultModel = state.overview?.model || "";
  openModal(
    "New agent",
    `
    <label class="field"><span>Name</span><input class="input" id="ca-name" style="width:100%" placeholder="ResearchAgent"></label>
    <label class="field"><span>Purpose</span><textarea class="input" id="ca-purpose" rows="3" style="width:100%" placeholder="What is this agent responsible for?"></textarea></label>
    <label class="field"><span>Model</span><input class="input" id="ca-model" list="model-list" style="width:100%" value="${esc(defaultModel)}"></label>
    ${modelDatalist()}
    <label class="field"><span>Task to hand off immediately (optional)</span><textarea class="input" id="ca-task" rows="3" style="width:100%" placeholder="Leave empty to create the agent idle"></textarea></label>
    <div class="modal-actions">
      <button class="btn" id="ca-cancel">Cancel</button>
      <button class="btn primary" id="ca-create">Create</button>
    </div>`
  );

  $("#ca-cancel").addEventListener("click", closeModal);
  $("#ca-create").addEventListener("click", async () => {
    const name = $("#ca-name").value.trim();
    const purpose = $("#ca-purpose").value.trim();
    const model = $("#ca-model").value.trim();
    const task = $("#ca-task").value.trim();
    if (!name || !purpose) {
      toast("<b>Name and purpose are required</b>");
      return;
    }
    $("#ca-create").disabled = true;
    try {
      const r = await api.post("/agents", { name, purpose, model: model || null });
      if (task) {
        await api.post(`/agents/${r.agentId}/handoff`, { task });
        toast(`Created <b>${esc(name)}</b> and handed off task`);
      } else {
        toast(`Created <b>${esc(name)}</b>`);
      }
      closeModal();
      await Promise.all([loadAgents(), loadOverview()]);
    } catch (err) {
      toast(`<b>Error:</b> ${esc(err.message)}`);
      $("#ca-create").disabled = false;
    }
  });
}

async function openFleetModal() {
  await ensureModels();
  const defaultModel = state.overview?.model || "";
  openModal(
    "Spawn fleet",
    `
    <p class="hint" style="margin:0 0 14px">Spawns N identically configured agents (name-1 … name-N). Use it to stand up a large worker pool in one click.</p>
    <div class="field-row" style="margin-bottom:14px">
      <label class="field" style="flex:2;margin:0"><span>Name prefix</span><input class="input" id="fl-name" style="width:100%" value="worker"></label>
      <label class="field" style="flex:1;margin:0"><span>Count</span><input class="input" id="fl-count" type="number" min="1" max="100" value="30" style="width:100%"></label>
    </div>
    <label class="field"><span>Purpose</span><textarea class="input" id="fl-purpose" rows="3" style="width:100%" placeholder="Shared purpose for every agent in the fleet"></textarea></label>
    <label class="field"><span>Model</span><input class="input" id="fl-model" list="model-list" style="width:100%" value="${esc(defaultModel)}"></label>
    ${modelDatalist()}
    <div class="modal-actions">
      <button class="btn" id="fl-cancel">Cancel</button>
      <button class="btn primary" id="fl-create">Spawn</button>
    </div>`
  );

  $("#fl-cancel").addEventListener("click", closeModal);
  $("#fl-create").addEventListener("click", async () => {
    const prefix = $("#fl-name").value.trim() || "worker";
    const count = Math.max(1, Math.min(100, parseInt($("#fl-count").value, 10) || 1));
    const purpose = $("#fl-purpose").value.trim();
    const model = $("#fl-model").value.trim();
    if (!purpose) {
      toast("<b>Purpose is required</b>");
      return;
    }
    $("#fl-create").disabled = true;
    $("#fl-create").textContent = "Spawning…";
    let ok = 0;
    let firstError = null;
    for (let i = 1; i <= count; i++) {
      try {
        await api.post("/agents", { name: `${prefix}-${i}`, purpose, model: model || null });
        ok++;
      } catch (err) {
        firstError = err.message;
        break;
      }
    }
    closeModal();
    toast(firstError ? `Spawned <b>${ok}</b>/${count} — ${esc(firstError)}` : `Spawned <b>${ok}</b> agents`);
    await Promise.all([loadAgents(), loadOverview()]);
  });
}

$("#btn-create-agent").addEventListener("click", openCreateAgentModal);
$("#qa-create").addEventListener("click", openCreateAgentModal);
$("#btn-fleet").addEventListener("click", openFleetModal);
$("#qa-fleet").addEventListener("click", openFleetModal);

function openHandoffModal(agentId) {
  const agent = state.agents.find((a) => a.agentId === agentId);
  openModal(
    `Hand off to ${agent ? agent.name : agentId}`,
    `
    <label class="field"><span>Task description</span><textarea class="input" id="ho-task" rows="6" style="width:100%" placeholder="Describe the task in detail — the agent works autonomously from this brief."></textarea></label>
    <div class="modal-actions">
      <button class="btn" id="ho-cancel">Cancel</button>
      <button class="btn primary" id="ho-send">Hand off</button>
    </div>`
  );
  $("#ho-task").focus();
  $("#ho-cancel").addEventListener("click", closeModal);
  $("#ho-send").addEventListener("click", async () => {
    const task = $("#ho-task").value.trim();
    if (!task) return;
    $("#ho-send").disabled = true;
    try {
      const r = await api.post(`/agents/${agentId}/handoff`, { task });
      toast(`Task <b>${esc(r.taskId)}</b> handed off`);
      closeModal();
      await Promise.all([loadAgents(), loadTasks(), loadOverview()]);
    } catch (err) {
      toast(`<b>Error:</b> ${esc(err.message)}`);
      $("#ho-send").disabled = false;
    }
  });
}

/* ---------- tasks ---------- */

async function loadTasks() {
  state.tasks = await api.get("/tasks");
  renderTasks();
  renderRail();
}

function renderTasks() {
  const { running, completed } = state.tasks;

  $("#task-running").innerHTML = running
    .map(
      (t) => `
      <div class="task-row">
        <span class="task-status running">running</span>
        <span class="task-desc" title="${esc(t.description)}">${esc(t.description)}</span>
        <span class="task-agent">${esc(t.agentName)}</span>
        <span class="task-time">${fmtTime(t.startedAt)}</span>
      </div>`
    )
    .join("") || `<div class="hint" style="padding:4px 2px">No running tasks.</div>`;

  $("#task-completed").innerHTML = completed
    .map(
      (t) => `
      <div class="task-row clickable" data-task="${esc(t.taskId)}">
        <span class="task-status ${t.success ? "done" : "failed"}">${t.success ? "done" : "failed"}</span>
        <span class="task-desc" title="${esc(t.description || "")}">${esc(t.description || t.result.slice(0, 160))}</span>
        <span class="task-agent">${esc(t.agentName)}</span>
        <span class="task-time">${fmtDuration(t.durationSeconds)}</span>
      </div>`
    )
    .join("") || `<div class="hint" style="padding:4px 2px">Nothing completed yet.</div>`;

  $$("#task-completed [data-task]").forEach((row) =>
    row.addEventListener("click", () => {
      const t = state.tasks.completed.find((x) => x.taskId === row.dataset.task);
      if (!t) return;
      openModal(
        `Task ${t.taskId} — ${t.success ? "completed" : "failed"}`,
        `
        <div class="kv"><span>Agent</span><span>${esc(t.agentName)} (${esc(t.agentId)})</span></div>
        <div class="kv"><span>Duration</span><span>${fmtDuration(t.durationSeconds)}</span></div>
        <div class="kv"><span>Finished</span><span>${new Date(t.completedAt).toLocaleString()}</span></div>
        ${t.description ? `<div class="hint" style="margin-top:10px;white-space:pre-wrap">${esc(t.description)}</div>` : ""}
        <div class="result-pre md" id="task-result-md" style="margin-top:14px"></div>`
      );
      renderMarkdown($("#task-result-md"), t.result);
    })
  );
}

$("#btn-clear-tasks").addEventListener("click", async () => {
  await api.post("/tasks/clear-completed");
  await Promise.all([loadTasks(), loadOverview()]);
  toast("Completed tasks cleared");
});

/* ---------- work sub-tabs ---------- */

function showWorkTab(tab) {
  state.workTab = tab;
  $$("#work-tabs .seg-item").forEach((b) => b.classList.toggle("active", b.dataset.worktab === tab));
  WORK_TABS.forEach((t) => {
    $(`#work-pane-${t}`).hidden = t !== tab;
  });
  syncHash();
  const load = tab === "queue" ? loadTodos : tab === "schedule" ? loadWakes : loadTasks;
  load().catch(() => {});
}

$("#work-tabs").addEventListener("click", (e) => {
  const btn = e.target.closest(".seg-item");
  if (btn) showWorkTab(btn.dataset.worktab);
});

/* ---------- orchestrator ---------- */

let streamBuffer = "";
let streamRenderPending = false;

// Re-parsing the whole buffer per chunk is wasteful at high token rates;
// coalesce renders to at most one every 80ms.
function scheduleStreamRender() {
  if (streamRenderPending) return;
  streamRenderPending = true;
  setTimeout(() => {
    streamRenderPending = false;
    // Capture stickiness before the render grows the scroll height. The
    // stream bubble can grow up to its 300px inner cap between renders, so
    // the follow margin must exceed that or following silently stops.
    const stick = isNearBottom(340);
    const el = $("#chat-stream-text");
    renderMarkdown(el, streamBuffer);
    el.scrollTop = el.scrollHeight;
    if (stick) scrollChatToBottom();
  }, 80);
}

function scrollChatToBottom() {
  const scroll = $("#chat-scroll");
  scroll.scrollTop = scroll.scrollHeight;
}

// "Following" the conversation means the user is within `margin` px of the
// bottom; anyone who scrolled up to read history is left alone.
function isNearBottom(margin = 160) {
  const scroll = $("#chat-scroll");
  return scroll.scrollHeight - scroll.scrollTop - scroll.clientHeight < margin;
}

async function loadTranscript() {
  const t = await api.get("/orchestrator/transcript");
  state.transcript = t.entries;
  setOrchestratorBusy(t.busy);
  renderTranscript({ stick: true });
}

function summarizeToolArgs(argsJson) {
  try {
    const args = JSON.parse(argsJson);
    const parts = Object.entries(args)
      .filter(([, v]) => v !== null && v !== undefined && v !== "")
      .slice(0, 2)
      .map(([k, v]) => `${k}: ${String(typeof v === "object" ? JSON.stringify(v) : v).slice(0, 48)}`);
    return parts.join(" · ");
  } catch {
    return "";
  }
}

function buildToolStrip(tools) {
  const details = document.createElement("details");
  details.className = "tool-strip";
  const summary = document.createElement("summary");
  summary.textContent = `⚙ ${tools.length} tool ${tools.length === 1 ? "call" : "calls"}`;
  details.appendChild(summary);
  const list = document.createElement("div");
  list.className = "tool-strip-body";
  for (const t of tools) {
    const row = document.createElement("div");
    row.className = "tool-row";
    row.innerHTML = `<span class="tool-row-name">${esc(t.name)}</span><span class="tool-row-args">${esc(t.args)}</span>`;
    list.appendChild(row);
  }
  details.appendChild(list);
  return details;
}

/* Only a window of recent messages lives in the DOM. While following the
   bottom the window trims back to CHAT_WINDOW (older nodes are disposed);
   scrolling toward the top restores CHAT_WINDOW_STEP more at a time, starting
   CHAT_LOAD_MARGIN px before the rendered history runs out. */
const CHAT_WINDOW = 20;
const CHAT_WINDOW_STEP = 20;
const CHAT_LOAD_MARGIN = 800;

function renderTranscript(opts = {}) {
  // Compute stickiness before wiping the log — the wipe changes scrollHeight.
  const stick = opts.stick ?? isNearBottom();
  if (stick) state.chatWindow = CHAT_WINDOW;
  const log = $("#chat-log");
  log.innerHTML = "";
  const hidden = Math.max(0, state.transcript.length - state.chatWindow);
  if (hidden > 0) {
    const older = document.createElement("button");
    older.className = "chat-older";
    older.textContent = `↑ ${hidden} earlier message${hidden === 1 ? "" : "s"}`;
    older.addEventListener("click", growChatWindow);
    log.appendChild(older);
  }
  for (const e of state.transcript.slice(-state.chatWindow)) {
    if (e.role === "task") {
      const card = document.createElement("details");
      card.className = `chat-task${e.success === false ? " failed" : ""}`;
      const summary = document.createElement("summary");
      summary.innerHTML = `<span class="chat-task-mark">${e.success === false ? "✗" : "✓"}</span> <b>${esc(e.agentName || "agent")}</b> ${e.success === false ? "failed" : "completed"} ${esc(e.taskId || "a task")} <span class="chat-task-time">${e.timestamp ? fmtTime(e.timestamp) : ""}</span>`;
      card.appendChild(summary);
      const body = document.createElement("div");
      body.className = "md chat-task-body";
      renderMarkdown(body, e.content);
      card.appendChild(body);
      log.appendChild(card);
      continue;
    }

    const div = document.createElement("div");
    div.className = `chat-msg ${e.role}${e.optimistic ? " pending" : ""}`;
    if (e.role === "user" && e.source === "scheduler") {
      div.classList.add("scheduler");
    }
    if (e.role === "assistant") {
      div.classList.add("md");
      renderMarkdown(div, e.content);
      if (e.tools?.length) {
        div.prepend(buildToolStrip(e.tools));
      }
    } else {
      div.textContent = e.content;
    }
    log.appendChild(div);
  }
  if (stick) scrollChatToBottom();
}

function growChatWindow() {
  if (state.chatWindow >= state.transcript.length) return;
  const scroll = $("#chat-scroll");
  const prevHeight = scroll.scrollHeight;
  const prevTop = scroll.scrollTop;
  state.chatWindow = Math.min(state.transcript.length, state.chatWindow + CHAT_WINDOW_STEP);
  renderTranscript({ stick: false });
  // Older content was prepended; offset by the height it added so the
  // message the user was looking at stays put.
  scroll.scrollTop = prevTop + (scroll.scrollHeight - prevHeight);
}

$("#chat-scroll").addEventListener("scroll", () => {
  const scroll = $("#chat-scroll");
  if (scroll.scrollTop < CHAT_LOAD_MARGIN && state.chatWindow < state.transcript.length) {
    growChatWindow();
  }
});

let workingSince = null;
let workingTimer = null;

function updateWorkingLabel() {
  if (!workingSince) return;
  const s = Math.floor((Date.now() - workingSince) / 1000);
  $("#working-label").textContent = s >= 2 ? `assistant is working · ${fmtDuration(s)}` : "assistant is working";
}

function setOrchestratorBusy(busy) {
  state.orchestratorBusy = busy;
  $("#chat-send").disabled = busy;
  $("#chat-cancel").hidden = !busy;
  if (busy) {
    if (!workingSince) workingSince = Date.now();
    $("#chat-stream").hidden = false;
    if (isNearBottom(340)) scrollChatToBottom();
    updateWorkingLabel();
    if (!workingTimer) workingTimer = setInterval(updateWorkingLabel, 1000);
  } else {
    workingSince = null;
    clearInterval(workingTimer);
    workingTimer = null;
    $("#chat-stream").hidden = true;
    $("#chat-stream-text").innerHTML = "";
    $("#tool-log").innerHTML = "";
    streamBuffer = "";
  }
}

let currentTurnTools = [];

function renderToolLog() {
  const el = $("#tool-log");
  el.innerHTML = currentTurnTools
    .map(
      (t, i) => `
      <div class="tool-row ${i === currentTurnTools.length - 1 ? "latest" : ""}">
        <span class="tool-row-name">${esc(t.name)}</span>
        <span class="tool-row-args">${esc(t.args)}</span>
      </div>`
    )
    .join("");
  el.scrollTop = el.scrollHeight;
}

$("#chat-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  const message = $("#chat-text").value.trim();
  if (!message || state.orchestratorBusy) return;

  // Optimistic: show the message and working state immediately, before the
  // server round-trip; the SSE echo confirms it (or the catch rolls it back).
  const entry = { role: "user", content: message, optimistic: true };
  state.transcript.push(entry);
  renderTranscript({ stick: true });
  currentTurnTools = [];
  setOrchestratorBusy(true);
  $("#chat-text").value = "";

  try {
    await api.post("/orchestrator/message", { message });
  } catch (err) {
    state.transcript = state.transcript.filter((x) => x !== entry);
    renderTranscript();
    $("#chat-text").value = message;
    if (err.status === 409) {
      // A scheduler-initiated run is already in progress; the server's state
      // events keep driving the busy UI, so don't tear down the live stream.
      toast("The assistant is already busy with a run — try again shortly.");
    } else {
      setOrchestratorBusy(false);
      toast(`<b>Error:</b> ${esc(err.message)}`);
    }
  }
});

$("#chat-text").addEventListener("keydown", (e) => {
  if (e.key === "Enter" && !e.shiftKey) {
    e.preventDefault();
    $("#chat-form").requestSubmit();
  }
});

$("#chat-cancel").addEventListener("click", () => api.post("/orchestrator/cancel").catch(() => {}));

$("#chat-new").addEventListener("click", async () => {
  const ok = await confirmModal(
    "Start a fresh conversation?",
    "The current chat context is closed. History stays available in Sessions.",
    "New chat",
    false
  );
  if (!ok) return;
  try {
    await api.post("/orchestrator/new-session");
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
});

/* ---------- todos / task manager ---------- */

const TERMINAL_STATUSES = ["done", "failed", "cancelled"];
const isTerminal = (status) => TERMINAL_STATUSES.includes(status);

async function loadTodos() {
  const params = new URLSearchParams();
  if (state.todoScope !== "all") params.set("scope", state.todoScope);
  if (state.todoBoard) params.set("board", state.todoBoard);
  state.todos = await api.get(`/todos?${params}`);
  await loadBoards();
  renderTodos();
  renderRail();
}

async function loadBoards() {
  const showBoards = state.todoScope === "project" || state.todoScope === "agent";
  const select = $("#todo-board");
  select.hidden = !showBoards;
  if (!showBoards) {
    state.todoBoard = null;
    return;
  }
  try {
    const boards = await api.get("/todos/boards");
    const list = state.todoScope === "project" ? boards.project : boards.agent;
    select.innerHTML =
      `<option value="">all boards</option>` +
      list.map((b) => `<option value="${esc(b)}" ${b === state.todoBoard ? "selected" : ""}>${esc(b)}</option>`).join("");
  } catch {
    select.innerHTML = `<option value="">all boards</option>`;
  }
}

function taskBadges(t) {
  const badges = [];
  if (t.blocked) {
    const blockers = t.blockedBy.filter((b) => !TERMINAL_STATUSES.includes(b.status) || b.missing);
    badges.push(`<button class="task-badge alert" data-blockers="${esc(t.id)}" title="${esc(blockers.map((b) => b.title).join(", "))}">blocked (${blockers.length})</button>`);
  }
  if (t.dispatchedTo) badges.push(`<span class="task-badge live">→ ${esc(t.dispatchedTo)}</span>`);
  if (t.hasWaiters) badges.push(`<span class="task-badge">waiting</span>`);
  if (t.claimStatus === "pending_approval") badges.push(`<span class="task-badge alert">claim pending</span>`);
  if (t.recurrenceDescription) badges.push(`<span class="task-badge">${esc(t.recurrenceDescription)}</span>`);
  const flags = [];
  if (t.agentAvailable) flags.push(`<span class="task-flag" title="agents may take this task">A</span>`);
  if (t.requiresApproval) flags.push(`<span class="task-flag" title="requires your approval to claim">⚿</span>`);
  if (t.userHandoffOnly) flags.push(`<span class="task-flag" title="only you can hand this off">✋</span>`);
  return badges.join("") + flags.join("");
}

function renderTodos() {
  const filtered = state.todos.filter((t) => {
    if (state.todoFilter === "open") return !isTerminal(t.status);
    if (state.todoFilter === "done") return isTerminal(t.status);
    return true;
  });

  $("#todos-empty").classList.toggle("show", filtered.length === 0);

  $("#todo-list").innerHTML = filtered
    .map(
      (t, i) => `
      <li class="todo-item ${isTerminal(t.status) ? "done" : ""} ${t.blocked ? "blocked" : ""}" style="animation-delay:${Math.min(i * 0.02, 0.3)}s">
        <input type="checkbox" class="todo-check" data-id="${esc(t.id)}" ${isTerminal(t.status) ? "checked" : ""} ${t.blocked ? "disabled" : ""}>
        <span class="todo-title" contenteditable="true" spellcheck="false" data-id="${esc(t.id)}">${esc(t.title)}</span>
        <span class="task-badges">${taskBadges(t)}</span>
        ${state.todoScope === "all" ? `<span class="task-scope">${esc(t.scope)}${t.board !== "default" ? `/${esc(t.board)}` : ""}</span>` : ""}
        <button class="todo-pri ${esc(t.priority)}" data-id="${esc(t.id)}" data-pri="${esc(t.priority)}" title="cycle priority">${esc(t.priority)}</button>
        ${!isTerminal(t.status) && !t.dispatchedTo && !t.blocked ? `<button class="btn ghost sm" data-dispatch="${esc(t.id)}" title="hand off to an agent">▶</button>` : ""}
        <button class="btn ghost sm" data-more="${esc(t.id)}" title="more actions">⋯</button>
      </li>`
    )
    .join("");

  $$(".todo-check").forEach((c) =>
    c.addEventListener("change", async () => {
      try {
        if (c.checked) {
          await api.post(`/todos/${c.dataset.id}/complete`, {});
        } else {
          await api.patch(`/todos/${c.dataset.id}`, { status: "pending" });
        }
        await Promise.all([loadTodos(), loadOverview()]);
      } catch (err) {
        toast(`<b>Error:</b> ${esc(err.message)}`);
      }
    })
  );

  $$(".todo-title[contenteditable]").forEach((el) => {
    el.addEventListener("keydown", (e) => {
      if (e.key === "Enter") {
        e.preventDefault();
        el.blur();
      }
    });
    el.addEventListener("blur", () => {
      const t = state.todos.find((x) => x.id === el.dataset.id);
      const title = el.textContent.trim();
      if (t && title && title !== t.title) updateTodo(el.dataset.id, { title });
      else if (t) el.textContent = t.title;
    });
  });

  $$(".todo-pri").forEach((b) =>
    b.addEventListener("click", () => {
      const next = { low: "normal", normal: "high", high: "low" }[b.dataset.pri] || "normal";
      updateTodo(b.dataset.id, { priority: next });
    })
  );

  $$("[data-blockers]").forEach((b) =>
    b.addEventListener("click", () => {
      const t = state.todos.find((x) => x.id === b.dataset.blockers);
      if (!t) return;
      openModal(
        `Blocked: ${t.title}`,
        `<div class="msg-view">${t.blockedBy
          .map(
            (blocker) => `
            <div class="msg-row">
              <div class="msg-role">${esc(blocker.status)}${blocker.missing ? " · deleted" : ""}</div>
              <div class="msg-content">${esc(blocker.title)} <span class="hint">(${esc(blocker.id)})</span></div>
            </div>`
          )
          .join("")}</div>`
      );
    })
  );

  $$("[data-dispatch]").forEach((b) => b.addEventListener("click", () => openDispatchModal(b.dataset.dispatch)));
  $$("[data-more]").forEach((b) => b.addEventListener("click", (e) => {
    e.stopPropagation();
    openTodoMenu(b, b.dataset.more);
  }));
}

/* ---------- todo overflow menu ---------- */

function closeTodoMenu() {
  $("#todo-menu")?.remove();
}

function openTodoMenu(anchor, taskId) {
  if ($("#todo-menu")?.dataset.taskId === taskId) {
    closeTodoMenu();
    return;
  }
  closeTodoMenu();
  const menu = document.createElement("div");
  menu.className = "popmenu";
  menu.id = "todo-menu";
  menu.dataset.taskId = taskId;
  menu.innerHTML = `
    <button data-menu="detail">Details</button>
    <button data-menu="edit">Edit</button>
    <button data-menu="delete" class="danger">Delete</button>`;
  document.body.appendChild(menu);
  const r = anchor.getBoundingClientRect();
  menu.style.top = `${Math.min(r.bottom + 4, window.innerHeight - menu.offsetHeight - 8)}px`;
  menu.style.left = `${Math.min(r.right - menu.offsetWidth, window.innerWidth - menu.offsetWidth - 8)}px`;
  menu.querySelector('[data-menu="detail"]').addEventListener("click", () => { closeTodoMenu(); openTaskDetail(taskId); });
  menu.querySelector('[data-menu="edit"]').addEventListener("click", () => { closeTodoMenu(); openTaskModal(taskId); });
  menu.querySelector('[data-menu="delete"]').addEventListener("click", async () => {
    closeTodoMenu();
    try {
      await api.del(`/todos/${taskId}`);
      await Promise.all([loadTodos(), loadOverview()]);
    } catch (err) {
      toast(`<b>Error:</b> ${esc(err.message)}`);
    }
  });
}

document.addEventListener("click", (e) => {
  if (!e.target.closest("#todo-menu")) closeTodoMenu();
});

async function updateTodo(id, patch) {
  try {
    await api.patch(`/todos/${id}`, patch);
    await Promise.all([loadTodos(), loadOverview()]);
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
}

function openTaskModal(taskId) {
  const t = taskId ? state.todos.find((x) => x.id === taskId) : null;
  const openTasks = state.todos.filter((x) => !isTerminal(x.status) && x.id !== taskId);
  const currentDeps = t ? t.blockedBy.map((b) => b.id) : [];

  openModal(
    t ? `Edit task` : "New task",
    `
    <label class="field"><span>Title</span><input class="input" id="tm-title" style="width:100%" value="${esc(t?.title || "")}"></label>
    <label class="field"><span>Notes</span><textarea class="input" id="tm-notes" rows="2" style="width:100%">${esc(t?.notes || "")}</textarea></label>
    <div class="field-row" style="margin-bottom:14px">
      <label class="field" style="flex:1;margin:0"><span>Scope</span>
        <select class="input select" id="tm-scope" style="width:100%">
          ${["project", "global", "agent"].map((s) => `<option value="${s}" ${(t?.scope || "project") === s ? "selected" : ""}>${s}</option>`).join("")}
        </select></label>
      <label class="field" style="flex:1;margin:0"><span>Board</span><input class="input" id="tm-board" style="width:100%" value="${esc(t?.board || "default")}"></label>
      <label class="field" style="flex:1;margin:0"><span>Priority</span>
        <select class="input select" id="tm-priority" style="width:100%">
          ${["normal", "high", "low"].map((p) => `<option value="${p}" ${(t?.priority || "normal") === p ? "selected" : ""}>${p}</option>`).join("")}
        </select></label>
    </div>
    <div class="field"><span style="display:block;font-size:12px;color:var(--muted);margin-bottom:6px">Flags</span>
      <label class="check-row"><input type="checkbox" id="tm-agent-available" ${t?.agentAvailable ? "checked" : ""}> agent available — orchestrator may dispatch this to agents</label>
      <label class="check-row"><input type="checkbox" id="tm-requires-approval" ${t?.requiresApproval ? "checked" : ""}> requires my approval before the orchestrator takes it</label>
      <label class="check-row"><input type="checkbox" id="tm-user-handoff" ${t?.userHandoffOnly ? "checked" : ""}> wait for me to hand off — never auto-taken</label>
    </div>
    <div class="field"><span style="display:block;font-size:12px;color:var(--muted);margin-bottom:6px">Blocked by</span>
      <div class="dep-picker" id="tm-deps">
        ${openTasks.length === 0 ? '<span class="hint">no open tasks to depend on</span>' : openTasks
          .map(
            (x) => `<label class="check-row"><input type="checkbox" data-dep="${esc(x.id)}" ${currentDeps.includes(x.id) ? "checked" : ""}> ${esc(x.title)} <span class="hint">${esc(x.scope)}</span></label>`
          )
          .join("")}
      </div>
    </div>
    <div class="field"><span style="display:block;font-size:12px;color:var(--muted);margin-bottom:6px">Recurrence</span>
      <div class="seg" id="tm-recur-preset" style="flex-wrap:wrap">
        ${["none", "minutes", "hours", "daily", "weekly", "cron"].map((p) => `<button type="button" class="seg-item" data-preset="${p}">${p}</button>`).join("")}
      </div>
      <div id="tm-recur-fields" style="margin-top:10px"></div>
      <span class="hint" id="tm-cron-preview"></span>
    </div>
    <label class="field" id="tm-catchup-wrap" hidden><span>If a run is missed while Saturn is offline</span>
      <select class="input select" id="tm-catchup" style="width:100%">
        <option value="run_once" ${(t?.catchUpPolicy || "run_once") === "run_once" ? "selected" : ""}>run once on startup (catch up)</option>
        <option value="skip" ${t?.catchUpPolicy === "skip" ? "selected" : ""}>skip missed runs</option>
      </select>
    </label>
    <div class="modal-actions">
      <button class="btn" id="tm-cancel">Cancel</button>
      <button class="btn primary" id="tm-save">${t ? "Save" : "Create"}</button>
    </div>`
  );

  // --- recurrence presets ---
  const DAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

  function detectPreset() {
    if (!t || t.recurrenceKind === "none") return { preset: "none" };
    if (t.recurrenceKind === "interval") {
      const s = t.recurrenceIntervalSeconds || 3600;
      return s % 3600 === 0 ? { preset: "hours", n: s / 3600 } : { preset: "minutes", n: Math.round(s / 60) };
    }
    const cron = t.recurrenceCron || "";
    let m = cron.match(/^(\d+) (\d+) \* \* \*$/);
    if (m) return { preset: "daily", time: `${m[2].padStart(2, "0")}:${m[1].padStart(2, "0")}` };
    m = cron.match(/^(\d+) (\d+) \* \* ([\d,]+)$/);
    if (m) return { preset: "weekly", time: `${m[2].padStart(2, "0")}:${m[1].padStart(2, "0")}`, days: m[3].split(",").map(Number) };
    return { preset: "cron", cron };
  }

  const initial = detectPreset();
  let currentPreset = initial.preset;

  function renderRecurFields() {
    const el = $("#tm-recur-fields");
    $("#tm-catchup-wrap").hidden = currentPreset === "none";
    if (currentPreset === "none") { el.innerHTML = ""; validateRecur(); return; }
    if (currentPreset === "minutes" || currentPreset === "hours") {
      el.innerHTML = `<div class="field-row"><input class="input" id="tm-recur-n" type="number" min="1" value="${initial.preset === currentPreset ? initial.n || 1 : 1}" style="max-width:120px"><span style="align-self:center;color:var(--muted);font-size:12px">every N ${currentPreset}</span></div>`;
    } else if (currentPreset === "daily") {
      el.innerHTML = `<div class="field-row"><input class="input" id="tm-recur-time" type="time" value="${initial.preset === "daily" ? initial.time : "09:00"}" style="max-width:140px"><span style="align-self:center;color:var(--muted);font-size:12px">every day at this time</span></div>`;
    } else if (currentPreset === "weekly") {
      const selected = initial.preset === "weekly" ? initial.days || [] : [1];
      el.innerHTML = `
        <div style="display:flex;gap:6px;flex-wrap:wrap;margin-bottom:8px">${DAYS.map((d, i) => `<button type="button" class="seg-item tm-day ${selected.includes(i) ? "active" : ""}" data-day="${i}" style="border:1px solid var(--border);border-radius:6px">${d}</button>`).join("")}</div>
        <input class="input" id="tm-recur-time" type="time" value="${initial.preset === "weekly" ? initial.time : "09:00"}" style="max-width:140px">`;
      $$(".tm-day").forEach((b) => b.addEventListener("click", () => { b.classList.toggle("active"); validateRecur(); }));
    } else if (currentPreset === "cron") {
      el.innerHTML = `<input class="input" id="tm-recur-cron" style="width:100%" placeholder="0 9 * * 1-5 (min hour day month weekday, local time)" value="${esc(initial.preset === "cron" ? initial.cron : "")}">`;
      $("#tm-recur-cron").addEventListener("input", validateRecur);
    }
    el.querySelectorAll("input").forEach((i) => i.addEventListener("input", validateRecur));
    validateRecur();
  }

  function compileRecurrence() {
    if (currentPreset === "none") return { recurrenceKind: "none", recurrenceIntervalSeconds: null, recurrenceCron: null };
    if (currentPreset === "minutes" || currentPreset === "hours") {
      const n = Math.max(1, parseInt($("#tm-recur-n")?.value, 10) || 1);
      return { recurrenceKind: "interval", recurrenceIntervalSeconds: n * (currentPreset === "hours" ? 3600 : 60), recurrenceCron: null };
    }
    const time = ($("#tm-recur-time")?.value || "09:00").split(":");
    const minute = parseInt(time[1], 10) || 0;
    const hour = parseInt(time[0], 10) || 0;
    if (currentPreset === "daily") {
      return { recurrenceKind: "cron", recurrenceCron: `${minute} ${hour} * * *`, recurrenceIntervalSeconds: null };
    }
    if (currentPreset === "weekly") {
      const days = $$(".tm-day").filter((b) => b.classList.contains("active")).map((b) => b.dataset.day);
      return { recurrenceKind: "cron", recurrenceCron: `${minute} ${hour} * * ${days.length ? days.join(",") : "1"}`, recurrenceIntervalSeconds: null };
    }
    return { recurrenceKind: "cron", recurrenceCron: ($("#tm-recur-cron")?.value || "").trim(), recurrenceIntervalSeconds: null };
  }

  async function validateRecur() {
    const preview = $("#tm-cron-preview");
    const r = compileRecurrence();
    if (r.recurrenceKind !== "cron" || !r.recurrenceCron) { preview.textContent = ""; return; }
    try {
      const v = await api.get(`/todos/validate-cron?expr=${encodeURIComponent(r.recurrenceCron)}`);
      preview.textContent = v.valid
        ? `next: ${v.nextRuns.map((d) => new Date(d).toLocaleString()).join(" · ")}`
        : v.error;
    } catch { preview.textContent = ""; }
  }

  $$("#tm-recur-preset .seg-item").forEach((b) => {
    b.classList.toggle("active", b.dataset.preset === currentPreset);
    b.addEventListener("click", () => {
      currentPreset = b.dataset.preset;
      $$("#tm-recur-preset .seg-item").forEach((x) => x.classList.toggle("active", x === b));
      renderRecurFields();
    });
  });
  renderRecurFields();

  $("#tm-cancel").addEventListener("click", closeModal);
  $("#tm-save").addEventListener("click", async () => {
    const recurrence = compileRecurrence();
    const payload = {
      title: $("#tm-title").value.trim(),
      notes: $("#tm-notes").value,
      scope: $("#tm-scope").value,
      board: $("#tm-board").value.trim() || "default",
      priority: $("#tm-priority").value,
      agentAvailable: $("#tm-agent-available").checked,
      requiresApproval: $("#tm-requires-approval").checked,
      userHandoffOnly: $("#tm-user-handoff").checked,
      blockedBy: $$("#tm-deps [data-dep]").filter((c) => c.checked).map((c) => c.dataset.dep),
      ...recurrence,
      catchUpPolicy: $("#tm-catchup").value,
    };
    if (!payload.title) {
      toast("<b>Title is required</b>");
      return;
    }
    $("#tm-save").disabled = true;
    try {
      if (t) {
        await api.patch(`/todos/${t.id}`, payload);
        toast("Task <b>updated</b>");
      } else {
        const created = await api.post("/todos", payload);
        revealScope(created.scope);
        toast(`Task <b>${esc(created.title)}</b> added`);
      }
      closeModal();
      await Promise.all([loadTodos(), loadOverview()]);
    } catch (err) {
      toast(`<b>Error:</b> ${esc(err.message)}`);
      $("#tm-save").disabled = false;
    }
  });
}

async function openTaskDetail(taskId) {
  let d;
  try {
    d = await api.get(`/todos/${taskId}`);
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
    return;
  }
  const t = d.task;
  const section = (title, rows) =>
    `<div class="toolbar" style="margin:14px 0 8px"><span class="toolbar-title">${title}</span></div>` +
    (rows.length ? rows.join("") : '<div class="hint">none</div>');

  openModal(
    t.title,
    `
    <div class="kv"><span>Id</span><span>${esc(t.id)}</span></div>
    <div class="kv"><span>Scope</span><span>${esc(t.scope)}/${esc(t.board)}</span></div>
    <div class="kv"><span>Status</span><span>${esc(t.status)}${t.blocked ? " (blocked)" : ""}</span></div>
    <div class="kv"><span>Created by</span><span>${esc(t.createdBy)}</span></div>
    <div class="kv"><span>Claim</span><span>${esc(t.claimStatus)}${t.claimedBy ? ` by ${esc(t.claimedBy)}` : ""}</span></div>
    ${t.recurrenceDescription ? `<div class="kv"><span>Recurs</span><span>${esc(t.recurrenceDescription)} · next ${t.nextRunAt ? new Date(t.nextRunAt).toLocaleString() : "—"}</span></div>` : ""}
    ${t.notes ? `<div class="hint" style="margin-top:10px;white-space:pre-wrap">${esc(t.notes)}</div>` : ""}
    ${section("Blocked by", t.blockedBy.map((b) => `<div class="kv"><span>${esc(b.title)}${b.missing ? " (deleted)" : ""}</span><span>${esc(b.status)}</span></div>`))}
    ${section("Blocks", d.dependents.map((id) => `<div class="kv"><span>${esc(id)}</span><span></span></div>`))}
    ${section("Run history", d.runs.map((r) => `<div class="kv"><span>${new Date(r.firedAt).toLocaleString()}</span><span>${esc(r.outcome || "pending")}</span></div>`))}
    ${section("Dispatches", d.dispatches.map((x) => `<div class="kv"><span>${esc(x.agentName || "?")} · ${new Date(x.startedAt).toLocaleString()}${x.orphaned ? " · orphaned" : ""}</span><span>${x.completedAt ? (x.success ? "done" : "failed") : "running"}</span></div>`))}
    ${section("Waiting on this", d.waiters.map((w) => `<div class="kv"><span>${esc(w.waiterKind)}${w.waiterAgentName ? ` · ${esc(w.waiterAgentName)}` : ""}</span><span>since ${new Date(w.createdAt).toLocaleString()}</span></div>`))}
    ${d.dispatches.some((x) => x.result) ? `<div class="toolbar" style="margin:14px 0 8px"><span class="toolbar-title">Latest result</span></div><div class="result-pre md" id="task-detail-result"></div>` : ""}`
  );
  const latest = d.dispatches.find((x) => x.result);
  if (latest && $("#task-detail-result")) {
    renderMarkdown($("#task-detail-result"), latest.result);
  }
}

async function openDispatchModal(taskId) {
  const agents = await api.get("/agents");
  const idle = agents.filter((a) => !a.currentTask);
  openModal(
    "Hand off to an agent",
    `
    ${idle.length === 0 ? '<p class="hint">No idle agents. Create one in the Agents tab first, or let the orchestrator handle it.</p>' : `
    <label class="field"><span>Agent</span>
      <select class="input select" id="dp-agent" style="width:100%">
        ${idle.map((a) => `<option value="${esc(a.agentId)}">${esc(a.name)} (${esc(a.agentId)})</option>`).join("")}
      </select></label>`}
    <div class="modal-actions">
      <button class="btn" id="dp-cancel">Cancel</button>
      ${idle.length > 0 ? '<button class="btn primary" id="dp-send">Dispatch</button>' : ""}
    </div>`
  );
  $("#dp-cancel").addEventListener("click", closeModal);
  $("#dp-send")?.addEventListener("click", async () => {
    try {
      const r = await api.post(`/todos/${taskId}/dispatch`, { agentId: $("#dp-agent").value });
      toast(esc(r.message));
      closeModal();
      await Promise.all([loadTodos(), loadOverview()]);
    } catch (err) {
      toast(`<b>Error:</b> ${esc(err.message)}`);
    }
  });
}

// After adding, make sure the new task is actually visible: if the current
// scope/status filters would hide it, they read as "the add button is broken".
function revealScope(scope) {
  if (state.todoFilter === "done") {
    state.todoFilter = "all";
    $$("#todo-filter .seg-item").forEach((b) => b.classList.toggle("active", b.dataset.filter === "all"));
  }
  if (state.todoScope !== "all" && state.todoScope !== scope) {
    state.todoScope = scope;
    state.todoBoard = null;
    $$("#todo-scope .seg-item").forEach((b) => b.classList.toggle("active", b.dataset.scope === scope));
  }
}

$("#todo-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  const title = $("#todo-title").value.trim();
  if (!title) return;
  try {
    const created = await api.post("/todos", {
      title,
      priority: $("#todo-priority").value,
      scope: $("#todo-add-scope").value,
    });
    $("#todo-title").value = "";
    revealScope(created.scope);
    toast(`Task <b>${esc(created.title)}</b> added`);
    await Promise.all([loadTodos(), loadOverview()]);
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
});

$("#btn-task-detail-add").addEventListener("click", () => openTaskModal(null));

$("#todo-scope").addEventListener("click", (e) => {
  const btn = e.target.closest(".seg-item");
  if (!btn) return;
  state.todoScope = btn.dataset.scope;
  state.todoBoard = null;
  $$("#todo-scope .seg-item").forEach((b) => b.classList.toggle("active", b === btn));
  loadTodos().catch(() => {});
});

$("#todo-board").addEventListener("change", (e) => {
  state.todoBoard = e.target.value || null;
  loadTodos().catch(() => {});
});

$("#todo-filter").addEventListener("click", (e) => {
  const btn = e.target.closest(".seg-item");
  if (!btn) return;
  state.todoFilter = btn.dataset.filter;
  $$("#todo-filter .seg-item").forEach((b) => b.classList.toggle("active", b === btn));
  renderTodos();
});

$("#btn-clear-todos").addEventListener("click", async () => {
  const params = new URLSearchParams();
  if (state.todoScope !== "all") params.set("scope", state.todoScope);
  const r = await api.post(`/todos/clear-completed?${params}`);
  toast(`Removed <b>${r.removed}</b> completed tasks`);
  await Promise.all([loadTodos(), loadOverview()]);
});

/* ---------- sessions ---------- */

async function loadSessions() {
  state.sessions = await api.get("/sessions?limit=100");
  renderSessions();
}

function renderSessions() {
  $("#sessions-empty").classList.toggle("show", state.sessions.length === 0);
  $("#session-list").innerHTML = state.sessions
    .map(
      (s, i) => `
      <div class="session-row" data-session="${esc(s.id)}" style="animation-delay:${Math.min(i * 0.02, 0.3)}s">
        <span class="session-type">${esc(s.chatType)}</span>
        <span class="session-title">${esc(s.title || s.id)}</span>
        <span class="session-meta">${esc(s.agentName || "")}</span>
        <span class="session-meta">${new Date(s.updatedAt).toLocaleString()}</span>
      </div>`
    )
    .join("");

  $$("[data-session]").forEach((row) =>
    row.addEventListener("click", () => {
      const s = state.sessions.find((x) => x.id === row.dataset.session);
      if (s) openSessionDrawer(s);
    })
  );
}

async function openSessionDrawer(session) {
  try {
    const messages = await api.get(`/sessions/${session.id}/messages`);
    const body = openDrawer(
      session.title || session.id,
      `${session.chatType}${session.agentName ? ` · ${session.agentName}` : ""} · ${new Date(session.updatedAt).toLocaleString()}`
    );
    body.innerHTML = `<div class="msg-view drawer-msgs">${messages
      .map(
        (m, i) => `
        <div class="msg-row">
          <div class="msg-role">${esc(m.role)}${m.agentName ? ` · ${esc(m.agentName)}` : ""}</div>
          <div class="msg-content${m.role === "assistant" ? " md" : ""}" data-msg-index="${i}"></div>
        </div>`
      )
      .join("") || '<div class="hint">No messages in this session.</div>'}</div>`;
    body.querySelectorAll("[data-msg-index]").forEach((el) => {
      const m = messages[Number(el.dataset.msgIndex)];
      if (m.role === "assistant" && m.content !== "null") {
        renderMarkdown(el, m.content);
      } else {
        el.textContent = m.content;
      }
    });
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
}

$("#btn-refresh-sessions").addEventListener("click", loadSessions);

/* ---------- approvals ---------- */

async function loadApprovals() {
  state.approvals = await api.get("/approvals");
  renderApprovals();
}

function renderApprovals() {
  $("#approvals-empty").classList.toggle("show", state.approvals.length === 0);
  $("#approval-list").innerHTML = state.approvals
    .map(
      (a) => `
      <div class="approval-card">
        <div class="approval-meta">${a.type === "task_claim" ? "TASK CLAIM" : "COMMAND"} · requested ${fmtTime(a.requestedAt)}${a.agentName ? ` · ${esc(a.agentName)}` : ""}${a.workingDirectory ? ` · in ${esc(a.workingDirectory)}` : ""}</div>
        <div style="margin-top:8px;font-weight:600">${esc(a.title)}</div>
        ${a.detail ? `<div class="hint" style="margin-top:4px">${esc(a.detail)}</div>` : ""}
        ${a.command ? `<div class="approval-command">${esc(a.command)}</div>` : ""}
        ${a.taskId ? `<div class="approval-meta" style="margin-top:6px">task: ${esc(a.taskId)}</div>` : ""}
        <div class="approval-actions">
          <button class="btn primary" data-approve="${esc(a.id)}">Approve</button>
          <button class="btn danger" data-deny="${esc(a.id)}">Deny</button>
        </div>
      </div>`
    )
    .join("");

  $$("[data-approve]").forEach((b) =>
    b.addEventListener("click", () => resolveApproval(b.dataset.approve, true))
  );
  $$("[data-deny]").forEach((b) =>
    b.addEventListener("click", () => resolveApproval(b.dataset.deny, false))
  );
}

async function resolveApproval(id, approved) {
  // Drop the toast either way: on success the resolved event confirms it,
  // and a failure means the request is already gone (timeout, other client).
  removeApprovalToast(id);
  try {
    await api.post(`/approvals/${id}`, { approved });
    await Promise.all([loadApprovals(), loadOverview()]);
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
}

/* ---------- settings ---------- */

async function loadSettings() {
  const s = await api.get("/settings");
  state.settings = s;
  $("#setting-max-agents").value = s.maxConcurrentAgents;
  $("#setting-approval").checked = s.requireCommandApproval;
  $("#setting-trust").checked = s.trustMode;
  $("#setting-judge").checked = s.judgeEnabled;
  $("#setting-judge").disabled = s.trustMode;
  $("#setting-approval-timeout").value = s.approvalTimeoutMinutes;
  $("#setting-scheduler-interval").value = s.schedulerIntervalSeconds;
  $("#setting-max-wakes").value = s.maxWakesPerHour;
  $("#setting-provider").textContent = s.provider;
  $("#setting-model").textContent = s.model || "—";

  await Promise.all([
    withPanelFallback(loadProviderPanel, "#prov-settings"),
    withPanelFallback(loadSearchProviderPanel, "#search-prov-settings"),
    withPanelFallback(loadGenerationPanel, null),
    withPanelFallback(loadToolsPanel, "#tool-grid"),
    withPanelFallback(loadSubAgentPanel, null),
    withPanelFallback(loadRulesPanel, "#rules-path"),
    withPanelFallback(loadModesPanel, "#modes-list"),
  ]);
}

async function withPanelFallback(loader, containerSel) {
  try {
    await loader();
  } catch (err) {
    const el = containerSel && $(containerSel);
    if (el) {
      el.innerHTML = `<span class="hint">failed to load: ${esc(err.message)} — leave and reopen Settings to retry</span>`;
    }
  }
}

/* ---- provider panel ---- */

let providerData = [];

async function loadProviderPanel() {
  providerData = await api.get("/providers");
  const active = providerData.find((p) => p.active);
  const select = $("#prov-select");
  select.innerHTML = providerData
    .map((p) => `<option value="${esc(p.name)}" ${p.active ? "selected" : ""}>${esc(p.displayName)}</option>`)
    .join("");
  renderProviderSettings(select.value);
  $("#prov-model").value = state.settings?.model || active?.model || "";
  await refreshModelList();
}

function renderProviderSettings(providerName) {
  const provider = providerData.find((p) => p.name === providerName);
  if (!provider) return;
  $("#prov-settings").innerHTML = provider.settings
    .map(
      (d) => `
      <label class="field"><span>${esc(d.label)}${d.required ? " *" : ""}${d.configured ? " · configured" : ""}${d.environmentVariable ? ` (env: ${esc(d.environmentVariable)})` : ""}</span>
        <input class="input prov-setting" data-key="${esc(d.key)}" type="${d.kind === "secret" ? "password" : d.kind === "number" ? "number" : "text"}"
          style="width:100%" placeholder="${esc(d.kind === "secret" && d.configured ? "(unchanged)" : d.defaultValue || "")}"
          value="${esc(d.value || "")}">
      </label>`
    )
    .join("");
  const model = provider.model || "";
  if (!provider.active) $("#prov-model").value = model;
}

async function refreshModelList() {
  try {
    const models = await api.get("/models");
    $("#prov-model-list").innerHTML = models.map((m) => `<option value="${esc(m.id)}">${esc(m.displayName)}</option>`).join("");
  } catch { /* provider may be unreachable */ }
}

$("#prov-select").addEventListener("change", (e) => renderProviderSettings(e.target.value));

$("#prov-apply").addEventListener("click", async () => {
  const settings = {};
  $$(".prov-setting").forEach((i) => {
    if (i.value.trim() !== "") settings[i.dataset.key] = i.value.trim();
  });
  $("#prov-status").textContent = "Connecting…";
  $("#prov-apply").disabled = true;
  try {
    const r = await api.post("/providers/switch", {
      provider: $("#prov-select").value,
      settings,
      model: $("#prov-model").value.trim() || null,
    });
    $("#prov-status").textContent = `Switched to ${r.provider} · ${r.model || "no model"}${r.connected ? "" : " (connection unverified)"}`;
    await Promise.all([loadSettings(), loadOverview()]);
  } catch (err) {
    $("#prov-status").textContent = `Failed: ${err.message}`;
  } finally {
    $("#prov-apply").disabled = false;
  }
});

$("#model-apply").addEventListener("click", async () => {
  const model = $("#prov-model").value.trim();
  if (!model) return;
  try {
    const r = await api.post("/model", { model });
    toast(`Model set to <b>${esc(r.model)}</b>`);
    await Promise.all([loadSettings(), loadOverview()]);
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
});

/* ---- search provider panel ---- */

let searchProviderData = [];

async function loadSearchProviderPanel() {
  searchProviderData = await api.get("/search-providers");
  const select = $("#search-prov-select");
  select.innerHTML = searchProviderData
    .map((p) => `<option value="${esc(p.name)}" ${p.active ? "selected" : ""}>${esc(p.displayName)}</option>`)
    .join("");
  renderSearchProviderSettings(select.value);
}

function renderSearchProviderSettings(providerName) {
  const provider = searchProviderData.find((p) => p.name === providerName);
  if (!provider) return;
  $("#search-prov-settings").innerHTML = provider.settings
    .map(
      (d) => `
      <label class="field"><span>${esc(d.label)}${d.required ? " *" : ""}${d.configured ? " · configured" : ""}${d.environmentVariable ? ` (env: ${esc(d.environmentVariable)})` : ""}</span>
        <input class="input search-prov-setting" data-key="${esc(d.key)}" type="${d.kind === "secret" ? "password" : d.kind === "number" ? "number" : "text"}"
          style="width:100%" placeholder="${esc(d.kind === "secret" && d.configured ? "(unchanged)" : d.defaultValue || "")}"
          value="${esc(d.value || "")}">
      </label>`
    )
    .join("");
}

$("#search-prov-select").addEventListener("change", (e) => renderSearchProviderSettings(e.target.value));

$("#search-prov-apply").addEventListener("click", async () => {
  const settings = {};
  $$(".search-prov-setting").forEach((i) => {
    if (i.value.trim() !== "") settings[i.dataset.key] = i.value.trim();
  });
  $("#search-prov-status").textContent = "Saving…";
  $("#search-prov-apply").disabled = true;
  try {
    const r = await api.post("/search-providers/switch", {
      provider: $("#search-prov-select").value,
      settings,
    });
    $("#search-prov-status").textContent = `Search provider set to ${r.provider}`;
    await loadSearchProviderPanel();
  } catch (err) {
    $("#search-prov-status").textContent = `Failed: ${err.message}`;
  } finally {
    $("#search-prov-apply").disabled = false;
  }
});

/* ---- generation panel ---- */

async function loadGenerationPanel() {
  const c = await api.get("/agent-config");
  $("#gen-temp").value = c.temperature ?? "";
  $("#gen-maxtokens").value = c.maxTokens ?? "";
  $("#gen-topp").value = c.topP ?? "";
  $("#gen-maxhistory").value = c.maxHistoryMessages ?? "";
  $("#gen-streaming").checked = c.enableStreaming;
  $("#gen-history").checked = c.maintainHistory;
  $("#gen-userrules").checked = c.enableUserRules;
}

$("#gen-apply").addEventListener("click", async () => {
  try {
    await api.put("/agent-config", {
      temperature: parseFloat($("#gen-temp").value) || null,
      maxTokens: parseInt($("#gen-maxtokens").value, 10) || null,
      topP: parseFloat($("#gen-topp").value) || null,
      maxHistoryMessages: parseInt($("#gen-maxhistory").value, 10) || null,
      enableStreaming: $("#gen-streaming").checked,
      maintainHistory: $("#gen-history").checked,
      enableUserRules: $("#gen-userrules").checked,
    });
    toast("Generation settings <b>applied</b>");
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
});

/* ---- tools panel ---- */

async function loadToolsPanel() {
  const tools = await api.get("/tools");
  $("#tool-grid").innerHTML = tools
    .map(
      (t) => `<label class="check-row" title="${esc(t.description)}"><input type="checkbox" data-tool="${esc(t.name)}" ${t.enabled ? "checked" : ""}> <span style="font-family:var(--font-mono);font-size:11.5px">${esc(t.name)}</span></label>`
    )
    .join("");
}

$("#tools-apply").addEventListener("click", async () => {
  const names = $$("#tool-grid [data-tool]").filter((c) => c.checked).map((c) => c.dataset.tool);
  if (names.length === 0) {
    toast("<b>Select at least one tool</b>");
    return;
  }
  try {
    await api.put("/agent-config", { toolNames: names });
    toast(`Orchestrator now has <b>${names.length}</b> tools`);
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
});

/* ---- sub-agent defaults panel ---- */

async function loadSubAgentPanel() {
  const d = await api.get("/subagent-defaults");
  $("#sa-model").value = d.defaultModel || "";
  $("#sa-temp").value = d.defaultTemperature;
  $("#sa-maxtokens").value = d.defaultMaxTokens;
  $("#sa-topp").value = d.defaultTopP;
  $("#sa-tools").checked = d.defaultEnableTools;
  $("#sa-review").checked = d.enableReviewStage;
  $("#sa-reviewer").value = d.reviewerModel || "";
  $("#sa-revisions").value = d.maxRevisionCycles;
}

$("#sa-apply").addEventListener("click", async () => {
  try {
    await api.put("/subagent-defaults", {
      defaultModel: $("#sa-model").value.trim() || null,
      defaultTemperature: parseFloat($("#sa-temp").value),
      defaultMaxTokens: parseInt($("#sa-maxtokens").value, 10),
      defaultTopP: parseFloat($("#sa-topp").value),
      defaultEnableTools: $("#sa-tools").checked,
      enableReviewStage: $("#sa-review").checked,
      reviewerModel: $("#sa-reviewer").value,
      maxRevisionCycles: parseInt($("#sa-revisions").value, 10),
    });
    toast("Sub-agent defaults <b>applied</b>");
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
});

/* ---- user rules panel ---- */

async function loadRulesPanel() {
  const r = await api.get("/user-rules");
  $("#rules-content").value = r.content || "";
  $("#rules-path").textContent = `${r.path}${r.error ? ` — ${r.error}` : ""}${r.wasTruncated ? " (truncated)" : ""}`;
}

$("#rules-save").addEventListener("click", async () => {
  try {
    await api.put("/user-rules", { content: $("#rules-content").value });
    toast("User rules <b>saved</b> — applies to newly created agents");
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
});

/* ---- modes panel ---- */

async function loadModesPanel() {
  const modes = await api.get("/modes");
  $("#modes-list").innerHTML = modes.length === 0
    ? '<p class="hint">No saved modes.</p>'
    : modes
        .map(
          (m) => `
          <div class="kv">
            <span><b>${esc(m.name)}</b>${m.description ? ` — ${esc(m.description)}` : ""}<br><span class="hint">${esc(m.model)} · temp ${m.temperature}${m.toolCount != null ? ` · ${m.toolCount} tools` : ""}</span></span>
            <span><button class="btn sm" data-mode-apply="${esc(m.id)}">Apply</button></span>
          </div>`
        )
        .join("");
  $$("[data-mode-apply]").forEach((b) =>
    b.addEventListener("click", async () => {
      try {
        const r = await api.post(`/modes/${b.dataset.modeApply}/apply`, {});
        toast(`Mode <b>${esc(r.applied)}</b> applied (model: ${esc(r.model)})`);
        await loadSettings();
      } catch (err) {
        toast(`<b>Error:</b> ${esc(err.message)}`);
      }
    })
  );
}

async function putSetting(patch, message) {
  try {
    const r = await api.put("/settings", patch);
    state.settings = r;
    if (message) toast(message);
    return r;
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
    await loadSettings();
    return null;
  }
}

$("#save-max-agents").addEventListener("click", async () => {
  const value = parseInt($("#setting-max-agents").value, 10);
  if (!value || value < 1) return;
  const r = await putSetting({ maxConcurrentAgents: value });
  if (r) toast(`Max concurrent agents set to <b>${r.maxConcurrentAgents}</b>`);
  await loadOverview();
});

$("#setting-approval").addEventListener("change", (e) =>
  putSetting({ requireCommandApproval: e.target.checked },
    `Command approval ${e.target.checked ? "<b>enabled</b>" : "<b>disabled</b>"}`)
);

$("#setting-trust").addEventListener("change", async (e) => {
  if (e.target.checked) {
    const ok = await confirmModal(
      "Enable trust mode",
      "Trust mode auto-approves EVERY shell command from every agent, with no review.",
      "Enable trust mode"
    );
    if (!ok) {
      e.target.checked = false;
      return;
    }
  }
  await putSetting({ trustMode: e.target.checked },
    e.target.checked ? "<b>Trust mode ON</b> — all commands auto-approve" : "Trust mode off");
  $("#setting-judge").disabled = e.target.checked;
});

$("#setting-judge").addEventListener("change", (e) =>
  putSetting({ judgeEnabled: e.target.checked },
    `Command judge ${e.target.checked ? "<b>enabled</b>" : "<b>disabled</b>"}`)
);

$("#save-approval-timeout").addEventListener("click", () => {
  const v = parseInt($("#setting-approval-timeout").value, 10);
  if (isNaN(v) || v < 0) return;
  putSetting({ approvalTimeoutMinutes: v },
    v === 0 ? "Approvals now <b>wait forever</b>" : `Approval timeout set to <b>${v}m</b>`);
});

$("#save-scheduler-interval").addEventListener("click", () => {
  const v = parseInt($("#setting-scheduler-interval").value, 10);
  if (!v || v < 5) return;
  putSetting({ schedulerIntervalSeconds: v }, `Scheduler interval saved (<b>${v}s</b> after restart)`);
});

$("#save-max-wakes").addEventListener("click", () => {
  const v = parseInt($("#setting-max-wakes").value, 10);
  if (!v || v < 1) return;
  putSetting({ maxWakesPerHour: v }, `Max wakes per hour set to <b>${v}</b>`);
});

/* ---------- SSE ---------- */

function connectEvents() {
  // EventSource cannot send headers, so the token rides in the query string.
  const es = new EventSource(`/api/events?token=${encodeURIComponent(API_TOKEN)}`);
  let wasDown = false;

  es.onopen = () => {
    $("#conn-dot").classList.add("on");
    $("#conn-label").textContent = "live";
    if (wasDown) {
      // Events were missed while disconnected (e.g. server restart) — re-sync everything.
      wasDown = false;
      toast("<b>Reconnected</b> — refreshing state");
      loadOverview().catch(() => {});
      refreshView(state.view);
    }
  };
  es.onerror = () => {
    wasDown = true;
    $("#conn-dot").classList.remove("on");
    $("#conn-label").textContent = "reconnecting…";
  };

  // Bursts of events (e.g. spawning a 30-agent fleet) coalesce into one refresh.
  const pendingRefreshes = new Set();
  let refreshTimer = null;
  const refreshIf = (views, fns) => {
    if (views.includes(state.view)) fns.forEach((f) => pendingRefreshes.add(f));
    pendingRefreshes.add(loadOverview);
    clearTimeout(refreshTimer);
    refreshTimer = setTimeout(() => {
      const batch = [...pendingRefreshes];
      pendingRefreshes.clear();
      batch.forEach((f) => f().catch(() => {}));
    }, 200);
  };

  es.addEventListener("agent.created", (e) => {
    const d = JSON.parse(e.data);
    logActivity(`agent <b>${esc(d.name)}</b> created`);
    refreshIf(["agents", "orchestrator"], [loadAgents]);
  });

  es.addEventListener("agent.status", (e) => {
    const d = JSON.parse(e.data);
    logActivity(`<b>${esc(d.name)}</b> → ${esc(d.status)}`);
    refreshIf(["agents", "work", "orchestrator"], [loadAgents, loadTasks]);
  });

  es.addEventListener("task.completed", (e) => {
    const d = JSON.parse(e.data);
    const desc = d.description ? `: ${esc(d.description.slice(0, 60))}` : "";
    logActivity(`task <b>${esc(d.taskId)}</b> ${d.success ? "completed" : "failed"} (${esc(d.agentName)})${desc}`);
    toast(`Task ${d.success ? "completed" : "<b>failed</b>"} — ${esc(d.agentName)}${desc}`);
    refreshIf(["work", "agents", "orchestrator"], [loadTasks, loadAgents]);
  });

  es.addEventListener("agents.cleared", () => refreshIf(["agents", "work", "orchestrator"], [loadAgents, loadTasks]));
  es.addEventListener("tasks.cleared", () => refreshIf(["work"], [loadTasks]));
  es.addEventListener("todos.changed", () => refreshIf(["work", "orchestrator"], [loadTodos]));
  es.addEventListener("tasks.changed", () => refreshIf(["work", "orchestrator"], [loadTodos, loadWakes]));
  es.addEventListener("settings.changed", () => refreshIf(["settings"], [loadSettings]));
  es.addEventListener("wake.enqueued", (e) => {
    const d = JSON.parse(e.data);
    logActivity(`wake queued: <b>${esc(d.kind)}</b>${d.taskId ? ` (${esc(d.taskId)})` : ""}`);
    refreshIf(["work"], [loadWakes]);
  });
  es.addEventListener("wake.delivered", () => refreshIf(["work"], [loadWakes]));
  es.addEventListener("task.due", (e) => {
    const d = JSON.parse(e.data);
    logActivity(`recurring task due: <b>${esc(d.title)}</b>${d.skipped ? " (skipped)" : ""}`);
  });
  es.addEventListener("task.unblocked", (e) => {
    const d = JSON.parse(e.data);
    logActivity(`task unblocked: <b>${esc(d.title)}</b>`);
    refreshIf(["work", "orchestrator"], [loadTodos]);
  });
  es.addEventListener("task.dispatched", (e) => {
    const d = JSON.parse(e.data);
    logActivity(`task <b>${esc(d.taskId)}</b> dispatched to <b>${esc(d.agentName)}</b>`);
    refreshIf(["work", "agents", "orchestrator"], [loadTodos, loadAgents]);
  });

  es.addEventListener("approval.requested", (e) => {
    const d = JSON.parse(e.data);
    const what = d.command || d.title || "decision";
    logActivity(`approval requested: <b>${esc(what.slice(0, 60))}</b>`);
    if (state.view !== "approvals") approvalToast(d);
    refreshIf(["approvals"], [loadApprovals]);
  });

  es.addEventListener("approval.resolved", (e) => {
    const d = JSON.parse(e.data);
    removeApprovalToast(d.id);
    logDecision(`${d.approved ? "approved" : "denied"} by <b>${esc(d.resolvedBy || "user")}</b>${d.command ? `: ${esc(d.command.slice(0, 70))}` : ""}${d.reason ? ` — ${esc(d.reason.slice(0, 80))}` : ""}`);
    refreshIf(["approvals"], [loadApprovals]);
  });

  es.addEventListener("approval.judged", (e) => {
    const d = JSON.parse(e.data);
    logActivity(`judge ${esc(d.decision)}: <b>${esc((d.command || "").slice(0, 50))}</b> (${esc(d.agentName || "?")})`);
  });

  es.addEventListener("orchestrator.state", (e) => {
    const d = JSON.parse(e.data);
    if (d.busy && !state.orchestratorBusy) {
      currentTurnTools = [];
      renderToolLog();
    }
    setOrchestratorBusy(d.busy);
  });

  es.addEventListener("orchestrator.chunk", (e) => {
    const d = JSON.parse(e.data);
    streamBuffer += d.content;
    $("#chat-stream").hidden = false;
    scheduleStreamRender();
  });

  es.addEventListener("orchestrator.toolcall", (e) => {
    const d = JSON.parse(e.data);
    $("#chat-stream").hidden = false;
    currentTurnTools.push({ name: d.toolName, args: summarizeToolArgs(d.arguments) });
    renderToolLog();
    logActivity(`orchestrator ran <b>${esc(d.toolName)}</b>`);
  });

  es.addEventListener("orchestrator.message", (e) => {
    const entry = JSON.parse(e.data);
    // Exact duplicates (SSE replay, reconnect races) are dropped by id.
    if (entry.id && state.transcript.some((x) => x.id === entry.id)) return;
    if (entry.role === "user") {
      const match = state.transcript.find((x) => x.optimistic && x.content === entry.content);
      if (match) {
        Object.assign(match, entry);
        delete match.optimistic;
        if (state.view === "orchestrator") renderTranscript();
        return;
      }
    }
    if (entry.role === "assistant" && currentTurnTools.length) {
      entry.tools = currentTurnTools.slice();
      currentTurnTools = [];
    }
    state.transcript.push(entry);
    if (state.view === "orchestrator") renderTranscript();
  });

  es.addEventListener("orchestrator.cleared", () => {
    state.transcript = [];
    currentTurnTools = [];
    setOrchestratorBusy(false);
    if (state.view === "orchestrator") renderTranscript({ stick: true });
    toast("Started a <b>new conversation</b>");
  });

  es.addEventListener("provider.changed", (e) => {
    const d = JSON.parse(e.data);
    toast(`Provider: <b>${esc(d.provider)}</b> · ${esc(d.model || "no model")}`);
    state.models = [];
    ensureModels();
    loadOverview().catch(() => {});
    if (state.view === "settings") loadSettings().catch(() => {});
  });
}

/* ---------- boot ---------- */

(async function boot() {
  connectEvents();
  await loadOverview().catch(() => {});
  const { view, sub } = currentRoute();
  if (sub) state.workTab = sub;
  showView(view);
  if (view === "work") showWorkTab(state.workTab);
  ensureModels();

  setInterval(() => {
    loadOverview().catch(() => {});
    if (state.view === "agents" || state.view === "orchestrator") loadAgents().catch(() => {});
    if (state.view === "work" || state.view === "orchestrator") loadTasks().catch(() => {});
  }, 10000);
})();
