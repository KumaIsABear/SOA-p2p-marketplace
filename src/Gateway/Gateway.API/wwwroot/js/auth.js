import {
  ENDPOINTS,
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
  { label: "Player 1 (Seller)", email: "player1@demo.local", password: "Password123!" },
  { label: "Player 2 (Buyer)", email: "player2@demo.local", password: "Password123!" },
  { label: "Restricted User", email: "restricted@demo.local", password: "Password123!" },
  { label: "GM Admin", email: "gm@demo.local", password: "Password123!" },
  { label: "CS Staff", email: "cs@demo.local", password: "Password123!" },
];

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
      if (statusEl) statusEl.textContent = "Chưa cấu hình googleClientId từ /api/v1/auth/demo-config.";
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
    if (statusEl) statusEl.textContent = `GIS sẵn sàng (${clientId.slice(0, 18)}…)`;
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
    applyAuthPayload(res.data, "Đăng nhập Google thành công");
  }

  function applyAuthPayload(payload, toastTitle) {
    const accessToken = payload?.accessToken || payload?.token;
    const refreshToken = payload?.refreshToken;
    const user = payload?.user || payload?.profile || null;
    setSession({ accessToken, refreshToken, user });
    showToast(toastTitle, user?.email || user?.displayName || "Phiên đã sẵn sàng", "success");
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
      if (!silent) showToast("Lỗi /auth/me", problemMessage(res), "danger");
      return null;
    }
    setSession({ user: res.data });
    if (!silent) showToast("Đã xác thực /auth/me", `${res.data.displayName || res.data.email} (${res.data.role})`, "success");
    render();
    return res.data;
  }

  async function logout() {
    const { refreshToken } = getSession();
    if (refreshToken) {
      await apiRequest(ENDPOINTS.authLogout, {
        method: "POST",
        body: { refreshToken },
        retryOn401: false,
      });
    }
    clearSession();
    showToast("Đã đăng xuất", "Đã xóa token phiên hiện tại.", "info");
    render();
  }

  function render() {
    const session = getSession();
    const user = session.user;

    panel.innerHTML = `
      <div class="grid-2">
        <!-- AUTH-02: Login -->
        <div class="card">
          <div class="card-header">
            <div>
              <h3 class="card-title">Đăng nhập (AUTH-02 / AUTH-03)</h3>
              <p class="card-subtitle">Điền nhanh tài khoản seed hoặc đăng nhập Google Identity Services</p>
            </div>
          </div>

          <div class="seed-buttons">
            ${SEED_ACCOUNTS.map(
              (acc, idx) =>
                `<button type="button" class="btn btn-xs btn-secondary" data-seed-idx="${idx}">${escapeHtml(acc.label)}</button>`
            ).join("")}
          </div>

          <form id="loginForm">
            <div class="form-row">
              <div class="form-group">
                <label for="loginEmail">Email</label>
                <input id="loginEmail" type="email" required placeholder="player1@demo.local" />
              </div>
              <div class="form-group">
                <label for="loginPassword">Mật khẩu</label>
                <input id="loginPassword" type="password" required placeholder="••••••••" />
              </div>
            </div>
            <div class="form-row">
              <button type="submit" class="btn btn-primary">Đăng nhập</button>
              <div id="gisButtonSlot" class="gis-container"></div>
            </div>
            <div id="gisStatusText" class="text-muted" style="font-size:12px;"></div>
          </form>
        </div>

        <!-- AUTH-01: Register -->
        <div class="card">
          <div class="card-header">
            <div>
              <h3 class="card-title">Đăng ký tài khoản mới (AUTH-01)</h3>
              <p class="card-subtitle">Tự động khởi tạo Ví Gem và Kho đồ thông qua sự kiện đăng ký</p>
            </div>
          </div>

          <form id="registerForm">
            <div class="form-row">
              <div class="form-group">
                <label for="regDisplayName">Tên hiển thị</label>
                <input id="regDisplayName" type="text" required placeholder="DragonSlayer99" />
              </div>
              <div class="form-group">
                <label for="regEmail">Email</label>
                <input id="regEmail" type="email" required placeholder="newplayer@demo.local" />
              </div>
            </div>
            <div class="form-row">
              <div class="form-group">
                <label for="regPassword">Mật khẩu</label>
                <input id="regPassword" type="password" required minlength="6" placeholder="Tối thiểu 6 ký tự" />
              </div>
              <button type="submit" class="btn btn-success">Tạo tài khoản</button>
            </div>
          </form>
        </div>
      </div>

      <!-- AUTH-04 / AUTH-05: Session Overview -->
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Trạng thái Phiên hiện tại (AUTH-04 / AUTH-05)</h3>
            <p class="card-subtitle">Token được giữ trong localStorage và tự động refresh khi gặp HTTP 401</p>
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
              <div class="stat-value" style="font-size:18px;">${escapeHtml(user.displayName || user.username || user.email)}</div>
              <div class="stat-meta">${escapeHtml(user.email || "")}</div>
            </div>
            <div class="stat-box">
              <div class="stat-label">User ID</div>
              <div class="stat-meta" style="margin-top:8px;">${renderIdChip(user.id || user.userId)}</div>
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
            : `<div class="alert alert-info mb-0">Chưa có phiên đăng nhập. Hãy chọn nhanh một tài khoản seed ở trên hoặc đăng ký mới.</div>`
        }
      </div>
    `;

    bindEvents();
    renderGoogleButton();
  }

  function bindEvents() {
    panel.querySelectorAll("[data-seed-idx]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const acc = SEED_ACCOUNTS[Number(btn.dataset.seedIdx)];
        if (!acc) return;
        panel.querySelector("#loginEmail").value = acc.email;
        panel.querySelector("#loginPassword").value = acc.password;
      });
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

    panel.querySelector("#registerForm")?.addEventListener("submit", async (e) => {
      e.preventDefault();
      const displayName = panel.querySelector("#regDisplayName").value.trim();
      const email = panel.querySelector("#regEmail").value.trim();
      const password = panel.querySelector("#regPassword").value;
      const res = await apiRequest(ENDPOINTS.authRegister, {
        method: "POST",
        auth: false,
        body: { displayName, email, password },
      });
      if (!res.ok) {
        showToast("Đăng ký thất bại", problemMessage(res), "danger");
        return;
      }
      if (res.data?.accessToken || res.data?.token) {
        applyAuthPayload(res.data, "Đăng ký & đăng nhập thành công");
      } else {
        showToast("Đăng ký thành công", "Hãy đăng nhập bằng tài khoản vừa tạo.", "success");
        panel.querySelector("#loginEmail").value = email;
        panel.querySelector("#loginPassword").value = password;
      }
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