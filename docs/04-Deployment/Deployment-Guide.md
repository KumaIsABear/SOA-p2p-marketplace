# Deployment Guide

Version 0.1.0 | Planned — chưa triển khai thực tế

## 1. Mục tiêu và phạm vi

Triển khai toàn bộ hệ thống bằng Docker Compose trên một máy host. YARP là điểm vào duy nhất; 5 service có image/container độc lập, database sở hữu riêng. ngrok đưa Gateway ra Internet để trình diễn và nhận callback ZaloPay. Prometheus/Grafana chỉ dành cho nhóm trên local. Đây là môi trường development/demo, không phải production/HA. Không phát triển UI hoàn chỉnh, Kubernetes, replica cluster hoặc thanh toán thật.

## 2. Deployment topology

Tham chiếu Figure 26 và Deployment-Architecture.md. Luồng truy cập: Player/GM/CS → ngrok HTTPS (hoặc localhost) → YARP → service. Google/ZaloPay nằm ngoài Docker; Wallet gọi ZaloPay qua HTTPS, callback quay về tunnel. Auth gọi/kiểm chứng định danh Google. Mỗi API truy cập DB của mình. Marketplace, Wallet, Inventory và Admin sử dụng RabbitMQ theo Contracts. Marketplace đọc/ghi Mongo read model và đọc Redis; Admin ghi price policy/analytics vào Redis. Prometheus scrape các API + Gateway, Grafana query Prometheus.

| Container | Internal port | Host binding đề xuất | Dữ liệu |
|---|---:|---|---|
| gateway | 8080 | 127.0.0.1:8080 | config |
| auth-api | 8080 | none | AuthDb |
| wallet-api | 8080 | none | WalletDb |
| inventory-api | 8080 | none | InventoryDb |
| marketplace-api | 8080 | none | MarketplaceWriteDb, MarketplaceReadDb |
| admin-api | 8080 | none | AdminAnalyticsDb |
| sqlserver | 1433 | none | sqlserver-data |
| mongodb | 27017 | none | mongodb-data |
| rabbitmq | 5672; UI 15672 | 127.0.0.1:15672 (UI) | rabbitmq-data |
| redis | 6379 | none | cache/lock, optional redis-data |
| prometheus | 9090 | 127.0.0.1:9090 | prometheus-data |
| grafana | 3000 | 127.0.0.1:3000 | grafana-data |

12 containers thường trực, không tính ngrok host và job migration/seed one-shot. Network bridge `marketplace-network` có outbound Internet để gọi Google/ZaloPay (không đặt toàn bộ network `internal: true` nếu không bố trí egress khác). Không publish port DB/API nội bộ. Nếu cần local debug dùng override giới hạn loopback, không commit override chứa secrets. Swagger xem bằng local developer profile hoặc import YAML; không route Swagger/metrics qua public gateway. Nếu cần health Gateway từ host, dùng quản trị local riêng/exec, không thêm public health route.

## 3. Prerequisites

- Docker Desktop Linux containers/WSL2 (Windows) hoặc Docker Engine + Compose v2 (Linux), Git, .NET SDK khớp TargetFramework của repo; Postman/browser, ngrok agent.
- Host x64 phù hợp image SQL Server, Internet, đủ disk, port 8080/3000/9090/15672 rảnh. Đề xuất host 16GB RAM trở lên, cấp Docker khoảng 8–12GB nếu máy đủ tài nguyên, 20–30GB disk trống làm điểm xuất phát; đo bằng `docker stats`, không phải minimum đã benchmark. SQL Server cần được cấp bộ nhớ phù hợp; tránh ép limit quá thấp.
- Google web OAuth client ID + test users nếu app ở chế độ thử nghiệm; ZaloPay Sandbox app_id, Key1, Key2; ngrok authtoken. Không sử dụng credential production.
- Freeze Contracts và image tags chung trước khi build; chọn bản .NET được nhóm hỗ trợ, pin tag phù hợp SDK/runtime và các image hạ tầng thay vì `latest`; không tự chọn version thay source hiện tại.

Kiểm tra môi trường (PowerShell hoặc shell tương đương):

```powershell
docker version
docker compose version
dotnet --info
git --version
docker info
```

## 4. Repository và deliverables cần hiện thực

```text
Marketplace/
    docs/04-Deployment/
    src/Gateway/Dockerfile
    src/Auth.API/Dockerfile
    src/Wallet.API/Dockerfile
    src/Inventory.API/Dockerfile
    src/Marketplace.API/Dockerfile
    src/Admin.API/Dockerfile
    src/Marketplace.Contracts/
    infrastructure/
        sql/                  # bootstrap DB/users (proposed)
        prometheus/prometheus.yml
        grafana/provisioning/  # datasource/dashboard
        grafana/dashboards/
    scripts/                  # migration/seed/backup helpers (proposed)
    docker-compose.yml
    .dockerignore
    .env.example
    .gitignore
    Marketplace.sln
```

Tên thư mục khớp thiết kế, có thể thay đổi theo repo thực. Dockerfile multi-stage: SDK restore/build/publish, runtime ASP.NET image đúng major, chạy user không root khi hỗ trợ, nghe `ASPNETCORE_HTTP_PORTS=8080` hoặc `ASPNETCORE_URLS=http://+:8080`. Build context repo root để resolve shared contracts và ProjectReference; copy csproj phụ thuộc trước restore; loại bin/obj/.git/.env khỏi context, không copy secrets. Chốt entrypoint DLL theo assembly thật. Compose build 6 images với Dockerfile tương ứng; images hạ tầng pin phiên bản. Runtime connection strings inject qua environment/secrets. Không bake mật khẩu vào Dockerfile.

## 5. Dependency and startup design

| Service | Dependency để sẵn sàng |
|---|---|
| Auth | AuthDb; config JWT/Google client ID; Google network only for Google login |
| Wallet | WalletDb + RabbitMQ; ZaloPay config, provider failure chỉ ảnh hưởng top-up |
| Inventory | InventoryDb + RabbitMQ |
| Marketplace | MarketplaceWriteDb + MongoDB + Redis + RabbitMQ; Auth/Inventory internal calls khi thao tác |
| Admin | AdminAnalyticsDb + Redis + RabbitMQ; internal source APIs khi tra soát |
| Gateway | config routes/JWT; Auth status check và route downstream |
| Prometheus/Grafana | internal metrics endpoints / datasource |

1. Khởi động SQL Server, MongoDB, Redis, RabbitMQ; chờ healthcheck (không chỉ trạng thái running). SQL probe kết nối SELECT 1 bằng client có thật trong image/sidecar; Mongo `ping` với auth; Redis `PING` authenticated nếu có; RabbitMQ `rabbitmq-diagnostics -q ping` và kiểm listener khi phù hợp. Không assume curl/sqlcmd tồn tại mọi image.
2. Bootstrap 5 DB và service logins; chạy migrations cho từng DB đúng owner. Đề xuất migration job một lần có exit code 0, hoặc migration CLI theo service; không chạy đồng thời nhiều replicas auto-migrate. `depends_on: condition: service_healthy` cho dependency có healthcheck, `service_completed_successfully` nếu dùng one-shot job. Service vẫn có retry/backoff lúc dependency gián đoạn. Không tạo cyclic readiness (Admin ↔ Marketplace ↔ Auth/Gateway).
3. Khởi động APIs, declare durable queues, outbox dispatcher, projection, analytics consumer; initialize cache từ durable facts/price policies. Khi hoạt động chưa sẵn sàng, trả 503 theo Contracts, không fallback sang dữ liệu sai.
4. Khởi động Gateway và monitoring. Cấu hình tunnel sau khi xác minh router và auth local; outbound provider chỉ cần khi sử dụng tính năng tương ứng.

## 6. Migration & seed

| DB | Owner | Việc cần thiết |
|---|---|---|
| AuthDb | Auth | Users, identity, role, refresh; seed GM/CS và player test password hash nếu dùng |
| WalletDb | Wallet | Wallet, holds, ledger, top-up, unique constraints; package config; wallet lazy-init |
| InventoryDb | Inventory | template, inventory/slot, lock/transfer; seed template và kho mẫu qua API grant/seeder |
| MarketplaceWriteDb | Marketplace | listings/trades/saga/steps, operation guard/outbox |
| AdminAnalyticsDb | Admin | policies (base price), trade facts, summaries, moderation audit |
| MarketplaceReadDb | Marketplace | catalog indexes + version tombstone; tạo read model rỗng hoặc rebuild |

Order seed: account IDs → item templates → price policies → cache warmup → inventory grant → dùng sandbox/top-up test có chứng từ. Tạo buyer đủ capacity và buyer kho đầy, seller có item tradable; CS/GM không cấp qua đăng nhập Google. Seed idempotent (stable IDs, không cộng số dư mỗi lần boot); dữ liệu ví mẫu nếu dùng phải ghi ledger hoặc có cơ chế fixture tách biệt, không giả mạo payment Paid. Mỗi service dùng credential SQL riêng, migration credential tách runtime nếu khả thi; không dùng sa trong runtime. Không cross-DB FK/query. Persist Saga, outbox/inbox và payment unique keys như Contracts; tạo index trước demo. Xác nhận Admin price snapshot trong Redis trước create listing.

## 7. Start-up commands (sau khi repo có cấu hình thực tế)

Chạy ở root repository. Đây là lệnh dự kiến; không khẳng định hiện có các Dockerfile/compose.

```powershell
Copy-Item .env.example .env
# Sửa .env local, không commit; chuẩn bị credentials.
docker compose config --quiet
docker compose pull sqlserver mongodb rabbitmq redis prometheus grafana
docker compose up -d sqlserver mongodb rabbitmq redis
# Chạy bootstrap + migrate/seed theo scripts mà nhóm hiện thực.
docker compose up -d --build
docker compose ps
docker compose logs --tail=100 gateway auth-api marketplace-api
```

`docker compose config` có thể in secrets nếu không dùng --quiet; không chụp/share. Nếu chỉ có infra ở ngày 1–2, dùng lệnh up các service hạ tầng trên sau khi compose thực sự có các khai báo đó. `docker compose up -d --build` là đường chạy thống nhất sau khi migration được tự động hóa theo compose; nếu migration external, ghi đúng lệnh prerequisite vào guide thực tế. `docker compose ps` và internal probes phải xác minh ready, không chỉ running. `docker compose exec <service> ...` để kiểm tra bên trong bằng công cụ có sẵn. Xem Prometheus Targets và thử login qua gateway. Không gửi dữ liệu test hoặc traffic ngrok nếu allowlist chưa hoàn chỉnh.

## 8. Smoke verification & demo delivery

| Gate | Kết quả cần xác minh | Evidence dự kiến |
|---|---|---|
| D1 Infrastructure | 4 infra healthy, volume tồn tại | compose ps/logs |
| D2 Services | 5 API + gateway ready, migration/seed thành công | health result đã redacted, migration logs |
| D3 Local API | login + JWT, GET balance/inventory/catalog, 401/403 phù hợp | Postman/Swagger |
| D4 Distributed flow | message queues, successful trade, compensation, CQRS | trade trace và ledger trước/sau |
| D5 External | Google token verified; ZaloPay signed callback/query; Gem cộng đúng một lần | status Paid, ledger đã che thông tin |
| D6 Observability | 6 targets UP, request metrics có giá trị | Grafana/Prometheus |
| D7 Security | public /internal, /metrics, /swagger bị chặn; service ports không public | request output đã redacted |

Các gate là acceptance dự kiến, không phải kết quả. Chi tiết kế hoạch Postman/xUnit/load testing thuộc 05-Testing; code kiểm thử và kết quả thực thi chưa có. Nếu provider thiếu credentials, ghi rõ blocker và test local mock tách biệt; mock không được ghi là sandbox thành công.

## 9. Shutdown, update, backup and recovery

```powershell
docker compose logs --tail=100 wallet-api
docker compose stop
docker compose start
# sau khi chỉnh source: build lại service rồi recreate theo config
docker compose up -d --build wallet-api
# gỡ container/network nhưng giữ named volumes
docker compose down
```

Không dùng `down -v`, `volume prune`, purge queue để chữa lỗi hoặc tắt khi còn giao dịch đang xử lý nếu tránh được. Dừng nhận yêu cầu mới/tắt tunnel, chờ hoặc ghi nhận Pending trades/top-ups, kiểm outbox và queue, sau đó stop. Trước update: tag commit, lưu image tags/config schema, sao lưu từng SQL DB bằng SQL backup hoặc công cụ đã kiểm chứng, Mongo dump/restore nếu cần, lưu Grafana config và broker definitions; durable RabbitMQ messages không nằm trong bản export definitions, phải có kế hoạch lưu/khôi phục message volume phù hợp khi dừng broker. Không backup live DB bằng cách copy volume thô. Giữ backup cục bộ có bảo vệ; không commit secrets/dumps chứa dữ liệu cá nhân. Restore phải đối soát trade/hold/payment giữa các DB theo một checkpoint phối hợp; không restore riêng một DB rồi cho traffic nếu có Saga đang chạy. Mongo/Redis có thể rebuild từ authoritative state theo thiết kế; không reset wallet/ledger. Khi rollback code, kiểm schema backward compatibility; tránh downgrade DB tự động, giữ snapshot và kế hoạch sửa tiến. Recreate một API không được tạo double effect nhờ persistent data và idempotency đã hiện thực.

## 10. Deployment acceptance & handover

Một người phụ trách compose/YARP/monitoring, mỗi service owner chịu trách nhiệm Dockerfile, options, migration/seed, health/metrics; Wallet owner chịu callback và ZaloPay, Auth owner chịu Google. Cả nhóm review allowlist và chạy clean setup trước code freeze. Lưu trong README release: commit/tag, phiên bản thực tế, host OS, resource, thời điểm, URL demo (không secrets), trạng thái từng gate, lỗi còn tồn tại. Tắt tunnel sau demo. Không tuyên bố production-ready hoặc đã đạt SLA.
