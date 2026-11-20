# Non-Functional Requirements — In-game P2P Marketplace

Version: 1.0.0 | Frozen baseline

## 1. Measurement policy

Kế thừa mã NFR gốc trong Analyst, mở rộng theo `a(1).txt` và Contracts v0.1. Số liệu hiệu năng cũ (50–100ms, <5ms, 1–2s) là mục tiêu tham chiếu cho local demo, không phải SLA hoặc số đo thực tế. Các thay đổi của bản này ưu tiên khả năng kiểm chứng và không hứa loại bỏ tuyệt đối bottleneck/lỗi. Ghi lại hardware, Docker config, dataset, số client, warmup, sample size, latency, error rate và timestamps khi chạy; báo cáo cả những mục tiêu không đạt.

## 2. Performance & latency

| ID | Requirement | Verification / target |
|---|---|---|
| NFR-PERF-01 | Catalog API dùng MongoDB read model/index, không đọc transaction SQL để browse. | Benchmark local warmed dataset; mục tiêu mean <=100ms (50ms stretch); ghi p95, sample size, lỗi. |
| NFR-PERF-02 | Đọc price policy từ Redis thay vì tính lại từ lịch sử mỗi request. | Đo thao tác đọc cache riêng, mục tiêu mean <5ms trong môi trường local; fail closed nếu thiếu policy. |
| NFR-PERF-03 | Analytics cập nhật incremental từ events/facts, 00:00 upsert summary; không full scan giao dịch trong giờ cao điểm. | Kiểm code/query và chạy job lặp; kiểm số liệu đúng khi duplicate/late event. |

## 3. Concurrency

| ID | Requirement | Verification / target |
|---|---|---|
| NFR-CONC-01 | Tối đa một Completed trade cho một listing, số dư không âm. | 100 requests đồng thời từ buyer đủ tiền/kho (mục tiêu lab); một Completed khi hệ thống khỏe, còn lại conflict; kiểm DB/ledger. |
| NFR-CONC-02 | Redis lock giảm contention; SQL atomic claim là ràng buộc correctness kể cả TTL hết, không cam kết 99% mọi tải. | So sánh số request có lock/SQL claim; thử lock expiry và nhiều listings trên cùng ví; không double-spend. |
| NFR-CONC-03 | Cancel và purchase cạnh tranh dùng state guard. | Race cancel/purchase, kiểm chỉ một transition hợp lệ, không unlock khi Processing. |

## 4. Data consistency & integrity

| ID | Requirement | Verification / target |
|---|---|---|
| NFR-CONS-01 | Local ACID trong từng service; ví Total >= Hold >= 0, ownership duy nhất. | Kiểm transaction và constraints; đối soát số dư/ledger/ownership sau test. |
| NFR-CONS-02 | Saga eventual consistency; compensation khi lỗi xác định trước settle; retry/review nếu unknown; forward recovery sau settle. | Kho đầy -> release + Active; reject settle -> reverse/release; mô phỏng timeout/finalize failure; không ghi terminal sai. |
| NFR-CONS-03 | CQRS sync eventual, không hiển thị bản ghi cũ vĩnh viễn. | Đo thời gian SQL commit đến Mongo projection, mục tiêu 1–2s khi healthy. |
| NFR-CONS-04 | At-least-once + idempotent consumer; HTTP replay không tạo effect thứ hai. | Repeat callbacks/messages/HTTP keys, kiểm count và aggregate; inject out-of-order event. |

## 5. Reliability & fault tolerance

| ID | Requirement | Verification / target |
|---|---|---|
| NFR-REL-01 | Durable RabbitMQ queues/messages + persistent volume; transactional outbox cho state+message, inbox/operation guard cho consumer. | Dừng consumer, gửi message, restart và kiểm xử lý; kiểm outbox dispatch khi broker gián đoạn. Không bảo đảm zero loss mọi sự cố. |
| NFR-REL-02 | Retry có giới hạn cho transient faults; error queue/requiresReview và trace khi hết retry. | Inject transient/permanent lỗi; kiểm log, queue, trạng thái và redrive an toàn; timeout không đồng nghĩa business reject. |
| NFR-REL-03 | Redis lock TTL đề xuất 20s (10–30s trong file gốc), compare-delete theo token; SQL không phụ thuộc TTL để giữ correctness. | Giả lập hết TTL/worker restart; recover state, lock TTL không tự biến Processing thành Active. |

## 6. Security & economic safety

| ID | Requirement | Verification / target |
|---|---|---|
| NFR-SEC-01 | Gateway và services JWT/RBAC/ownership/status check; ngoại lệ anonymous login/refresh/logout và signed callback; internal credential scoped. | 401/403/404 theo Contracts; test JWT sai/hết hạn, ban với token cũ, truy cập /internal bị chặn. |
| NFR-SEC-02 | Validate giá, quyền sở hữu, số dư, amount server-side; Google ID token verified, callback MAC Key2 và đối soát. | Tamper payload, sai amount/signature, self-trade, giá ngoài band; không side effect sai. |
| NFR-SEC-03 | Ledger/audit ghi vết append-only ở tầng ứng dụng, correlation ID, tránh log secrets. | Tra theo TradeId/user/operation, kiểm GM/CS audit và log redaction; không tuyên bố chống sửa DB cấp admin. |

## 7. Scalability & deployment

| ID | Requirement | Verification / target |
|---|---|---|
| NFR-SCAL-01 | Service boundaries có thể triển khai/scale riêng; CQRS đọc/ghi tách logic nhưng chung Marketplace.API trong MVP. | Kiểm isolation và API ownership; không yêu cầu scale Query riêng hoặc multi-node HA trong MVP. |
| NFR-SCAL-02 | 5 service + Gateway Dockerfile, 4 infra containers, monitoring; Docker Compose khởi chạy trên môi trường demo; ngrok chỉ public Gateway allowlisted routes. | Clean setup theo guide, health/live & ready, thử Google/ZaloPay callback; không expose DB/broker/internal ports Internet. |

## 8. Observability, interoperability & testing

| ID | Requirement | Verification / target |
|---|---|---|
| NFR-OBS-01 | Prometheus scrape /metrics của 5 services + Gateway; Grafana dashboard theo dõi request, error, latency, readiness/up; correlate log theo trade/operation. | Tạo traffic, kiểm scrape/dashboard có số liệu; /metrics không public Internet. |
| NFR-INT-01 | REST contract OpenAPI v1, message contract cùng version; provider adapters Google/ZaloPay; không đọc DB chéo. | Kiểm schema/status/route và producer-consumer; sandbox end-to-end; không tự nhận mock là tích hợp ngoài. |
| NFR-TEST-01 | Postman phủ public/internal nghiệp vụ theo quyền, e2e và negative cases; tối thiểu 3–5 unit tests xUnit cho price/fee, hold/settle/release và idempotency/projection theo năng lực. | Lưu collection, test output, evidence race/recovery; đối chiếu T01..T11 trong Contracts. |

NFR-INT-01, NFR-TEST-01 là mã bổ sung minh bạch để truy vết tích hợp/kiểm thử, NFR-OBS-01 lấy từ `a(1).txt`. Không phát sinh cam kết production. Hướng dẫn triển khai và kế hoạch testing nằm tại docs/04-Deployment và docs/05-Testing; kết quả thực thi vẫn chưa có.
