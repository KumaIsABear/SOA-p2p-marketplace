import {
  ENDPOINTS,
  apiRequest,
  escapeHtml,
  formatDateTime,
  formatGem,
  formatVnd,
  getSession,
  newUuid,
  problemMessage,
  renderIdChip,
  showToast,
  statusBadge,
} from "./api.js";

const DEFAULT_PACKAGES = [
  { code: "GEM_100", name: "Gói Khởi Đầu", gemAmount: 100, priceVnd: 20000 },
  { code: "GEM_500", name: "Gói Chiến Binh", gemAmount: 500, priceVnd: 95000 },
  { code: "GEM_1200", name: "Gói Huyền Thoại", gemAmount: 1200, priceVnd: 220000 },
  { code: "GEM_5000", name: "Gói Đại Gia", gemAmount: 5000, priceVnd: 850000 },
];

const getPkgId = (p) => p?.packageId || p?.id || p?.code || "";
const getTopUpId = (d) => d?.topUpId || d?.id || d?.orderId || "";

export function createWalletModule() {
  const panel = document.getElementById("panel-wallet");

  const state = {
    wallet: null,
    packages: DEFAULT_PACKAGES,
    selectedPackageCode: DEFAULT_PACKAGES[0].code,
    idempotencyKey: newUuid(),
    activeTopUp: null,
    pollTimer: null,
    ledger: { items: [], page: 1, pageSize: 15, totalCount: 0 },
    loading: false,
  };

  function stopPolling() {
    if (state.pollTimer) {
      clearInterval(state.pollTimer);
      state.pollTimer = null;
    }
  }

  async function loadWallet() {
    if (!getSession().isAuthenticated) {
      state.wallet = null;
      render();
      return;
    }
    state.loading = true;
    const [walletRes, pkgRes, ledgerRes] = await Promise.all([
      apiRequest(ENDPOINTS.walletMe),
      apiRequest(ENDPOINTS.walletPackages, { retryOn401: false }),
      apiRequest(ENDPOINTS.walletLedger, { query: { page: state.ledger.page, pageSize: state.ledger.pageSize } }),
    ]);
    state.loading = false;

    if (walletRes.ok && walletRes.data) {
      state.wallet = walletRes.data;
    }
    if (pkgRes.ok && pkgRes.data) {
    const list = Array.isArray(pkgRes.data)
        ? pkgRes.data
        : pkgRes.data.items || pkgRes.data.packages || [];
    if (list.length > 0) {
        state.packages = list;
        if (!state.packages.some((p) => getPkgId(p) === state.selectedPackageCode)) {
        state.selectedPackageCode = getPkgId(state.packages[0]);
            }
        }
    }
    if (ledgerRes.ok && ledgerRes.data) {
      const d = ledgerRes.data;
      state.ledger.items = d.items || d.entries || (Array.isArray(d) ? d : []);
      state.ledger.totalCount = d.totalCount ?? state.ledger.items.length;
    }
    render();
  }

  async function createTopUpOrder() {
    const pkg = state.packages.find((p) => getPkgId(p) === state.selectedPackageCode);
    if (!pkg) return;

    const keyInput = panel.querySelector("#walletIdemKey");
    const key = (keyInput?.value || state.idempotencyKey || newUuid()).trim();
    state.idempotencyKey = key;

    const res = await apiRequest(ENDPOINTS.walletTopUp, {
    method: "POST",
    idempotencyKey: key,
    body: {
        packageId: getPkgId(pkg),
        returnUrl: `${window.location.origin}${window.location.pathname}?topUpReturn=1`,
        },
    });

    if (!res.ok) {
      showToast("Tạo đơn nạp ZaloPay thất bại", problemMessage(res), "danger");
      return;
    }

    state.activeTopUp = res.data;
    const orderId = getTopUpId(res.data);
    showToast(
      "Đã tạo đơn nạp ZaloPay",
      `Mã đơn: ${orderId || "N/A"} (giữ nguyên Idempotency-Key để thử Replay)`,
      "success"
    );

    if (orderId) {
      sessionStorage.setItem("p2p_last_topup_order", orderId);
      startPollingOrder(orderId);
    }
    render();
  }

  function startPollingOrder(orderId) {
    stopPolling();
    pollOrderOnce(orderId);
    state.pollTimer = setInterval(() => pollOrderOnce(orderId), 3000);
  }

  async function pollOrderOnce(orderId) {
    if (!orderId || !getSession().isAuthenticated) {
        stopPolling();
        return;
    }
    const res = await apiRequest(ENDPOINTS.walletTopUpOrder(orderId));
    if (!res.ok) {
        if ([400, 403, 404].includes(res.status)) {
        stopPolling();
        sessionStorage.removeItem("p2p_last_topup_order");
        render();
        }
        return;
    }
    if (!res.data) return;

    state.activeTopUp = { ...(state.activeTopUp || {}), ...res.data };
    const st = String(res.data.status || "").toLowerCase();
    if (["completed", "succeeded", "paid"].includes(st)) {
        stopPolling();
        showToast("Nạp Gem thành công!", "Số dư ví và sổ cái đã được cập nhật.", "success");
        await loadWallet();
        } else if (["failed", "expired", "cancelled", "canceled"].includes(st)) {
        stopPolling();
        showToast("Đơn nạp kết thúc", `Trạng thái: ${res.data.status}`, "warning");
        render();
        } else {
        render();
        }
    }

  function checkTopUpReturnParam() {
    const params = new URLSearchParams(window.location.search);
    if (params.get("topUpReturn") === "1") {
      const orderId = params.get("orderId") || sessionStorage.getItem("p2p_last_topup_order");
      showToast("Đã quay lại từ ZaloPay Sandbox", "Đang kiểm tra trạng thái đơn nạp...", "info");
      if (orderId) startPollingOrder(orderId);
      params.delete("topUpReturn");
      const cleanUrl = `${window.location.pathname}${params.toString() ? `?${params.toString()}` : ""}`;
      window.history.replaceState({}, "", cleanUrl);
    }
  }

  function render() {
    if (!getSession().isAuthenticated) {
      panel.innerHTML = `<div class="card"><div class="alert alert-warning mb-0">Vui lòng đăng nhập ở tab <strong>Đăng nhập & Phiên</strong> để xem Ví Gem và nạp ZaloPay.</div></div>`;
      return;
    }

    const w = state.wallet || {};
    const available = w.availableGem ?? w.availableBalance ?? 0;
    const locked = w.lockedGem ?? w.lockedBalance ?? 0;
    const total = w.totalGem ?? (BigInt(available || 0) + BigInt(locked || 0)).toString();
    const totalPages = Math.max(1, Math.ceil(state.ledger.totalCount / state.ledger.pageSize));

    panel.innerHTML = `
      <!-- WAL-01: Balance -->
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Tổng quan Ví Gem (WAL-01)</h3>
            <p class="card-subtitle">Phân tách số dư khả dụng và số dư đang tạm khóa trong Saga giao dịch</p>
          </div>
          <button id="btnRefreshWallet" type="button" class="btn btn-sm btn-secondary">↻ Làm mới ví</button>
        </div>

        <div class="grid-3">
          <div class="stat-box">
            <div class="stat-label">Khả dụng (Available)</div>
            <div class="stat-value text-accent">${formatGem(available)} <span style="font-size:13px;">Gem</span></div>
            <div class="stat-meta">Dùng để mua vật phẩm ngay</div>
          </div>
          <div class="stat-box">
            <div class="stat-label">Đang tạm khóa (Locked / Escrow)</div>
            <div class="stat-value text-warning">${formatGem(locked)} <span style="font-size:13px;">Gem</span></div>
            <div class="stat-meta">Giữ tạm trong khi Saga khớp lệnh</div>
          </div>
          <div class="stat-box">
            <div class="stat-label">Tổng số dư</div>
            <div class="stat-value text-success">${formatGem(total)} <span style="font-size:13px;">Gem</span></div>
            <div class="stat-meta">${w.walletId ? renderIdChip(w.walletId, "Wallet") : "Authoritative SQL Ledger"}</div>
          </div>
        </div>
      </div>

      <!-- WAL-02 / WAL-03: ZaloPay Top-up & Idempotency Replay -->
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Nạp Gem qua ZaloPay Sandbox (WAL-02..04)</h3>
            <p class="card-subtitle">Hỗ trợ test Replay Idempotency-Key và tự động poll trạng thái đơn mỗi 3 giây</p>
          </div>
        </div>

        <div class="idem-bar">
          <label for="walletIdemKey">Idempotency-Key:</label>
          <input id="walletIdemKey" type="text" value="${escapeHtml(state.idempotencyKey)}" />
          <button id="btnGenWalletIdem" type="button" class="btn btn-xs btn-secondary">Tạo key mới</button>
        </div>

        <div class="grid-4" style="margin-bottom:14px;">
          ${state.packages
            .map((p) => {
              const code = getPkgId(p);
              const selected = code === state.selectedPackageCode ? "selected" : "";
              return `
                <div class="package-card ${selected}" data-pkg-code="${escapeHtml(code)}">
                  <div class="text-muted" style="font-size:12px;">${escapeHtml(p.name || code)}</div>
                  <div class="package-gem">+${formatGem(p.gemAmount || p.gems)} Gem</div>
                  <div class="package-vnd">${formatVnd(p.priceVnd || p.amountVnd)}</div>
                </div>
              `;
            })
            .join("")}
        </div>

        <div class="form-row">
          <button id="btnCreateTopUp" type="button" class="btn btn-primary">Tạo đơn nạp ZaloPay</button>
          <button id="btnReplayTopUp" type="button" class="btn btn-warning" title="Gửi lại đúng Idempotency-Key hiện tại">Replay cùng Idempotency-Key</button>
        </div>

        ${
          state.activeTopUp
            ? `
          <div class="alert alert-info" style="margin-top:12px;margin-bottom:0;">
            <div style="display:flex;flex-wrap:wrap;justify-content:space-between;align-items:center;gap:8px;">
              <div>
                <strong>Đơn nạp hiện tại:</strong>
                ${renderIdChip(getTopUpId(state.activeTopUp), "TopUpId")}
                ${state.activeTopUp.appTransId ? renderIdChip(state.activeTopUp.appTransId, "ZaloPay") : ""}
                ${statusBadge(state.activeTopUp.status || "Pending")}
                ${state.pollTimer ? `<span class="badge badge-info"><span class="spinner"></span> Auto-poll 3s</span>` : ""}
              </div>
              <div style="display:flex;gap:6px;">
                ${
                  state.activeTopUp.orderUrl || state.activeTopUp.paymentUrl
                    ? `<a href="${escapeHtml(state.activeTopUp.orderUrl || state.activeTopUp.paymentUrl)}" target="_blank" rel="noopener noreferrer" class="btn btn-xs btn-success">Mở cổng ZaloPay ↗</a>`
                    : ""
                }
                <button id="btnPollNow" type="button" class="btn btn-xs btn-secondary">Kiểm tra ngay</button>
                ${state.pollTimer ? `<button id="btnStopPoll" type="button" class="btn btn-xs btn-ghost">Dừng poll</button>` : ""}
              </div>
            </div>
          </div>`
            : ""
        }
      </div>

      <!-- WAL-05: Ledger with TaxBurn Highlight -->
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Sổ cái giao dịch Gem — Ledger (WAL-05)</h3>
            <p class="card-subtitle">Các dòng <code>TaxBurn</code> (phí sàn 5%) được tô tím nổi bật để đối soát kinh tế</p>
          </div>
          <span class="badge badge-purple">TaxBurn Highlight</span>
        </div>

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Thời gian</th>
                <th>Loại bút toán</th>
                <th>Biến động Gem</th>
                <th>Số dư sau GD</th>
                <th>Mã tham chiếu</th>
                <th>Ghi chú</th>
              </tr>
            </thead>
            <tbody>
              ${
                state.ledger.items.length === 0
                  ? `<tr><td colspan="6" class="text-muted" style="text-align:center;padding:22px;">Chưa có bút toán sổ cái nào.</td></tr>`
                  : state.ledger.items
                      .map((entry) => {
                        const type = entry.entryType || entry.type || "Unknown";
                        const isTaxBurn = String(type).toLowerCase().includes("taxburn");
                        const delta = BigInt(String(entry.amountGem ?? entry.amount ?? 0).split(".")[0]);
                        const deltaClass = delta > 0n ? "text-success" : delta < 0n ? "text-danger" : "";
                        const sign = delta > 0n ? "+" : "";
                        return `
                          <tr class="${isTaxBurn ? "row-taxburn" : ""}">
                            <td>${escapeHtml(formatDateTime(entry.createdAt || entry.timestamp))}</td>
                            <td>${statusBadge(type)}</td>
                            <td class="mono ${deltaClass}" style="font-weight:700;">${sign}${formatGem(delta)}</td>
                            <td class="mono">${formatGem(entry.balanceAfterGem ?? entry.balanceAfter ?? "—")}</td>
                            <td>${renderIdChip(entry.referenceId || entry.tradeId || entry.id)}</td>
                            <td>${escapeHtml(entry.description || entry.note || "—")}</td>
                          </tr>
                        `;
                      })    
                      .join("")
              }
            </tbody>
          </table>
        </div>

        <div class="pagination-bar">
          <span>Trang ${state.ledger.page} / ${totalPages} (Tổng ${state.ledger.totalCount} bút toán)</span>
          <div class="header-actions">
            <button id="btnLedgerPrev" type="button" class="btn btn-xs btn-secondary" ${state.ledger.page <= 1 ? "disabled" : ""}>← Trước</button>
            <button id="btnLedgerNext" type="button" class="btn btn-xs btn-secondary" ${state.ledger.page >= totalPages ? "disabled" : ""}>Sau →</button>
          </div>
        </div>
      </div>
    `;

    bindEvents();
  }

  function bindEvents() {
    panel.querySelector("#btnRefreshWallet")?.addEventListener("click", loadWallet);

    panel.querySelector("#btnGenWalletIdem")?.addEventListener("click", () => {
      state.idempotencyKey = newUuid();
      const input = panel.querySelector("#walletIdemKey");
      if (input) input.value = state.idempotencyKey;
    });

    panel.querySelectorAll("[data-pkg-code]").forEach((card) => {
      card.addEventListener("click", () => {
        state.selectedPackageCode = card.dataset.pkgCode;
        render();
      });
    });

    panel.querySelector("#btnCreateTopUp")?.addEventListener("click", () => {
      state.idempotencyKey = (panel.querySelector("#walletIdemKey")?.value || newUuid()).trim();
      createTopUpOrder();
    });

    panel.querySelector("#btnReplayTopUp")?.addEventListener("click", createTopUpOrder);

    panel.querySelector("#btnPollNow")?.addEventListener("click", () => {
    const id = getTopUpId(state.activeTopUp);
    if (id) pollOrderOnce(id);
    });

    panel.querySelector("#btnStopPoll")?.addEventListener("click", () => {
      stopPolling();
      render();
    });

    panel.querySelector("#btnLedgerPrev")?.addEventListener("click", () => {
      if (state.ledger.page > 1) {
        state.ledger.page -= 1;
        loadWallet();
      }
    });

    panel.querySelector("#btnLedgerNext")?.addEventListener("click", () => {
      state.ledger.page += 1;
      loadWallet();
    });
  }

  return {
    init() {
      checkTopUpReturnParam();
      render();
    },
    refresh: loadWallet,
    render,
    dispose: stopPolling,
  };
}