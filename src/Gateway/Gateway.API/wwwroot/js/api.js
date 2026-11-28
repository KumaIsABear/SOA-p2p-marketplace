// Central HTTP Client, Auth Token Store, Activity Log, ProblemDetails & UI Helpers

const STORAGE_KEYS = {
  baseUrl: "p2p_api_base_url",
  accessToken: "p2p_access_token",
  refreshToken: "p2p_refresh_token",
  user: "p2p_user_profile",
  demoRbac: "p2p_demo_rbac_all_tabs",
};

export const SEED_TEMPLATES = [
  { templateId: "11111111-1111-4111-8111-000000000001", name: "Dragon Fang Sword" },
  { templateId: "11111111-1111-4111-8111-000000000002", name: "Aegis Knight Shield" },
  { templateId: "11111111-1111-4111-8111-000000000003", name: "Greater Healing Potion" },
  { templateId: "11111111-1111-4111-8111-000000000004", name: "Starlight Ore" },
];

export const SEED_USERS = {
  gm: { userId: "10000000-0000-4000-8000-000000000001", label: "GM Admin", email: "gm@marketplace.local", role: "GM" },
  cs: { userId: "10000000-0000-4000-8000-000000000002", label: "CS Staff", email: "cs@marketplace.local", role: "CS" },
  seller: { userId: "10000000-0000-4000-8000-000000000003", label: "Demo Seller", email: "seller@marketplace.local", role: "Player" },
  buyer: { userId: "10000000-0000-4000-8000-000000000004", label: "Demo Buyer", email: "buyer@marketplace.local", role: "Player" },
  buyerFull: { userId: "10000000-0000-4000-8000-000000000005", label: "Full-Inv Buyer", email: "buyer.full@marketplace.local", role: "Player" },
};

export function getTemplateName(templateId) {
  const found = SEED_TEMPLATES.find((t) => t.templateId.toLowerCase() === String(templateId || "").toLowerCase());
  return found ? found.name : "";
}

export const ENDPOINTS = {
  authDemoConfig: "/demo-config",
  authLogin: "/api/v1/auth/login",
  authGoogle: "/api/v1/auth/google",
  authRefresh: "/api/v1/auth/refresh",
  authLogout: "/api/v1/auth/logout",
  authMe: "/api/v1/auth/me",

  walletMe: "/api/v1/wallet/balance",
  walletLedger: "/api/v1/wallet/transactions",
  walletPackages: "/api/v1/wallet/top-up-packages",
  walletTopUp: "/api/v1/wallet/top-ups",
  walletTopUpOrder: (id) => `/api/v1/wallet/top-ups/${encodeURIComponent(id)}`,

  inventoryMe: "/api/v1/inventory/items",

  listings: "/api/v1/marketplace/listings",
  listingsMine: "/api/v1/marketplace/my-listings",
  listingDetail: (id) => `/api/v1/marketplace/listings/${encodeURIComponent(id)}`,
  listingCancel: (id) => `/api/v1/marketplace/listings/${encodeURIComponent(id)}`,
  listingBuy: (id) => `/api/v1/marketplace/listings/${encodeURIComponent(id)}/purchase`,
  tradesMine: "/api/v1/marketplace/history",
  tradeDetail: (id) => `/api/v1/marketplace/trades/${encodeURIComponent(id)}`,

  adminAnalytics: "/api/v1/admin/analytics",
  adminAnalyticsHistory: "/api/v1/admin/analytics/history",
  adminPricePolicies: "/api/v1/admin/price-policies",
  adminPricePolicyUpsert: (templateId) => `/api/v1/admin/price-policies/${encodeURIComponent(templateId)}`,
  adminTradeTrace: (tradeId) => `/api/v1/admin/trades/${encodeURIComponent(tradeId)}/trace`,
  adminTradesSearch: "/api/v1/admin/trades",
  adminModerateUser: (userId) => `/api/v1/admin/users/${encodeURIComponent(userId)}/status`,
};

const listeners = {
  session: new Set(),
  logs: new Set(),
};

const activityLogs = [];
const MAX_LOGS = 80;
let refreshPromise = null;

// --- Storage & Session ---
export function getApiBaseUrl() {
  return (localStorage.getItem(STORAGE_KEYS.baseUrl) ?? "").trim().replace(/\/+$/, "");
}

export function setApiBaseUrl(url) {
  const clean = (url ?? "").trim().replace(/\/+$/, "");
  localStorage.setItem(STORAGE_KEYS.baseUrl, clean);
  return clean;
}

export function getDemoRbacEnabled() {
  return localStorage.getItem(STORAGE_KEYS.demoRbac) === "1";
}

export function setDemoRbacEnabled(enabled) {
  localStorage.setItem(STORAGE_KEYS.demoRbac, enabled ? "1" : "0");
}

export function getSession() {
  const accessToken = localStorage.getItem(STORAGE_KEYS.accessToken) || null;
  const refreshToken = localStorage.getItem(STORAGE_KEYS.refreshToken) || null;
  let user = null;
  try {
    const raw = localStorage.getItem(STORAGE_KEYS.user);
    if (raw) user = JSON.parse(raw);
  } catch {
    user = null;
  }
  return { accessToken, refreshToken, user, isAuthenticated: Boolean(accessToken) };
}

export function setSession({ accessToken, refreshToken, user }) {
  if (accessToken !== undefined) {
    if (accessToken) localStorage.setItem(STORAGE_KEYS.accessToken, accessToken);
    else localStorage.removeItem(STORAGE_KEYS.accessToken);
  }
  if (refreshToken !== undefined) {
    if (refreshToken) localStorage.setItem(STORAGE_KEYS.refreshToken, refreshToken);
    else localStorage.removeItem(STORAGE_KEYS.refreshToken);
  }
  if (user !== undefined) {
    if (user) localStorage.setItem(STORAGE_KEYS.user, JSON.stringify(user));
    else localStorage.removeItem(STORAGE_KEYS.user);
  }
  const snap = getSession();
  listeners.session.forEach((fn) => fn(snap));
}

export function clearSession() {
  localStorage.removeItem(STORAGE_KEYS.accessToken);
  localStorage.removeItem(STORAGE_KEYS.refreshToken);
  localStorage.removeItem(STORAGE_KEYS.user);
  const snap = getSession();
  listeners.session.forEach((fn) => fn(snap));
}

export function onSessionChange(fn) {
  listeners.session.add(fn);
  return () => listeners.session.delete(fn);
}

// --- Activity Log ---
export function getActivityLogs() {
  return activityLogs;
}

export function clearActivityLogs() {
  activityLogs.length = 0;
  listeners.logs.forEach((fn) => fn(activityLogs));
}

export function onLogsChange(fn) {
  listeners.logs.add(fn);
  return () => listeners.logs.delete(fn);
}

function pushActivityLog(entry) {
  activityLogs.unshift({
    id: crypto.randomUUID(),
    timestamp: new Date().toISOString(),
    ...entry,
  });
  if (activityLogs.length > MAX_LOGS) activityLogs.pop();
  listeners.logs.forEach((fn) => fn(activityLogs));
}

// --- Core HTTP Client ---
export function newUuid() {
  return crypto.randomUUID();
}

function extractProblemDetails(contentType, body, status) {
  if (!body || typeof body !== "object") return null;
  const isProblem =
    (contentType || "").toLowerCase().includes("application/problem+json") ||
    (status >= 400 && (body.title || body.detail || body.code || body.errors));
  if (!isProblem) return null;

  const flatErrors = [];
  if (Array.isArray(body.errors)) {
    body.errors.forEach((e) => flatErrors.push(typeof e === "string" ? e : JSON.stringify(e)));
  } else if (body.errors && typeof body.errors === "object") {
    Object.entries(body.errors).forEach(([field, msgs]) => {
      const list = Array.isArray(msgs) ? msgs : [msgs];
      list.forEach((m) => flatErrors.push(`${field}: ${m}`));
    });
  }

  return {
    type: body.type || "",
    title: body.title || `HTTP ${status}`,
    status: body.status || status,
    code: body.code || body.errorCode || body.extensions?.code || "",
    detail: body.detail || body.message || "",
    traceId: body.traceId || body.extensions?.traceId || "",
    errors: flatErrors,
  };
}

async function performRefreshToken() {
  if (refreshPromise) return refreshPromise;

  const { refreshToken } = getSession();
  if (!refreshToken) return false;

  refreshPromise = (async () => {
    try {
      const res = await apiRequest(ENDPOINTS.authRefresh, {
        method: "POST",
        body: { refreshToken },
        auth: false,
        retryOn401: false,
      });
      if (!res.ok || !res.data) {
        clearSession();
        return false;
      }
      const nextAccess = res.data.accessToken;
      const nextRefresh = res.data.refreshToken || refreshToken;
      const nextUser = res.data.user || getSession().user;
      if (!nextAccess) {
        clearSession();
        return false;
      }
      setSession({ accessToken: nextAccess, refreshToken: nextRefresh, user: nextUser });
      return true;
    } catch {
      clearSession();
      return false;
    } finally {
      refreshPromise = null;
    }
  })();

  return refreshPromise;
}

export async function refreshAccessToken() {
  return performRefreshToken();
}

export async function apiRequest(path, options = {}) {
  const {
    method = "GET",
    body = undefined,
    query = undefined,
    auth = true,
    idempotencyKey = undefined,
    headers: customHeaders = {},
    retryOn401 = true,
  } = options;

  let fullPath = path;
  if (query && typeof query === "object") {
    const params = new URLSearchParams();
    Object.entries(query).forEach(([k, v]) => {
      if (v !== undefined && v !== null && v !== "") params.set(k, String(v));
    });
    const qs = params.toString();
    if (qs) fullPath += (fullPath.includes("?") ? "&" : "?") + qs;
  }

  const url = `${getApiBaseUrl()}${fullPath}`;
  const correlationId = newUuid();
  const headers = {
    Accept: "application/json, application/problem+json",
    "X-Correlation-ID": correlationId,
    ...customHeaders,
  };

  if (body !== undefined) {
    headers["Content-Type"] = "application/json";
  }
  if (idempotencyKey) {
    headers["Idempotency-Key"] = idempotencyKey;
  }
  if (auth) {
    const { accessToken } = getSession();
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
  }

  const startedAt = performance.now();
  let response;
  try {
    response = await fetch(url, {
      method,
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch (networkErr) {
    const durationMs = Math.round(performance.now() - startedAt);
    const problem = {
      title: "Network Error",
      status: 0,
      code: "NETWORK_ERROR",
      detail: networkErr?.message || "Không thể kết nối tới API Gateway",
      traceId: "",
      errors: [],
    };
    pushActivityLog({
      method,
      path: fullPath,
      status: 0,
      ok: false,
      durationMs,
      correlationId,
      idempotencyKey: idempotencyKey || "",
      location: "",
      problem,
    });
    return { ok: false, status: 0, data: null, problem, headers: new Headers(), correlationId };
  }

  const durationMs = Math.round(performance.now() - startedAt);
  const contentType = response.headers.get("content-type") || "";
  const respCorrelationId = response.headers.get("x-correlation-id") || correlationId;
  const locationHeader = response.headers.get("location") || "";

  let data = null;
  if (response.status !== 204) {
    const text = await response.text();
    if (text) {
      try {
        data = JSON.parse(text);
      } catch {
        data = text;
      }
    }
  }

  const problem = extractProblemDetails(contentType, data, response.status);

  // Tuyệt đối không lưu token hay password trong Activity Log
  pushActivityLog({
    method,
    path: fullPath,
    status: response.status,
    ok: response.ok,
    durationMs,
    correlationId: respCorrelationId,
    idempotencyKey: idempotencyKey || "",
    location: locationHeader,
    problem,
  });

  if (
    response.status === 401 &&
    auth &&
    retryOn401 &&
    !fullPath.includes("/auth/refresh") &&
    !fullPath.includes("/auth/login")
  ) {
    const refreshed = await performRefreshToken();
    if (refreshed) {
      return apiRequest(path, { ...options, retryOn401: false });
    }
  }

  return {
    ok: response.ok,
    status: response.status,
    data,
    problem,
    headers: response.headers,
    correlationId: respCorrelationId,
    location: locationHeader,
  };
}

// --- Formatters & Security Helpers ---
export function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "\x26amp;")
    .replaceAll("<", "\x26lt;")
    .replaceAll(">", "\x26gt;")
    .replaceAll('"', "\x26quot;")
    .replaceAll("'", "\x26#39;");
}

export function formatGem(value) {
  if (value === null || value === undefined || value === "") return "0";
  try {
    const bi = BigInt(String(value).split(".")[0]);
    return bi.toLocaleString("vi-VN");
  } catch {
    const num = Number(value);
    return Number.isFinite(num) ? Math.trunc(num).toLocaleString("vi-VN") : String(value);
  }
}

export function formatSignedGem(value) {
  if (value === null || value === undefined || value === "") return "0";
  try {
    const bi = BigInt(String(value).split(".")[0]);
    if (bi > 0n) return `+${bi.toLocaleString("vi-VN")}`;
    return bi.toLocaleString("vi-VN");
  } catch {
    return formatGem(value);
  }
}

export function formatVnd(value) {
  const num = Number(value || 0);
  return `${num.toLocaleString("vi-VN")} ₫`;
}

export function formatDateTime(iso) {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return String(iso);
  return d.toLocaleString("vi-VN", {
    hour12: false,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

export function getTodayHoChiMinh() {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: "Asia/Ho_Chi_Minh",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());
}

export function addDaysYmd(ymd, deltaDays) {
  const [y, m, d] = String(ymd).split("-").map(Number);
  const dt = new Date(Date.UTC(y, m - 1, d + deltaDays));
  const yy = dt.getUTCFullYear();
  const mm = String(dt.getUTCMonth() + 1).padStart(2, "0");
  const dd = String(dt.getUTCDate()).padStart(2, "0");
  return `${yy}-${mm}-${dd}`;
}

export function shortId(id, len = 10) {
  const s = String(id ?? "");
  return s.length > len ? `${s.slice(0, len)}…` : s;
}

export function renderIdChip(id, label = "") {
  if (!id) return `<span class="text-muted">—</span>`;
  const safe = escapeHtml(id);
  const shown = escapeHtml(shortId(id, 12));
  return `<span class="id-chip" title="${safe}">
    ${label ? `<span class="text-muted">${escapeHtml(label)}:</span>` : ""}
    <span>${shown}</span>
    <button type="button" class="id-chip-copy" data-copy="${safe}">copy</button>
  </span>`;
}

export function rarityClass(rarity) {
  const r = String(rarity || "Common").toLowerCase();
  if (["legendary", "epic", "rare", "uncommon", "common"].includes(r)) return r;
  return "common";
}

export function statusBadge(status) {
  const s = String(status || "Unknown");
  const lower = s.toLowerCase();
  let cls = "badge-muted";
  if (["active", "completed", "succeeded", "paid", "available", "finalized", "settled"].includes(lower)) {
    cls = "badge-success";
  } else if (
    [
      "pending",
      "pendinglock",
      "processing",
      "locked",
      "pendingsettlement",
      "traderestricted",
      "holdingfunds",
      "transferring",
      "settling",
      "finalizing",
      "reversingitem",
      "releasingfunds",
      "held",
      "retrying",
      "unknown",
      "notsettled",
      "reversed",
    ].includes(lower)
  ) {
    cls = "badge-warning";
  } else if (["failed", "createfailed", "banned", "cancelled", "rejected", "expired"].includes(lower)) {
    cls = "badge-danger";
  } else if (["taxburn", "gm"].includes(lower)) {
    cls = "badge-purple";
  } else if (["cs", "sold", "player", "topup", "settlementcredit", "settlementdebit", "hold", "release", "released"].includes(lower)) {
    cls = "badge-info";
  }
  return `<span class="badge ${cls}">${escapeHtml(s)}</span>`;
}

export function problemMessage(res, fallback = "Yêu cầu thất bại") {
  if (res?.problem) {
    const code = res.problem.code ? `[${res.problem.code}] ` : "";
    return `${code}${res.problem.detail || res.problem.title || fallback}`;
  }
  return fallback;
}

export function renderProblemAlert(res) {
  if (!res?.problem) return "";
  const p = res.problem;
  return `
    <div class="alert alert-danger">
      <div>
        <strong>HTTP ${escapeHtml(p.status)}</strong>
        ${p.code ? `<code>${escapeHtml(p.code)}</code> — ` : ""}
        ${escapeHtml(p.detail || p.title)}
      </div>
      ${p.errors?.length ? `<ul style="margin:6px 0 0 18px;padding:0;">${p.errors.map((e) => `<li>${escapeHtml(e)}</li>`).join("")}</ul>` : ""}
      ${p.traceId ? `<div style="margin-top:6px;">${renderIdChip(p.traceId, "traceId")}</div>` : ""}
    </div>
  `;
}

// --- Toast & Modal ---
export function showToast(title, message = "", type = "info", durationMs = 4200) {
  const container = document.getElementById("toastContainer");
  if (!container) return;
  const el = document.createElement("div");
  el.className = `toast toast-${type}`;
  el.innerHTML = `
    <div>
      <div class="toast-title">${escapeHtml(title)}</div>
      ${message ? `<div class="toast-msg">${escapeHtml(message)}</div>` : ""}
    </div>
    <button type="button" class="btn btn-xs btn-ghost" aria-label="Đóng">✕</button>
  `;
  const close = () => el.remove();
  el.querySelector("button")?.addEventListener("click", close);
  container.appendChild(el);
  if (durationMs > 0) setTimeout(close, durationMs);
}

let activeModalCleanup = null;

export function openModal(title, htmlContent, { large = false, onClose = null } = {}) {
  const backdrop = document.getElementById("modalBackdrop");
  const dialog = document.getElementById("modalDialog");
  const titleEl = document.getElementById("modalTitle");
  const bodyEl = document.getElementById("modalBody");
  if (!backdrop || !dialog || !titleEl || !bodyEl) return null;

  if (typeof activeModalCleanup === "function") {
    activeModalCleanup();
    activeModalCleanup = null;
  }

  titleEl.textContent = title;
  bodyEl.innerHTML = htmlContent;
  dialog.classList.toggle("modal-lg", Boolean(large));
  backdrop.classList.remove("hidden");
  activeModalCleanup = onClose;
  return bodyEl;
}

export function closeModal() {
  const backdrop = document.getElementById("modalBackdrop");
  const bodyEl = document.getElementById("modalBody");
  if (typeof activeModalCleanup === "function") {
    activeModalCleanup();
    activeModalCleanup = null;
  }
  if (backdrop) backdrop.classList.add("hidden");
  if (bodyEl) bodyEl.innerHTML = "";
}

export async function copyToClipboard(text) {
  try {
    await navigator.clipboard.writeText(text);
    showToast("Đã copy", shortId(text, 36), "info", 1800);
  } catch {
    showToast("Không thể copy", text, "warning", 2500);
  }
}