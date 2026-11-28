import {
  ENDPOINTS,
  apiRequest,
  closeModal,
  escapeHtml,
  formatDateTime,
  formatGem,
  getSession,
  getTemplateName,
  newUuid,
  openModal,
  problemMessage,
  rarityClass,
  renderIdChip,
  showToast,
  statusBadge,
} from "./api.js";

const SAGA_MAIN_STEPS = [
  { key: "HoldingFunds", label: "1. HoldingFunds" },
  { key: "Transferring", label: "2. Transferring" },
  { key: "Settling", label: "3. Settling" },
  { key: "Finalizing", label: "4. Finalizing" },
  { key: "Completed", label: "5. Completed" },
];

const SAGA_COMP_STEPS = [
  { key: "ReversingItem", label: "Bồi hoàn: ReversingItem" },
  { key: "ReleasingFunds", label: "Bồi hoàn: ReleasingFunds" },
  { key: "Failed", label: "Kết thúc: Failed" },
];

const LISTING_STATUSES = ["PendingLock", "Active", "Processing", "Sold", "Cancelled", "CreateFailed"];
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

function computeFeeBigInt(gross) {
  try {
    return (BigInt(String(gross ?? 0)) * 500n) / 10000n;
  } catch {
    return 0n;
  }
}

export function createMarketplaceModule(appCtx) {
  const panel = document.getElementById("panel-marketplace");

  const state = {
    subTab: "catalog", // catalog | mine | history
    filters: { q: "", category: "", rarity: "", minPrice: "", maxPrice: "", sort: "newest" },
    catalog: { items: [], page: 1, pageSize: 12, totalCount: 0 },
    mine: { items: [], status: "Active", page: 1, pageSize: 15, totalCount: 0 },
    history: { items: [], side: "all", status: "Completed", page: 1, pageSize: 15, totalCount: 0 },
    buyIdemKey: newUuid(),
  };

  async function loadCurrentSubTab() {
    if (!getSession().isAuthenticated) {
      state.catalog.items = [];
      state.mine.items = [];
      state.history.items = [];
      render();
      return;
    }

    if (state.subTab === "catalog") {
      const res = await apiRequest(ENDPOINTS.listings, {
        query: {
          q: state.filters.q,
          category: state.filters.category,
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
        state.catalog.items = Array.isArray(d.items) ? d.items : [];
        state.catalog.totalCount = d.totalCount ?? state.catalog.items.length;
      } else if (!res.ok) {
        showToast("Lỗi tải Chợ P2P (MKT-04)", problemMessage(res), "danger");
      }
    } else if (state.subTab === "mine") {
      const res = await apiRequest(ENDPOINTS.listingsMine, {
        query: {
          status: state.mine.status,
          page: state.mine.page,
          pageSize: state.mine.pageSize,
        },
      });
      if (res.ok && res.data) {
        const d = res.data;
        state.mine.items = Array.isArray(d.items) ? d.items : [];
        state.mine.totalCount = d.totalCount ?? state.mine.items.length;
      } else if (!res.ok) {
        showToast("Lỗi tải Tin của tôi (MKT-06)", problemMessage(res), "danger");
      }
    } else if (state.subTab === "history") {
      const res = await apiRequest(ENDPOINTS.tradesMine, {
        query: {
          side: state.history.side,
          status: state.history.status,
          page: state.history.page,
          pageSize: state.history.pageSize,
        },
      });
      if (res.ok && res.data) {
        const d = res.data;
        state.history.items = Array.isArray(d.items) ? d.items : [];
        state.history.totalCount = d.totalCount ?? state.history.items.length;
      } else if (!res.ok) {
        showToast("Lỗi tải Lịch sử giao dịch (MKT-07)", problemMessage(res), "danger");
      }
    }
    render();
  }

  // MKT-05: Modal Chi tiết Listing SQL authoritative
  async function openListingDetailModal(listingId, catalogSnapshot = null) {
    const body = openModal(
      "Chi tiết Listing SQL Authoritative (MKT-05)",
      `<div class="empty-placeholder"><span class="spinner"></span> Đang tải snapshot SQL...</div>`,
      { large: true }
    );
    if (!body) return;

    const res = await apiRequest(ENDPOINTS.listingDetail(listingId));

    if (!res.ok) {
      body.innerHTML = `<div class="alert alert-danger">${escapeHtml(problemMessage(res))}</div>`;
      return;
    }

    const sql = res.data || {};
    const feeEst = computeFeeBigInt(sql.grossGem);
    body.innerHTML = `
      <div class="grid-2">
        <div class="card mb-0">
          <div class="card-header">
            <h4 class="card-title">SQL Authoritative Snapshot</h4>
            ${statusBadge(sql.status)}
          </div>
          <div class="item-kv-list">
            <div class="item-kv"><span class="text-muted">listingId</span>${renderIdChip(sql.listingId || listingId)}</div>
            <div class="item-kv"><span class="text-muted">Vật phẩm</span><strong>${escapeHtml(sql.itemName)} (×${escapeHtml(sql.quantity)})</strong></div>
            <div class="item-kv"><span class="text-muted">Đơn giá (unitPrice)</span><span class="mono">${formatGem(sql.unitPrice)} Gem</span></div>
            <div class="item-kv"><span class="text-muted">Tổng giá (grossGem)</span><span class="mono text-warning">${formatGem(sql.grossGem)} Gem</span></div>
            <div class="item-kv"><span class="text-muted">Phí ước tính 5%</span><span class="mono">${formatGem(feeEst)} Gem</span></div>
            <div class="item-kv"><span class="text-muted">sellerId</span>${renderIdChip(sql.sellerId)}</div>
            <div class="item-kv"><span class="text-muted">itemId</span>${renderIdChip(sql.itemId)}</div>
            <div class="item-kv"><span class="text-muted">Version</span><span class="mono">v${escapeHtml(sql.version)}</span></div>
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
              : `<div class="text-muted">Mở từ danh sách SQL (không có MongoDB snapshot đi kèm).</div>`
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
    const listingId = listing.listingId;
    const keyInput = panel.querySelector("#mktBuyIdemKey");
    const idemKey = (keyInput?.value || state.buyIdemKey || newUuid()).trim();
    state.buyIdemKey = idemKey;

    const res = await apiRequest(ENDPOINTS.listingBuy(listingId), {
      method: "POST",
      idempotencyKey: idemKey,
    });

    if (!res.ok) {
      showToast("Mua vật phẩm thất bại (MKT-03)", problemMessage(res), "danger");
      return;
    }

    const tradeId =
      res.data?.tradeId ||
      (res.location ? res.location.split("/").filter(Boolean).pop() : null);

    showToast(
      "Đã tiếp nhận lệnh mua (202 Accepted)",
      `TradeId: ${tradeId || "N/A"} • Gross: ${formatGem(res.data?.grossGem)} Gem`,
      "info"
    );
    if (tradeId) {
      openSagaTrackerModal(tradeId, res.data);
    } else {
      await loadCurrentSubTab();
    }
  }

  function renderSagaMarkup(tradeId, trade) {
    const status = String(trade?.status || "HoldingFunds");
    const isComp = ["ReversingItem", "ReleasingFunds", "Failed"].includes(status);
    const mainIdx = SAGA_MAIN_STEPS.findIndex((s) => s.key === status);
    const compIdx = SAGA_COMP_STEPS.findIndex((s) => s.key === status);

    return `
      <div style="display:flex;flex-wrap:wrap;justify-content:space-between;align-items:center;gap:8px;margin-bottom:10px;">
        <div>${renderIdChip(tradeId, "TradeId")}</div>
        <div>${statusBadge(status)}</div>
      </div>

      <div class="saga-track">
        ${SAGA_MAIN_STEPS.map((step, i) => {
          let cls = "";
          if (!isComp) {
            if (mainIdx > i || status === "Completed") cls = "done";
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
            if (status === "Failed" && step.key === "Failed") cls = "failed";
            else if (compIdx >= i) cls = "comp";
            return `<div class="saga-step ${cls}">${escapeHtml(step.label)}</div>`;
          }).join("")}
        </div>`
          : ""
      }

      ${
        trade?.grossGem !== undefined
          ? `
        <div class="kv-grid">
          <div class="kv-cell"><div class="kv-cell-label">grossGem</div><strong class="mono text-warning">${formatGem(trade.grossGem)} Gem</strong></div>
          <div class="kv-cell"><div class="kv-cell-label">feeGem (5% TaxBurn)</div><strong class="mono">${formatGem(trade.feeGem)} Gem</strong></div>
          <div class="kv-cell"><div class="kv-cell-label">sellerNetGem</div><strong class="mono text-success">${formatGem(trade.sellerNetGem)} Gem</strong></div>
        </div>`
          : ""
      }

      ${
        trade?.failureCode
          ? `<div class="alert alert-danger">
              <strong>Lỗi Saga (failureCode):</strong> <code>${escapeHtml(trade.failureCode)}</code> — Giao dịch đã kích hoạt nhánh bồi hoàn.
            </div>`
          : ""
      }

      ${
        trade?.requiresReview
          ? `<div class="alert alert-warning">
              <strong>Cảnh báo requiresReview = true:</strong> Trạng thái chưa xác định hoàn toàn — cần CS kiểm tra bằng <code>ADM-05</code> ở tab CS.
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

      const st = String(res.data.status || "");
      if (st === "Completed" || st === "Failed") {
        if (timer) {
          clearInterval(timer);
          timer = null;
        }
        const indicator = body.querySelector("#sagaPollIndicator");
        if (indicator) {
          indicator.textContent = st === "Completed" ? "✓ Saga hoàn tất thành công." : "✕ Saga kết thúc với trạng thái Failed.";
        }
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
      showToast("Hủy tin thất bại (MKT-02)", problemMessage(res), "danger");
      return;
    }
    const unlockStatus = res.data?.inventoryUnlockStatus || "Pending";
    showToast("Đã hủy tin đăng bán (MKT-02)", `inventoryUnlockStatus: ${unlockStatus} (mở khóa eventual qua RabbitMQ).`, "success");
    await loadCurrentSubTab();
    appCtx.refreshTab("inventory");
  }

  function renderCatalogSubTab() {
    if (!getSession().isAuthenticated) {
      return `<div class="card"><div class="alert alert-warning mb-0">Vui lòng đăng nhập tài khoản <strong>Player</strong> để duyệt Chợ P2P (MKT-04 yêu cầu JWT role Player).</div></div>`;
    }
    const userId = getSession().user?.userId;
    const totalPages = Math.max(1, Math.ceil(state.catalog.totalCount / state.catalog.pageSize));

    return `
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Duyệt Chợ P2P — MongoDB Read Model (MKT-04)</h3>
            <p class="card-subtitle">Chỉ hiển thị tin <code>Active</code> • Mua trọn stack qua <code>MKT-03</code> kèm <code>Idempotency-Key</code></p>
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
            <label for="mktQ">Từ khóa (q)</label>
            <input id="mktQ" type="text" value="${escapeHtml(state.filters.q)}" placeholder="Tên vật phẩm..." />
          </div>
          <div class="form-group" style="max-width:140px;">
            <label for="mktCategory">Category</label>
            <input id="mktCategory" type="text" value="${escapeHtml(state.filters.category)}" placeholder="Weapon..." />
          </div>
          <div class="form-group" style="max-width:140px;">
            <label for="mktRarity">Rarity</label>
            <select id="mktRarity">
              <option value="">Tất cả</option>
              ${["Legendary", "Epic", "Rare", "Uncommon", "Common"]
                .map((r) => `<option value="${r}" ${state.filters.rarity === r ? "selected" : ""}>${r}</option>`)
                .join("")}
            </select>
          </div>
          <div class="form-group" style="max-width:120px;">
            <label for="mktMinPrice">Min Price</label>
            <input id="mktMinPrice" type="number" min="0" value="${escapeHtml(state.filters.minPrice)}" />
          </div>
          <div class="form-group" style="max-width:120px;">
            <label for="mktMaxPrice">Max Price</label>
            <input id="mktMaxPrice" type="number" min="0" value="${escapeHtml(state.filters.maxPrice)}" />
          </div>
          <div class="form-group" style="max-width:155px;">
            <label for="mktSort">Sắp xếp (sort)</label>
            <select id="mktSort">
              <option value="newest" ${state.filters.sort === "newest" ? "selected" : ""}>Mới nhất (newest)</option>
              <option value="priceAsc" ${state.filters.sort === "priceAsc" ? "selected" : ""}>Giá tăng (priceAsc)</option>
              <option value="priceDesc" ${state.filters.sort === "priceDesc" ? "selected" : ""}>Giá giảm (priceDesc)</option>
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
                    const isMine = userId && String(item.sellerId).toLowerCase() === String(userId).toLowerCase();
                    return `
                      <div class="item-card rarity-border-${rCls}">
                        <div>
                          <div class="item-card-top">
                            <div class="item-name">${escapeHtml(item.itemName)} <span class="text-accent mono">×${escapeHtml(item.quantity)}</span></div>
                            ${statusBadge(item.status)}
                          </div>
                          <div class="item-meta">
                            <span class="badge rarity-${rCls}">${escapeHtml(item.rarity)}</span>
                            <span class="badge badge-muted">${escapeHtml(item.category)}</span>
                          </div>
                        </div>

                        <div class="item-price-block">
                          <div>
                            <div class="text-muted" style="font-size:11px;">Tổng giá (grossGem)</div>
                            <div class="text-muted mono" style="font-size:11px;">Đơn giá: ${formatGem(item.unitPrice)} / cái</div>
                          </div>
                          <span class="item-price-main">${formatGem(item.grossGem)} Gem</span>
                        </div>

                        <div class="item-kv-list">
                          <div class="item-kv"><span class="text-muted">listingId</span>${renderIdChip(item.listingId)}</div>
                          <div class="item-kv"><span class="text-muted">sellerId</span>${renderIdChip(item.sellerId)}</div>
                        </div>

                        <div class="item-actions">
                          <button type="button" class="btn btn-sm btn-secondary" data-detail-idx="${idx}">Chi tiết SQL</button>
                          <button type="button" class="btn btn-sm btn-success" data-buy-idx="${idx}" ${isMine ? "disabled title='Không thể tự mua tin của mình (409 SELF_PURCHASE)'" : ""}>
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
    const totalPages = Math.max(1, Math.ceil(state.mine.totalCount / state.mine.pageSize));
    return `
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Tin đăng bán của tôi (MKT-06)</h3>
            <p class="card-subtitle">Đọc SQL authoritative theo seller • Lọc theo <code>status</code> (mặc định <code>Active</code>)</p>
          </div>
          <button id="btnRefreshMine" type="button" class="btn btn-sm btn-secondary">↻ Làm mới</button>
        </div>

        <form id="mineFilterForm" class="form-row">
          <div class="form-group" style="max-width:220px;">
            <label for="mineStatusSelect">Trạng thái (status)</label>
            <select id="mineStatusSelect">
              ${LISTING_STATUSES.map((s) => `<option value="${s}" ${state.mine.status === s ? "selected" : ""}>${s}</option>`).join("")}
            </select>
          </div>
          <button type="submit" class="btn btn-primary">Lọc tin</button>
        </form>

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Listing ID</th>
                <th>Vật phẩm</th>
                <th>SL</th>
                <th>unitPrice</th>
                <th>grossGem</th>
                <th>Trạng thái</th>
                <th>Ngày tạo</th>
                <th>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              ${
                state.mine.items.length === 0
                  ? `<tr><td colspan="8" class="text-muted" style="text-align:center;padding:22px;">Không có tin đăng bán nào ở trạng thái ${escapeHtml(state.mine.status)}.</td></tr>`
                  : state.mine.items
                      .map((l) => {
                        const canCancel = l.status === "Active";
                        return `
                          <tr>
                            <td>${renderIdChip(l.listingId)}</td>
                            <td>
                              <strong>${escapeHtml(l.itemName)}</strong>
                              <span class="badge rarity-${rarityClass(l.rarity)}" style="margin-left:6px;">${escapeHtml(l.rarity)}</span>
                            </td>
                            <td class="mono">${escapeHtml(l.quantity)}</td>
                            <td class="mono">${formatGem(l.unitPrice)}</td>
                            <td class="mono text-warning" style="font-weight:700;">${formatGem(l.grossGem)}</td>
                            <td>${statusBadge(l.status)}</td>
                            <td>${escapeHtml(formatDateTime(l.createdAt))}</td>
                            <td>
                              <button type="button" class="btn btn-xs btn-secondary" data-sql-id="${escapeHtml(l.listingId)}">SQL</button>
                              <button type="button" class="btn btn-xs btn-danger" data-cancel-id="${escapeHtml(l.listingId)}" ${!canCancel ? "disabled" : ""}>Hủy tin</button>
                            </td>
                          </tr>
                        `;
                      })
                      .join("")
              }
            </tbody>
          </table>
        </div>

        <div class="pagination-bar">
          <span>Trang ${state.mine.page} / ${totalPages} (Tổng ${state.mine.totalCount} tin)</span>
          <div class="header-actions">
            <button id="btnMinePrev" type="button" class="btn btn-xs btn-secondary" ${state.mine.page <= 1 ? "disabled" : ""}>← Trước</button>
            <button id="btnMineNext" type="button" class="btn btn-xs btn-secondary" ${state.mine.page >= totalPages ? "disabled" : ""}>Sau →</button>
          </div>
        </div>
      </div>
    `;
  }

  function renderHistorySubTab() {
    if (!getSession().isAuthenticated) {
      return `<div class="card"><div class="alert alert-warning mb-0">Vui lòng đăng nhập để xem lịch sử giao dịch P2P.</div></div>`;
    }
    const userId = String(getSession().user?.userId || "").toLowerCase();
    const totalPages = Math.max(1, Math.ceil(state.history.totalCount / state.history.pageSize));
    return `
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Lịch sử giao dịch & Saga (MKT-07 / MKT-08)</h3>
            <p class="card-subtitle">Lọc theo <code>side</code> và <code>status</code> • Mở lại Live Saga Tracker cho bất kỳ Trade ID nào</p>
          </div>
          <button id="btnRefreshHistory" type="button" class="btn btn-sm btn-secondary">↻ Làm mới</button>
        </div>

        <form id="historyFilterForm" class="form-row">
          <div class="form-group" style="max-width:170px;">
            <label for="histSideSelect">Vai trò (side)</label>
            <select id="histSideSelect">
              <option value="all" ${state.history.side === "all" ? "selected" : ""}>Tất cả (all)</option>
              <option value="buy" ${state.history.side === "buy" ? "selected" : ""}>Mua (buy)</option>
              <option value="sell" ${state.history.side === "sell" ? "selected" : ""}>Bán (sell)</option>
            </select>
          </div>
          <div class="form-group" style="max-width:210px;">
            <label for="histStatusSelect">Trạng thái (status)</label>
            <select id="histStatusSelect">
              ${TRADE_STATUSES.map((s) => `<option value="${s}" ${state.history.status === s ? "selected" : ""}>${s}</option>`).join("")}
            </select>
          </div>
          <button type="submit" class="btn btn-primary">Lọc lịch sử</button>
        </form>

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Trade ID</th>
                <th>Chiều</th>
                <th>Template / SL</th>
                <th>grossGem</th>
                <th>feeGem</th>
                <th>sellerNetGem</th>
                <th>Trạng thái Saga</th>
                <th>Thời gian</th>
                <th>Theo dõi</th>
              </tr>
            </thead>
            <tbody>
              ${
                state.history.items.length === 0
                  ? `<tr><td colspan="9" class="text-muted" style="text-align:center;padding:22px;">Không có giao dịch nào khớp bộ lọc.</td></tr>`
                  : state.history.items
                      .map((t) => {
                        const isBuyer = userId && String(t.buyerId || "").toLowerCase() === userId;
                        const tplName = getTemplateName(t.templateId);
                        return `
                          <tr>
                            <td>${renderIdChip(t.tradeId)}</td>
                            <td>${isBuyer ? `<span class="badge badge-info">Buy</span>` : `<span class="badge badge-success">Sell</span>`}</td>
                            <td>
                              ${tplName ? `<strong>${escapeHtml(tplName)}</strong> ` : renderIdChip(t.templateId)}
                              <span class="mono text-muted">×${escapeHtml(t.quantity)}</span>
                            </td>
                            <td class="mono">${formatGem(t.grossGem)}</td>
                            <td class="mono text-warning">${formatGem(t.feeGem)}</td>
                            <td class="mono text-success">${formatGem(t.sellerNetGem)}</td>
                            <td>
                              ${statusBadge(t.status)}
                              ${t.failureCode ? `<div class="mono text-danger" style="font-size:11px;margin-top:2px;">${escapeHtml(t.failureCode)}</div>` : ""}
                              ${t.requiresReview ? `<span class="badge badge-warning" style="margin-top:2px;">requiresReview</span>` : ""}
                            </td>
                            <td>${escapeHtml(formatDateTime(t.createdAt))}</td>
                            <td><button type="button" class="btn btn-xs btn-primary" data-track-trade="${escapeHtml(t.tradeId)}">Mở Saga</button></td>
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
        <button type="button" class="sub-tab ${state.subTab === "catalog" ? "active" : ""}" data-subtab="catalog">Duyệt Chợ (MKT-04)</button>
        <button type="button" class="sub-tab ${state.subTab === "mine" ? "active" : ""}" data-subtab="mine">Tin của tôi (MKT-06)</button>
        <button type="button" class="sub-tab ${state.subTab === "history" ? "active" : ""}" data-subtab="history">Lịch sử giao dịch (MKT-07)</button>
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
      state.filters.category = panel.querySelector("#mktCategory").value.trim();
      state.filters.rarity = panel.querySelector("#mktRarity").value;
      state.filters.minPrice = panel.querySelector("#mktMinPrice").value;
      state.filters.maxPrice = panel.querySelector("#mktMaxPrice").value;
      state.filters.sort = panel.querySelector("#mktSort").value;
      state.catalog.page = 1;
      loadCurrentSubTab();
    });

    panel.querySelector("#mineFilterForm")?.addEventListener("submit", (e) => {
      e.preventDefault();
      state.mine.status = panel.querySelector("#mineStatusSelect").value;
      state.mine.page = 1;
      loadCurrentSubTab();
    });

    panel.querySelector("#historyFilterForm")?.addEventListener("submit", (e) => {
      e.preventDefault();
      state.history.side = panel.querySelector("#histSideSelect").value;
      state.history.status = panel.querySelector("#histStatusSelect").value;
      state.history.page = 1;
      loadCurrentSubTab();
    });

    panel.querySelector("#btnRefreshCatalog")?.addEventListener("click", loadCurrentSubTab);
    panel.querySelector("#btnRefreshMine")?.addEventListener("click", loadCurrentSubTab);
    panel.querySelector("#btnRefreshHistory")?.addEventListener("click", loadCurrentSubTab);

    panel.querySelectorAll("[data-detail-idx]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const item = state.catalog.items[Number(btn.dataset.detailIdx)];
        if (item) openListingDetailModal(item.listingId, item);
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
    panel.querySelector("#btnMinePrev")?.addEventListener("click", () => {
      if (state.mine.page > 1) {
        state.mine.page -= 1;
        loadCurrentSubTab();
      }
    });
    panel.querySelector("#btnMineNext")?.addEventListener("click", () => {
      state.mine.page += 1;
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