import {
  ENDPOINTS,
  apiRequest,
  escapeHtml,
  formatDateTime,
  formatGem,
  formatSignedGem,
  formatVnd,
  getSession,
  newUuid,
  problemMessage,
  renderIdChip,
  showToast,
  statusBadge,
} from "./api.js";

const DEFAULT_PACKAGES = [
  { packageId: "pkg-100", amountVnd: 10000, gemAmount: 100 },
  { packageId: "pkg-500", amountVnd: 50000, gemAmount: 500 },
  { packageId: "pkg-1000", amountVnd: 100000, gemAmount: 1000 },
  { packageId: "pkg-2500", amountVnd: 250000, gemAmount: 2500 },
];

const LEDGER_TYPES = ["TopUp", "Hold", "SettlementDebit", "SettlementCredit", "Release", "TaxBurn"];

export function createWalletModule() {
  const panel = document.getElementById("panel-wallet");

  const state = {
    wallet: null,
    packages: DEFAULT_PACKAGES,
    selectedPackageId: DEFAULT_PACKAGES[0].packageId,
    idempotencyKey: newUuid(),
    activeTopUp: null,
    pollTimer: null,
    ledger: { items: [], page: 1, pageSize: 15, totalCount: 0, type: "", tradeId: "" },
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
      apiRequest(ENDPOINTS.walletLedger, {
        query: {
          page: state.ledger.page,
          pageSize: state.ledger.pageSize,
          type: state.ledger.type,
          tradeId: state.ledger.tradeId,
        },
      }),
    ]);
    state.loading = false;

    if (walletRes.ok && walletRes.data) {
      state.wallet = walletRes.data;
    } else if (!walletRes.ok) {
      showToast("Lỗi tải Ví Gem (WAL-01)", problemMessage(walletRes), "danger");
    }

    if (pkgRes.ok && pkgRes.data) {
      const list = Array.isArray(pkgRes.data.items) ? pkgRes.data.items : [];
      if (list.length > 0) {
        state.packages = list;
        if (!state.packages.some((p) => p.packageId === state.selectedPackageId)) {
          state.selectedPackageId = state.packages[0].packageId;
        }
      }
    }

    if (ledgerRes.ok && ledgerRes.data) {
      const d = ledgerRes.data;
      state.ledger.items = Array.isArray(d.items) ? d.items : [];
      state.ledger.totalCount = d.totalCount ?? state.ledger.items.length;
    } else if (!ledgerRes.ok) {
      showToast("Lỗi tải Sổ cái (WAL-02)", problemMessage(ledgerRes), "danger");
    }

    render();
  }

  async function createTopUpOrder() {
    const pkg = state.packages.find((p) => p.packageId === state.selectedPackageId);
    if (!pkg) return;

    const keyInput = panel.querySelector("#walletIdemKey");
    const key = (keyInput?.value || state.idempotencyKey || newUuid()).trim();
    state.idempotencyKey = key;

    // WAL-04: CreateTopUpRequest chỉ nhận { packageId }
    const res = await apiRequest(ENDPOINTS.walletTopUp, {
      method: "POST",
      idempotencyKey: key,
      body: { packageId: pkg.packageId },
    });

    if (!res.ok) {
      showToast("Tạo đơn nạp ZaloPay thất bại (WAL-04)", problemMessage(res), "danger");
      return;
    }

    state.activeTopUp = res.data;
    const topUpId = res.data?.topUpId || "";
    const statusNote = res.status === 202
      ? "202 Accepted (Pending — chờ reconcile)"
      : "201 Created";
    showToast(
      `Đơn nạp ZaloPay (${statusNote})`,
      `TopUpId: ${topUpId || "N/A"} (giữ Idempotency-Key để test Replay / 409 Conflict)`,
      "success"
    );

    if (topUpId) {
      sessionStorage.setItem("p2p_last_topup_order", topUpId);
      startPollingOrder(topUpId);
    }
    render();
  }

  function startPollingOrder(topUpId) {
    stopPolling();
    pollOrderOnce(topUpId);
    state.pollTimer = setInterval(() => pollOrderOnce(topUpId), 3000);
  }

  async function pollOrderOnce(topUpId) {
    if (!topUpId || !getSession().isAuthenticated) {
      stopPolling();
      return;
    }
    const res = await apiRequest(ENDPOINTS.walletTopUpOrder(topUpId));
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
    if (st === "paid") {
      stopPolling();
      showToast("Nạp Gem thành công!", "Số dư ví và sổ cái đã được cập nhật.", "success");
      await loadWallet();
    } else if (["failed", "expired"].includes(st)) {
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
      const topUpId = params.get("topUpId") || params.get("orderId") || sessionStorage.getItem("p2p_last_topup_order");
      showToast("Đã quay lại từ ZaloPay Sandbox", "Đang kiểm tra trạng thái đơn nạp (WAL-05)...", "info");
      if (topUpId) startPollingOrder(topUpId);
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
    const totalBalance = w.totalBalance ?? 0;
    const holdBalance = w.holdBalance ?? 0;
    const availableBalance = w.availableBalance ?? 0;
    const version = w.version ?? "—";
    const totalPages = Math.max(1, Math.ceil(state.ledger.totalCount / state.ledger.pageSize));

    panel.innerHTML = `
      <!-- WAL-01: Balance -->
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Tổng quan Ví Gem (WAL-01)</h3>
            <p class="card-subtitle">Invariant: <code>totalBalance = availableBalance + holdBalance</code> • Currency: <code>${escapeHtml(w.currency || "GEM")}</code></p>
          </div>
          <button id="btnRefreshWallet" type="button" class="btn btn-sm btn-secondary">↻ Làm mới ví</button>
        </div>

        <div class="grid-3">
          <div class="stat-box">
            <div class="stat-label">Khả dụng (availableBalance)</div>
            <div class="stat-value text-accent">${formatGem(availableBalance)} <span style="font-size:13px;">Gem</span></div>
            <div class="stat-meta">Dùng để mua vật phẩm ngay</div>
          </div>
          <div class="stat-box">
            <div class="stat-label">Đang tạm giữ (holdBalance)</div>
            <div class="stat-value text-warning">${formatGem(holdBalance)} <span style="font-size:13px;">Gem</span></div>
            <div class="stat-meta">Escrow trong khi Saga khớp lệnh</div>
          </div>
          <div class="stat-box">
            <div class="stat-label">Tổng số dư (totalBalance)</div>
            <div class="stat-value text-success">${formatGem(totalBalance)} <span style="font-size:13px;">Gem</span></div>
            <div class="stat-meta">Version: <strong class="mono">v${escapeHtml(version)}</strong> ${w.userId ? `• ${renderIdChip(w.userId, "User")}` : ""}</div>
          </div>
        </div>
      </div>

      <!-- WAL-03 / WAL-04 / WAL-05: ZaloPay Top-up & Idempotency Replay -->
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Nạp Gem qua ZaloPay Sandbox (WAL-03..05)</h3>
            <p class="card-subtitle"><code>POST /api/v1/wallet/top-ups</code> gửi <code>{ packageId }</code> kèm <code>Idempotency-Key</code> và poll SQL mỗi 3 giây</p>
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
              const selected = p.packageId === state.selectedPackageId ? "selected" : "";
              return `
                <div class="package-card ${selected}" data-pkg-id="${escapeHtml(p.packageId)}">
                  <div class="text-muted mono" style="font-size:12px;">${escapeHtml(p.packageId)}</div>
                  <div class="package-gem">+${formatGem(p.gemAmount)} Gem</div>
                  <div class="package-vnd">${formatVnd(p.amountVnd)}</div>
                </div>
              `;
            })
            .join("")}
        </div>

        <div class="form-row">
          <button id="btnCreateTopUp" type="button" class="btn btn-primary">Tạo đơn nạp ZaloPay</button>
          <button id="btnReplayTopUp" type="button" class="btn btn-warning" title="Gửi lại đúng Idempotency-Key hiện tại (cùng gói => replay, khác gói => 409 IDEMPOTENCY_CONFLICT)">Replay cùng Idempotency-Key</button>
        </div>

        ${
          state.activeTopUp
            ? `
          <div class="alert alert-info" style="margin-top:12px;margin-bottom:0;">
            <div style="display:flex;flex-wrap:wrap;justify-content:space-between;align-items:center;gap:8px;">
              <div>
                <strong>Đơn nạp hiện tại:</strong>
                ${renderIdChip(state.activeTopUp.topUpId, "TopUpId")}
                ${state.activeTopUp.appTransId ? renderIdChip(state.activeTopUp.appTransId, "appTransId") : ""}
                ${state.activeTopUp.providerTxnId ? renderIdChip(String(state.activeTopUp.providerTxnId), "zpTransId") : ""}
                ${statusBadge(state.activeTopUp.status || "Pending")}
                ${state.pollTimer ? `<span class="badge badge-info"><span class="spinner"></span> Auto-poll 3s</span>` : ""}
              </div>
              <div style="display:flex;gap:6px;">
                ${
                  state.activeTopUp.paymentUrl
                    ? `<a href="${escapeHtml(state.activeTopUp.paymentUrl)}" target="_blank" rel="noopener noreferrer" class="btn btn-xs btn-success">Mở cổng ZaloPay ↗</a>`
                    : ""
                }
                <button id="btnPollNow" type="button" class="btn btn-xs btn-secondary">Kiểm tra ngay (WAL-05)</button>
                ${state.pollTimer ? `<button id="btnStopPoll" type="button" class="btn btn-xs btn-ghost">Dừng poll</button>` : ""}
              </div>
            </div>
          </div>`
            : ""
        }
      </div>

      <!-- WAL-02: Ledger with Type Filter & TaxBurn Highlight -->
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Sổ cái giao dịch Gem — Ledger (WAL-02)</h3>
            <p class="card-subtitle">Dòng <code>TaxBurn</code> ghi dưới Seller với <code>totalDelta = 0</code>, <code>holdDelta = 0</code>, <code>feeGem > 0</code> (audit-only)</p>
          </div>
          <span class="badge badge-purple">TaxBurn Highlight</span>
        </div>

        <form id="ledgerFilterForm" class="form-row">
          <div class="form-group" style="max-width:210px;">
            <label for="ledgerTypeFilter">Loại bút toán (type)</label>
            <select id="ledgerTypeFilter">
              <option value="">Tất cả</option>
              ${LEDGER_TYPES.map((t) => `<option value="${t}" ${state.ledger.type === t ? "selected" : ""}>${t}</option>`).join("")}
            </select>
          </div>
          <div class="form-group" style="max-width:320px;">
            <label for="ledgerTradeIdFilter">Lọc theo Trade ID (tuỳ chọn)</label>
            <input id="ledgerTradeIdFilter" type="text" value="${escapeHtml(state.ledger.tradeId)}" placeholder="UUID tradeId..." />
          </div>
          <button type="submit" class="btn btn-primary">Lọc sổ cái</button>
          <button id="btnResetLedgerFilter" type="button" class="btn btn-secondary">Xóa lọc</button>
        </form>

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Thời gian</th>
                <th>Entry ID</th>
                <th>Loại (type)</th>
                <th>totalDelta</th>
                <th>holdDelta</th>
                <th>feeGem</th>
                <th>Trade / TopUp ID</th>
              </tr>
            </thead>
            <tbody>
              ${
                state.ledger.items.length === 0
                  ? `<tr><td colspan="7" class="text-muted" style="text-align:center;padding:22px;">Chưa có bút toán sổ cái nào khớp bộ lọc.</td></tr>`
                  : state.ledger.items
                      .map((entry) => {
                        const isTaxBurn = String(entry.type) === "TaxBurn";
                        const totalBi = BigInt(String(entry.totalDelta ?? 0));
                        const holdBi = BigInt(String(entry.holdDelta ?? 0));
                        const totalCls = totalBi > 0n ? "text-success" : totalBi < 0n ? "text-danger" : "text-muted";
                        const holdCls = holdBi > 0n ? "text-warning" : holdBi < 0n ? "text-accent" : "text-muted";
                        const refHtml = entry.tradeId
                          ? renderIdChip(entry.tradeId, "Trade")
                          : entry.topUpId
                            ? renderIdChip(entry.topUpId, "TopUp")
                            : `<span class="text-muted">—</span>`;
                        return `
                          <tr class="${isTaxBurn ? "row-taxburn" : ""}">
                            <td>${escapeHtml(formatDateTime(entry.createdAt))}</td>
                            <td>${renderIdChip(entry.entryId)}</td>
                            <td>${statusBadge(entry.type)}</td>
                            <td class="mono ${totalCls}" style="font-weight:700;">${escapeHtml(formatSignedGem(entry.totalDelta))}</td>
                            <td class="mono ${holdCls}" style="font-weight:700;">${escapeHtml(formatSignedGem(entry.holdDelta))}</td>
                            <td class="mono">
                              ${
                                entry.feeGem !== null && entry.feeGem !== undefined
                                  ? `<span class="badge badge-purple">🔥 ${formatGem(entry.feeGem)} Gem</span>`
                                  : `<span class="text-muted">—</span>`
                              }
                            </td>
                            <td>${refHtml}</td>
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

    panel.querySelectorAll("[data-pkg-id]").forEach((card) => {
      card.addEventListener("click", () => {
        state.selectedPackageId = card.dataset.pkgId;
        render();
      });
    });

    panel.querySelector("#btnCreateTopUp")?.addEventListener("click", () => {
      state.idempotencyKey = (panel.querySelector("#walletIdemKey")?.value || newUuid()).trim();
      createTopUpOrder();
    });

    panel.querySelector("#btnReplayTopUp")?.addEventListener("click", createTopUpOrder);

    panel.querySelector("#btnPollNow")?.addEventListener("click", () => {
      const id = state.activeTopUp?.topUpId;
      if (id) pollOrderOnce(id);
    });

    panel.querySelector("#btnStopPoll")?.addEventListener("click", () => {
      stopPolling();
      render();
    });

    panel.querySelector("#ledgerFilterForm")?.addEventListener("submit", (e) => {
      e.preventDefault();
      state.ledger.type = panel.querySelector("#ledgerTypeFilter").value;
      state.ledger.tradeId = panel.querySelector("#ledgerTradeIdFilter").value.trim();
      state.ledger.page = 1;
      loadWallet();
    });

    panel.querySelector("#btnResetLedgerFilter")?.addEventListener("click", () => {
      state.ledger.type = "";
      state.ledger.tradeId = "";
      state.ledger.page = 1;
      loadWallet();
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