/* Saturn web UI */
"use strict";

/* ---------- helpers ---------- */

const $ = (sel) => document.querySelector(sel);
const $$ = (sel) => Array.from(document.querySelectorAll(sel));

const api = {
  async request(method, path, body) {
    const res = await fetch(`/api${path}`, {
      method,
      headers: body ? { "Content-Type": "application/json" } : undefined,
      body: body ? JSON.stringify(body) : undefined,
    });
    if (!res.ok) {
      let message = `${res.status} ${res.statusText}`;
      try {
        const data = await res.json();
        message = data.error || data.detail || message;
      } catch { /* not json */ }
      throw new Error(message);
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
  const div = document.createElement("div");
  div.textContent = value ?? "";
  return div.innerHTML;
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

/* ---------- state ---------- */

const state = {
  view: "overview",
  overview: null,
  agents: [],
  tasks: { running: [], completed: [] },
  todos: [],
  todoFilter: "all",
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
  tasks: "Tasks",
  orchestrator: "Orchestrator",
  todos: "Todo List",
  sessions: "Sessions",
  approvals: "Approvals",
  settings: "Settings",
};

function showView(name) {
  state.view = name;
  $$(".nav-item").forEach((b) => b.classList.toggle("active", b.dataset.view === name));
  $$(".view").forEach((v) => v.classList.toggle("active", v.id === `view-${name}`));
  $("#view-title").textContent = VIEW_TITLES[name] || name;
  refreshView(name);
}

async function refreshView(name) {
  try {
    if (name === "overview") await loadOverview();
    else if (name === "agents") await loadAgents();
    else if (name === "tasks") await loadTasks();
    else if (name === "orchestrator") await loadTranscript();
    else if (name === "todos") await loadTodos();
    else if (name === "sessions") await loadSessions();
    else if (name === "approvals") await loadApprovals();
    else if (name === "settings") await loadSettings();
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
}

$$(".nav-item").forEach((b) => b.addEventListener("click", () => showView(b.dataset.view)));
$$("[data-goto]").forEach((el) => el.addEventListener("click", () => showView(el.dataset.goto)));

/* ---------- activity feed ---------- */

function logActivity(html) {
  state.activity.unshift({ time: new Date(), html });
  state.activity = state.activity.slice(0, 120);
  renderActivity();
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
  setBadge("#badge-tasks", o.tasks.running);
  setBadge("#badge-todos", o.todos.open);
  setBadge("#badge-approvals", o.pendingApprovals);
}

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

/* ---------- agents ---------- */

async function loadAgents() {
  state.agents = await api.get("/agents");
  renderAgents();
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
          <button class="btn sm danger" data-terminate="${esc(a.agentId)}">Terminate</button>
        </div>
      </div>`;
    })
    .join("");

  $$("#agent-grid [data-handoff]").forEach((b) =>
    b.addEventListener("click", () => openHandoffModal(b.dataset.handoff))
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

async function terminateAll() {
  if (!confirm("Terminate all agents and clear completed tasks?")) return;
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

function closeModal() {
  $("#modal-backdrop").hidden = true;
  $("#modal-body").innerHTML = "";
}

$("#modal-close").addEventListener("click", closeModal);
$("#modal-backdrop").addEventListener("mousedown", (e) => {
  if (e.target === $("#modal-backdrop")) closeModal();
});
document.addEventListener("keydown", (e) => {
  if (e.key === "Escape" && !$("#modal-backdrop").hidden) closeModal();
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
}

function renderTasks() {
  const { running, completed } = state.tasks;
  $("#tasks-empty").classList.toggle("show", running.length === 0 && completed.length === 0);

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
        <span class="task-desc">${esc(t.result.slice(0, 160))}</span>
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
        <pre class="result-pre" style="margin-top:14px">${esc(t.result)}</pre>`
      );
    })
  );
}

$("#btn-clear-tasks").addEventListener("click", async () => {
  await api.post("/tasks/clear-completed");
  await Promise.all([loadTasks(), loadOverview()]);
  toast("Completed tasks cleared");
});

/* ---------- orchestrator ---------- */

let streamBuffer = "";

async function loadTranscript() {
  const t = await api.get("/orchestrator/transcript");
  state.transcript = t.entries;
  setOrchestratorBusy(t.busy);
  renderTranscript();
}

function renderTranscript() {
  $("#chat-log").innerHTML = state.transcript
    .map((e) => `<div class="chat-msg ${esc(e.role)}">${esc(e.content)}</div>`)
    .join("");
  $("#chat-log").scrollTop = $("#chat-log").scrollHeight;
}

function setOrchestratorBusy(busy) {
  state.orchestratorBusy = busy;
  $("#chat-send").disabled = busy;
  $("#chat-cancel").hidden = !busy;
  if (!busy) {
    $("#chat-stream").hidden = true;
    $("#chat-stream-text").textContent = "";
    $("#tool-chips").innerHTML = "";
    streamBuffer = "";
  }
}

$("#chat-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  const message = $("#chat-text").value.trim();
  if (!message || state.orchestratorBusy) return;
  try {
    await api.post("/orchestrator/message", { message });
    $("#chat-text").value = "";
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
});

$("#chat-text").addEventListener("keydown", (e) => {
  if (e.key === "Enter" && !e.shiftKey) {
    e.preventDefault();
    $("#chat-form").requestSubmit();
  }
});

$("#chat-cancel").addEventListener("click", () => api.post("/orchestrator/cancel").catch(() => {}));

/* ---------- todos ---------- */

async function loadTodos() {
  state.todos = await api.get("/todos");
  renderTodos();
}

function renderTodos() {
  const filtered = state.todos.filter((t) => {
    if (state.todoFilter === "open") return t.status !== "done";
    if (state.todoFilter === "done") return t.status === "done";
    return true;
  });

  $("#todos-empty").classList.toggle("show", filtered.length === 0);

  $("#todo-list").innerHTML = filtered
    .map(
      (t, i) => `
      <li class="todo-item ${t.status === "done" ? "done" : ""}" style="animation-delay:${Math.min(i * 0.02, 0.3)}s">
        <input type="checkbox" class="todo-check" data-id="${esc(t.id)}" ${t.status === "done" ? "checked" : ""}>
        <span class="todo-title" contenteditable="true" spellcheck="false" data-id="${esc(t.id)}">${esc(t.title)}</span>
        <button class="todo-pri ${esc(t.priority)}" data-id="${esc(t.id)}" data-pri="${esc(t.priority)}" title="cycle priority">${esc(t.priority)}</button>
        <span class="todo-move">
          <button data-move-up="${esc(t.id)}" title="move up">▲</button>
          <button data-move-down="${esc(t.id)}" title="move down">▼</button>
        </span>
        <button class="todo-del" data-del="${esc(t.id)}" title="delete">✕</button>
      </li>`
    )
    .join("");

  $$(".todo-check").forEach((c) =>
    c.addEventListener("change", () =>
      updateTodo(c.dataset.id, { status: c.checked ? "done" : "pending" })
    )
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

  $$("[data-move-up]").forEach((b) => b.addEventListener("click", () => moveTodo(b.dataset.moveUp, -1)));
  $$("[data-move-down]").forEach((b) => b.addEventListener("click", () => moveTodo(b.dataset.moveDown, 1)));

  $$("[data-del]").forEach((b) =>
    b.addEventListener("click", async () => {
      await api.del(`/todos/${b.dataset.del}`);
      await Promise.all([loadTodos(), loadOverview()]);
    })
  );
}

async function updateTodo(id, patch) {
  try {
    await api.patch(`/todos/${id}`, patch);
    await Promise.all([loadTodos(), loadOverview()]);
  } catch (err) {
    toast(`<b>Error:</b> ${esc(err.message)}`);
  }
}

function moveTodo(id, delta) {
  const ordered = [...state.todos].sort((a, b) => a.order - b.order);
  const index = ordered.findIndex((t) => t.id === id);
  if (index < 0) return;
  const target = index + delta;
  if (target < 0 || target >= ordered.length) return;
  updateTodo(id, { order: target });
}

$("#todo-form").addEventListener("submit", async (e) => {
  e.preventDefault();
  const title = $("#todo-title").value.trim();
  if (!title) return;
  await api.post("/todos", { title, priority: $("#todo-priority").value });
  $("#todo-title").value = "";
  await Promise.all([loadTodos(), loadOverview()]);
});

$("#todo-filter").addEventListener("click", (e) => {
  const btn = e.target.closest(".seg-item");
  if (!btn) return;
  state.todoFilter = btn.dataset.filter;
  $$("#todo-filter .seg-item").forEach((b) => b.classList.toggle("active", b === btn));
  renderTodos();
});

$("#btn-clear-todos").addEventListener("click", async () => {
  const r = await api.post("/todos/clear-completed");
  toast(`Removed <b>${r.removed}</b> completed todos`);
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
    row.addEventListener("click", async () => {
      const id = row.dataset.session;
      try {
        const messages = await api.get(`/sessions/${id}/messages`);
        openModal(
          "Session transcript",
          `<div class="msg-view">${messages
            .map(
              (m) => `
              <div class="msg-row">
                <div class="msg-role">${esc(m.role)}${m.agentName ? ` · ${esc(m.agentName)}` : ""}</div>
                <div class="msg-content">${esc(m.content)}</div>
              </div>`
            )
            .join("") || '<div class="hint">No messages in this session.</div>'}</div>`
        );
      } catch (err) {
        toast(`<b>Error:</b> ${esc(err.message)}`);
      }
    })
  );
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
        <div class="approval-meta">requested ${fmtTime(a.requestedAt)} · in ${esc(a.workingDirectory)}</div>
        <div class="approval-command">${esc(a.command)}</div>
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
  $("#setting-provider").textContent = s.provider;
  $("#setting-model").textContent = s.model || "—";
}

$("#save-max-agents").addEventListener("click", async () => {
  const value = parseInt($("#setting-max-agents").value, 10);
  if (!value || value < 1) return;
  const r = await api.put("/settings", { maxConcurrentAgents: value });
  toast(`Max concurrent agents set to <b>${r.maxConcurrentAgents}</b>`);
  await loadOverview();
});

$("#setting-approval").addEventListener("change", async (e) => {
  const r = await api.put("/settings", { requireCommandApproval: e.target.checked });
  toast(`Command approval ${r.requireCommandApproval ? "<b>enabled</b>" : "<b>disabled</b>"}`);
});

/* ---------- SSE ---------- */

function connectEvents() {
  const es = new EventSource("/api/events");

  es.onopen = () => {
    $("#conn-dot").classList.add("on");
    $("#conn-label").textContent = "live";
  };
  es.onerror = () => {
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
    refreshIf(["agents"], [loadAgents]);
  });

  es.addEventListener("agent.status", (e) => {
    const d = JSON.parse(e.data);
    logActivity(`<b>${esc(d.name)}</b> → ${esc(d.status)}`);
    refreshIf(["agents", "tasks"], [loadAgents, loadTasks]);
  });

  es.addEventListener("task.completed", (e) => {
    const d = JSON.parse(e.data);
    logActivity(`task <b>${esc(d.taskId)}</b> ${d.success ? "completed" : "failed"} (${esc(d.agentName)})`);
    toast(`Task <b>${esc(d.taskId)}</b> ${d.success ? "completed" : "<b>failed</b>"} — ${esc(d.agentName)}`);
    refreshIf(["tasks", "agents"], [loadTasks, loadAgents]);
  });

  es.addEventListener("agents.cleared", () => refreshIf(["agents", "tasks"], [loadAgents, loadTasks]));
  es.addEventListener("tasks.cleared", () => refreshIf(["tasks"], [loadTasks]));
  es.addEventListener("todos.changed", () => refreshIf(["todos"], [loadTodos]));
  es.addEventListener("settings.changed", () => refreshIf(["settings"], [loadSettings]));

  es.addEventListener("approval.requested", (e) => {
    const d = JSON.parse(e.data);
    logActivity(`approval requested: <b>${esc(d.command.slice(0, 60))}</b>`);
    toast(`<b>Approval needed:</b> ${esc(d.command.slice(0, 80))}`, 6000);
    refreshIf(["approvals"], [loadApprovals]);
  });

  es.addEventListener("approval.resolved", () => refreshIf(["approvals"], [loadApprovals]));

  es.addEventListener("orchestrator.state", (e) => {
    const d = JSON.parse(e.data);
    setOrchestratorBusy(d.busy);
  });

  es.addEventListener("orchestrator.chunk", (e) => {
    const d = JSON.parse(e.data);
    streamBuffer += d.content;
    $("#chat-stream").hidden = false;
    $("#chat-stream-text").textContent = streamBuffer;
    $("#chat-stream-text").scrollTop = $("#chat-stream-text").scrollHeight;
  });

  es.addEventListener("orchestrator.toolcall", (e) => {
    const d = JSON.parse(e.data);
    $("#chat-stream").hidden = false;
    const chip = document.createElement("span");
    chip.className = "tool-chip";
    chip.textContent = d.toolName;
    $("#tool-chips").appendChild(chip);
    logActivity(`orchestrator ran <b>${esc(d.toolName)}</b>`);
  });

  es.addEventListener("orchestrator.message", (e) => {
    const entry = JSON.parse(e.data);
    state.transcript.push(entry);
    if (state.view === "orchestrator") renderTranscript();
  });
}

/* ---------- boot ---------- */

(async function boot() {
  connectEvents();
  await loadOverview().catch(() => {});
  await refreshView(state.view);
  ensureModels();

  setInterval(() => {
    loadOverview().catch(() => {});
    if (state.view === "agents") loadAgents().catch(() => {});
    if (state.view === "tasks") loadTasks().catch(() => {});
  }, 10000);
})();
