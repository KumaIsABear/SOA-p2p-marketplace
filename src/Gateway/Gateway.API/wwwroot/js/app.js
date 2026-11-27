import {
  clearActivityLogs,
  closeModal,
  copyToClipboard,
  escapeHtml,
  getActivityLogs,
  getApiBaseUrl,
  getSession,
  onLogsChange,
  onSessionChange,
  refreshAccessToken,
  renderIdChip,
  setApiBaseUrl,
  showToast,
  statusBadge,
} from "./api.js";
import { createAuthModule } from "./auth.js";
import { createWalletModule } from "./wallet.js";
import { createInventoryModule } from "./inventory.js";
import { createMarketplaceModule } from "./marketplace.js";

const tabModules = new Map();
let activeTab = "auth";

const appCtx = {
  getActiveTab: () => activeTab,
  switchTab,
  refreshTab(tabName) {
    const mod = tabModules.get(tabName);
    if (mod && typeof mod.refresh === "function") {
      return mod.refresh();
    }
  },
  refreshAllActiveData() {
    ["wallet", "inventory", "marketplace", "gm", "cs"].forEach((tab) => {
      const mod = tabModules.get(tab);
      if (mod && typeof mod.refresh === "function") mod.refresh();
    });
  },
};

// Extension hook cho Phần 2 (gm.js & cs.js)
export function registerTabModule(tabName, moduleInstance) {
  tabModules.set(tabName, moduleInstance);
  if (typeof moduleInstance?.init === "function") {
    moduleInstance.init(appCtx);
  }
  if (activeTab === tabName && typeof moduleInstance?.refresh === "function") {
    moduleInstance.refresh();
  }
}
window.registerTabModule = registerTabModule;

function switchTab(tabName) {
  activeTab = tabName;
  document.querySelectorAll(".nav-tab").forEach((btn) => {
    btn.classList.toggle("active", btn.dataset.tab === tabName);
  });
  document.querySelectorAll(".tab-panel").forEach((sec) => {
    sec.classList.toggle("active", sec.id === `panel-${tabName}`);
  });

  const mod = tabModules.get(tabName);
  if (mod && typeof mod.refresh === "function") {
    mod.refresh();
  } else if (mod && typeof mod.render === "function") {
    mod.render();
  }
}

function renderHeaderAndBanner(session) {
  const pill = document.getElementById("headerSessionInfo");
  const btnMe = document.getElementById("btnHeaderMe");
  const btnRefresh = document.getElementById("btnHeaderRefresh");
  const btnLogout = document.getElementById("btnHeaderLogout");
  const banner = document.getElementById("accountStatusBanner");

  const isAuth = session.isAuthenticated;
  btnMe.disabled = !isAuth;
  btnRefresh.disabled = !isAuth;
  btnLogout.disabled = !isAuth;

  if (!isAuth || !session.user) {
    pill.innerHTML = `<span class="badge badge-muted">Chưa đăng nhập</span>`;
    banner.className = "status-banner hidden";
    banner.innerHTML = "";
    return;
  }

  const u = session.user;
  const display = u.displayName || u.username || u.email || "User";
  pill.innerHTML = `
    <strong>${escapeHtml(display)}</strong>
    ${statusBadge(u.role || "Player")}
    ${statusBadge(u.status || "Active")}
  `;

  const st = String(u.status || "Active");
  if (st === "TradeRestricted") {
    banner.className = "status-banner warning";
    banner.innerHTML = `<span>⚠ Tài khoản đang ở trạng thái <strong>TradeRestricted</strong> — không thể đăng bán hoặc mua vật phẩm P2P.</span>`;
  } else if (st === "Banned") {
    banner.className = "status-banner danger";
    banner.innerHTML = `<span>⛔ Tài khoản đang bị <strong>Banned</strong> — các thao tác giao dịch sẽ bị từ chối bởi hệ thống.</span>`;
  } else {
    banner.className = "status-banner hidden";
    banner.innerHTML = "";
  }
}

function renderActivityDrawer() {
  const logs = getActivityLogs();
  const errorsOnly = document.getElementById("chkErrorsOnly")?.checked;
  const listEl = document.getElementById("activityLogList");
  const countBadge = document.getElementById("drawerCountBadge");
  const errBadge = document.getElementById("drawerErrorBadge");

  const errorCount = logs.filter((l) => !l.ok).length;
  countBadge.textContent = `${logs.length} req`;
  errBadge.textContent = `${errorCount} err`;
  errBadge.classList.toggle("hidden", errorCount === 0);

  const visible = errorsOnly ? logs.filter((l) => !l.ok) : logs;
  if (visible.length === 0) {
    listEl.innerHTML = `<div class="empty-log">${errorsOnly ? "Không có request lỗi nào." : "Chưa có HTTP request nào."}</div>`;
    return;
  }

  listEl.innerHTML = visible
    .map((entry) => {
      const statusCls = entry.ok ? "badge-success" : entry.status >= 500 || entry.status === 0 ? "badge-danger" : "badge-warning";
      return `
        <div class="log-item ${!entry.ok ? "is-error" : ""}">
          <div class="log-head">
            <div>
              <span class="badge badge-info">${escapeHtml(entry.method)}</span>
              <code style="margin-left:6px;">${escapeHtml(entry.path)}</code>
            </div>
            <div style="display:flex;align-items:center;gap:6px;">
              <span class="badge ${statusCls}">${entry.status || "NET"}</span>
              <span class="text-muted mono">${entry.durationMs}ms</span>
            </div>
          </div>
          <div class="log-meta">
            ${renderIdChip(entry.correlationId, "X-Correlation-ID")}
            ${entry.idempotencyKey ? renderIdChip(entry.idempotencyKey, "Idempotency-Key") : ""}
            ${entry.location ? renderIdChip(entry.location, "Location") : ""}
          </div>
          ${
            entry.problem
              ? `<div class="problem-box">
                  <div>
                    <strong>ProblemDetails:</strong>
                    ${entry.problem.code ? `<code>${escapeHtml(entry.problem.code)}</code> — ` : ""}
                    ${escapeHtml(entry.problem.detail || entry.problem.title)}
                  </div>
                  ${entry.problem.traceId ? `<div style="margin-top:4px;">${renderIdChip(entry.problem.traceId, "traceId")}</div>` : ""}
                  ${
                    entry.problem.errors?.length
                      ? `<ul>${entry.problem.errors.map((e) => `<li>${escapeHtml(e)}</li>`).join("")}</ul>`
                      : ""
                  }
                </div>`
              : ""
          }
        </div>
      `;
    })
    .join("");
}

function initGlobalControls(authMod) {
  // Base URL Controls
  const baseInput = document.getElementById("apiBaseUrlInput");
  baseInput.value = getApiBaseUrl();

  document.getElementById("btnBaseUrlSameOrigin")?.addEventListener("click", () => {
    baseInput.value = setApiBaseUrl("");
    showToast("API Base URL", "Đã chuyển về same-origin.", "info", 2000);
  });
  document.getElementById("btnBaseUrlLocal")?.addEventListener("click", () => {
    baseInput.value = setApiBaseUrl("http://localhost:8080");
    showToast("API Base URL", "Đã chuyển sang http://localhost:8080", "info", 2000);
  });
  document.getElementById("btnBaseUrlSave")?.addEventListener("click", () => {
    const saved = setApiBaseUrl(baseInput.value);
    showToast("Đã lưu API Base URL", saved || "(same-origin)", "success", 2000);
  });

  // Header Actions
  document.getElementById("btnHeaderMe")?.addEventListener("click", () => authMod.fetchMe(false));
  document.getElementById("btnHeaderRefresh")?.addEventListener("click", async () => {
    const ok = await refreshAccessToken();
    showToast(ok ? "Refresh Token thành công" : "Refresh Token thất bại", "", ok ? "success" : "danger");
  });
  document.getElementById("btnHeaderLogout")?.addEventListener("click", () => authMod.logout());

  // Navigation Tabs
  document.querySelectorAll(".nav-tab").forEach((btn) => {
    btn.addEventListener("click", () => switchTab(btn.dataset.tab));
  });

  // Modal Close
  document.getElementById("btnModalClose")?.addEventListener("click", closeModal);
  document.getElementById("modalBackdrop")?.addEventListener("click", (e) => {
    if (e.target.id === "modalBackdrop") closeModal();
  });
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") closeModal();
  });

  // Delegated Copy Button for all .id-chip-copy
  document.addEventListener("click", (e) => {
    const copyBtn = e.target.closest("[data-copy]");
    if (copyBtn) copyToClipboard(copyBtn.dataset.copy);
  });

  // Activity Drawer Controls
  const drawer = document.getElementById("activityDrawer");
  const btnToggle = document.getElementById("btnToggleDrawer");
  const btnExpand = document.getElementById("btnExpandDrawer");

  btnToggle?.addEventListener("click", () => {
    const collapsed = drawer.classList.toggle("collapsed");
    if (collapsed) drawer.classList.remove("expanded");
    btnToggle.textContent = collapsed ? "▲ Mở Drawer" : "▼ Thu gọn";
  });

  btnExpand?.addEventListener("click", () => {
    drawer.classList.remove("collapsed");
    btnToggle.textContent = "▼ Thu gọn";
    const expanded = drawer.classList.toggle("expanded");
    btnExpand.textContent = expanded ? "Thu vừa" : "Mở rộng";
  });

  document.getElementById("chkErrorsOnly")?.addEventListener("change", renderActivityDrawer);
  document.getElementById("btnClearLogs")?.addEventListener("click", clearActivityLogs);
}

async function bootstrap() {
  const authMod = createAuthModule(appCtx);
  const walletMod = createWalletModule(appCtx);
  const inventoryMod = createInventoryModule(appCtx);
  const marketplaceMod = createMarketplaceModule(appCtx);

  tabModules.set("auth", authMod);
  tabModules.set("wallet", walletMod);
  tabModules.set("inventory", inventoryMod);
  tabModules.set("marketplace", marketplaceMod);

  initGlobalControls(authMod);
  onSessionChange(renderHeaderAndBanner);
  onLogsChange(renderActivityDrawer);

  renderHeaderAndBanner(getSession());
  renderActivityDrawer();

  await authMod.init();
  walletMod.init();
  inventoryMod.init();
  marketplaceMod.init();

  if (getSession().isAuthenticated) {
    await authMod.fetchMe(true);
    if (new URLSearchParams(window.location.search).has("topUpReturn")) {
      switchTab("wallet");
    }
  }
}

bootstrap();