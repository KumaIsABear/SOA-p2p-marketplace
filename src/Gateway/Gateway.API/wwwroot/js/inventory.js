import {
  ENDPOINTS,
  apiRequest,
  closeModal,
  escapeHtml,
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

function computeListingFeeBigInt(unitPriceStr, quantityNum) {
  try {
    const unit = BigInt(String(unitPriceStr || "0").replace(/[^\d]/g, "") || "0");
    const qty = BigInt(Math.max(1, Number(quantityNum || 1)));
    const gross = unit * qty;
    const fee = (gross * 500n) / 10000n; // feeBps = 500 (5%)
    const net = gross - fee;
    return { gross, fee, net };
  } catch {
    return { gross: 0n, fee: 0n, net: 0n };
  }
}

export function createInventoryModule(appCtx) {
  const panel = document.getElementById("panel-inventory");

  const state = {
    items: [],
    usedSlots: 0,
    capacity: 20,
    search: "",
    rarityFilter: "",
  };

  async function loadInventory() {
    if (!getSession().isAuthenticated) {
      state.items = [];
      render();
      return;
    }
    const res = await apiRequest(ENDPOINTS.inventoryMe);
    if (!res.ok) {
      showToast("Không tải được Kho đồ (INV-01)", problemMessage(res), "danger");
      render();
      return;
    }
    const d = res.data || {};
    // InventoryViewDto: { userId, capacity, usedSlots, items } — usedSlots đã bao gồm ReservedSlots
    state.items = Array.isArray(d.items) ? d.items : [];
    state.usedSlots = Number(d.usedSlots ?? state.items.length);
    state.capacity = Number(d.capacity ?? 20);
    render();
  }

  function openCreateListingModal(item) {
    const itemId = item.itemId;
    const quantity = Number(item.quantity ?? 1);
    const defaultUnitPrice = 1000;
    let currentKey = newUuid();
    let pendingPollTimer = null;

    const modalBody = openModal(
      `Đăng bán vật phẩm (MKT-01) — ${item.name}`,
      `
      <div class="alert alert-info">
        <strong>Vật phẩm:</strong> ${escapeHtml(item.name)} (x${escapeHtml(quantity)})
        <span class="badge rarity-${rarityClass(item.rarity)}" style="margin-left:6px;">${escapeHtml(item.rarity)}</span>
        <span class="badge badge-muted" style="margin-left:4px;">${escapeHtml(item.category)}</span>
        <div style="margin-top:6px;display:flex;flex-wrap:wrap;gap:6px;">
          ${renderIdChip(itemId, "itemId")}
          ${renderIdChip(item.templateId, "templateId")}
        </div>
      </div>

      <form id="createListingForm">
        <div class="idem-bar">
          <label for="listIdemKey">Idempotency-Key:</label>
          <input id="listIdemKey" type="text" value="${escapeHtml(currentKey)}" />
          <button id="btnNewListIdem" type="button" class="btn btn-xs btn-secondary">Tạo mới</button>
        </div>

        <div class="form-row">
          <div class="form-group" style="max-width:150px;">
            <label for="listQuantity">Số lượng (quantity)</label>
            <input id="listQuantity" type="number" min="1" step="1" required value="${escapeHtml(quantity)}" readonly title="Quy tắc MVP: đăng bán nguyên stack" />
          </div>
          <div class="form-group">
            <label for="listUnitPrice">Đơn giá — unitPrice (Gem / 1 item)</label>
            <input id="listUnitPrice" type="number" min="1" step="1" required value="${escapeHtml(defaultUnitPrice)}" />
          </div>
        </div>

        <div class="fee-preview">
          <div class="fee-preview-row">
            <span class="text-muted">Tổng giá trị (grossGem = unitPrice × quantity):</span>
            <span id="previewGross" class="mono">0 Gem</span>
          </div>
          <div class="fee-preview-row">
            <span class="text-muted">Phí sàn 5% (feeGem — TaxBurn):</span>
            <span id="previewFee" class="mono text-warning">-0 Gem</span>
          </div>
          <div class="fee-preview-row total">
            <span>Người bán thực nhận (sellerNetGem):</span>
            <span id="previewNet" class="mono">0 Gem</span>
          </div>
        </div>

        <div id="pendingLockStatus" class="hidden"></div>

        <div class="modal-footer">
          <button type="button" id="btnCancelModal" class="btn btn-secondary">Đóng</button>
          <button type="submit" id="btnSubmitListing" class="btn btn-primary">Đăng bán lên Chợ</button>
        </div>
      </form>
      `,
      {
        onClose: () => {
          if (pendingPollTimer) {
            clearInterval(pendingPollTimer);
            pendingPollTimer = null;
          }
        },
      }
    );

    if (!modalBody) return;

    const priceInput = modalBody.querySelector("#listUnitPrice");
    const qtyInput = modalBody.querySelector("#listQuantity");
    const updateFeePreview = () => {
      const { gross, fee, net } = computeListingFeeBigInt(priceInput.value, qtyInput.value);
      modalBody.querySelector("#previewGross").textContent = `${formatGem(gross)} Gem`;
      modalBody.querySelector("#previewFee").textContent = `-${formatGem(fee)} Gem`;
      modalBody.querySelector("#previewNet").textContent = `${formatGem(net)} Gem`;
    };
    updateFeePreview();
    priceInput.addEventListener("input", updateFeePreview);

    modalBody.querySelector("#btnNewListIdem")?.addEventListener("click", () => {
      currentKey = newUuid();
      modalBody.querySelector("#listIdemKey").value = currentKey;
    });

    modalBody.querySelector("#btnCancelModal")?.addEventListener("click", closeModal);

    modalBody.querySelector("#createListingForm")?.addEventListener("submit", async (e) => {
      e.preventDefault();
      const submitBtn = modalBody.querySelector("#btnSubmitListing");
      const statusBox = modalBody.querySelector("#pendingLockStatus");
      const idemKey = modalBody.querySelector("#listIdemKey").value.trim() || newUuid();
      const unitPrice = Number(priceInput.value);
      const qty = Number(qtyInput.value);

      if (!Number.isInteger(unitPrice) || unitPrice <= 0) {
        showToast("Đơn giá không hợp lệ", "Vui lòng nhập số nguyên Gem lớn hơn 0.", "warning");
        return;
      }

      submitBtn.disabled = true;
      // MKT-01: CreateListingRequest = { itemId, quantity, unitPrice }
      const res = await apiRequest(ENDPOINTS.listings, {
        method: "POST",
        idempotencyKey: idemKey,
        body: { itemId, quantity: qty, unitPrice },
      });

      if (!res.ok) {
        submitBtn.disabled = false;
        showToast("Đăng bán thất bại (MKT-01)", problemMessage(res), "danger");
        return;
      }

      const listingId = res.data?.listingId;
      const listingStatus = String(res.data?.status || "");

      if (res.status === 202 || listingStatus === "PendingLock") {
        statusBox.className = "alert alert-warning";
        statusBox.innerHTML = `<span class="spinner"></span> Nhận <strong>202 PendingLock</strong> — đang chờ Inventory khóa vật phẩm (${escapeHtml(listingId || "")})...`;

        if (listingId) {
          let attempts = 0;
          pendingPollTimer = setInterval(async () => {
            attempts += 1;
            const check = await apiRequest(ENDPOINTS.listingDetail(listingId));
            const st = String(check.data?.status || "");
            if (check.ok && st === "Active") {
              clearInterval(pendingPollTimer);
              pendingPollTimer = null;
              showToast("Đăng bán thành công", `Tin ${listingId} đã Active.`, "success");
              closeModal();
              await loadInventory();
              appCtx.refreshTab("marketplace");
            } else if ((check.ok && ["CreateFailed", "Cancelled"].includes(st)) || attempts >= 20) {
              clearInterval(pendingPollTimer);
              pendingPollTimer = null;
              submitBtn.disabled = false;
              statusBox.className = "alert alert-danger";
              statusBox.textContent = `Khóa vật phẩm không hoàn tất (Trạng thái: ${check.data?.status || "Timeout"}).`;
            }
          }, 1500);
        }
        return;
      }

      showToast("Đăng bán thành công (201 Active)", `Đơn giá ${formatGem(unitPrice)} Gem (gross ${formatGem(res.data?.grossGem ?? unitPrice * qty)} Gem).`, "success");
      closeModal();
      await loadInventory();
      appCtx.refreshTab("marketplace");
    });
  }

  function render() {
    if (!getSession().isAuthenticated) {
      panel.innerHTML = `<div class="card"><div class="alert alert-warning mb-0">Vui lòng đăng nhập để xem Kho đồ và đăng bán vật phẩm.</div></div>`;
      return;
    }

    // usedSlots từ INV-01 ĐÃ bao gồm ReservedSlots — tuyệt đối không cộng đôi
    const reservedEstimate = Math.max(0, state.usedSlots - state.items.length);
    const pct = Math.min(100, Math.round((state.usedSlots / Math.max(1, state.capacity)) * 100));
    const isFull = state.usedSlots >= state.capacity;

    const filtered = state.items.filter((it) => {
      const name = String(it.name || "").toLowerCase();
      const matchQ = !state.search || name.includes(state.search.toLowerCase());
      const matchRarity = !state.rarityFilter || String(it.rarity || "").toLowerCase() === state.rarityFilter;
      return matchQ && matchRarity;
    });

    panel.innerHTML = `
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Kho đồ cá nhân (INV-01 & MKT-01)</h3>
            <p class="card-subtitle"><code>usedSlots</code> từ API đã bao gồm <code>ReservedSlots</code> dự trữ khi chờ reverse/finalize</p>
          </div>
          <button id="btnRefreshInventory" type="button" class="btn btn-sm btn-secondary">↻ Làm mới kho</button>
        </div>

        <div class="capacity-bar-wrap">
          <div class="capacity-header">
            <span>
              Sức chứa: <strong>${state.usedSlots} / ${state.capacity}</strong> ô
              <span class="text-muted">(Số item hiện tại: ${state.items.length}${reservedEstimate > 0 ? ` • ReservedSlots: ${reservedEstimate}` : ""})</span>
            </span>
            <span class="${isFull ? "text-danger" : "text-accent"}" style="font-weight:700;">${pct}%</span>
          </div>
          <div class="capacity-track">
            <div class="capacity-fill ${isFull ? "full" : ""}" style="width:${pct}%"></div>
          </div>
        </div>

        <div class="form-row">
          <div class="form-group">
            <label for="invSearchInput">Tìm theo tên vật phẩm</label>
            <input id="invSearchInput" type="text" value="${escapeHtml(state.search)}" placeholder="VD: Dragon Fang Sword..." />
          </div>
          <div class="form-group" style="max-width:200px;">
            <label for="invRaritySelect">Độ hiếm (Rarity)</label>
            <select id="invRaritySelect">
              <option value="">Tất cả</option>
              <option value="legendary" ${state.rarityFilter === "legendary" ? "selected" : ""}>Legendary</option>
              <option value="epic" ${state.rarityFilter === "epic" ? "selected" : ""}>Epic</option>
              <option value="rare" ${state.rarityFilter === "rare" ? "selected" : ""}>Rare</option>
              <option value="uncommon" ${state.rarityFilter === "uncommon" ? "selected" : ""}>Uncommon</option>
              <option value="common" ${state.rarityFilter === "common" ? "selected" : ""}>Common</option>
            </select>
          </div>
        </div>

        ${
          filtered.length === 0
            ? `<div class="empty-placeholder">Không có vật phẩm nào phù hợp trong kho đồ.</div>`
            : `<div class="items-grid">
                ${filtered
                  .map((item, idx) => {
                    const rCls = rarityClass(item.rarity);
                    const canSell = Boolean(item.tradable) && item.status === "Available";
                    const disabledReason = !item.tradable
                      ? "Không thể giao dịch"
                      : item.status === "Locked"
                        ? "Đang khóa (Locked)"
                        : "Đang chờ tất toán (PendingSettlement)";
                    return `
                      <div class="item-card rarity-border-${rCls}">
                        <div>
                          <div class="item-card-top">
                            <div class="item-name">${escapeHtml(item.name)} <span class="text-accent mono">×${escapeHtml(item.quantity)}</span></div>
                            ${statusBadge(item.status)}
                          </div>
                          <div class="item-meta">
                            <span class="badge rarity-${rCls}">${escapeHtml(item.rarity)}</span>
                            <span class="badge badge-muted">${escapeHtml(item.category)}</span>
                            ${item.tradable ? `<span class="badge badge-success">Tradable</span>` : `<span class="badge badge-danger">Non-Tradable</span>`}
                          </div>
                        </div>

                        <div class="item-kv-list">
                          <div class="item-kv">
                            <span class="text-muted">itemId</span>
                            ${renderIdChip(item.itemId)}
                          </div>
                          <div class="item-kv">
                            <span class="text-muted">templateId</span>
                            ${renderIdChip(item.templateId)}
                          </div>
                          ${item.listingId ? `<div class="item-kv"><span class="text-muted">listingId</span>${renderIdChip(item.listingId)}</div>` : ""}
                          ${item.tradeId ? `<div class="item-kv"><span class="text-muted">tradeId</span>${renderIdChip(item.tradeId)}</div>` : ""}
                        </div>

                        <div class="item-actions">
                          <button type="button" class="btn btn-sm btn-primary" data-sell-idx="${idx}" ${!canSell ? "disabled" : ""}>
                            ${canSell ? "Đăng bán (MKT-01)" : disabledReason}
                          </button>
                        </div>
                      </div>
                    `;
                  })
                  .join("")}
              </div>`
        }
      </div>
    `;

    bindEvents(filtered);
  }

  function bindEvents(filteredItems) {
    panel.querySelector("#btnRefreshInventory")?.addEventListener("click", loadInventory);
    panel.querySelector("#invSearchInput")?.addEventListener("input", (e) => {
      state.search = e.target.value;
      render();
    });
    panel.querySelector("#invRaritySelect")?.addEventListener("change", (e) => {
      state.rarityFilter = e.target.value;
      render();
    });
    panel.querySelectorAll("[data-sell-idx]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const item = filteredItems[Number(btn.dataset.sellIdx)];
        if (item && item.tradable && item.status === "Available") {
          openCreateListingModal(item);
        }
      });
    });
  }

  return {
    init: render,
    refresh: loadInventory,
    render,
  };
}