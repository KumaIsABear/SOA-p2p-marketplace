# AI-CONTEXT — Shared Rules & Frozen Architecture Baseline

**Project:** In-game P2P Marketplace and Economy Management System (SOA Finals)  
**Baseline:** `v1.0.0` (Frozen — synced with `v1.0-contract-freeze` + post-review patches)  
**Purpose:** Tài liệu ngữ cảnh dùng chung (Layer 1) cho cả 3 thành viên khi làm việc với AI (Gemini Gem / Chat). Mọi AI assistant **bắt buộc** tuân thủ chặt chẽ các quy tắc trong file này để code của 5 microservices ghép nối khớp 100%.

---

## 1. Phạm vi & Bối cảnh nhanh

* **Kiến trúc:** Monorepo + Microservices (**5 ASP.NET Core Web APIs + 1 YARP API Gateway**).
* **Nhóm & Thời gian:** 3 thành viên, triển khai MVP trong 11–12 ngày, backend-only (không xây dựng frontend hoàn chỉnh, chỉ có trang HTML nhỏ test Google Sign-In).
* **Triết lý tổ chức code:** Mỗi service là **1 project ASP.NET Core duy nhất** (Modular Monolith bên trong mỗi microservice), tách folder: `Controllers/`, `Application/`, `Domain/`, `Infrastructure/`. **Không** tách 4 project Clean Architecture cho từng service.
* **Out of scope (KHÔNG tự ý thêm vào code/compose):** Không tạo Notification Service, Payment.API riêng, PostgreSQL, Kafka, Jaeger, Loki, OpenTelemetry stack, đấu giá, mua lẻ (partial purchase), hệ thống Gold, hay cash-out.

---

## 2. Tech Stack & Thông số cần nhóm điền cứng 1 lần

| Thành phần | Giá trị chuẩn |
|---|---|
| Language / Framework | C# / ASP.NET Core Web API — TargetFramework: `.net9.0` |
| API Gateway | YARP (`gateway:8080` — điểm vào public duy nhất, host `127.0.0.1:8080`) |
| RDBMS | 1 SQL Server container chứa **5 logical DBs** (`AuthDb`, `WalletDb`, `InventoryDb`, `MarketplaceWriteDb`, `AdminAnalyticsDb`) |
| Read DB | MongoDB (`MarketplaceReadDb` — **chỉ `Marketplace.API` được truy cập**) |
| Cache / Lock | Redis (`redis:6379`) |
| Message Broker | RabbitMQ (`rabbitmq:5672`, vhost `marketplace`) + **MassTransit** (EF Core Transactional Outbox/Inbox) |
| External Providers | **Google Identity Services** (ID token flow qua `POST /api/v1/auth/google`), **ZaloPay Sandbox v2** (`https://sb-openapi.zalopay.vn`) |
| Monitoring | `prometheus-net.AspNetCore` (`/metrics`, `/health/live`, `/health/ready`) + Prometheus + Grafana |
| JWT Signing | `HMAC đối xứng (HS256 với Jwt__SigningKey có độ dài/entropy ≥ 256 bits)` |
| Token & Lock TTL | Access Token `15m`, Refresh Token `7d`, Redis Lock TTL `20s` |
| Economic Policies | Fee = `500 bps` (5%), Band = `2000 bps` (±20%), Inventory Capacity mặc định = `20 slots]` |
| Top-Up Packages | Server-owned config tại `Wallet.API`: `pkg-100 (10,000 VND → 100 Gem), pkg-500 (50,000 VND → 500 Gem), pkg-1000 (100,000 VND → 1,000 Gem), pkg-2500 (250,000 VND → 2,500 Gem).` |

---

## 3. Phân công 5 Microservices & Quyền sở hữu dữ liệu

**Luật thép Database-per-Service:** Mỗi service chỉ được kết nối đúng DB mình sở hữu. **Nghiêm cấm** cross-DB query, cross-DB FK, hoặc chia sẻ `Entity` / `DbContext` / `Repository` giữa các service.

| Thành viên | Service | Internal DNS | DB sở hữu | Trách nhiệm chính |
|---|---|---|---|---|
| **TV 1** | `Marketplace.API` | `http://marketplace-api:8080` | `MarketplaceWriteDb` + Mongo `MarketplaceReadDb` | Listing, Cancel, Direct Purchase, Trade Saga State Machine, CQRS Projection, Redis Lock, `Marketplace.Contracts` |
| **TV 2** | `Wallet.API` | `http://wallet-api:8080` | `WalletDb` | Balance, Ledger, Top-up ZaloPay Sandbox (Create/Callback/Query), Hold / Settle / Release consumers |
| **TV 2** | `Inventory.API` | `http://inventory-api:8080` | `InventoryDb` | ItemTemplate, Player Inventory, Seeder Grant, Sync Lock/Unlock/Status, Async Transfer / Reverse / Finalize / Unlock |
| **TV 3** | `Auth.API` | `http://auth-api:8080` | `AuthDb` | Seed login, Google Sign-In, JWT/Refresh rotation, Account Status & Moderation execution |
| **TV 3** | `Admin.API` | `http://admin-api:8080` | `AdminAnalyticsDb` | Price Policy (SQL + Redis writer), Analytics Consumer & Daily Job (`00:00 Asia/Ho_Chi_Minh`), CS Trade Trace & Moderation |
| **TV 3** | `Gateway.API` | `http://gateway:8080` | *(Không có DB)* | YARP routing allowlist, JWT + Auth status guard, Rate limit, CORS, Docker Compose, CI, Prometheus/Grafana |

---

## 4. Hợp đồng giao tiếp bắt buộc (Frozen Contracts)

Nguồn chuẩn duy nhất: `docs/03-Contracts/API-Contracts.txt` và `docs/03-Contracts/marketplace-openapi.yaml` (**41 REST operations**: 27 public `/api/v1/...`, 14 internal `/internal/v1/...`).

### 4.1. Chuẩn HTTP & JSON
* **Response thành công:** Trả DTO trực tiếp (KHÔNG bọc wrapper `{ success, data }`). Phân trang dùng `{ items, page, pageSize, totalCount }` (`page` 1-based, default `pageSize=20`, max `100`, luôn có tie-break sort ổn định theo ID).
* **Serialization:** JSON UTF-8 `camelCase` cho mọi API nội bộ/public; **riêng ZaloPay giữ nguyên `snake_case`**.
* **Kiểu dữ liệu:** Mọi tiền tệ (`Gem`, `VND`) dùng `long` (Int64) với `checked` arithmetic (chống overflow). Timestamp API dùng UTC RFC3339 (`DateTimeOffset` UTC); riêng ngày nghiệp vụ Analytics và prefix `app_trans_id` của ZaloPay dùng múi giờ `Asia/Ho_Chi_Minh`.
* **201 / 202 / 204:** `201` và `202` kèm header `Location`; `204` không có body. `202 Accepted` chỉ trả sau khi đã persist bền vững vào SQL, không đồng nghĩa hoàn tất.
* **Lỗi (`application/problem+json`):** Luôn tuân theo schema `ProblemDetails` với đủ 7 trường bắt buộc: `type`, `title`, `status`, `detail`, `instance`, `code` (UPPER_SNAKE_CASE theo Contract), `traceId` (Correlation ID), và `errors` (optional `string[]` cho lỗi validation). Không lộ stack trace, SQL error hay secret.
* **Validation mặc định đã chốt:** `q` max `100` ký tự; `reason` max `500` ký tự; default query: `MKT-04` (`sort=newest`), `MKT-06` (`status=Active`), `MKT-07` (`side=all`, `status=Completed`).

### 4.2. Headers & Idempotency
* `X-Correlation-ID`: UUID optional; nếu thiếu thì tự generate, luôn echo ở response và truyền tiếp qua internal HTTP + MassTransit `CorrelationId`.
* `Idempotency-Key`: Bắt buộc (1..128 ký tự) ở các endpoint mutation được đánh dấu (`WAL-04`, `INV-I01`, `MKT-01`, `MKT-03`, `ADM-04`, `ADM-07`).
  * Phải lưu bền vững trong **cùng SQL transaction** với business state theo khóa unique `(ActorId, Operation, ResourceScope, IdempotencyKey)` kèm `RequestFingerprint`.
  * Replay đúng payload trả lại kết quả/resource cũ (không tạo side effect lần 2). Replay khác payload trả `409 IDEMPOTENCY_CONFLICT`.

### 4.3. Bảo mật & Phân quyền
* **JWT Claims:** `sub` = `userId` (UUID), `role` = `Player | GM | CS`, kèm `iss`, `aud`, `exp`, `iat`, `jti`. Không có role JWT riêng cho `Buyer` hay `Seller`.
* **Ownership:** Mọi API cá nhân lấy `userId` duy nhất từ JWT `sub` đã xác minh. **Tuyệt đối không tin** `userId`, `sellerId`, `unitPrice`/`feeGem` do client truyền lậu qua header (`X-User-Id`) hoặc body. Tài nguyên không thuộc quyền sở hữu trả `404` (hoặc `403` theo đúng spec từng API).
* **Account Status (BR-10):** `Banned` chặn toàn bộ login/refresh/protected routes (`403 ACCOUNT_BANNED`). `TradeRestricted` chặn `MKT-01` và `MKT-03` (`403 TRADE_RESTRICTED`), nhưng vẫn cho xem, hủy listing của mình và nạp Gem; Saga đã accept tiếp tục chạy đến kết thúc. Khi `Auth.API` lỗi -> **fail closed (`503`)**.
* **Internal REST (`/internal/v1/*`):** Không bao giờ expose ra Gateway/ngrok. Xác thực bằng header `X-Service-Key` riêng cho từng caller (so sánh constant-time) + kiểm tra scope đường dẫn:
  * `gateway` → Auth (`AUTH-I01`)
  * `marketplace` → Auth (`AUTH-I01`), Inventory (`INV-I02..I06`), Wallet (`WAL-I01`), Admin (`ADM-I01`)
  * `admin` → Auth (`AUTH-I01..I02`), Wallet (`WAL-I01..I02`), Inventory (`INV-I06`), Marketplace (`MKT-I01..I03`)
  * `seeder` → Inventory (`INV-I01`)
* **`AUTH-I02` (`PATCH /internal/v1/auth/users/{userId}/status`):** Chỉ được sửa tài khoản `Player`. Nếu target là `GM`/`CS` phải trả `409 INVALID_ACCOUNT_TARGET`.

### 4.4. Asynchronous Messaging (RabbitMQ + MassTransit)
* **Shared Project:** `src/Shared/Marketplace.Contracts/` — namespace chuẩn **`Marketplace.Contracts.V1`**.
* **Quy tắc sống còn:** Mọi service phải reference trực tiếp C# `record` từ `Marketplace.Contracts.V1`. **Không bao giờ** tự định nghĩa lại class Command/Event cùng tên trong namespace riêng của từng service (MassTransit route theo full type name).
* **5 Queues cố định:** `wallet-commands`, `inventory-commands`, `marketplace-saga`, `marketplace-projection`, `admin-analytics`.
* **Common fields trong mọi message:** `CorrelationId` (Guid = `TradeId` hoặc `ListingId`), `OperationId` (Guid ổn định xuyên suốt các lần retry), và `OccurredAt` (UTC `DateTimeOffset`, chỉ có trên Event).
* **23 Message Contracts:**
  * *Commands (7):* `HoldBuyerFundsCommand`, `TransferItemCommand`, `SettleTradeCommand`, `RefundBuyerFundsCommand`, `ReverseItemTransferCommand`, `FinalizeItemTransferCommand`, `UnlockItemCommand`.
  * *Events (16):* `BuyerFundsHeldEvent`, `BuyerFundsHoldFailedEvent`, `ItemTransferredEvent`, `ItemTransferFailedEvent`, `TradeSettledEvent`, `TradeSettlementRejectedEvent`, `BuyerFundsRefundedEvent`, `ItemTransferReversedEvent`, `ItemTransferFinalizedEvent`, `ItemUnlockedEvent`, `ListingCreatedEvent`, `ListingProcessingEvent`, `ListingCancelledEvent`, `ListingReactivatedEvent`, `TradeCompletedEvent`, `TradeFailedEvent`.
* **Outbox / Inbox:** Luôn dùng MassTransit EF Core Outbox để commit business state + message cùng 1 transaction. Consumer phải idempotent theo cả `MessageId` lẫn khóa nghiệp vụ (`TradeId` / `ListingId` / `OperationId`).

---

## 5. Các điểm chốt kỹ thuật quan trọng (tránh code sai lệch)

1. **Redis Namespaces cố định:**
   * Listing Lock: `lock:listing:{listingId}` (`Marketplace` sở hữu, `SET NX PX 20000`, compare-and-delete bằng Lua script; SQL `WHERE Status = 'Active'` là chốt chặn cuối cùng).
   * Price Policy Cache: **`price:policy:{templateId}`** (`Admin` ghi, `Marketplace` đọc). Khi Redis miss, `Marketplace` gọi fallback **`ADM-I01` (`GET /internal/v1/admin/price-policies/{templateId}`)** để `Admin` đọc SQL và refresh lại Redis. Nếu vẫn không có (`404`) hoặc `Admin` lỗi -> `Marketplace` trả `503 PRICE_POLICY_UNAVAILABLE` (không dùng giá mặc định mù).
   * Analytics Cache: `analytics:{day}:{templateId}` (`Admin` sở hữu, tái tạo được từ `Trade_Facts`).
2. **Hủy Listing (`MKT-02`):** `Marketplace` chuyển nguyên tử `Active -> Cancelled`, đồng thời qua Outbox gửi **`UnlockItemCommand`** tới `inventory-commands` và publish **`ListingCancelledEvent`** tới `marketplace-projection`.
3. **Wallet Ledger & `TaxBurn`:**
   * Invariant: `TotalBalance >= HoldBalance >= 0`, `AvailableBalance = TotalBalance - HoldBalance`.
   * Deltas: `Hold` `(0, +gross)` | `Release` `(0, -gross)` | `SettlementDebit` `(-gross, -gross)` | `SettlementCredit` `(+net, 0)`.
   * **`TaxBurn`:** Chỉ để audit, ghi dưới `userId = sellerId` với `totalDelta = 0`, `holdDelta = 0`, `feeGem = fee` (các loại giao dịch khác `feeGem = null`). Tuyệt đối không trừ phí 2 lần.
   * Settlement cập nhật 2 ví trong 1 transaction SQL (lock row theo thứ tự `UserId` tăng dần để tránh deadlock). `FundHold` đã `Settled` không bao giờ được `Released`.
4. **ZaloPay Callback (`WAL-06`) & Reconciliation:**
   * Không yêu cầu JWT. Verify HMAC-SHA256 bằng `Key2` trên **nguyên chuỗi `data` gốc** (constant-time compare).
   * DTO `ZaloPayCallbackRequest.Type` là `int` (không gắn validation enum chặn ở model binding) để `type != 1` đi vào handler và trả HTTP `200` với `return_code = -1`.
   * Luôn trả HTTP `200 { return_code, return_message }`: `1` = credit thành công, `2` = exact duplicate đã paid trước đó, `0` = lỗi tạm thời / sai lệch dữ liệu cần reconcile, `-1` = sai MAC hoặc sai `type`. Chỉ trả `400` khi HTTP JSON body hỏng không parse được.
   * Callback và Reconciliation Worker (`/v2/query`) dùng **chung một hàm local atomic settlement** (khóa `TopUpOrder`, guard `OperationKey = "topup:{topUpId}"` và unique filtered index `ProviderTxnId`). Không bao giờ credit từ redirect URL.
5. **Inventory Lock Tombstone & Seller Reserved Slot:**
   * Lưu bảng `ItemLocks` theo `ListingId` (trạng thái `Locked | Released`) làm tombstone chặn request lock cũ chạy muộn tái khóa item đã release.
   * Khi `TransferItemCommand` chuyển item sang buyer ở trạng thái `PendingSettlement`, giữ `ReservedSlots` (+1) trên `Inventories` của seller (`usedSlots = itemCount + ReservedSlots`) để đảm bảo đủ slot nếu phải `ReverseItemTransferCommand`. Chỉ giảm `ReservedSlots` khi `FinalizeItemTransferCommand` thành công.
6. **Saga Recovery Rule:**
   * Lỗi kỹ thuật / timeout **KHÔNG đồng nghĩa** lỗi nghiệp vụ -> tuyệt đối không refund mù; retry cùng `OperationId`, hết retry đánh dấu `RequiresReview = true`.
   * Sau khi `Wallet` đã commit `SettleTrade` -> **chỉ forward recovery** (retry `FinalizeItemTransferCommand`), không refund ngược.
7. **MongoDB Projection (`Marketplace_Catalog`):**
   * Bắt buộc có đủ các trường của DTO `Listing` gồm cả `grossGem` và `updatedAt`.
   * Upsert có điều kiện `incomingVersion > currentVersion`. Giữ tombstone cho `Sold` / `Cancelled` để event cũ đến trễ không làm sống lại listing đã kết thúc.

---

## 6. Quy tắc làm việc với AI cho nhóm 3 người

### Cách thiết lập Gemini Gem (làm 1 lần cho mỗi thành viên)
Tạo 1 Gem tên `SOA-Marketplace-Dev` và đính kèm **Layer 1 (Shared Context)**:
1. File này (`docs/AI-CONTEXT.md`)
2. `docs/03-Contracts/API-Contracts.txt`
3. Toàn bộ file `.cs` trong `src/Shared/Marketplace.Contracts/` (ngay khi TV 1 push xong)
4. File ERD `.puml` của riêng service mình phụ trách (ví dụ TV 2 đính kèm `09-wallet-erd.puml`, `10-inventory-erd.puml`, `22A/22B`).

### Cách cung cấp Local Context (Layer 2) trong từng phiên chat
* **Tách chat theo cụm nhỏ:** Không gộp cả 2 service vào 1 đoạn chat dài (ví dụ TV 2 tách riêng: Chat A = `Wallet` Ledger + Saga Consumers; Chat B = `Wallet` ZaloPay Create/Callback/Query; Chat C = `Inventory` API + Consumers).
* **Chỉ dump thư mục service của mình:** Dùng Code Dump Export chỉ chọn `src/Services/<ServiceCủaMình>/` (cộng thêm `Program.cs` mẫu nếu đang dựng service mới). **Không dump** source code các service của thành viên khác.
* **Chỉ dẫn mặc định cho AI trong mọi prompt code:**
  > *"Chỉ sinh code cho `<Tên Service>`. Tuân thủ tuyệt đối `docs/AI-CONTEXT.md` và `API-Contracts.txt`. Không tạo lại DTO/Message contract trùng với `Marketplace.Contracts.V1`. Không truy cập DB của service khác."*
* **Đồng bộ thay đổi phát sinh:** Nếu trong lúc code buộc phải chỉnh contract/env/shared config, tạo Pull Request, ghi 2–3 dòng vào `docs/SYNC-LOG.md` (ngày, file đổi, service bị ảnh hưởng) và báo nhóm pull `main` trước khi tiếp tục.