import {
  ENDPOINTS,
  SEED_TEMPLATES,
  addDaysYmd,
  apiRequest,
  escapeHtml,
  formatDateTime,
  formatGem,
  getSession,
  getTemplateName,
  getTodayHoChiMinh,
  newUuid,
  problemMessage,
  renderIdChip,
  renderProblemAlert,
  showToast,
} from "./api.js";

function computePreviewBounds(basePriceVal, bandBpsVal) {
  try {
    const ref = BigInt(String(basePriceVal || "0").trim());
    const bps = BigInt(String(bandBpsVal || "0").trim());
    if (ref < 1n || bps < 0n || bps > 9999n) return null;
    const denom = 10000n;
    const minNum = ref * (denom - bps);
    let minPrice = (minNum + denom - 1n) / denom;
    if (minPrice < 1n) minPrice = 1n;
    const maxPrice = (ref * (denom + bps)) / denom;
    return { minPrice, maxPrice };
  } catch {
    return null;
  }
}

function formatBpsPercent(bps) {
  const num = Number(bps);
  if (!Number.isFinite(num)) return "";
  return `±${(num / 100).toFixed(2)}%`;
}

export function createGmModule() {
  const panel = document.getElementById("panel-gm");
  const todayVn = getTodayHoChiMinh();

  const state = {
    overviewDay: todayVn,
    overview: null,
    overviewError: null,

    history: {
      from: addDaysYmd(todayVn, -6),
      to: addDaysYmd(todayVn, 1), // Khoảng nửa mở [from, to) bao gồm hôm nay
      templateId: "",
      page: 1,
      pageSize: 15,
      items: [],
      totalCount: 0,
      error: null,
    },

    policies: {
      page: 1,
      pageSize: 20,
      items: [],
      totalCount: 0,
      error: null,
    },

    form: {
      templateId: SEED_TEMPLATES[0].templateId,
      basePrice: "1000",
      bandBasisPoints: "2000",
      expectedVersion: "0",
      reason: "Điều chỉnh biên độ giá thị trường",
      idempotencyKey: newUuid(),
      lastErrorRes: null,
      lastSaved: null,
    },
  };

  async function loadOverview() {
    if (!getSession().isAuthenticated) return;
    const res = await apiRequest(ENDPOINTS.adminAnalytics, {
      query: { day: state.overviewDay },
    });
    if (res.ok && res.data) {
      state.overview = res.data;
      state.overviewError = null;
    } else {
      state.overview = null;
      state.overviewError = res;
      showToast("Lỗi ADM-01 (Analytics Overview)", problemMessage(res), "danger");
    }
    render();
  }

  async function loadHistory() {
    if (!getSession().isAuthenticated) return;
    const res = await apiRequest(ENDPOINTS.adminAnalyticsHistory, {
      query: {
        from: state.history.from,
        to: state.history.to,
        templateId: state.history.templateId,
        page: state.history.page,
        pageSize: state.history.pageSize,
      },
    });
    if (res.ok && res.data) {
      state.history.items = Array.isArray(res.data.items) ? res.data.items : [];
      state.history.totalCount = res.data.totalCount ?? state.history.items.length;
      state.history.error = null;
    } else {
      state.history.items = [];
      state.history.totalCount = 0;
      state.history.error = res;
      showToast("Lỗi ADM-02 (Analytics History)", problemMessage(res), "danger");
    }
    render();
  }

  async function loadPolicies() {
    if (!getSession().isAuthenticated) return;
    const res = await apiRequest(ENDPOINTS.adminPricePolicies, {
      query: {
        page: state.policies.page,
        pageSize: state.policies.pageSize,
      },
    });
    if (res.ok && res.data) {
      state.policies.items = Array.isArray(res.data.items) ? res.data.items : [];
      state.policies.totalCount = res.data.totalCount ?? state.policies.items.length;
      state.policies.error = null;
    } else {
      state.policies.items = [];
      state.policies.totalCount = 0;
      state.policies.error = res;
      showToast("Lỗi ADM-03 (Price Policies)", problemMessage(res), "danger");
    }
    render();
  }

  async function refreshAll() {
    if (!getSession().isAuthenticated) {
      render();
      return;
    }
    await Promise.all([loadOverview(), loadHistory(), loadPolicies()]);
  }

  async function submitPolicyUpsert() {
    const templateId = state.form.templateId.trim();
    const idemKey = state.form.idempotencyKey.trim() || newUuid();
    state.form.idempotencyKey = idemKey;

    if (!templateId) {
      showToast("Thiếu templateId", "Vui lòng nhập hoặc chọn UUID template.", "warning");
      return;
    }

    const basePrice = Number(state.form.basePrice);
    const bandBasisPoints = Number(state.form.bandBasisPoints);
    const expectedVersion = Number(state.form.expectedVersion);

    const res = await apiRequest(ENDPOINTS.adminPricePolicyUpsert(templateId), {
      method: "PUT",
      idempotencyKey: idemKey,
      body: {
        basePrice: Number.isFinite(basePrice) ? basePrice : null,
        bandBasisPoints: Number.isFinite(bandBasisPoints) ? bandBasisPoints : null,
        expectedVersion: Number.isFinite(expectedVersion) ? expectedVersion : null,
        reason: state.form.reason,
      },
    });

    if (!res.ok) {
      state.form.lastErrorRes = res;
      state.form.lastSaved = null;
      const code = res.problem?.code || "";
      const title =
        code === "VERSION_CONFLICT"
          ? "409 VERSION_CONFLICT — Sai expectedVersion"
          : code === "INVALID_PRICE_POLICY"
            ? "422 INVALID_PRICE_POLICY — Vi phạm biên độ/giá"
            : "Lỗi lưu Chính sách giá (ADM-04)";
      showToast(title, problemMessage(res), "danger");
      render();
      return;
    }

    state.form.lastErrorRes = null;
    state.form.lastSaved = res.data;
    state.form.expectedVersion = String(res.data.version);
    showToast(
      "Cập nhật Chính sách giá thành công (ADM-04)",
      `Version mới: v${res.data.version} • [${formatGem(res.data.minUnitPrice)}, ${formatGem(res.data.maxUnitPrice)}] Gem`,
      "success"
    );
    await loadPolicies();
  }

  function renderOverviewSection() {
    const ov = state.overview;
    const isPartial = Boolean(ov && (ov.partial || (ov.unavailableSources && ov.unavailableSources.length > 0)));
    const unavailable = ov?.unavailableSources || [];
    const hasSupply = Boolean(ov?.gemSupply);
    const hasActiveCount = ov !== null && ov.activeListingCount !== null && ov.activeListingCount !== undefined;

    return `
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Tổng quan kinh tế ngày (ADM-01)</h3>
            <p class="card-subtitle"><code>GET /api/v1/admin/analytics?day=YYYY-MM-DD</code> — Tổng hợp Trade Facts + Wallet Supply + Active Listings</p>
          </div>
          <form id="gmOverviewForm" class="form-row" style="margin-bottom:0;">
            <input id="gmOverviewDay" type="date" value="${escapeHtml(state.overviewDay)}" />
            <button type="submit" class="btn btn-sm btn-primary">Xem ngày</button>
          </form>
        </div>

        ${state.overviewError ? renderProblemAlert(state.overviewError) : ""}

        ${
          isPartial
            ? `<div class="alert alert-warning">
                <strong>⚠ Dữ liệu không đầy đủ (<code>partial = true</code>):</strong>
                Nguồn đang lỗi: ${unavailable.map((s) => `<span class="badge badge-danger" style="margin-left:4px;">${escapeHtml(s)}</span>`).join("")}
                — Các chỉ số từ nguồn lỗi hiển thị <strong>N/A</strong> (không giả định 0).
              </div>`
            : ""
        }

        ${
          ov
            ? `
          <div class="grid-4" style="margin-bottom:12px;">
            <div class="stat-box">
              <div class="stat-label">Số giao dịch (tradeCount)</div>
              <div class="stat-value text-accent">${formatGem(ov.tradeCount)}</div>
              <div class="stat-meta">Ngày nghiệp vụ: <strong>${escapeHtml(ov.day)}</strong></div>
            </div>
            <div class="stat-box">
              <div class="stat-label">Sản lượng item (volume)</div>
              <div class="stat-value">${formatGem(ov.volume)}</div>
              <div class="stat-meta">Tổng số lượng vật phẩm khớp lệnh</div>
            </div>
            <div class="stat-box">
              <div class="stat-label">Tổng giao dịch (totalGrossGem)</div>
              <div class="stat-value text-success">${formatGem(ov.totalGrossGem)} <span style="font-size:12px;">Gem</span></div>
              <div class="stat-meta">Doanh số gộp trong ngày</div>
            </div>
            <div class="stat-box">
              <div class="stat-label">Tổng thuế đốt (totalFeeGem)</div>
              <div class="stat-value text-warning">${formatGem(ov.totalFeeGem)} <span style="font-size:12px;">Gem</span></div>
              <div class="stat-meta">5% TaxBurn sink</div>
            </div>
          </div>

          <div class="grid-4">
            <div class="stat-box">
              <div class="stat-label">Tin đang mở bán (activeListingCount)</div>
              <div class="stat-value ${hasActiveCount ? "text-accent" : "text-danger"}">${hasActiveCount ? formatGem(ov.activeListingCount) : "N/A"}</div>
              <div class="stat-meta">Nguồn: Marketplace SQL (MKT-I03)</div>
            </div>
            <div class="stat-box">
              <div class="stat-label">Tổng cung Gem (gemSupply.totalGem)</div>
              <div class="stat-value ${hasSupply ? "" : "text-danger"}">${hasSupply ? formatGem(ov.gemSupply.totalGem) : "N/A"}</div>
              <div class="stat-meta">Nguồn: Wallet SQL (WAL-I02)</div>
            </div>
            <div class="stat-box">
              <div class="stat-label">Gem đang Hold (gemSupply.heldGem)</div>
              <div class="stat-value ${hasSupply ? "text-warning" : "text-danger"}">${hasSupply ? formatGem(ov.gemSupply.heldGem) : "N/A"}</div>
              <div class="stat-meta">Escrow đang khóa</div>
            </div>
            <div class="stat-box">
              <div class="stat-label">Gem khả dụng (gemSupply.availableGem)</div>
              <div class="stat-value ${hasSupply ? "text-success" : "text-danger"}">${hasSupply ? formatGem(ov.gemSupply.availableGem) : "N/A"}</div>
              <div class="stat-meta">Cập nhật: ${escapeHtml(formatDateTime(ov.asOf))}</div>
            </div>
          </div>`
            : !state.overviewError
              ? `<div class="empty-placeholder">Chưa có dữ liệu tổng quan. Bấm "Xem ngày" để tải.</div>`
              : ""
        }
      </div>
    `;
  }

  function renderHistorySection() {
    const h = state.history;
    const totalPages = Math.max(1, Math.ceil(h.totalCount / h.pageSize));

    return `
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Lịch sử thống kê theo ngày & Template (ADM-02)</h3>
            <p class="card-subtitle"><code>GET /api/v1/admin/analytics/history</code> — Khoảng nửa mở <code>[from, to)</code> tối đa 31 ngày</p>
          </div>
        </div>

        <form id="gmHistoryForm" class="form-row">
          <div class="form-group" style="max-width:170px;">
            <label for="gmHistFrom">Từ ngày (from)</label>
            <input id="gmHistFrom" type="date" required value="${escapeHtml(h.from)}" />
          </div>
          <div class="form-group" style="max-width:170px;">
            <label for="gmHistTo">Đến trước ngày (to)</label>
            <input id="gmHistTo" type="date" required value="${escapeHtml(h.to)}" />
          </div>
          <div class="form-group" style="min-width:250px;">
            <label for="gmHistTemplate">Template (tuỳ chọn)</label>
            <select id="gmHistTemplate">
              <option value="">Tất cả Template</option>
              ${SEED_TEMPLATES.map(
                (t) => `<option value="${t.templateId}" ${h.templateId === t.templateId ? "selected" : ""}>${escapeHtml(t.name)} (${t.templateId.slice(-4)})</option>`
              ).join("")}
            </select>
          </div>
          <div class="form-group" style="max-width:110px;">
            <label for="gmHistPageSize">pageSize</label>
            <input id="gmHistPageSize" type="number" min="1" max="100" value="${escapeHtml(h.pageSize)}" />
          </div>
          <button type="submit" class="btn btn-primary">Truy vấn lịch sử</button>
        </form>

        ${h.error ? renderProblemAlert(h.error) : ""}

        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Ngày (day)</th>
                <th>Template</th>
                <th>tradeCount</th>
                <th>volume</th>
                <th>totalGrossGem</th>
                <th>totalFeeGem</th>
                <th>minUnitPrice</th>
                <th>maxUnitPrice</th>
                <th>referencePrice</th>
              </tr>
            </thead>
            <tbody>
              ${
                h.items.length === 0
                  ? `<tr><td colspan="9" class="text-muted" style="text-align:center;padding:22px;">Không có bản ghi thống kê nào trong khoảng [${escapeHtml(h.from)}, ${escapeHtml(h.to)}).</td></tr>`
                  : h.items
                      .map((row) => {
                        const name = getTemplateName(row.templateId);
                        return `
                          <tr>
                            <td class="mono"><strong>${escapeHtml(row.day)}</strong></td>
                            <td>
                              ${name ? `<div><strong>${escapeHtml(name)}</strong></div>` : ""}
                              ${renderIdChip(row.templateId)}
                            </td>
                            <td class="mono">${formatGem(row.tradeCount)}</td>
                            <td class="mono">${formatGem(row.volume)}</td>
                            <td class="mono text-success">${formatGem(row.totalGrossGem)}</td>
                            <td class="mono text-warning">${formatGem(row.totalFeeGem)}</td>
                            <td class="mono">${row.minUnitPrice !== null && row.minUnitPrice !== undefined ? formatGem(row.minUnitPrice) : "—"}</td>
                            <td class="mono">${row.maxUnitPrice !== null && row.maxUnitPrice !== undefined ? formatGem(row.maxUnitPrice) : "—"}</td>
                            <td class="mono text-accent" style="font-weight:700;">${formatGem(row.referencePrice)}</td>
                          </tr>
                        `;
                      })
                      .join("")
              }
            </tbody>
          </table>
        </div>

        <div class="pagination-bar">
          <span>Trang ${h.page} / ${totalPages} (Tổng ${h.totalCount} dòng)</span>
          <div class="header-actions">
            <button id="btnGmHistPrev" type="button" class="btn btn-xs btn-secondary" ${h.page <= 1 ? "disabled" : ""}>← Trước</button>
            <button id="btnGmHistNext" type="button" class="btn btn-xs btn-secondary" ${h.page >= totalPages ? "disabled" : ""}>Sau →</button>
          </div>
        </div>
      </div>
    `;
  }

  function renderPricePoliciesSection() {
    const p = state.policies;
    const f = state.form;
    const totalPages = Math.max(1, Math.ceil(p.totalCount / p.pageSize));
    const preview = computePreviewBounds(f.basePrice, f.bandBasisPoints);

    return `
      <div class="card">
        <div class="card-header">
          <div>
            <h3 class="card-title">Quản lý Chính sách giá (ADM-03 & ADM-04)</h3>
            <p class="card-subtitle">Danh sách <code>GET /api/v1/admin/price-policies</code> & Upsert <code>PUT /api/v1/admin/price-policies/{templateId}</code> đồng bộ Redis <code>price:policy:{templateId}</code></p>
          </div>
          <div class="header-actions">
            <button id="btnNewPolicyMode" type="button" class="btn btn-sm btn-secondary">+ Tạo mới (expectedVersion=0)</button>
            <button id="btnRefreshPolicies" type="button" class="btn btn-sm btn-secondary">↻ Làm mới</button>
          </div>
        </div>

        ${p.error ? renderProblemAlert(p.error) : ""}

        <div class="table-wrap" style="margin-bottom:16px;">
          <table>
            <thead>
              <tr>
                <th>Template</th>
                <th>basePrice</th>
                <th>referencePrice</th>
                <th>bandBasisPoints</th>
                <th>Biên độ [min, max]</th>
                <th>version</th>
                <th>asOf</th>
                <th>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              ${
                p.items.length === 0
                  ? `<tr><td colspan="8" class="text-muted" style="text-align:center;padding:22px;">Chưa có chính sách giá nào.</td></tr>`
                  : p.items
                      .map((pol, idx) => {
                        const name = getTemplateName(pol.templateId);
                        return `
                          <tr>
                            <td>
                              ${name ? `<div><strong>${escapeHtml(name)}</strong></div>` : ""}
                              ${renderIdChip(pol.templateId)}
                            </td>
                            <td class="mono">${formatGem(pol.basePrice)}</td>
                            <td class="mono text-accent" style="font-weight:700;">${formatGem(pol.referencePrice)}</td>
                            <td class="mono">${escapeHtml(pol.bandBasisPoints)} bps <span class="text-muted">(${escapeHtml(formatBpsPercent(pol.bandBasisPoints))})</span></td>
                            <td class="mono text-warning">[${formatGem(pol.minUnitPrice)}, ${formatGem(pol.maxUnitPrice)}]</td>
                            <td class="mono"><span class="badge badge-info">v${escapeHtml(pol.version)}</span></td>
                            <td>${escapeHtml(formatDateTime(pol.asOf))}</td>
                            <td>
                              <button type="button" class="btn btn-xs btn-primary" data-edit-policy-idx="${idx}">Chỉnh sửa</button>
                            </td>
                          </tr>
                        `;
                      })
                      .join("")
              }
            </tbody>
          </table>
        </div>

        <div class="pagination-bar" style="margin-bottom:16px;">
          <span>Trang ${p.page} / ${totalPages} (Tổng ${p.totalCount} chính sách)</span>
          <div class="header-actions">
            <button id="btnPolPrev" type="button" class="btn btn-xs btn-secondary" ${p.page <= 1 ? "disabled" : ""}>← Trước</button>
            <button id="btnPolNext" type="button" class="btn btn-xs btn-secondary" ${p.page >= totalPages ? "disabled" : ""}>Sau →</button>
          </div>
        </div>

        <!-- ADM-04 Upsert Form -->
        <div class="trace-section mb-0">
          <div class="trace-section-header">
            <h4 class="trace-section-title">Cập nhật / Tạo mới Chính sách giá (ADM-04)</h4>
            <div class="header-actions">
              <button id="btnDemoVersionConflict" type="button" class="btn btn-xs btn-warning" title="Đặt expectedVersion = 999 để demo 409 VERSION_CONFLICT">Demo 409 Conflict</button>
              <button id="btnDemoInvalidPolicy" type="button" class="btn btn-xs btn-danger" title="Đặt bandBasisPoints = 10000 để demo 422 INVALID_PRICE_POLICY">Demo 422 Invalid</button>
            </div>
          </div>

          <div class="idem-bar">
            <label for="gmPolIdemKey">Idempotency-Key:</label>
            <input id="gmPolIdemKey" type="text" value="${escapeHtml(f.idempotencyKey)}" />
            <button id="btnGmPolGenIdem" type="button" class="btn btn-xs btn-secondary">Tạo key mới</button>
          </div>

          <form id="gmPolicyUpsertForm" novalidate>
            <div class="form-row">
              <div class="form-group" style="min-width:260px;">
                <label for="gmPolTemplateSelect">Chọn nhanh Template seed</label>
                <select id="gmPolTemplateSelect">
                  <option value="">-- Nhập UUID tùy chỉnh bên cạnh --</option>
                  ${SEED_TEMPLATES.map(
                    (t) => `<option value="${t.templateId}" ${f.templateId === t.templateId ? "selected" : ""}>${escapeHtml(t.name)} (${t.templateId.slice(-4)})</option>`
                  ).join("")}
                </select>
              </div>
              <div class="form-group" style="min-width:290px;">
                <label for="gmPolTemplateId">Template UUID (path param)</label>
                <input id="gmPolTemplateId" type="text" value="${escapeHtml(f.templateId)}" placeholder="11111111-1111-4111-8111-000000000001" />
              </div>
            </div>

            <div class="form-row">
              <div class="form-group">
                <label for="gmPolBasePrice">basePrice (Gem ≥ 1)</label>
                <input id="gmPolBasePrice" type="number" value="${escapeHtml(f.basePrice)}" />
              </div>
              <div class="form-group">
                <label for="gmPolBandBps">bandBasisPoints (0..9999) <span id="gmPolBandPct" class="text-accent">${escapeHtml(formatBpsPercent(f.bandBasisPoints))}</span></label>
                <input id="gmPolBandBps" type="number" value="${escapeHtml(f.bandBasisPoints)}" />
              </div>
              <div class="form-group">
                <label for="gmPolExpectedVersion">expectedVersion (0 = tạo mới)</label>
                <input id="gmPolExpectedVersion" type="number" value="${escapeHtml(f.expectedVersion)}" />
              </div>
            </div>

            <div class="form-row">
              <div class="form-group">
                <label for="gmPolReason">Lý do thay đổi — reason (bắt buộc, tối đa 500 ký tự)</label>
                <input id="gmPolReason" type="text" maxlength="500" value="${escapeHtml(f.reason)}" placeholder="Lý do điều chỉnh chính sách giá..." />
              </div>
            </div>

            <div id="gmPolBoundsPreview" class="text-muted mono" style="font-size:12px;margin-bottom:10px;">
              ${
                preview
                  ? `Biên độ dự kiến theo basePrice: [${formatGem(preview.minPrice)}, ${formatGem(preview.maxPrice)}] Gem (nếu đã có trade, server tính theo referencePrice).`
                  : "Giá trị basePrice hoặc bandBasisPoints nằm ngoài miền hợp lệ (server sẽ trả 422 INVALID_PRICE_POLICY)."
              }
            </div>

            ${f.lastErrorRes ? renderProblemAlert(f.lastErrorRes) : ""}
            ${
              f.lastSaved
                ? `<div class="alert alert-success">
                    Đã lưu thành công <code>${escapeHtml(f.lastSaved.templateId)}</code> • <strong>v${escapeHtml(f.lastSaved.version)}</strong> •
                    Biên độ thực tế: <strong>[${formatGem(f.lastSaved.minUnitPrice)}, ${formatGem(f.lastSaved.maxUnitPrice)}]</strong> Gem (reference: ${formatGem(f.lastSaved.referencePrice)}).
                  </div>`
                : ""
            }

            <div class="form-row mb-0">
              <button type="submit" class="btn btn-primary">Lưu Chính sách giá (PUT ADM-04)</button>
              <button id="btnGmPolReplay" type="button" class="btn btn-warning" title="Gửi lại đúng Idempotency-Key hiện tại">Replay cùng Idempotency-Key</button>
            </div>
          </form>
        </div>
      </div>
    `;
  }

  function render() {
    if (!getSession().isAuthenticated) {
      panel.innerHTML = `<div class="card"><div class="alert alert-warning mb-0">Vui lòng đăng nhập tài khoản <strong>GM</strong> (<code>gm@marketplace.local</code>) để sử dụng GM Portal.</div></div>`;
      return;
    }

    panel.innerHTML = `
      ${renderOverviewSection()}
      ${renderHistorySection()}
      ${renderPricePoliciesSection()}
    `;

    bindEvents();
  }

  function bindEvents() {
    panel.querySelector("#gmOverviewForm")?.addEventListener("submit", (e) => {
      e.preventDefault();
      state.overviewDay = panel.querySelector("#gmOverviewDay").value || getTodayHoChiMinh();
      loadOverview();
    });

    panel.querySelector("#gmHistoryForm")?.addEventListener("submit", (e) => {
      e.preventDefault();
      state.history.from = panel.querySelector("#gmHistFrom").value;
      state.history.to = panel.querySelector("#gmHistTo").value;
      state.history.templateId = panel.querySelector("#gmHistTemplate").value;
      state.history.pageSize = Number(panel.querySelector("#gmHistPageSize").value || 15);
      state.history.page = 1;
      loadHistory();
    });

    panel.querySelector("#btnGmHistPrev")?.addEventListener("click", () => {
      if (state.history.page > 1) {
        state.history.page -= 1;
        loadHistory();
      }
    });
    panel.querySelector("#btnGmHistNext")?.addEventListener("click", () => {
      state.history.page += 1;
      loadHistory();
    });

    panel.querySelector("#btnRefreshPolicies")?.addEventListener("click", loadPolicies);
    panel.querySelector("#btnPolPrev")?.addEventListener("click", () => {
      if (state.policies.page > 1) {
        state.policies.page -= 1;
        loadPolicies();
      }
    });
    panel.querySelector("#btnPolNext")?.addEventListener("click", () => {
      state.policies.page += 1;
      loadPolicies();
    });

    panel.querySelector("#btnNewPolicyMode")?.addEventListener("click", () => {
      state.form = {
        templateId: newUuid(),
        basePrice: "500",
        bandBasisPoints: "2000",
        expectedVersion: "0",
        reason: "Khởi tạo chính sách giá mới",
        idempotencyKey: newUuid(),
        lastErrorRes: null,
        lastSaved: null,
      };
      render();
      panel.querySelector("#gmPolTemplateId")?.focus();
    });

    panel.querySelectorAll("[data-edit-policy-idx]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const pol = state.policies.items[Number(btn.dataset.editPolicyIdx)];
        if (!pol) return;
        state.form = {
          templateId: pol.templateId,
          basePrice: String(pol.basePrice),
          bandBasisPoints: String(pol.bandBasisPoints),
          expectedVersion: String(pol.version),
          reason: `Cập nhật chính sách giá ${getTemplateName(pol.templateId) || pol.templateId}`,
          idempotencyKey: newUuid(),
          lastErrorRes: null,
          lastSaved: null,
        };
        render();
        panel.querySelector("#gmPolicyUpsertForm")?.scrollIntoView({ behavior: "smooth", block: "center" });
      });
    });

    panel.querySelector("#btnGmPolGenIdem")?.addEventListener("click", () => {
      state.form.idempotencyKey = newUuid();
      const input = panel.querySelector("#gmPolIdemKey");
      if (input) input.value = state.form.idempotencyKey;
    });

    panel.querySelector("#gmPolIdemKey")?.addEventListener("input", (e) => {
      state.form.idempotencyKey = e.target.value;
    });

    panel.querySelector("#gmPolTemplateSelect")?.addEventListener("change", (e) => {
      if (!e.target.value) return;
      state.form.templateId = e.target.value;
      const existing = state.policies.items.find((x) => x.templateId.toLowerCase() === e.target.value.toLowerCase());
      if (existing) {
        state.form.basePrice = String(existing.basePrice);
        state.form.bandBasisPoints = String(existing.bandBasisPoints);
        state.form.expectedVersion = String(existing.version);
      } else {
        state.form.expectedVersion = "0";
      }
      state.form.idempotencyKey = newUuid();
      render();
    });

    panel.querySelector("#gmPolTemplateId")?.addEventListener("input", (e) => {
      state.form.templateId = e.target.value;
    });

    const syncBoundsPreview = () => {
      state.form.basePrice = panel.querySelector("#gmPolBasePrice").value;
      state.form.bandBasisPoints = panel.querySelector("#gmPolBandBps").value;
      const pctEl = panel.querySelector("#gmPolBandPct");
      if (pctEl) pctEl.textContent = formatBpsPercent(state.form.bandBasisPoints);
      const previewEl = panel.querySelector("#gmPolBoundsPreview");
      const preview = computePreviewBounds(state.form.basePrice, state.form.bandBasisPoints);
      if (previewEl) {
        previewEl.textContent = preview
          ? `Biên độ dự kiến theo basePrice: [${formatGem(preview.minPrice)}, ${formatGem(preview.maxPrice)}] Gem (nếu đã có trade, server tính theo referencePrice).`
          : "Giá trị basePrice hoặc bandBasisPoints nằm ngoài miền hợp lệ (server sẽ trả 422 INVALID_PRICE_POLICY).";
      }
    };

    panel.querySelector("#gmPolBasePrice")?.addEventListener("input", syncBoundsPreview);
    panel.querySelector("#gmPolBandBps")?.addEventListener("input", syncBoundsPreview);
    panel.querySelector("#gmPolExpectedVersion")?.addEventListener("input", (e) => {
      state.form.expectedVersion = e.target.value;
    });
    panel.querySelector("#gmPolReason")?.addEventListener("input", (e) => {
      state.form.reason = e.target.value;
    });

    panel.querySelector("#btnDemoVersionConflict")?.addEventListener("click", () => {
      state.form.expectedVersion = "999";
      state.form.idempotencyKey = newUuid();
      render();
    });

    panel.querySelector("#btnDemoInvalidPolicy")?.addEventListener("click", () => {
      state.form.bandBasisPoints = "10000";
      state.form.idempotencyKey = newUuid();
      render();
    });

    panel.querySelector("#gmPolicyUpsertForm")?.addEventListener("submit", (e) => {
      e.preventDefault();
      submitPolicyUpsert();
    });

    panel.querySelector("#btnGmPolReplay")?.addEventListener("click", () => {
      submitPolicyUpsert();
    });
  }

  return {
    init: render,
    refresh: refreshAll,
    render,
  };
}