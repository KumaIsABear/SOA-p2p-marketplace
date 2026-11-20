# Project Scope — In-game P2P Marketplace

version: 1.0.0 | Status: Proposed baseline — group review / freeze

## 1. Project overview

**Tên đề tài:** In-game P2P Marketplace and Economy Management System Using Microservices Architecture.

Hệ thống backend chợ giao dịch vật phẩm cho game Live-Service, cho phép người chơi mua bán trực tiếp bằng Gem; hỗ trợ quản lý ví, quyền sở hữu vật phẩm, điều tiết giá và tra soát giao dịch. Không xây dựng game hoàn chỉnh. Đây là đồ án Kiến trúc hướng dịch vụ (SOA), tập trung vào phối hợp dịch vụ, giao dịch phân tán, xử lý đồng thời và tích hợp dịch vụ ngoài.

## 2. Problem statement & objectives

| Bài toán | Mục tiêu |
|---|---|
| Nguồn cung Gem tăng, giá vật phẩm biến động | Mô phỏng Currency Sink qua phí sàn và kiểm soát biên độ giá |
| Nhiều buyer mua cùng listing | Không bán trùng, không chi tiêu kép |
| Wallet, Inventory và Marketplace sở hữu dữ liệu riêng | Giao dịch có thể hoàn tất hoặc bồi hoàn an toàn; lưu vết lỗi chưa giải quyết |
| Luồng đọc tải lớn hơn luồng ghi | Tách command/query, kiểm tra độ trễ đồng bộ |
| Tích hợp hệ thống bên ngoài | Google Sign-In và ZaloPay Sandbox; xác thực, xử lý lặp và đối soát |

## 3. Phạm vi MVP

| Business module | Phạm vi hiện thực | Service |
|---|---|---|
| Identity | Đăng nhập Google, đăng nhập tài khoản seed, JWT/refresh/logout, RBAC, account status | Auth.API |
| Wallet & Payment | Số dư Gem, ledger, gói nạp, ZaloPay Sandbox, callback/query, hold/settle/release | Wallet.API |
| Inventory | Xem kho, cấp vật phẩm bằng seeder, lock/unlock, transfer/finalize/reverse | Inventory.API |
| Marketplace Command | Kiểm tra giá, đăng/hủy bán, mua trực tiếp, Saga, chống tranh chấp | Marketplace.API |
| Marketplace Query | Catalog, tìm/lọc/sắp xếp/phân trang, tin của mình, lịch sử và trạng thái trade | Marketplace.API |
| GM & Analytics | Giá gốc/giá tham chiếu, biên độ, thống kê giao dịch, báo cáo ngày | Admin.API |
| Customer Support | Tra cứu theo người chơi/trade, xem trace, khóa/hạn chế tài khoản | Admin.API |

Cross-cutting: YARP API Gateway; RabbitMQ/MassTransit; Redis; SQL Server và MongoDB; Docker Compose; ngrok; Prometheus/Grafana; OpenAPI/Swagger, Postman và xUnit. 7 business modules, 6 bounded contexts theo thiết kế DDD hiện tại, 5 microservices + 1 gateway. Một SQL Server instance chứa 5 DB logic; Marketplace sở hữu thêm MongoDB read model. Không có Payment.API riêng.

## 4. Out of scope

- Game client, gameplay Visual Novel, cơ chế drop thực tế, hệ thống Gold hoạt động, NPC shop, crafting/equipment.
- Frontend Web/Mobile hoàn chỉnh (có thể dùng trang tối thiểu hỗ trợ Google Sign-In và mở URL thanh toán); demo qua Swagger, Postman, trình duyệt, RabbitMQ UI, Grafana.
- OTP (FR-AUTH-03), Ticket CS, tự động phát hiện RMT, GM bơm hàng/thu mua (FR-GM-05/06).
- Đấu giá, Buy Order, đặt lệnh chờ, mua một phần listing; Trader không là role/actor riêng.
- VNPAY hoặc nhà cung cấp khác; thanh toán production, tiền thật, cash-out hoặc quy đổi Gem thành tiền thật.
- HA cluster/multi-region, cam kết SLA production, phân tách Marketplace Query thành service vật lý riêng.

## 5. Stakeholders, assumptions & constraints

Player (Buyer/Seller), Game Master, Customer Support, nhóm phát triển, giảng viên. Google Identity, ZaloPay Sandbox và Core Game Server/Seeder là external systems; timer 00:00 là tác nhân thời gian logic nếu được mô hình hóa ngoài boundary nghiệp vụ.

Nhóm 3 người, cửa sổ hiện thực dự kiến 11–12 ngày; ưu tiên đường đi end-to-end, code freeze trước báo cáo. Kế hoạch đăng ký 12 tuần là kế hoạch danh nghĩa, không đồng nghĩa 12 tuần còn lại. Có internet, Google OAuth client và ZaloPay Sandbox credentials hợp lệ; nếu chưa có thì ghi nhận blocker, mock nội bộ chỉ dùng để test chứ không chứng minh tích hợp bên thứ ba. Dữ liệu người chơi, GM/CS, item template, gói nạp và kho đồ được seed hoặc tạo bởi các chức năng đã đặc tả. Giá trị mặc định ở Business-Requirements cần được nhóm duyệt.

## 6. Expected deliverables / acceptance at scope level

- Requirements baseline, kiến trúc/DDD/ERD/sequence diagrams, REST OpenAPI và message contracts.
- 5 service và gateway chạy qua Docker Compose, có phân quyền, sở hữu DB riêng.
- Demo Google Sign-In, ZaloPay Sandbox hợp lệ, direct purchase, concurrent purchase, compensation khi kho đầy, CQRS, GM/CS và dashboard monitoring.
- Bằng chứng kiểm thử API, tối thiểu 3–5 unit tests cốt lõi, log/metrics và báo cáo kết quả thực tế. Không tuyên bố đã đạt hiệu năng hoặc điểm rubric khi chưa chạy/được chấm.

## 7. References

`a(1).txt` (ưu tiên phạm vi); `SOA - Analyst(1).md`; `SOA - Live-Service Story-telling Visual Novel Game System _ Game Item Marketplace(1).md`; quyết định mới ZaloPay + Google; `../02-Architecture/`; `../03-Contracts/API-Contracts.txt` và `marketplace-openapi.yaml` (baseline v0.1).
