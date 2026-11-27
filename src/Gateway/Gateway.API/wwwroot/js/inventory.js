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

function computeListingFeeBigInt(grossStr) {
  try {
    const gross = BigInt(String(grossStr || "0").replace(/[^\d]/g, "") || "0");
    const fee = (gross * 5n) / 100n;
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
    reservedSlots: 0,
    maxSlots: 20,
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
      showToast("Không tải được Kho đồ", problemMessage(res), "danger");
      render();
      return;
    }
    const d = res.data || {};
    state.items = d.items || (Array.isArray(d) ? d : []);
    state.usedSlots = d.usedSlots ?? state.items.length;
    state.reservedSlots = d.reservedSlots ?? 0;
    state.maxSlots = d.maxSlots ?? d.capacity ?? 20;
    render();
  }

  function openCreateListingModal(item) {
    const itemId = item.id || item.itemInstanceId;
    const defaultPrice = item.suggestedPriceGem || item.floorPriceGem || 1000;
    let currentKey = newUuid();
    let pendingPollTimer = null;

    const modalBody = openModal(
      `Đăng bán vật phẩm (MKT-01) — ${item.name || item.itemName || "Item"}`,
      `
      <div class="alert alert-info">
        <strong>Vật phẩm:</strong> ${escapeHtml(item.name || item.itemName)}
        <span class="badge rarity-${rarityClass(item.rarity)}" style="margin-left:6px;">${escapeHtml(item.rarity || "Common")}</span>
        <div style="margin-top:6px;">${renderIdChip(itemId, "ItemId")}</div>
      </div>

      <form id="createListingForm">
        <div class="idem-bar">
          <label for="listIdemKey">Idempotency-Key:</label>
          <input id="listIdemKey" type="text" value="${escapeHtml(currentKey)}" />
          <button id="btnNewListIdem" type="button" class="btn btn-xs btn-secondary">Tạo mới</button>
        </div>

        <div class="form-row">
          <div class="form-group">
            <label for="listPriceGem">Giá niêm yết (Gem - số nguyên)</label>
            <input id="listPriceGem" type="number" min="1" step="1" required value="${escapeHtml(defaultPrice)}" />
          </div>
        </div>

        <div class="fee-preview">
          <div class="fee-preview-row">
            <span class="text-muted">Giá niêm yết (grossGem):</span>
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

    const priceInput = modalBody.querySelector("#listPriceGem");
    const updateFeePreview = () => {
      const { gross, fee, net } = computeListingFeeBigInt(priceInput.value);
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
      const priceGem = Number(priceInput.value);

      if (!Number.isInteger(priceGem) || priceGem <= 0) {
        showToast("Giá không hợp lệ", "Vui lòng nhập số nguyên Gem lớn hơn 0.", "warning");
        return;
      }

      submitBtn.disabled = true;
      const res = await apiRequest(ENDPOINTS.listings, {
        method: "POST",
        idempotencyKey: idemKey,
        body: { itemInstanceId: itemId, priceGem },
      });

      if (!res.ok) {
        submitBtn.disabled = false;
        showToast("Đăng bán thất bại", problemMessage(res), "danger");
        return;
      }

      const listingId = res.data?.listingId || res.data?.id;
      const listingStatus = String(res.data?.status || "");

      // Xử lý HTTP 202 PendingLock -> poll tới khi Active hoặc Failed
      if (res.status === 202 || listingStatus.toLowerCase() === "pendinglock") {
        statusBox.className = "alert alert-warning";
        statusBox.innerHTML = `<span class="spinner"></span> Nhận <strong>202 PendingLock</strong> — đang chờ InventoryService khóa vật phẩm (${escapeHtml(listingId || "")})...`;

        if (listingId) {
          let attempts = 0;
          pendingPollTimer = setInterval(async () => {
            attempts += 1;
            const check = await apiRequest(ENDPOINTS.listingDetail(listingId));
            const st = String(check.data?.status || "").toLowerCase();
            if (check.ok && ["active", "open"].includes(st)) {
              clearInterval(pendingPollTimer);
              pendingPollTimer = null;
              showToast("Đăng bán thành công", `Tin ${listingId} đã mở bán (Active).`, "success");
              closeModal();
              await loadInventory();
              appCtx.refreshTab("marketplace");
            } else if ((check.ok && ["failed", "rejected", "cancelled"].includes(st)) || attempts >= 20) {
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

      showToast("Đăng bán thành công", `Đã niêm yết với giá ${formatGem(priceGem)} Gem.`, "success");
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

    const totalOccupied = state.usedSlots + state.reservedSlots;
    const pct = Math.min(100, Math.round((totalOccupied / Math.max(1, state.maxSlots)) * 100));
    const isFull = totalOccupied >= state.maxSlots;

    const filtered = state.items.filter((it) => {
      const name = String(it.name || it.itemName || "").toLowerCase();
      const matchQ = !state.search || name.includes(state.search.toLowerCase());
      const matchRarity = !state.rarityFilter || String(it.rarity || "").toLowerCase() === state.rarityFilter;
      return matchQ && matchRarity;
    });

    panel.innerHTML = `
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Kho đồ cá nhân (INV-01 & MKT-01)</h3>
            <p class="card-subtitle">Theo dõi sức chứa gồm cả ReservedSlots khi đang có lệnh mua chưa hoàn tất</p>
          </div>
          <button id="btnRefreshInventory" type="button" class="btn btn-sm btn-secondary">↻ Làm mới kho</button>
        </div>

        <div class="capacity-bar-wrap">
          <div class="capacity-header">
            <span>
              Sức chứa: <strong>${totalOccupied} / ${state.maxSlots}</strong> ô
              <span class="text-muted">(Đang dùng: ${state.usedSlots} • ReservedSlots: ${state.reservedSlots})</span>
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
            <input id="invSearchInput" type="text" value="${escapeHtml(state.search)}" placeholder="VD: Dragon Blade..." />
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
                    const status = item.status || "Available";
                    const canSell = ["available", "idle", "unlocked"].includes(String(status).toLowerCase());
                    return `
                      <div class="item-card rarity-border-${rCls}">
                        <div>
                          <div class="item-card-top">
                            <div class="item-name">${escapeHtml(item.name || item.itemName || "Vật phẩm")}</div>
                            ${statusBadge(status)}
                          </div>
                          <div class="item-meta">
                            <span class="badge rarity-${rCls}">${escapeHtml(item.rarity || "Common")}</span>
                            ${item.category ? `<span class="badge badge-muted">${escapeHtml(item.category)}</span>` : ""}
                            ${item.level !== undefined ? `<span class="badge badge-info">Lv.${escapeHtml(item.level)}</span>` : ""}
                          </div>
                        </div>

                        <div class="item-kv-list">
                          <div class="item-kv">
                            <span class="text-muted">Instance ID</span>
                            ${renderIdChip(item.id || item.itemInstanceId)}
                          </div>
                          ${
                            item.templateCode
                              ? `<div class="item-kv"><span class="text-muted">Template</span><span class="mono">${escapeHtml(item.templateCode)}</span></div>`
                              : ""
                          }
                        </div>

                        <div class="item-actions">
                          <button type="button" class="btn btn-sm btn-primary" data-sell-idx="${idx}" ${!canSell ? "disabled" : ""}>
                            ${canSell ? "Đăng bán (MKT-01)" : "Đang khóa / niêm yết"}
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
        if (item) openCreateListingModal(item);
      });
    });
  }

  return {
    init: render,
    refresh: loadInventory,
    render,
  };
}