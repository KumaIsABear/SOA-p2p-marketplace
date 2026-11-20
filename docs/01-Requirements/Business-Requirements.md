# Business Requirements — In-game P2P Marketplace

Version: 1.0.0 | Frozen baseline

## 1. Economic model

Gold là tiền kiếm trong gameplay và chỉ là bối cảnh; không lưu thông trên Marketplace. Gem là đơn vị giao dịch duy nhất: được phát hành khi đơn nạp Sandbox được xác nhận, hoặc nhận lại từ việc bán vật phẩm (chuyển từ ví khác, không phát hành thêm). Vật phẩm được cấp bởi seeder để mô phỏng nguồn cung game. Không thanh toán thật/cash-out.

Currency Sink: phí trừ từ số Gem trả cho seller, không cộng vào ví người chơi khác. Cơ chế góp phần điều tiết nguồn cung Gem; không bảo đảm loại bỏ lạm phát hoặc gian lận. Chỉ số volume không đồng nghĩa lượng tiền lưu thông.

## 2. Actors

| Actor | Nghiệp vụ |
|---|---|
| Player | Đăng nhập, xem ví/kho, lịch sử; role Player |
| Buyer (kế thừa Player) | Nạp Gem, lướt chợ, mua, theo dõi trade |
| Seller (kế thừa Player) | Đăng, hủy, xem listing cá nhân, nhận Gem |
| Game Master (GM) | Thống kê, giá tham chiếu, cấu hình biên độ |
| Customer Support (CS) | Tra cứu giao dịch và hạn chế tài khoản |
| Google Identity | Xác thực danh tính ngoài, Auth kiểm chứng ID token |
| ZaloPay Sandbox | Tạo/truy vấn đơn và thông báo thanh toán |
| Core Game Server / Seeder | Cấp vật phẩm kiểm thử qua API nội bộ |
| Market Analytics Job / Timer | Kích hoạt báo cáo ngày; chỉ biểu diễn actor timer khi boundary là nghiệp vụ, job thực tế trong Admin.API |

Saga Orchestrator, CQRS Worker, Analytics Consumer, Gateway và cơ chế validation là thành phần nội bộ, không phải human actors hoặc external systems. Buyer/Seller không phải JWT role riêng. GM/CS seed; Google JIT tạo Player. Actor generalization chỉ kế thừa khả năng thực hiện Use Case, không chia sẻ dữ liệu giữa Player. API cá nhân lấy UserId từ JWT `sub` đã xác minh và kiểm ownership/participant phía service; không tin UserId do client tự truyền. GM/CS chỉ tra soát theo đúng phạm vi role, không có quyền xem toàn bộ kho cá nhân.

## 3. Business Rules

| ID | Rule | Đặc tả |
|---|---|---|
| BR-01 | Dual Currency | Chỉ Gem cho marketplace; Gold ngoài phạm vi hiện thực. |
| BR-02 | Transaction Tax | Phí sàn chỉ ghi nhận lúc tất toán, tiêu hủy khỏi lưu thông; không trừ phí hai lần. |
| BR-03 | Price Bounds | Giá từng đơn vị phải nằm trong khoảng giá sàn/trần tại lúc đăng bán. |
| BR-04 | Asset Locking | Vật phẩm được khóa theo ListingId, không sử dụng/bán lại khi Locked hoặc PendingSettlement; khóa Redis giảm tranh chấp, SQL bảo vệ trạng thái. |
| BR-05 | Saga Compensation | Lỗi xác định trước tất toán: bồi hoàn tương ứng; lỗi chưa rõ: retry/đối soát, không refund mù; đã tất toán: forward recovery. |
| BR-06 | Balance Integrity | Total >= Hold >= 0; Available = Total - Hold. Tạm giữ sau khi kiểm đủ Available. |
| BR-07 | Exclusive Purchase | Một listing tối đa một trade Completed; mua/hủy cạnh tranh phải chỉ một transition thành công. |
| BR-08 | Ownership Validation | Seller sở hữu item tradable đúng số lượng; mọi validation phía server. |
| BR-09 | Self-trading Restriction | Buyer và Seller khác nhau. |
| BR-10 | Account Restriction | Banned chặn login/refresh/protected routes; TradeRestricted chặn đăng/mua mới nhưng xem, hủy và nạp vẫn được; trade đã accept tiếp tục. |
| BR-11 | Reference Price | BasePrice > 0 fallback khi chưa có lịch sử; giá tham chiếu theo giao dịch hoàn tất; policy do Admin sở hữu. |
| BR-12 | Inventory Capacity | Buyer phải đủ slot; seller slot giữ cho khả năng reverse cho đến finalize. |
| BR-13 | Payment Integrity | Chỉ credit khi callback/query xác thực và khớp đơn, số tiền, provider IDs; một đơn nạp credit tối đa một lần. Redirect không phải bằng chứng. |
| BR-14 | Whole-listing Purchase | Mua toàn bộ item/stack của một listing, không partial; giá, số lượng, phí snapshot server-side. |
| BR-15 | Traceability | Giao dịch tài chính và thao tác GM/CS ghi vết; quyền xem theo ownership và role. |

BR-01–05 kế thừa Analyst; BR-06–12 tách rõ ràng từ ràng buộc đã có và thiết kế trước; BR-13–15 bổ sung để phản ánh API Contracts v0.1. Không phải các mã mới đã được giảng viên phê duyệt.

## 4. Proposed economic policies (confirm before freeze)

| Policy | Proposed baseline |
|---|---|
| Gem/VND and quantity | Số nguyên; Gem/VND Int64, quantity > 0; tính tiền server, kiểm overflow; gói nạp cấu hình server, chưa chốt tỷ lệ |
| Fee | 500 basis points = 5%; fee = floor(gross * feeBps / 10000), sellerNet = gross - fee; gross = unitPrice * quantity |
| Price band | 2000 bps = ±20%; min = max(1, ceil(reference*(10000-bandBps)/10000)); max = floor(reference*(10000+bandBps)/10000) |
| Reference | floor(sum grossGem / sum quantity), tối thiểu 1, theo ngày Asia/Ho_Chi_Minh; dùng ngày có trade gần nhất nếu ngày hiện tại chưa có, BasePrice nếu chưa từng giao dịch |
| Old listings | Policy mới không sửa giá listing cũ; snapshot khi đăng và tại trade |
| Inventory capacity | Hữu hạn, số slot cụ thể nhóm cần quyết định; seed quantity=1 để đơn giản nhưng API vẫn hỗ trợ toàn stack |
| Session / locking | Access 15 phút, refresh 7 ngày, Redis Lock 20 giây là đề xuất kỹ thuật; DB là guard cuối cùng |

Ví dụ mua giá 1000 Gem (quantity=1, fee 5%): buyer -1000, seller +950, supply -50. Khi hold, Total chưa đổi, Hold tăng 1000; khi release, Hold giảm 1000. Khi settle, buyer Total và Hold giảm cùng khoản hold, seller Total tăng 950; TaxBurn chỉ audit, không thêm debit.

## 5. Core business workflows

### WF-01 — Sign-in
Google ID token hoặc thông tin tài khoản seed -> Auth xác minh -> tài khoản nội bộ và role -> JWT/refresh token. Banned bị từ chối; token hết hạn không hủy các trade đã nhận.

### WF-02 — Gem top-up
Player chọn gói -> Wallet lưu Pending order -> ZaloPay Create Order -> thanh toán Sandbox -> callback MAC Key2 hoặc Query Order -> local transaction Paid + credit + ledger. Duplicate không cộng lại; lỗi không rõ giữ Pending để đối soát; không tin redirect. Xem trạng thái bằng TopUpId. Diagram 22A/22B.

### WF-03 — Create listing
Seller chọn item toàn stack và giá -> Marketplace kiểm quyền/giá -> persist PendingLock -> REST Inventory lock -> Active + event. Khi kết quả không rõ, phục hồi cùng listing; khi lỗi xác định, release lock trước khi kết thúc CreateFailed. Mongo projection chỉ hiển thị Active.

### WF-04 — Cancel listing
Seller yêu cầu hủy -> atomic Active -> Cancelled -> phát unlock command + projection event. Item được mở khóa eventual; nếu Processing thì từ chối. Không coi Cancelled là đã unlock tức thời.

### WF-05 — Direct purchase / Saga
Buyer gửi yêu cầu -> Redis lock + SQL claim Active -> Processing, persist Trade/Saga -> 202 + tradeId -> Wallet hold -> Inventory transfer pending settlement (giữ slot seller) -> Wallet settle -> Inventory finalize -> Marketplace Sold/Completed -> TradeCompletedEvent. Buyer poll trạng thái trade.

### WF-06 — Failure & compensation
Hold rejection -> listing Active, trade Failed. Transfer rejection -> release hold -> Active/Failed. Confirmed settlement rejection (không thể commit muộn) -> reverse item về seller Locked -> release hold -> Active/Failed. Unknown/timeout -> giữ trạng thái hiện tại, retry/trace/review. Sau settle chỉ forward recovery (finalize); không tuyên bố hoàn tiền nếu chưa có xác nhận. Trade Failed chỉ khi các bồi hoàn cần thiết đã hoàn tất.

### WF-07 — Catalog, history & analytics
Marketplace projection nhận listing snapshots versioned, chỉ hiển thị Active, không resurrect stale listing. Lịch sử/chi tiết theo SQL owner. Admin nhận TradeCompleted duy nhất theo TradeId, cập nhật giá và chỉ số từ Trade_Facts durable/Redis projection, job 00:00 upsert ngày trước; tính trade muộn theo ngày phù hợp. GM xem volume, fee burn, reference price, supply/active listings nếu lấy dữ liệu nguồn; đây là các chỉ báo, không tự chứng minh lạm phát.

### WF-08 — CS moderation & trace
CS tìm theo userId hoặc tradeId; Admin gọi internal API của Marketplace/Wallet/Inventory, không đọc chéo DB; trạng thái thiếu nguồn phải nêu rõ. CS hạn chế hoặc khóa Player với lý do/audit; Auth sở hữu trạng thái; hạn chế có hiệu lực với hành động tiếp theo theo BR-10.

## 6. Business acceptance scenarios (design-level)

| Scenario | Expected result |
|---|---|
| Valid top-up, callback gửi lại | 1 credit, 1 ledger effect, Paid |
| Seller đăng giá ngoài band | Từ chối, không có listing Active hoặc lock kéo dài |
| Buyer tự mua | Từ chối |
| Purchase success | 1 item Available tại buyer, tiền giảm/tăng đúng phí, listing Sold |
| Buyer kho đầy | Hold release, listing Active, item seller Locked |
| Concurrent purchase/cancel | Chỉ một claim hợp lệ; không double-spend/oversell |
| GM/CS trái quyền | Từ chối, không đổi dữ liệu |
| CS ban khi token cũ còn hiệu lực | Lệnh protected tiếp theo bị chặn |

Use case overview: `diagrams/01-use-case-overview.puml` (vẽ các khả năng bên ngoài; các bước internal xem Sequence Diagrams ở 02-Architecture). Đây là sơ đồ tổng quan, không chứa toàn bộ FR kỹ thuật.
