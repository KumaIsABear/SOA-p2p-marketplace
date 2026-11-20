# Functional Requirements — In-game P2P Marketplace

Version: 1.0.0 | Frozen baseline

## 1. Conventions

FR IDs giữ theo Analyst và phạm vi `a(1).txt`. 29 FR còn lại. Google Sign-In là mở rộng FR-AUTH-01, không tái sử dụng FR-AUTH-03 (OTP). API IDs trong bảng là mã riêng của `../03-Contracts/API-Contracts.txt`, không phải FR IDs. Các task tự động có thể không có REST endpoint. Acceptance criteria ở đây là điều kiện cần kiểm chứng, không phải kết quả đã đạt. Chi tiết payload/status/error codes xem Contracts/OpenAPI.

## 2. Auth & Identity — Auth.API (3)

| FR | Actor | Yêu cầu chức năng | Điều kiện chấp nhận | Contract |
|---|---|---|---|---|
| FR-AUTH-01 | Player, GM, CS; Google Identity | Đăng nhập tài khoản seed hoặc Google; cấp JWT và refresh token, xem thông tin mình. Google JIT tạo Player, role lấy từ DB; không auto-link bằng email. | Valid login trả token; Google token sai audience/expiry bị từ chối; không tự cấp GM/CS. | AUTH-01,02,05 |
| FR-AUTH-02 | Player, GM, CS | Refresh phiên và logout; rotate refresh token, lưu hash, revoke khi logout/ban. | Refresh cũ không dùng lại; logout lặp không tạo side effect; access token hết hạn tự nhiên. | AUTH-03,04 |
| FR-AUTH-04 | Gateway, Marketplace, Auth | Kiểm tra account status, role, chặn ban/hạn chế theo BR-10. | Token còn hạn nhưng Banned vẫn bị chặn; TradeRestricted không mua/đăng mới; Auth lỗi fail closed. | AUTH-I01,I02; Gateway policies |

## 3. Wallet & Payment — Wallet.API (6)

| FR | Actor | Yêu cầu chức năng | Điều kiện chấp nhận | Contract |
|---|---|---|---|---|
| FR-WAL-01 | Player | Xem Total/Hold/Available của ví mình, ledger cá nhân; lazy init ví rỗng. | Available=Total-Hold; không đọc ví người khác; lịch sử đúng owner. | WAL-01,02 |
| FR-WAL-02 | Buyer | Xem gói nạp server, tạo Pending order, lấy ZaloPay URL, tra trạng thái. | 201 khi có URL; 202 cho kết quả chưa rõ sau khi lưu intent; cùng key trả đơn cũ, khác payload reject. | WAL-03,04,05 |
| FR-WAL-03 | ZaloPay, Wallet reconciliation | Verify callback Key2 hoặc query xác nhận; đối soát đơn, số tiền, IDs; atomic Paid + credit + ledger. | Invalid MAC/amount mismatch không credit; callback/query lặp chỉ cộng một lần, timeout không làm mất trạng thái Pending. | WAL-06,05; reconciliation job |
| FR-WAL-04 | Saga Orchestrator | Hold khoản Gem theo trade, không giảm Total, kiểm khả dụng nguyên tử. | Thiếu tiền reject; retry không hold lần hai; không overspend giữa listings. | HoldBuyerFundsCommand; BuyerFundsHeldEvent / BuyerFundsHoldFailedEvent |
| FR-WAL-05 | Saga Orchestrator | Tất toán hold, buyer debit; seller credit sau phí; ledger/audit burn. | Local tx đảm bảo gross=net+fee; supply giảm đúng fee; replay không trừ lần nữa. | SettleTradeCommand; TradeSettledEvent / TradeSettlementRejectedEvent |
| FR-WAL-06 | Saga Orchestrator | Release hold khi giao dịch thất bại xác định; kiểm trạng thái. | Hold về đúng giá trị, Total không đổi; Settled không release; replay harmless. | RefundBuyerFundsCommand; BuyerFundsRefundedEvent |

## 4. Inventory — Inventory.API (5)

| FR | Actor | Yêu cầu chức năng | Điều kiện chấp nhận | Contract |
|---|---|---|---|---|
| FR-INV-01 | Player | Xem kho cá nhân và sức chứa, trạng thái item. | Có Available/Locked/PendingSettlement; không xem kho khác. | INV-01 |
| FR-INV-02 | Core Game Server / Seeder | Cấp vật phẩm theo template và số lượng bằng credential nội bộ. | Template/capacity hợp lệ; idempotent grant; endpoint không public. | INV-I01 |
| FR-INV-03 | Marketplace | Tra metadata, khóa item theo ListingId sau khi kiểm owner, tradable, toàn stack, availability; trạng thái lock phục vụ recovery. | Cùng item không lock bởi listing khác; retry cùng operation không tạo lock khác. | INV-I02,I03,I05 |
| FR-INV-04 | Marketplace | Mở khóa khi cancel hoặc bồi hoàn create lỗi; bảo vệ đúng ListingId. | Cancel eventual unlock; lock lặp an toàn; PendingSettlement không unlock tùy tiện. | INV-I04,I05; UnlockItemCommand; ItemUnlockedEvent |
| FR-INV-05 | Saga Orchestrator | Kiểm capacity, chuyển quyền sở hữu item ở PendingSettlement, reserve seller slot; finalize hoặc reverse tùy Saga. | Full -> không chuyển; finalize -> Available; reverse -> seller Locked; duplicate không nhân item. | TransferItemCommand, FinalizeItemTransferCommand, ReverseItemTransferCommand; ItemTransferredEvent / ItemTransferFailedEvent, ItemTransferFinalizedEvent, ItemTransferReversedEvent; INV-I06 |

## 5. Marketplace Command — Marketplace.API (5)

| FR | Actor | Yêu cầu chức năng | Điều kiện chấp nhận | Contract |
|---|---|---|---|---|
| FR-MKT-01 | Marketplace | Kiểm giá đơn vị theo price policy/version từ Redis (cache miss gọi fallback Admin nội bộ). | Ngoài band ->422; thiếu policy hoặc Admin lỗi ->503, không dùng mặc định mù. | MKT-01; ADM-I01 |
| FR-MKT-02 | Seller | Tạo listing với item/số lượng/giá; PendingLock -> Active sau lock; bồi hoàn hoặc phục hồi create lỗi. | 201 Active hoặc 202 PendingLock; duplicate không sinh tin; không publish Active trước lock. | MKT-01,05; INV-I02..I05; ListingCreatedEvent |
| FR-MKT-03 | Seller | Hủy tin của mình khi Active; atomic transition + unlock command/projection. | Processing ->409; replay Cancelled trả kết quả; item mở khóa eventual. | MKT-02; UnlockItemCommand, ListingCancelledEvent, ItemUnlockedEvent |
| FR-MKT-04 | Buyer | Direct purchase toàn listing; kiểm quyền/self-purchase; Redis lock và SQL conditional claim. | 202 sau khi durable accept; cùng listing chỉ một claim Active; key replay trả trade cũ. | MKT-03; ListingProcessingEvent |
| FR-MKT-05 | Saga Orchestrator | Điều phối hold/transfer/settle/finalize; compensation trước settlement khi cần; trạng thái trace và retry. | Completed chỉ sau finalize; Failed chỉ sau bồi hoàn; timeout chưa rõ không refund mù; listing Active/Sold đúng kết quả. | MKT-03,08,I01; Saga commands/events; ListingReactivatedEvent, TradeCompletedEvent, TradeFailedEvent |

## 6. Marketplace Query — Marketplace.API (4)

| FR | Actor | Yêu cầu chức năng | Điều kiện chấp nhận | Contract |
|---|---|---|---|---|
| FR-QRY-01 | CQRS Sync Worker | Project versioned listing events sang MongoDB; ẩn non-Active, giữ tombstone/version. | Duplicate/out-of-order không resurrect Sold/Cancelled; eventually sync. | ListingCreated/Processing/Cancelled/ReactivatedEvent, TradeCompletedEvent |
| FR-QRY-02 | Buyer, Seller | Lướt chợ, tìm tên, filter category/rarity/price, sort, pagination; xem chi tiết Active. | API list đọc Mongo; detail từ SQL; stable ordering; validation query; người ngoài không xem non-Active. | MKT-04,05 |
| FR-QRY-03 | Seller | Xem danh sách tin cá nhân, mặc định Active, filter theo trạng thái. | Chỉ dữ liệu seller, SQL authoritative; poll PendingLock/Processing theo detail. | MKT-06,05 |
| FR-QRY-04 | Player | Lịch sử trade cá nhân theo buy/sell và trạng thái, phí và thực nhận; số dư biến động từ Wallet riêng. | Chỉ participant truy cập trade; SQL; không join WalletDb. | MKT-07,08; WAL-02 |

## 7. GM & Analytics — Admin.API (4)

| FR | Actor | Yêu cầu chức năng | Điều kiện chấp nhận | Contract |
|---|---|---|---|---|
| FR-GM-01 | Analytics Consumer | Nhận TradeCompleted, lưu fact dedup TradeId, cập nhật Redis volume, gross, fees, min/max, reference. | Mỗi trade tính đúng một lần; dữ liệu late nằm đúng ngày, có thể rebuild. | TradeCompletedEvent |
| FR-GM-02 | Market Analytics Job | 00:00 Asia/Ho_Chi_Minh upsert báo cáo ngày trước vào SQL, giữ phân vùng ngày. | Chạy lại không nhân thống kê; không reset dữ liệu trade đến muộn gây mất số liệu. | Daily Summary Job |
| FR-GM-03 | GM | Xem thống kê current/history và price policy; lấy tổng supply/active listing từ service nguồn. | GM-only; thể hiện volume, fee, giá, supply/stock riêng biệt; nêu rõ nguồn thiếu thay vì bịa 0. | ADM-01..03; WAL-I02; MKT-I03 |
| FR-GM-04 | GM | Tạo/cập nhật BasePrice và band theo template; kiểm version, persist SQL, cập nhật Redis versioned. | GM-only; valid policy; stale write reject; audit và khôi phục cache. | ADM-03,04 |

## 8. Customer Support — Admin.API (2)

| FR | Actor | Yêu cầu chức năng | Điều kiện chấp nhận | Contract |
|---|---|---|---|---|
| FR-CS-01 | CS | Tìm trade theo ID/user, xem listing, saga steps, hold/ledger, inventory transfer qua internal API. | CS-only; nguồn thiếu/error được biểu thị rõ; không cross-DB join. | ADM-05,06; MKT-I01,I02; WAL-I01; INV-I06 |
| FR-CS-02 | CS | Đặt Active/TradeRestricted/Banned cho Player, lý do và audit; Auth thực thi. | CS-only, không sửa GM/CS hay role; replay idempotent; ban check hiệu lực với request mới. | ADM-07; AUTH-I01,I02 |

## 9. Coverage summary

| Module | FR | Count |
|---|---|---:|
| Auth | AUTH-01,02,04 | 3 |
| Wallet | WAL-01..06 | 6 |
| Inventory | INV-01..05 | 5 |
| Marketplace Command | MKT-01..05 | 5 |
| Marketplace Query | QRY-01..04 | 4 |
| GM | GM-01..04 | 4 |
| CS | CS-01..02 | 2 |
| Total | | 29 |

Removed IDs: FR-AUTH-03 (OTP), FR-GM-05/06 (market intervention). Ticket/automated anti-fraud are also excluded; do not renumber retained IDs. Google, ZaloPay and finalize/reverse are incorporated into existing requirements. Technical endpoint /health and /metrics are NFR, not extra business FR.
