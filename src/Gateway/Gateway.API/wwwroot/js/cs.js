import {
  ENDPOINTS,
  SEED_USERS,
  apiRequest,
  escapeHtml,
  formatDateTime,
  formatGem,
  formatSignedGem,
  getSession,
  getTemplateName,
  newUuid,
  problemMessage,
  renderIdChip,
  renderProblemAlert,
  showToast,
  statusBadge,
} from "./api.js";

const TRADE_STATUSES = [
  "HoldingFunds",
  "Transferring",
  "Settling",
  "Finalizing",
  "ReversingItem",
  "ReleasingFunds",
  "Completed",
  "Failed",
];

const QUICK_PLAYERS = [SEED_USERS.seller, SEED_USERS.buyer, SEED_USERS.buyerFull];

export function createCsModule() {
  const panel = document.getElementById("panel-cs");

  const state = {
    search: {
      userId: SEED_USERS.seller.userId,
      status: "",
      page: 1,
      pageSize: 15,
      items: [],
      totalCount: 0,
      hasSearched: false,
      error: null,
    },

    trace: {
      tradeId: "",
      data: null,
      error: null,
      loading: false,
    },

    mod: {
      userId: SEED_USERS.buyer.userId,
      status: "TradeRestricted",
      reason: "Kiểm tra vi phạm giao dịch (CS Demo)",
      idempotencyKey: newUuid(),
      result: null,
      error: null,
    },
  };

  async function searchTrades() {
    if (!getSession().isAuthenticated) return;
    const uid = state.search.userId.trim();
    if (!uid) {
      showToast("Thiếu userId", "Vui lòng nhập hoặc chọn nhanh một Player seed.", "warning");
      return;
    }

    const res = await apiRequest(ENDPOINTS.adminTradesSearch, {
      query: {
        userId: uid,
        status: state.search.status,
        page: state.search.page,
        pageSize: state.search.pageSize,
      },
    });

    state.search.hasSearched = true;
    if (res.ok && res.data) {
      state.search.items = Array.isArray(res.data.items) ? res.data.items : [];
      state.search.totalCount = res.data.totalCount ?? state.search.items.length;
      state.search.error = null;
    } else {
      state.search.items = [];
      state.search.totalCount = 0;
      state.search.error = res;
      showToast("Lỗi ADM-06 (Tìm giao dịch)", problemMessage(res), "danger");
    }
    render();
  }

  async function loadTradeTrace(tradeIdInput) {
    if (!getSession().isAuthenticated) return;
    const tid = (tradeIdInput ?? state.trace.tradeId).trim();
    state.trace.tradeId = tid;

    if (!tid) {
      showToast("Thiếu tradeId", "Vui lòng nhập UUID tradeId hoặc bấm 'Soi Trace' từ bảng giao dịch.", "warning");
      return;
    }

    state.trace.loading = true;
    state.trace.error = null;
    render();

    const res = await apiRequest(ENDPOINTS.adminTradeTrace(tid));
    state.trace.loading = false;

    if (res.ok && res.data) {
      state.trace.data = res.data;
      state.trace.error = null;
      if (res.data.partial) {
        showToast("Trace trả về Partial", `Nguồn lỗi: ${(res.data.unavailableSources || []).join(", ")}`, "warning");
      }
    } else {
      state.trace.data = null;
      state.trace.error = res;
      showToast("Lỗi ADM-05 (Trade Trace)", problemMessage(res), "danger");
    }
    render();
    panel.querySelector("#csTraceCard")?.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  async function submitModeration() {
    if (!getSession().isAuthenticated) return;
    const uid = state.mod.userId.trim();
    const idemKey = state.mod.idempotencyKey.trim() || newUuid();
    state.mod.idempotencyKey = idemKey;

    if (!uid) {
      showToast("Thiếu userId", "Vui lòng nhập hoặc chọn một userId.", "warning");
      return;
    }

    const res = await apiRequest(ENDPOINTS.adminModerateUser(uid), {
      method: "PATCH",
      idempotencyKey: idemKey,
      body: {
        status: state.mod.status,
        reason: state.mod.reason,
      },
    });

    if (!res.ok) {
      state.mod.error = res;
      state.mod.result = null;
      const code = res.problem?.code || "";
      const title =
        code === "INVALID_ACCOUNT_TARGET"
          ? "409 INVALID_ACCOUNT_TARGET — Chỉ được xử lý Player"
          : code === "IDEMPOTENCY_CONFLICT"
            ? "409 IDEMPOTENCY_CONFLICT — Trùng key khác payload"
            : "Lỗi ADM-07 (Moderation)";
      showToast(title, problemMessage(res), "danger");
      render();
      return;
    }

    state.mod.error = null;
    state.mod.result = res.data;
    showToast(
      "Cập nhật trạng thái tài khoản thành công (ADM-07)",
      `Target: ${res.data.targetUserId} → ${res.data.status} (v${res.data.version})`,
      "success"
    );
    render();
  }

  function renderSearchSection() {
    const s = state.search;
    const totalPages = Math.max(1, Math.ceil(s.totalCount / s.pageSize));

    return `
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Tìm giao dịch theo User (ADM-06)</h3>
            <p class="card-subtitle"><code>GET /api/v1/admin/trades?userId=...</code> — Tìm mọi giao dịch mà user là Buyer hoặc Seller</p>
          </div>
        </div>

        <div class="seed-buttons">
          <span class="text-muted" style="font-size:12px;align-self:center;margin-right:4px;">Điền nhanh Player:</span>
          ${QUICK_PLAYERS.map(
            (p) => `<button type="button" class="btn btn-xs btn-secondary" data-cs-search-user="${escapeHtml(p.userId)}">${escapeHtml(p.label)} (${p.userId.slice(-4)})</button>`
          ).join("")}
        </div>

        <form id="csSearchForm" class="form-row">
          <div class="form-group" style="min-width:290px;">
            <label for="csSearchUserId">User ID (bắt buộc — UUID)</label>
            <input id="csSearchUserId" type="text" required value="${escapeHtml(s.userId)}" placeholder="10000000-0000-4000-8000-000000000003" />
          </div>
          <div class="form-group" style="max-width:210px;">
            <label for="csSearchStatus">Trạng thái (status)</label>
            <select id="csSearchStatus">
              <option value="">Tất cả trạng thái</option>
              ${TRADE_STATUSES.map((st) => `<option value="${st}" ${s.status === st ? "selected" : ""}>${st}</option>`).join("")}
            </select>
          </div>
          <button type="submit" class="btn btn-primary">Tìm giao dịch</button>
        </form>

        ${s.error ? renderProblemAlert(s.error) : ""}

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Trade ID</th>
                <th>grossGem</th>
                <th>feeGem</th>
                <th>sellerNetGem</th>
                <th>status</th>
                <th>failureCode</th>
                <th>requiresReview</th>
                <th>Thời gian</th>
                <th>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              ${
                s.items.length === 0
                  ? `<tr><td colspan="9" class="text-muted" style="text-align:center;padding:22px;">${
                      s.hasSearched ? "Không tìm thấy giao dịch nào cho user này." : "Chọn Player hoặc nhập userId rồi bấm Tìm giao dịch."
                    }</td></tr>`
                  : s.items
                      .map(
                        (t) => `
                        <tr>
                          <td>${renderIdChip(t.tradeId)}</td>
                          <td class="mono">${formatGem(t.grossGem)}</td>
                          <td class="mono text-warning">${formatGem(t.feeGem)}</td>
                          <td class="mono text-success">${formatGem(t.sellerNetGem)}</td>
                          <td>${statusBadge(t.status)}</td>
                          <td class="mono">${t.failureCode ? `<span class="text-danger">${escapeHtml(t.failureCode)}</span>` : `<span class="text-muted">—</span>`}</td>
                          <td>${t.requiresReview ? `<span class="badge badge-warning">⚠ Review</span>` : `<span class="text-muted">false</span>`}</td>
                          <td>${escapeHtml(formatDateTime(t.createdAt))}</td>
                          <td>
                            <button type="button" class="btn btn-xs btn-primary" data-inspect-trace="${escapeHtml(t.tradeId)}">Soi Trace (ADM-05)</button>
                          </td>
                        </tr>`
                      )
                      .join("")
              }
            </tbody>
          </table>
        </div>

        <div class="pagination-bar">
          <span>Trang ${s.page} / ${totalPages} (Tổng ${s.totalCount} giao dịch)</span>
          <div class="header-actions">
            <button id="btnCsSearchPrev" type="button" class="btn btn-xs btn-secondary" ${s.page <= 1 ? "disabled" : ""}>← Trước</button>
            <button id="btnCsSearchNext" type="button" class="btn btn-xs btn-secondary" ${s.page >= totalPages ? "disabled" : ""}>Sau →</button>
          </div>
        </div>
      </div>
    `;
  }

  function renderTraceSection() {
    const tr = state.trace;
    const data = tr.data;
    const unavailable = data?.unavailableSources || [];
    const walletUnavailable = unavailable.includes("wallet");
    const inventoryUnavailable = unavailable.includes("inventory");

    const mkt = data?.marketplace;
    const trade = mkt?.trade;
    const steps = mkt?.steps || [];
    const wal = data?.wallet;
    const inv = data?.inventory;

    return `
      <div id="csTraceCard" class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Trade Trace 3 Dịch Vụ (ADM-05)</h3>
            <p class="card-subtitle"><code>GET /api/v1/admin/trades/{tradeId}/trace</code> — Đối soát Marketplace (Saga) + Wallet (Hold/Ledger) + Inventory (Transfer)</p>
          </div>
        </div>

        <form id="csTraceForm" class="form-row">
          <div class="form-group">
            <label for="csTraceTradeId">Trade ID (UUID)</label>
            <input id="csTraceTradeId" type="text" value="${escapeHtml(tr.tradeId)}" placeholder="Dán tradeId hoặc bấm 'Soi Trace' từ bảng trên..." />
          </div>
          <button type="submit" class="btn btn-primary" ${tr.loading ? "disabled" : ""}>
            ${tr.loading ? `<span class="spinner"></span> Đang soi...` : "Soi Trace (ADM-05)"}
          </button>
        </form>

        ${tr.error ? renderProblemAlert(tr.error) : ""}

        ${
          data
            ? `
          ${
            data.partial || unavailable.length > 0
              ? `<div class="alert alert-warning">
                  <strong>⚠ Trace chỉ lấy được một phần (<code>partial = true</code>):</strong>
                  Dịch vụ không phản hồi: ${unavailable.map((u) => `<span class="badge badge-danger" style="margin-left:4px;">${escapeHtml(u)}</span>`).join("")}
                </div>`
              : ""
          }

          <!-- 1. Marketplace Block -->
          <div class="trace-section">
            <div class="trace-section-header">
              <h4 class="trace-section-title">1. Marketplace — Saga & Trade State</h4>
              <div>
                ${statusBadge(trade?.status)}
                ${trade?.requiresReview ? `<span class="badge badge-warning">requiresReview</span>` : ""}
              </div>
            </div>

            <div class="kv-grid">
              <div class="kv-cell"><div class="kv-cell-label">tradeId</div>${renderIdChip(trade?.tradeId)}</div>
              <div class="kv-cell"><div class="kv-cell-label">correlationId</div>${renderIdChip(mkt?.correlationId)}</div>
              <div class="kv-cell"><div class="kv-cell-label">listingId</div>${renderIdChip(trade?.listingId)}</div>
              <div class="kv-cell"><div class="kv-cell-label">buyerId</div>${renderIdChip(trade?.buyerId)}</div>
              <div class="kv-cell"><div class="kv-cell-label">sellerId</div>${renderIdChip(trade?.sellerId)}</div>
              <div class="kv-cell"><div class="kv-cell-label">Template & SL</div><strong>${escapeHtml(getTemplateName(trade?.templateId) || "Template")}</strong> <span class="mono">×${escapeHtml(trade?.quantity)}</span></div>
              <div class="kv-cell"><div class="kv-cell-label">grossGem / feeGem / sellerNetGem</div><span class="mono">${formatGem(trade?.grossGem)} / <span class="text-warning">${formatGem(trade?.feeGem)}</span> / <span class="text-success">${formatGem(trade?.sellerNetGem)}</span></span></div>
              <div class="kv-cell"><div class="kv-cell-label">failureCode</div>${trade?.failureCode ? `<code class="text-danger">${escapeHtml(trade.failureCode)}</code>` : `<span class="text-muted">null</span>`}</div>
            </div>

            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>#</th>
                    <th>step</th>
                    <th>outcome</th>
                    <th>errorCode</th>
                    <th>at</th>
                  </tr>
                </thead>
                <tbody>
                  ${
                    steps.length === 0
                      ? `<tr><td colspan="5" class="text-muted" style="text-align:center;padding:14px;">Chưa có Saga step log nào.</td></tr>`
                      : steps
                          .map(
                            (st, i) => `
                            <tr>
                              <td class="mono text-muted">${i + 1}</td>
                              <td class="mono"><strong>${escapeHtml(st.step)}</strong></td>
                              <td>${statusBadge(st.outcome)}</td>
                              <td class="mono">${st.errorCode ? `<span class="text-danger">${escapeHtml(st.errorCode)}</span>` : `<span class="text-muted">—</span>`}</td>
                              <td>${escapeHtml(formatDateTime(st.at))}</td>
                            </tr>`
                          )
                          .join("")
                  }
                </tbody>
              </table>
            </div>
          </div>

          <!-- 2. Wallet Block -->
          <div class="trace-section">
            <div class="trace-section-header">
              <h4 class="trace-section-title">2. Wallet — FundHold & Ledger Entries</h4>
              <div>
                ${
                  walletUnavailable
                    ? `<span class="badge badge-danger">Unavailable</span>`
                    : wal
                      ? statusBadge(wal.settlementStatus)
                      : `<span class="badge badge-muted">null</span>`
                }
              </div>
            </div>

            ${
              walletUnavailable
                ? `<div class="alert alert-danger mb-0">Wallet service không khả dụng (<code>unavailableSources</code> chứa <code>wallet</code>).</div>`
                : wal
                  ? `
                <div class="kv-grid">
                  <div class="kv-cell"><div class="kv-cell-label">settlementStatus</div>${statusBadge(wal.settlementStatus)}</div>
                  <div class="kv-cell"><div class="kv-cell-label">hold.status</div>${wal.hold ? statusBadge(wal.hold.status) : `<span class="text-muted">null (chưa tạo Hold)</span>`}</div>
                  <div class="kv-cell"><div class="kv-cell-label">hold.amount</div><strong class="mono">${wal.hold ? `${formatGem(wal.hold.amount)} Gem` : "—"}</strong></div>
                  <div class="kv-cell"><div class="kv-cell-label">hold.buyerId</div>${wal.hold ? renderIdChip(wal.hold.buyerId) : `<span class="text-muted">—</span>`}</div>
                </div>

                <div class="table-wrap">
                  <table>
                    <thead>
                      <tr>
                        <th>type</th>
                        <th>userId</th>
                        <th>totalDelta</th>
                        <th>holdDelta</th>
                        <th>feeGem</th>
                        <th>createdAt</th>
                      </tr>
                    </thead>
                    <tbody>
                      ${
                        (wal.entries || []).length === 0
                          ? `<tr><td colspan="6" class="text-muted" style="text-align:center;padding:14px;">Chưa có bút toán Wallet nào cho trade này.</td></tr>`
                          : wal.entries
                              .map((e) => {
                                const isTaxBurn = String(e.type) === "TaxBurn";
                                return `
                                  <tr class="${isTaxBurn ? "row-taxburn" : ""}">
                                    <td>${statusBadge(e.type)}</td>
                                    <td>${renderIdChip(e.userId)}</td>
                                    <td class="mono">${escapeHtml(formatSignedGem(e.totalDelta))}</td>
                                    <td class="mono">${escapeHtml(formatSignedGem(e.holdDelta))}</td>
                                    <td class="mono">${e.feeGem !== null && e.feeGem !== undefined ? `<span class="badge badge-purple">🔥 ${formatGem(e.feeGem)}</span>` : "—"}</td>
                                    <td>${escapeHtml(formatDateTime(e.createdAt))}</td>
                                  </tr>`;
                              })
                              .join("")
                      }
                    </tbody>
                  </table>
                </div>`
                  : `<div class="alert alert-info mb-0">Wallet trace là <code>null</code>.</div>`
            }
          </div>

          <!-- 3. Inventory Block -->
          <div class="trace-section mb-0">
            <div class="trace-section-header">
              <h4 class="trace-section-title">3. Inventory — Item Transfer Record</h4>
              <div>
                ${
                  inventoryUnavailable
                    ? `<span class="badge badge-danger">Unavailable</span>`
                    : inv
                      ? statusBadge(inv.status)
                      : `<span class="badge badge-muted">Chưa có record (null)</span>`
                }
              </div>
            </div>

            ${
              inventoryUnavailable
                ? `<div class="alert alert-danger mb-0">Inventory service không khả dụng (<code>unavailableSources</code> chứa <code>inventory</code>).</div>`
                : inv
                  ? `
                <div class="kv-grid mb-0">
                  <div class="kv-cell"><div class="kv-cell-label">status</div>${statusBadge(inv.status)}</div>
                  <div class="kv-cell"><div class="kv-cell-label">itemId</div>${renderIdChip(inv.itemId)}</div>
                  <div class="kv-cell"><div class="kv-cell-label">sellerId</div>${renderIdChip(inv.sellerId)}</div>
                  <div class="kv-cell"><div class="kv-cell-label">buyerId</div>${renderIdChip(inv.buyerId)}</div>
                  <div class="kv-cell"><div class="kv-cell-label">quantity</div><strong class="mono">${escapeHtml(inv.quantity)}</strong></div>
                  <div class="kv-cell"><div class="kv-cell-label">updatedAt</div><span>${escapeHtml(formatDateTime(inv.updatedAt))}</span></div>
                </div>`
                  : `<div class="alert alert-info mb-0">Chưa có bản ghi transfer trong Inventory (<code>inventory: null</code> — bước TransferItem chưa chạy hoặc Hold thất bại trước đó; không phải lỗi dependency).</div>`
            }
          </div>`
            : !tr.error && !tr.loading
              ? `<div class="empty-placeholder">Nhập Trade ID hoặc bấm <strong>Soi Trace (ADM-05)</strong> ở bảng giao dịch phía trên để đối soát 3 dịch vụ.</div>`
              : ""
        }
      </div>
    `;
  }

  function renderModerationSection() {
    const m = state.mod;

    return `
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Moderation tài khoản Player (ADM-07)</h3>
            <p class="card-subtitle"><code>PATCH /api/v1/admin/users/{userId}/status</code> — Chỉ áp dụng cho <code>Player</code> (nhắm GM/CS trả <code>409 INVALID_ACCOUNT_TARGET</code>)</p>
          </div>
        </div>

        <div class="seed-buttons">
          <span class="text-muted" style="font-size:12px;align-self:center;margin-right:4px;">Chọn nhanh Target:</span>
          ${QUICK_PLAYERS.map(
            (p) => `<button type="button" class="btn btn-xs btn-secondary" data-cs-mod-user="${escapeHtml(p.userId)}">${escapeHtml(p.label)} (${p.userId.slice(-4)})</button>`
          ).join("")}
          <button type="button" class="btn btn-xs btn-danger" data-cs-mod-user="${escapeHtml(SEED_USERS.gm.userId)}" title="Chọn GM (...0001) để demo 409 INVALID_ACCOUNT_TARGET">
            ⚠ Chọn GM (...0001) — Demo 409
          </button>
        </div>

        <div class="idem-bar">
          <label for="csModIdemKey">Idempotency-Key:</label>
          <input id="csModIdemKey" type="text" value="${escapeHtml(m.idempotencyKey)}" />
          <button id="btnCsModGenIdem" type="button" class="btn btn-xs btn-secondary">Tạo key mới</button>
        </div>

        <form id="csModForm">
          <div class="form-row">
            <div class="form-group" style="min-width:290px;">
              <label for="csModUserId">Target User ID (UUID)</label>
              <input id="csModUserId" type="text" required value="${escapeHtml(m.userId)}" placeholder="10000000-0000-4000-8000-000000000004" />
            </div>
            <div class="form-group" style="max-width:220px;">
              <label for="csModStatus">Trạng thái mới (status)</label>
              <select id="csModStatus">
                <option value="Active" ${m.status === "Active" ? "selected" : ""}>Active</option>
                <option value="TradeRestricted" ${m.status === "TradeRestricted" ? "selected" : ""}>TradeRestricted</option>
                <option value="Banned" ${m.status === "Banned" ? "selected" : ""}>Banned</option>
              </select>
            </div>
          </div>

          <div class="form-row">
            <div class="form-group">
              <label for="csModReason">Lý do — reason (bắt buộc, <span id="csModReasonCount">${m.reason.length}</span>/500 ký tự)</label>
              <textarea id="csModReason" rows="2" maxlength="500" required placeholder="Nhập lý do xử lý vi phạm...">${escapeHtml(m.reason)}</textarea>
            </div>
          </div>

          ${m.error ? renderProblemAlert(m.error) : ""}
          ${
            m.result
              ? `<div class="alert alert-success">
                  <strong>✓ Đã cập nhật trạng thái:</strong>
                  ${renderIdChip(m.result.actionId, "actionId")}
                  ${renderIdChip(m.result.targetUserId, "targetUserId")}
                  ${statusBadge(m.result.status)}
                  <span class="badge badge-info">v${escapeHtml(m.result.version)}</span>
                  <span class="text-muted" style="margin-left:6px;">${escapeHtml(formatDateTime(m.result.updatedAt))}</span>
                </div>`
              : ""
          }

          <div class="form-row mb-0">
            <button type="submit" class="btn btn-primary">Áp dụng Moderation (PATCH ADM-07)</button>
            <button id="btnCsModReplay" type="button" class="btn btn-warning" title="Gửi lại đúng Idempotency-Key hiện tại">Replay cùng Idempotency-Key</button>
          </div>
        </form>
      </div>
    `;
  }

  function render() {
    if (!getSession().isAuthenticated) {
      panel.innerHTML = `<div class="card"><div class="alert alert-warning mb-0">Vui lòng đăng nhập tài khoản <strong>CS</strong> (<code>cs@marketplace.local</code>) để sử dụng Customer Support Portal.</div></div>`;
      return;
    }

    panel.innerHTML = `
      ${renderSearchSection()}
      ${renderTraceSection()}
      ${renderModerationSection()}
    `;

    bindEvents();
  }

  function bindEvents() {
    panel.querySelectorAll("[data-cs-search-user]").forEach((btn) => {
      btn.addEventListener("click", () => {
        state.search.userId = btn.dataset.csSearchUser;
        state.search.page = 1;
        searchTrades();
      });
    });

    panel.querySelector("#csSearchForm")?.addEventListener("submit", (e) => {
      e.preventDefault();
      state.search.userId = panel.querySelector("#csSearchUserId").value.trim();
      state.search.status = panel.querySelector("#csSearchStatus").value;
      state.search.page = 1;
      searchTrades();
    });

    panel.querySelector("#btnCsSearchPrev")?.addEventListener("click", () => {
      if (state.search.page > 1) {
        state.search.page -= 1;
        searchTrades();
      }
    });

    panel.querySelector("#btnCsSearchNext")?.addEventListener("click", () => {
      state.search.page += 1;
      searchTrades();
    });

    panel.querySelectorAll("[data-inspect-trace]").forEach((btn) => {
      btn.addEventListener("click", () => loadTradeTrace(btn.dataset.inspectTrace));
    });

    panel.querySelector("#csTraceForm")?.addEventListener("submit", (e) => {
      e.preventDefault();
      loadTradeTrace(panel.querySelector("#csTraceTradeId").value);
    });

    panel.querySelectorAll("[data-cs-mod-user]").forEach((btn) => {
      btn.addEventListener("click", () => {
        state.mod.userId = btn.dataset.csModUser;
        state.mod.idempotencyKey = newUuid();
        state.mod.error = null;
        state.mod.result = null;
        render();
      });
    });

    panel.querySelector("#btnCsModGenIdem")?.addEventListener("click", () => {
      state.mod.idempotencyKey = newUuid();
      const input = panel.querySelector("#csModIdemKey");
      if (input) input.value = state.mod.idempotencyKey;
    });

    panel.querySelector("#csModIdemKey")?.addEventListener("input", (e) => {
      state.mod.idempotencyKey = e.target.value;
    });
    panel.querySelector("#csModUserId")?.addEventListener("input", (e) => {
      state.mod.userId = e.target.value;
    });
    panel.querySelector("#csModStatus")?.addEventListener("change", (e) => {
      state.mod.status = e.target.value;
    });
    panel.querySelector("#csModReason")?.addEventListener("input", (e) => {
      state.mod.reason = e.target.value;
      const counter = panel.querySelector("#csModReasonCount");
      if (counter) counter.textContent = String(state.mod.reason.length);
    });

    panel.querySelector("#csModForm")?.addEventListener("submit", (e) => {
      e.preventDefault();
      submitModeration();
    });

    panel.querySelector("#btnCsModReplay")?.addEventListener("click", () => {
      submitModeration();
    });
  }

  return {
    init: render,
    refresh() {
      if (getSession().isAuthenticated && state.search.userId) {
        return searchTrades();
      }
      render();
    },
    render,
  };
}