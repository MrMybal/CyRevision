const state = {
  token: sessionStorage.getItem("cyrevision-token") || "",
  projects: [],
  locale: localStorage.getItem("cyrevision-language") || "en",
  messages: {}
};
const login = document.querySelector("#login");
const dashboard = document.querySelector("#dashboard");
const loginForm = document.querySelector("#login-form");
const loginError = document.querySelector("#login-error");
const activity = document.querySelector("#activity");

function t(key, values = {}) {
  let message = state.messages[key] || key;
  for (const [name, value] of Object.entries(values)) {
    message = message.replaceAll(`{${name}}`, value);
  }
  return message;
}

async function setLanguage(code, persist = true) {
  const supported = ["en", "fr"];
  state.locale = supported.includes(code) ? code : "en";
  const response = await fetch(`/locales/${state.locale}.json`);
  state.messages = response.ok ? await response.json() : {};
  document.documentElement.lang = state.locale;
  document.querySelectorAll("[data-language-picker]").forEach(select => { select.value = state.locale; });
  document.querySelectorAll("[data-i18n]").forEach(element => {
    element.textContent = t(element.dataset.i18n);
  });
  document.querySelectorAll("[data-i18n-placeholder]").forEach(element => {
    element.placeholder = t(element.dataset.i18nPlaceholder);
  });
  if (persist) localStorage.setItem("cyrevision-language", state.locale);
  if (state.projects.length) renderProjects();
}

async function api(path, options = {}) {
  const headers = new Headers(options.headers || {});
  headers.set("Authorization", `Bearer ${state.token}`);
  if (options.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");
  const response = await fetch(path, { ...options, headers });
  if (response.status === 401) throw new Error(t("error.token"));
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.error || problem.detail || t("error.http", { status: response.status }));
  }
  return response.status === 204 ? null : response.json();
}

function setAuthenticated(value) {
  login.classList.toggle("hidden", value);
  dashboard.classList.toggle("hidden", !value);
}

function setActivity(message, isError = false) {
  activity.textContent = message;
  activity.style.color = isError ? "#ff91a4" : "";
}

async function loadProjects() {
  setActivity(t("common.refreshing"));
  state.projects = await api("/api/v1/projects");
  renderProjects();
  const culture = state.locale === "fr" ? "fr-FR" : "en-US";
  const time = new Date().toLocaleTimeString(culture, { hour: "2-digit", minute: "2-digit" });
  setActivity(t("activity.refreshed", { time }));
}

function renderProjects() {
  const list = document.querySelector("#project-list");
  const empty = document.querySelector("#empty-state");
  list.replaceChildren();
  empty.classList.toggle("hidden", state.projects.length > 0);
  document.querySelector("#project-count").textContent = state.projects.length;
  document.querySelector("#git-count").textContent = state.projects.filter(p => p.features.gitEnabled).length;
  document.querySelector("#sync-count").textContent = state.projects.filter(p => p.features.peerSyncEnabled).length;
  document.querySelector("#backup-count").textContent = state.projects.filter(p => p.features.backupEnabled).length;

  for (const project of state.projects) {
    const fragment = document.querySelector("#project-template").content.cloneNode(true);
    const row = fragment.querySelector(".project-row");
    fragment.querySelector(".project-avatar").textContent = project.name.slice(0, 1).toUpperCase();
    fragment.querySelector(".project-name").textContent = project.name;
    fragment.querySelector(".project-path").textContent = project.rootPath;
    const badges = fragment.querySelector(".badges");
    for (const [enabled, label] of [
      [project.features.gitEnabled, "Git"], [project.features.lfsEnabled, "LFS"],
      [project.features.peerSyncEnabled, "Sync"], [project.features.backupEnabled, "Backup"]
    ]) {
      if (!enabled) continue;
      const badge = document.createElement("span");
      badge.className = "badge";
      badge.textContent = label;
      badges.append(badge);
    }
    fragment.querySelector(".backup-action").addEventListener("click", () => createSnapshot(project, row));
    fragment.querySelector(".history-action").textContent = t("project.history");
    fragment.querySelector(".remove-action").textContent = t("project.remove");
    fragment.querySelector(".history-action").addEventListener("click", () => showHistory(project, row));
    fragment.querySelector(".remove-action").addEventListener("click", () => removeProject(project));
    list.append(fragment);
  }
}

async function createSnapshot(project, row) {
  try {
    setActivity(t("activity.snapshot", { name: project.name }));
    const snapshot = await api(`/api/v1/projects/${project.id}/backups`, { method: "POST" });
    showDetail(row, t("snapshot.created", {
      id: snapshot.snapshotId.slice(0, 8),
      logical: formatBytes(snapshot.logicalSizeBytes),
      stored: formatBytes(snapshot.storedSizeBytes)
    }));
    setActivity(t("activity.snapshotDone"));
  } catch (error) { setActivity(error.message, true); }
}

async function showHistory(project, row) {
  try {
    const revisions = await api(`/api/v1/projects/${project.id}/git/history`);
    const text = revisions.length
      ? revisions.slice(0, 12).map(r => `${r.shortHash}  ${r.subject}  — ${r.authorName}`).join("\n")
      : t("history.empty");
    showDetail(row, text);
  } catch (error) { setActivity(error.message, true); }
}

function showDetail(row, text) {
  const detail = row.querySelector(".project-detail");
  detail.textContent = text;
  detail.classList.remove("hidden");
}

async function removeProject(project) {
  if (!confirm(t("remove.confirm", { name: project.name }))) return;
  try {
    await api(`/api/v1/projects/${project.id}`, { method: "DELETE" });
    await loadProjects();
  } catch (error) { setActivity(error.message, true); }
}

function formatBytes(bytes) {
  const units = state.locale === "fr" ? ["o", "Ko", "Mo", "Go", "To"] : ["B", "KB", "MB", "GB", "TB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit += 1; }
  const culture = state.locale === "fr" ? "fr-FR" : "en-US";
  return `${value.toLocaleString(culture, { maximumFractionDigits: 1 })} ${units[unit]}`;
}

loginForm.addEventListener("submit", async event => {
  event.preventDefault();
  state.token = new FormData(loginForm).get("token").trim();
  try {
    await loadProjects();
    sessionStorage.setItem("cyrevision-token", state.token);
    loginError.textContent = "";
    setAuthenticated(true);
  } catch (error) {
    loginError.textContent = error.message;
    state.token = "";
  }
});

document.querySelector("#create-project").addEventListener("submit", async event => {
  event.preventDefault();
  const name = document.querySelector("#project-name").value.trim();
  const preset = document.querySelector("#project-preset").value;
  try {
    setActivity(t("activity.creating", { name }));
    await api("/api/v1/projects", { method: "POST", body: JSON.stringify({ name, preset }) });
    event.target.reset();
    await loadProjects();
  } catch (error) { setActivity(error.message, true); }
});

document.querySelectorAll("[data-language-picker]").forEach(select => {
  select.addEventListener("change", () => setLanguage(select.value).catch(error => setActivity(error.message, true)));
});
document.querySelector("#refresh").addEventListener("click", () => loadProjects().catch(error => setActivity(error.message, true)));
document.querySelector("#logout").addEventListener("click", () => {
  sessionStorage.removeItem("cyrevision-token");
  state.token = "";
  state.projects = [];
  setAuthenticated(false);
});

async function start() {
  await setLanguage(state.locale, false);
  if (state.token) {
    loadProjects().then(() => setAuthenticated(true)).catch(() => {
      sessionStorage.removeItem("cyrevision-token");
      state.token = "";
      setAuthenticated(false);
    });
  }
}

start().catch(error => { loginError.textContent = error.message; });
