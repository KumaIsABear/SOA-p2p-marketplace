import {
  ENDPOINTS,
  apiRequest,
  closeModal,
  escapeHtml,
  formatDateTime,
  formatGem,
  getSession,
  newUuid,
  openModal,
  problemMessage,
  rarityClass,
  renderIdChip,
  showToast,
  statusBadge,
} from "./api.js";

const SAGA_MAIN_STEPS = [
  { key: "Initiated", label: "1. Initiated" },
  { key: "FundsReserved", label: "2. FundsReserved" },
  { key: "ItemTransferred", label: "3. ItemTransferred" },
  { key: "FundsSettled", label: "4. FundsSettled" },
  { key: "Completed", label: "5. Completed" },
];

const SAGA_COMP_STEPS = [
  { key: "ReversingItem", label: "Bồi hoàn: ReversingItem" },
  { key: "ReleasingFunds", label: "Bồi hoàn: ReleasingFunds" },
  { key: "Failed", label: "Kết thúc: Failed" },
];

export function createMarketplaceModule(appCtx) {
  const panel = document.getElementById("panel-marketplace");

  const state = {
    subTab: "catalog", // catalog | mine | history
    filters: { q: "", rarity: "", minPrice: "", maxPrice: "", sort: "newest" },
    catalog: { items: [], page: 1, pageSize: 12, totalCount: 0 },
    mine: { items: [] },
    history: { items: [], page: 1, pageSize: 15, totalCount: 0 },
    buyIdemKey: newUuid(),
  };

  async function loadCurrentSubTab() {
    if (state.subTab === "catalog") {
      const res = await apiRequest(ENDPOINTS.listings, {
        auth: getSession().isAuthenticated,
        query: {
          q: state.filters.q,
          rarity: state.filters.rarity,
          minPrice: state.filters.minPrice,
          maxPrice: state.filters.maxPrice,
          sort: state.filters.sort,
          page: state.catalog.page,
          pageSize: state.catalog.pageSize,
        },
      });
      if (res.ok && res.data) {
        const d = res.data;
        state.catalog.items = d.items || (Array.isArray(d) ? d : []);
        state.catalog.totalCount = d.totalCount ?? state.catalog.items.length;
      }
    } else if (state.subTab === "mine") {
      if (getSession().isAuthenticated) {
        const res = await apiRequest(ENDPOINTS.listingsMine);
        if (res.ok && res.data) {
          state.mine.items = res.data.items || (Array.isArray(res.data) ? res.data : []);
        }
      } else {
        state.mine.items = [];
      }
    } else if (state.subTab === "history") {
      if (getSession().isAuthenticated) {
        const res = await apiRequest(ENDPOINTS.tradesMine, {
          query: { page: state.history.page, pageSize: state.history.pageSize },
        });
        if (res.ok && res.data) {
          const d = res.data;
          state.history.items = d.items || (Array.isArray(d) ? d : []);
          state.history.totalCount = d.totalCount ?? state.history.items.length;
        }
      } else {
        state.history.items = [];
      }
    }
    render();
  }

  // MKT-05: Modal Chi tiết Listing SQL authoritative
  async function openListingDetailModal(listingId, catalogSnapshot = null) {
    const body = openModal("Chi tiết Listing SQL Authoritative (MKT-05)", `<div class="empty-placeholder"><span class="spinner"></span> Đang tải snapshot SQL...</div>`, { large: true });
    if (!body) return;

    const res = await apiRequest(ENDPOINTS.listingDetail(listingId), {
      auth: getSession().isAuthenticated,
    });

    if (!res.ok) {
      body.innerHTML = `<div class="alert alert-danger">${escapeHtml(problemMessage(res))}</div>`;
      return;
    }

    const sql = res.data || {};
    body.innerHTML = `
      <div class="grid-2">
        <div class="card mb-0">
          <div class="card-header">
            <h4 class="card-title">SQL Authoritative Snapshot</h4>
            ${statusBadge(sql.status || "Active")}
          </div>
          <div class="item-kv-list">
            <div class="item-kv"><span class="text-muted">Listing ID</span>${renderIdChip(sql.id || sql.listingId || listingId)}</div>
            <div class="item-kv"><span class="text-muted">Vật phẩm</span><strong>${escapeHtml(sql.itemName || sql.name || "—")}</strong></div>
            <div class="item-kv"><span class="text-muted">Giá niêm yết</span><span class="mono text-warning">${formatGem(sql.priceGem)} Gem</span></div>
            <div class="item-kv"><span class="text-muted">Phí 5% (feeGem)</span><span class="mono">${formatGem(sql.feeGem ?? Math.floor(Number(sql.priceGem || 0) * 0.05))} Gem</span></div>
            <div class="item-kv"><span class="text-muted">Seller ID</span>${renderIdChip(sql.sellerId)}</div>
            <div class="item-kv"><span class="text-muted">Cập nhật lúc</span><span>${escapeHtml(formatDateTime(sql.updatedAt || sql.createdAt))}</span></div>
          </div>
        </div>

        <div class="card mb-0">
          <div class="card-header">
            <h4 class="card-title">MongoDB Catalog Snapshot</h4>
            <span class="badge badge-info">Read Model</span>
          </div>
          ${
            catalogSnapshot
              ? `<pre class="json-box">${escapeHtml(JSON.stringify(catalogSnapshot, null, 2))}</pre>`
              : `<div class="text-muted">Mở từ danh sách không có MongoDB snapshot đi kèm.</div>`
          }
        </div>
      </div>
      <div class="modal-footer">
        <button type="button" class="btn btn-secondary" id="btnCloseDetailModal">Đóng</button>
      </div>
    `;
    body.querySelector("#btnCloseDetailModal")?.addEventListener("click", closeModal);
  }

  // MKT-03 & MKT-08: Mua hàng + Live Saga Tracker (poll 1.5s)
  async function buyListing(listing) {
    if (!getSession().isAuthenticated) {
      showToast("Cần đăng nhập", "Hãy đăng nhập tài khoản Buyer trước khi mua.", "warning");
      return;
    }
    const listingId = listing.id || listing.listingId;
    const keyInput = panel.querySelector("#mktBuyIdemKey");
    const idemKey = (keyInput?.value || state.buyIdemKey || newUuid()).trim();
    state.buyIdemKey = idemKey;

    const res = await apiRequest(ENDPOINTS.listingBuy(listingId), {
      method: "POST",
      idempotencyKey: idemKey,
    });

    if (!res.ok) {
      showToast("Mua vật phẩm thất bại", problemMessage(res), "danger");
      return;
    }

    const tradeId =
      res.data?.tradeId ||
      res.data?.id ||
      (res.location ? res.location.split("/").filter(Boolean).pop() : null);

    showToast("Đã gửi lệnh mua (Saga)", `Trade ID: ${tradeId || "đang khởi tạo"}`, "info");
    if (tradeId) {
      openSagaTrackerModal(tradeId, res.data);
    } else {
      await loadCurrentSubTab();
    }
  }

  function renderSagaMarkup(tradeId, trade) {
    const status = String(trade?.status || trade?.state || "Initiated");
    const lower = status.toLowerCase();
    const isComp = ["reversingitem", "releasingfunds", "failed"].includes(lower);
    const mainIdx = SAGA_MAIN_STEPS.findIndex((s) => s.key.toLowerCase() === lower);
    const compIdx = SAGA_COMP_STEPS.findIndex((s) => s.key.toLowerCase() === lower);

    return `
      <div style="display:flex;flex-wrap:wrap;justify-content:space-between;align-items:center;gap:8px;margin-bottom:10px;">
        <div>${renderIdChip(tradeId, "TradeId")}</div>
        <div>${statusBadge(status)}</div>
      </div>

      <div class="saga-track">
        ${SAGA_MAIN_STEPS.map((step, i) => {
          let cls = "";
          if (!isComp) {
            if (mainIdx > i || lower === "completed") cls = "done";
            else if (mainIdx === i) cls = "current";
          } else if (i === 0) {
            cls = "done";
          }
          return `<div class="saga-step ${cls}">${escapeHtml(step.label)}</div>`;
        }).join("")}
      </div>

      ${
        isComp
          ? `
        <div class="saga-track">
          ${SAGA_COMP_STEPS.map((step, i) => {
            let cls = "";
            if (lower === "failed" && step.key === "Failed") cls = "failed";
            else if (compIdx >= i) cls = "comp";
            return `<div class="saga-step ${cls}">${escapeHtml(step.label)}</div>`;
          }).join("")}
        </div>`
          : ""
      }

      ${
        trade?.failureCode || trade?.failureReason
          ? `<div class="alert alert-danger">
              <strong>Lỗi Saga (${escapeHtml(trade.failureCode || "FAILED")}):</strong>
              ${escapeHtml(trade.failureReason || "Giao dịch đã được bồi hoàn tự động.")}
            </div>`
          : ""
      }

      ${
        trade?.requiresReview
          ? `<div class="alert alert-warning">
              <strong>Cảnh báo requiresReview = true:</strong> Bồi hoàn cần nhân viên CS kiểm tra thủ công ở tab CS Moderation.
            </div>`
          : ""
      }

      <pre class="json-box">${escapeHtml(JSON.stringify(trade || { tradeId, status }, null, 2))}</pre>
    `;
  }

  function openSagaTrackerModal(tradeId, initialData = null) {
    let timer = null;
    const body = openModal(
      "Live Saga Tracker (MKT-03 / MKT-08)",
      `
        <div id="sagaLiveContainer">${renderSagaMarkup(tradeId, initialData)}</div>
        <div class="modal-footer">
          <span id="sagaPollIndicator" class="text-muted" style="margin-right:auto;font-size:12px;">
            <span class="spinner"></span> Đang poll trạng thái Saga mỗi 1.5s...
          </span>
          <button type="button" id="btnSagaRefreshNow" class="btn btn-sm btn-secondary">Poll ngay</button>
          <button type="button" id="btnSagaClose" class="btn btn-sm btn-primary">Đóng</button>
        </div>
      `,
      {
        large: true,
        onClose: () => {
          if (timer) {
            clearInterval(timer);
            timer = null;
          }
        },
      }
    );

    if (!body) return;

    const pollOnce = async () => {
      const res = await apiRequest(ENDPOINTS.tradeDetail(tradeId));
      if (!res.ok || !res.data) return;

      const container = body.querySelector("#sagaLiveContainer");
      if (container) container.innerHTML = renderSagaMarkup(tradeId, res.data);

      const st = String(res.data.status || res.data.state || "").toLowerCase();
      if (["completed", "failed"].includes(st)) {
        if (timer) {
          clearInterval(timer);
          timer = null;
        }
        const indicator = body.querySelector("#sagaPollIndicator");
        if (indicator) {
          indicator.textContent = st === "completed" ? "✓ Saga hoàn tất thành công." : "✕ Saga kết thúc với trạng thái Failed.";
        }
        // Làm mới chéo Wallet, Inventory và Marketplace
        appCtx.refreshAllActiveData();
      }
    };

    pollOnce();
    timer = setInterval(pollOnce, 1500);

    body.querySelector("#btnSagaRefreshNow")?.addEventListener("click", pollOnce);
    body.querySelector("#btnSagaClose")?.addEventListener("click", closeModal);
  }

  async function cancelListing(listingId) {
    const res = await apiRequest(ENDPOINTS.listingCancel(listingId), { method: "DELETE" });
    if (!res.ok) {
      showToast("Hủy tin thất bại", problemMessage(res), "danger");
      return;
    }
    showToast("Đã hủy tin đăng bán", "Vật phẩm đã được mở khóa về Kho đồ.", "success");
    await loadCurrentSubTab();
    appCtx.refreshTab("inventory");
  }

  function renderCatalogSubTab() {
    const userId = getSession().user?.id || getSession().user?.userId;
    const totalPages = Math.max(1, Math.ceil(state.catalog.totalCount / state.catalog.pageSize));

    return `
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Duyệt Chợ P2P — MongoDB Read Model (MKT-02)</h3>
            <p class="card-subtitle">Tìm kiếm nhanh trên catalog projection và mua với Idempotency-Key</p>
          </div>
          <button id="btnRefreshCatalog" type="button" class="btn btn-sm btn-secondary">↻ Tải lại chợ</button>
        </div>

        <div class="idem-bar">
          <label for="mktBuyIdemKey">Buy Idempotency-Key:</label>
          <input id="mktBuyIdemKey" type="text" value="${escapeHtml(state.buyIdemKey)}" />
          <button id="btnGenBuyIdem" type="button" class="btn btn-xs btn-secondary">Tạo key mới</button>
        </div>

        <form id="catalogFilterForm" class="form-row">
          <div class="form-group">
            <label for="mktQ">Từ khóa</label>
            <input id="mktQ" type="text" value="${escapeHtml(state.filters.q)}" placeholder="Tên vật phẩm..." />
          </div>
          <div class="form-group" style="max-width:150px;">
            <label for="mktRarity">Rarity</label>
            <select id="mktRarity">
              <option value="">Tất cả</option>
              ${["Legendary", "Epic", "Rare", "Uncommon", "Common"]
                .map((r) => `<option value="${r}" ${state.filters.rarity === r ? "selected" : ""}>${r}</option>`)
                .join("")}
            </select>
          </div>
          <div class="form-group" style="max-width:130px;">
            <label for="mktMinPrice">Giá từ (Gem)</label>
            <input id="mktMinPrice" type="number" min="0" value="${escapeHtml(state.filters.minPrice)}" />
          </div>
          <div class="form-group" style="max-width:130px;">
            <label for="mktMaxPrice">Đến (Gem)</label>
            <input id="mktMaxPrice" type="number" min="0" value="${escapeHtml(state.filters.maxPrice)}" />
          </div>
          <div class="form-group" style="max-width:160px;">
            <label for="mktSort">Sắp xếp</label>
            <select id="mktSort">
              <option value="newest" ${state.filters.sort === "newest" ? "selected" : ""}>Mới nhất</option>
              <option value="price_asc" ${state.filters.sort === "price_asc" ? "selected" : ""}>Giá tăng dần</option>
              <option value="price_desc" ${state.filters.sort === "price_desc" ? "selected" : ""}>Giá giảm dần</option>
            </select>
          </div>
          <button type="submit" class="btn btn-primary">Lọc</button>
        </form>

        ${
          state.catalog.items.length === 0
            ? `<div class="empty-placeholder">Chưa có tin đăng bán nào khớp bộ lọc.</div>`
            : `<div class="items-grid">
                ${state.catalog.items
                  .map((item, idx) => {
                    const rCls = rarityClass(item.rarity);
                    const isMine = userId && String(item.sellerId) === String(userId);
                    return `
                      <div class="item-card rarity-border-${rCls}">
                        <div>
                          <div class="item-card-top">
                            <div class="item-name">${escapeHtml(item.itemName || item.name || "Vật phẩm")}</div>
                            ${statusBadge(item.status || "Active")}
                          </div>
                          <div class="item-meta">
                            <span class="badge rarity-${rCls}">${escapeHtml(item.rarity || "Common")}</span>
                            ${item.category ? `<span class="badge badge-muted">${escapeHtml(item.category)}</span>` : ""}
                          </div>
                        </div>

                        <div class="item-price-block">
                          <span class="text-muted" style="font-size:12px;">Giá bán</span>
                          <span class="item-price-main">${formatGem(item.priceGem)} Gem</span>
                        </div>

                        <div class="item-kv-list">
                          <div class="item-kv"><span class="text-muted">Listing</span>${renderIdChip(item.id || item.listingId)}</div>
                          <div class="item-kv"><span class="text-muted">Người bán</span><span>${escapeHtml(item.sellerName || item.sellerId || "—")}</span></div>
                        </div>

                        <div class="item-actions">
                          <button type="button" class="btn btn-sm btn-secondary" data-detail-idx="${idx}">Chi tiết SQL</button>
                          <button type="button" class="btn btn-sm btn-success" data-buy-idx="${idx}" ${isMine ? "disabled title='Không thể tự mua tin của mình'" : ""}>
                            ${isMine ? "Tin của bạn" : "Mua ngay (MKT-03)"}
                          </button>
                        </div>
                      </div>
                    `;
                  })
                  .join("")}
              </div>`
        }

        <div class="pagination-bar">
          <span>Trang ${state.catalog.page} / ${totalPages} (Tổng ${state.catalog.totalCount} tin)</span>
          <div class="header-actions">
            <button id="btnCatPrev" type="button" class="btn btn-xs btn-secondary" ${state.catalog.page <= 1 ? "disabled" : ""}>← Trước</button>
            <button id="btnCatNext" type="button" class="btn btn-xs btn-secondary" ${state.catalog.page >= totalPages ? "disabled" : ""}>Sau →</button>
          </div>
        </div>
      </div>
    `;
  }

  function renderMineSubTab() {
    if (!getSession().isAuthenticated) {
      return `<div class="card"><div class="alert alert-warning mb-0">Vui lòng đăng nhập để quản lý tin đăng bán của bạn.</div></div>`;
    }
    return `
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Tin đăng bán của tôi</h3>
            <p class="card-subtitle">Theo dõi trạng thái khóa vật phẩm và hủy tin đang mở bán</p>
          </div>
          <button id="btnRefreshMine" type="button" class="btn btn-sm btn-secondary">↻ Làm mới</button>
        </div>
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Listing ID</th>
                <th>Vật phẩm</th>
                <th>Giá (Gem)</th>
                <th>Trạng thái</th>
                <th>Ngày tạo</th>
                <th>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              ${
                state.mine.items.length === 0
                  ? `<tr><td colspan="6" class="text-muted" style="text-align:center;padding:22px;">Bạn chưa có tin đăng bán nào.</td></tr>`
                  : state.mine.items
                      .map((l) => {
                        const id = l.id || l.listingId;
                        const canCancel = ["active", "open"].includes(String(l.status || "").toLowerCase());
                        return `
                          <tr>
                            <td>${renderIdChip(id)}</td>
                            <td>
                              <strong>${escapeHtml(l.itemName || l.name || "—")}</strong>
                              <span class="badge rarity-${rarityClass(l.rarity)}" style="margin-left:6px;">${escapeHtml(l.rarity || "Common")}</span>
                            </td>
                            <td class="mono">${formatGem(l.priceGem)}</td>
                            <td>${statusBadge(l.status)}</td>
                            <td>${escapeHtml(formatDateTime(l.createdAt))}</td>
                            <td>
                              <button type="button" class="btn btn-xs btn-secondary" data-sql-id="${escapeHtml(id)}">SQL</button>
                              <button type="button" class="btn btn-xs btn-danger" data-cancel-id="${escapeHtml(id)}" ${!canCancel ? "disabled" : ""}>Hủy tin</button>
                            </td>
                          </tr>
                        `;
                      })
                      .join("")
              }
            </tbody>
          </table>
        </div>
      </div>
    `;
  }

  function renderHistorySubTab() {
    if (!getSession().isAuthenticated) {
      return `<div class="card"><div class="alert alert-warning mb-0">Vui lòng đăng nhập để xem lịch sử giao dịch P2P.</div></div>`;
    }
    const totalPages = Math.max(1, Math.ceil(state.history.totalCount / state.history.pageSize));
    return `
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Lịch sử giao dịch & Saga (MKT-08)</h3>
            <p class="card-subtitle">Mở lại Live Saga Tracker cho bất kỳ Trade ID nào</p>
          </div>
          <button id="btnRefreshHistory" type="button" class="btn btn-sm btn-secondary">↻ Làm mới</button>
        </div>
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Trade ID</th>
                <th>Vật phẩm</th>
                <th>Giá (Gem)</th>
                <th>Phí 5%</th>
                <th>Trạng thái Saga</th>
                <th>Thời gian</th>
                <th>Theo dõi</th>
              </tr>
            </thead>
            <tbody>
              ${
                state.history.items.length === 0
                  ? `<tr><td colspan="7" class="text-muted" style="text-align:center;padding:22px;">Chưa có giao dịch nào.</td></tr>`
                  : state.history.items
                      .map((t) => {
                        const id = t.id || t.tradeId;
                        return `
                          <tr>
                            <td>${renderIdChip(id)}</td>
                            <td>${escapeHtml(t.itemName || "—")}</td>
                            <td class="mono">${formatGem(t.grossGem ?? t.priceGem)}</td>
                            <td class="mono text-warning">${formatGem(t.feeGem ?? 0)}</td>
                            <td>
                              ${statusBadge(t.status || t.state)}
                              ${t.requiresReview ? `<span class="badge badge-warning">Review</span>` : ""}
                            </td>
                            <td>${escapeHtml(formatDateTime(t.createdAt))}</td>
                            <td><button type="button" class="btn btn-xs btn-primary" data-track-trade="${escapeHtml(id)}">Mở Saga</button></td>
                          </tr>
                        `;
                      })
                      .join("")
              }
            </tbody>
          </table>
        </div>
        <div class="pagination-bar">
          <span>Trang ${state.history.page} / ${totalPages} (Tổng ${state.history.totalCount} giao dịch)</span>
          <div class="header-actions">
            <button id="btnHistPrev" type="button" class="btn btn-xs btn-secondary" ${state.history.page <= 1 ? "disabled" : ""}>← Trước</button>
            <button id="btnHistNext" type="button" class="btn btn-xs btn-secondary" ${state.history.page >= totalPages ? "disabled" : ""}>Sau →</button>
          </div>
        </div>
      </div>
    `;
  }

  function render() {
    panel.innerHTML = `
      <div class="sub-nav">
        <button type="button" class="sub-tab ${state.subTab === "catalog" ? "active" : ""}" data-subtab="catalog">Duyệt Chợ (Catalog)</button>
        <button type="button" class="sub-tab ${state.subTab === "mine" ? "active" : ""}" data-subtab="mine">Tin của tôi</button>
        <button type="button" class="sub-tab ${state.subTab === "history" ? "active" : ""}" data-subtab="history">Lịch sử giao dịch</button>
      </div>
      ${state.subTab === "catalog" ? renderCatalogSubTab() : ""}
      ${state.subTab === "mine" ? renderMineSubTab() : ""}
      ${state.subTab === "history" ? renderHistorySubTab() : ""}
    `;
    bindEvents();
  }

  function bindEvents() {
    panel.querySelectorAll("[data-subtab]").forEach((btn) => {
      btn.addEventListener("click", () => {
        state.subTab = btn.dataset.subtab;
        loadCurrentSubTab();
      });
    });

    panel.querySelector("#btnGenBuyIdem")?.addEventListener("click", () => {
      state.buyIdemKey = newUuid();
      const input = panel.querySelector("#mktBuyIdemKey");
      if (input) input.value = state.buyIdemKey;
    });

    panel.querySelector("#catalogFilterForm")?.addEventListener("submit", (e) => {
      e.preventDefault();
      state.filters.q = panel.querySelector("#mktQ").value.trim();
      state.filters.rarity = panel.querySelector("#mktRarity").value;
      state.filters.minPrice = panel.querySelector("#mktMinPrice").value;
      state.filters.maxPrice = panel.querySelector("#mktMaxPrice").value;
      state.filters.sort = panel.querySelector("#mktSort").value;
      state.catalog.page = 1;
      loadCurrentSubTab();
    });

    panel.querySelector("#btnRefreshCatalog")?.addEventListener("click", loadCurrentSubTab);
    panel.querySelector("#btnRefreshMine")?.addEventListener("click", loadCurrentSubTab);
    panel.querySelector("#btnRefreshHistory")?.addEventListener("click", loadCurrentSubTab);

    panel.querySelectorAll("[data-detail-idx]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const item = state.catalog.items[Number(btn.dataset.detailIdx)];
        if (item) openListingDetailModal(item.id || item.listingId, item);
      });
    });

    panel.querySelectorAll("[data-buy-idx]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const item = state.catalog.items[Number(btn.dataset.buyIdx)];
        if (item) buyListing(item);
      });
    });

    panel.querySelectorAll("[data-sql-id]").forEach((btn) => {
      btn.addEventListener("click", () => openListingDetailModal(btn.dataset.sqlId));
    });

    panel.querySelectorAll("[data-cancel-id]").forEach((btn) => {
      btn.addEventListener("click", () => cancelListing(btn.dataset.cancelId));
    });

    panel.querySelectorAll("[data-track-trade]").forEach((btn) => {
      btn.addEventListener("click", () => openSagaTrackerModal(btn.dataset.trackTrade));
    });

    panel.querySelector("#btnCatPrev")?.addEventListener("click", () => {
      if (state.catalog.page > 1) {
        state.catalog.page -= 1;
        loadCurrentSubTab();
      }
    });
    panel.querySelector("#btnCatNext")?.addEventListener("click", () => {
      state.catalog.page += 1;
      loadCurrentSubTab();
    });
    panel.querySelector("#btnHistPrev")?.addEventListener("click", () => {
      if (state.history.page > 1) {
        state.history.page -= 1;
        loadCurrentSubTab();
      }
    });
    panel.querySelector("#btnHistNext")?.addEventListener("click", () => {
      state.history.page += 1;
      loadCurrentSubTab();
    });
  }

  return {
    init: render,
    refresh: loadCurrentSubTab,
    render,
    openSagaTrackerModal,
  };
}