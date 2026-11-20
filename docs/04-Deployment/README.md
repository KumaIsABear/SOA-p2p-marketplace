# 04 — Deployment Documentation

**In-game P2P Marketplace | v0.1.0 — Proposed deployment baseline**

Bộ tài liệu mô tả cách triển khai MVP 5 Microservices + YARP, SQL Server (5 DB logic), MongoDB, RabbitMQ/MassTransit, Redis, Prometheus, Grafana, ngrok, Google Identity và ZaloPay Sandbox trên một máy host. Đây là tài liệu chuẩn bị triển khai, không phải báo cáo hệ thống đã chạy. Không tạo source code, Dockerfile, compose hay test project trong bộ này; ví dụ cấu hình bên trong Markdown cần được hiện thực cùng source.

| File | Nội dung |
|---|---|
| `Deployment-Guide.md` | Topology, prerequisites, Docker Compose, migration/seed, bring-up, shutdown, backup, release gates |
| `Environment-Configuration.md` | Port/DNS, biến môi trường, quyền, secrets, queue và cấu hình từng service |
| `External-Integration-Guide.md` | ngrok, Google Sign-In và ZaloPay Sandbox, callback, đối soát |
| `Monitoring-Guide.md` | Health checks, Prometheus, Grafana, logs và chỉ số nghiệp vụ |
| `Troubleshooting.md` | Chẩn đoán, khôi phục và xử lý lỗi an toàn |

## Baseline & traceability

Đọc cùng `../01-Requirements/Project-Scope.md`, `Non-Functional-Requirements.md` trong thư mục Requirements, `../02-Architecture/Deployment-Architecture.md`, Figure 26 (bản đã chốt ZaloPay), Figure 22A/22B và `../03-Contracts/API-Contracts.txt` + `marketplace-openapi.yaml`. Ưu tiên Contracts v0.1 cho endpoint /api/v1 và /internal/v1. `a(1).txt` quy định Docker Compose, ngrok, monitoring và scope 11–12 ngày. NFR-SCAL-02, NFR-OBS-01, NFR-SEC-01..03, NFR-INT-01 và NFR-REL-01..03 là các yêu cầu trực tiếp liên quan.

## Trình tự sử dụng

1. Chốt tên project/assembly, .NET target framework, image tags, cấu hình và quyền thực tế với cả nhóm.
2. Dựng infra, tạo DB và migrations/seed, sau đó chạy 5 API + Gateway.
3. Kiểm tra local health, routing và core workflow trước khi mở tunnel.
4. Cấu hình Google/ZaloPay và dashboard, kiểm tra public allowlist.
5. Ghi evidence thực tế khi triển khai; ghi kết quả vào Test-Execution-Register.csv và Test-Report.md trong 05-Testing.

Giả định mới: ngrok chạy trên host, các API nghe HTTP :8080 trong Docker, các công cụ quản trị bind loopback và không có route public. Mọi tên env, tên volume bổ sung và ví dụ config trong bộ này là đề xuất cần khớp source; các tên service, DB và queue kế thừa Contracts. Không cam kết SLA, production HA hay khả năng chạy được khi chưa có repo/credentials. Không yêu cầu thêm diagram vì Figure 26 đã mô tả topology; nếu cấu hình vật lý thay đổi thì cập nhật Figure 26.

## Tài liệu kỹ thuật chính thức

- Docker Compose: https://docs.docker.com/compose/how-tos/startup-order/ ; https://docs.docker.com/compose/how-tos/networking/
- ngrok: https://ngrok.com/docs/start
- Google Identity: https://developers.google.com/identity/gsi/web/guides/get-google-api-clientid ; https://developers.google.com/identity/gsi/web/guides/verify-google-id-token
- ZaloPay v2: https://developers.zalopay.vn/v2/general/overview.html ; https://developers.zalopay.vn/v2/docs/gateway/guide.html
- Prometheus: https://prometheus.io/docs/prometheus/latest/configuration/configuration/

Đối chiếu tài liệu công khai trên ngày 19/11/2026; không kiểm thử live với tài khoản hoặc Docker host của nhóm.
