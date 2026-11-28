import {
  ENDPOINTS,
  SEED_USERS,
  apiRequest,
  clearSession,
  escapeHtml,
  getSession,
  problemMessage,
  renderIdChip,
  setSession,
  showToast,
  statusBadge,
} from "./api.js";

const SEED_ACCOUNTS = [
  SEED_USERS.seller,
  SEED_USERS.buyer,
  SEED_USERS.buyerFull,
  SEED_USERS.gm,
  SEED_USERS.cs,
];

const SEED_PW_STORAGE_KEY = "p2p_demo_seed_password";

let demoConfig = { googleClientId: "" };
let gisInitialized = false;

export function createAuthModule(appCtx) {
  const panel = document.getElementById("panel-auth");

  async function loadDemoConfig() {
    const res = await apiRequest(ENDPOINTS.authDemoConfig, { auth: false, retryOn401: false });
    if (res.ok && res.data) {
      demoConfig = res.data;
    }
  }

  function renderGoogleButton() {
    const container = panel.querySelector("#gisButtonSlot");
    const statusEl = panel.querySelector("#gisStatusText");
    if (!container) return;

    const clientId = demoConfig?.googleClientId || "";
    if (!clientId) {
      if (statusEl) statusEl.textContent = "Chưa cấu hình Google:ClientId từ GET /demo-config (để trống GOOGLE_CLIENT_ID trong .env).";
      return;
    }

    if (!window.google?.accounts?.id) {
      if (statusEl) statusEl.textContent = "Đang chờ tải Google Identity Services (gsi/client)...";
      setTimeout(renderGoogleButton, 800);
      return;
    }

    if (!gisInitialized) {
      window.google.accounts.id.initialize({
        client_id: clientId,
        callback: handleGoogleCredential,
      });
      gisInitialized = true;
    }

    container.innerHTML = "";
    window.google.accounts.id.renderButton(container, {
      theme: "filled_black",
      size: "medium",
      shape: "rectangular",
      text: "signin_with",
    });
    if (statusEl) statusEl.textContent = `GIS sẵn sàng (${clientId.slice(0, 22)}…)`;
  }

  async function handleGoogleCredential(response) {
    if (!response?.credential) return;
    const res = await apiRequest(ENDPOINTS.authGoogle, {
      method: "POST",
      auth: false,
      body: { idToken: response.credential },
    });
    if (!res.ok) {
      showToast("Đăng nhập Google thất bại", problemMessage(res), "danger");
      return;
    }
    applyAuthPayload(res.data, "Đăng nhập Google thành công (AUTH-02)");
  }

  function applyAuthPayload(payload, toastTitle) {
    const accessToken = payload?.accessToken;
    const refreshToken = payload?.refreshToken;
    const user = payload?.user || null;
    setSession({ accessToken, refreshToken, user });
    showToast(toastTitle, `${user?.displayName || user?.email || ""} (${user?.role || ""})`, "success");
    fetchMe(true);
    appCtx.refreshAllActiveData();
  }

  async function fetchMe(silent = false) {
    const { isAuthenticated } = getSession();
    if (!isAuthenticated) {
      if (!silent) showToast("Chưa đăng nhập", "Vui lòng đăng nhập trước.", "warning");
      return null;
    }
    const res = await apiRequest(ENDPOINTS.authMe);
    if (!res.ok) {
      if (!silent) showToast("Lỗi AUTH-05 (/auth/me)", problemMessage(res), "danger");
      return null;
    }
    setSession({ user: res.data });
    if (!silent) {
      showToast("AUTH-05 (/auth/me)", `${res.data.displayName || res.data.email} • ${res.data.role} • ${res.data.status}`, "success");
    }
    render();
    return res.data;
  }

  async function logout() {
    const { refreshToken } = getSession();
    if (refreshToken) {
      await apiRequest(ENDPOINTS.authLogout, {
        method: "POST",
        auth: false,
        body: { refreshToken },
        retryOn401: false,
      });
    }
    clearSession();
    showToast("Đã đăng xuất (AUTH-04)", "Refresh token đã được thu hồi và xóa phiên local.", "info");
    render();
  }

  function render() {
    const session = getSession();
    const user = session.user;
    const savedSeedPw = sessionStorage.getItem(SEED_PW_STORAGE_KEY) || "";

    panel.innerHTML = `
      <div class="grid-2">
        <!-- AUTH-01: Seed Account Login -->
        <div class="card">
          <div class="card-header">
            <div>
              <h3 class="card-title">Đăng nhập tài khoản Seed (AUTH-01)</h3>
              <p class="card-subtitle"><code>POST /api/v1/auth/login</code> — không có public register theo Contract</p>
            </div>
          </div>

          <div class="form-row">
            <div class="form-group">
              <label for="seedPasswordInput">Seed Password chung (từ <code>AUTH_SEED_PASSWORD</code> trong .env)</label>
              <input id="seedPasswordInput" type="password" value="${escapeHtml(savedSeedPw)}" placeholder="Nhập AUTH_SEED_PASSWORD để điền nhanh..." autocomplete="off" />
            </div>
          </div>

          <div class="seed-buttons">
            ${SEED_ACCOUNTS.map(
              (acc, idx) =>
                `<button type="button" class="btn btn-xs btn-secondary" data-seed-idx="${idx}" title="${escapeHtml(acc.email)}">
                  ${escapeHtml(acc.label)} (${escapeHtml(acc.role)})
                </button>`
            ).join("")}
          </div>

          <form id="loginForm">
            <div class="form-row">
              <div class="form-group">
                <label for="loginEmail">Email</label>
                <input id="loginEmail" type="email" required placeholder="seller@marketplace.local" />
              </div>
              <div class="form-group">
                <label for="loginPassword">Mật khẩu</label>
                <input id="loginPassword" type="password" required placeholder="Nhập mật khẩu" />
              </div>
            </div>
            <div class="form-row">
              <button type="submit" class="btn btn-primary">Đăng nhập (AUTH-01)</button>
            </div>
          </form>
        </div>

        <!-- AUTH-02: Google Sign-In -->
        <div class="card">
          <div class="card-header">
            <div>
              <h3 class="card-title">Đăng nhập Google Identity Services (AUTH-02)</h3>
              <p class="card-subtitle"><code>POST /api/v1/auth/google</code> — JIT tạo tài khoản <code>Player</code> nếu chưa có</p>
            </div>
            <button id="btnReloadDemoConfig" type="button" class="btn btn-xs btn-secondary">↻ Tải /demo-config</button>
          </div>

          <div class="alert alert-info">
            Client ID được đọc chỉ-đọc từ <code>GET /demo-config</code> trên <strong>Gateway.API</strong> (same-origin, không lộ secret).
          </div>

          <div id="gisButtonSlot" class="gis-container" style="margin-bottom:8px;"></div>
          <div id="gisStatusText" class="text-muted" style="font-size:12px;"></div>
        </div>
      </div>

      <!-- AUTH-03 / AUTH-04 / AUTH-05: Session Overview -->
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Trạng thái Phiên hiện tại (AUTH-03 / AUTH-04 / AUTH-05)</h3>
            <p class="card-subtitle">Access token tự động rotate qua <code>/api/v1/auth/refresh</code> khi gặp HTTP 401</p>
          </div>
          <div class="header-actions">
            <button id="btnPanelMe" type="button" class="btn btn-sm btn-secondary" ${!session.isAuthenticated ? "disabled" : ""}>Gọi /auth/me</button>
            <button id="btnPanelLogout" type="button" class="btn btn-sm btn-danger" ${!session.isAuthenticated ? "disabled" : ""}>Logout</button>
          </div>
        </div>

        ${
          user
            ? `
          <div class="grid-4">
            <div class="stat-box">
              <div class="stat-label">Người dùng</div>
              <div class="stat-value" style="font-size:18px;">${escapeHtml(user.displayName || user.email)}</div>
              <div class="stat-meta">${escapeHtml(user.email || "")}</div>
            </div>
            <div class="stat-box">
              <div class="stat-label">User ID</div>
              <div class="stat-meta" style="margin-top:8px;">${renderIdChip(user.userId)}</div>
              <div class="stat-meta">Version: <strong class="mono">v${escapeHtml(user.version ?? "—")}</strong></div>
            </div>
            <div class="stat-box">
              <div class="stat-label">Vai trò (Role)</div>
              <div style="margin-top:8px;">${statusBadge(user.role || "Player")}</div>
            </div>
            <div class="stat-box">
              <div class="stat-label">Trạng thái tài khoản</div>
              <div style="margin-top:8px;">${statusBadge(user.status || "Active")}</div>
            </div>
          </div>`
            : `<div class="alert alert-info mb-0">Chưa có phiên đăng nhập. Nhập Seed Password (hoặc gõ tay) và chọn một tài khoản seed ở trên, hoặc đăng nhập bằng Google.</div>`
        }
      </div>
    `;

    bindEvents();
    renderGoogleButton();
  }

  function bindEvents() {
    panel.querySelector("#seedPasswordInput")?.addEventListener("input", (e) => {
      sessionStorage.setItem(SEED_PW_STORAGE_KEY, e.target.value);
    });

    panel.querySelectorAll("[data-seed-idx]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const acc = SEED_ACCOUNTS[Number(btn.dataset.seedIdx)];
        if (!acc) return;
        panel.querySelector("#loginEmail").value = acc.email;
        const seedPw = panel.querySelector("#seedPasswordInput")?.value || "";
        if (seedPw) {
          panel.querySelector("#loginPassword").value = seedPw;
        } else {
          panel.querySelector("#loginPassword").focus();
        }
      });
    });

    panel.querySelector("#btnReloadDemoConfig")?.addEventListener("click", async () => {
      gisInitialized = false;
      await loadDemoConfig();
      renderGoogleButton();
    });

    panel.querySelector("#loginForm")?.addEventListener("submit", async (e) => {
      e.preventDefault();
      const email = panel.querySelector("#loginEmail").value.trim();
      const password = panel.querySelector("#loginPassword").value;
      const res = await apiRequest(ENDPOINTS.authLogin, {
        method: "POST",
        auth: false,
        body: { email, password },
      });
      if (!res.ok) {
        showToast("Đăng nhập thất bại", problemMessage(res), "danger");
        return;
      }
      applyAuthPayload(res.data, "Đăng nhập thành công");
    });

    panel.querySelector("#btnPanelMe")?.addEventListener("click", () => fetchMe(false));
    panel.querySelector("#btnPanelLogout")?.addEventListener("click", logout);
  }

  return {
    async init() {
      await loadDemoConfig();
      render();
    },
    render,
    fetchMe,
    logout,
  };
}