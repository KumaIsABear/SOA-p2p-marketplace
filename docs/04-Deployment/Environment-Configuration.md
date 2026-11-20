# Environment Configuration

Version 0.1.0 | Proposed — key names require implementation alignment

## 1. Environment model

Development: chạy local hoặc Docker, Swagger/Postman, fixture; Sandbox Demo: Docker + ngrok + Google/ZaloPay sandbox, không tiền thật; Production: ngoài phạm vi. Sử dụng `ASPNETCORE_ENVIRONMENT=Development` chỉ local; demo nên cấu hình môi trường Production hoặc Demo riêng với safe error handling, không bật Developer Exception Page trên ngrok. Environment variables ASP.NET Core dùng `__` để map nested keys. `.env` cung cấp giá trị để Compose nội suy; Compose phải truyền từng biến bằng `environment` hoặc secret mount (không ngầm định mọi biến được truyền). Không đưa toàn bộ `.env` vào tất cả containers.

## 2. Network & service DNS

| Target | Internal address | Host access |
|---|---|---|
| Gateway | http://gateway:8080 | http://localhost:8080 |
| Auth | http://auth-api:8080 | private |
| Wallet | http://wallet-api:8080 | private |
| Inventory | http://inventory-api:8080 | private |
| Marketplace | http://marketplace-api:8080 | private |
| Admin | http://admin-api:8080 | private |
| SQL | sqlserver:1433 | private |
| Mongo | mongodb:27017 | private |
| RabbitMQ | rabbitmq:5672 | UI localhost:15672 |
| Redis | redis:6379 | private |
| Prometheus | http://prometheus:9090 | localhost:9090 |
| Grafana | http://grafana:3000 | localhost:3000 |

Inside container `localhost` chỉ chính container, không phải máy host/service khác. `marketplace-network` bridge, gateway publish loopback 8080. Local UI ports bind 127.0.0.1. ngrok chạy host; không expose broker/UI/metrics qua Gateway. Không hardcode container IP.

## 3. Proposed configuration dictionary

Các key dưới đây là đề xuất để owner bind `IOptions<T>`; không phải tên đã tồn tại trong source. Định nghĩa `appsettings.json` chỉ chứa giá trị không nhạy cảm và có validation on startup. Các bí mật từ ignored .env/secret store.

| Key (application env) | Service | Value/meaning |
|---|---|---|
| ASPNETCORE_ENVIRONMENT | all | Demo/Production theo config team |
| ASPNETCORE_URLS | all | http://+:8080 |
| ConnectionStrings__DefaultConnection | 5 APIs | SQL connection theo DB owner, runtime login riêng |
| Jwt__Issuer | Gateway + 5 APIs | same agreed issuer |
| Jwt__Audience | Gateway + 5 APIs | same agreed audience |
| Jwt__SigningKey | Auth + validator services nếu HMAC | secret >=256 bits entropy; cùng key, không dùng cho internal auth |
| Jwt__AccessTokenMinutes | Auth | 15 |
| Jwt__RefreshTokenDays | Auth | 7 |
| Google__ClientId | Auth | web OAuth client ID; frontend helper sử dụng cùng ID |
| RabbitMq__Host | Wallet, Inventory, Marketplace, Admin | rabbitmq |
| RabbitMq__Port | same | 5672 |
| RabbitMq__VirtualHost | same | marketplace (create explicitly) |
| RabbitMq__Username / __Password | same | per-service broker user (permissions scoped) |
| Redis__ConnectionString | Marketplace, Admin | redis:6379,password=... |
| Redis__LockTtlSeconds | Marketplace | 20 |
| MongoDb__ConnectionString | Marketplace | mongodb://...@mongodb:27017/... |
| MongoDb__Database | Marketplace | MarketplaceReadDb |
| Services__Auth__BaseUrl | Gateway, Marketplace, Admin | http://auth-api:8080 |
| Services__Wallet__BaseUrl | Gateway, Marketplace, Admin | http://wallet-api:8080 |
| Services__Inventory__BaseUrl | Gateway, Marketplace, Admin | http://inventory-api:8080 |
| Services__Marketplace__BaseUrl | Gateway, Admin | http://marketplace-api:8080 |
| Services__Admin__BaseUrl | Gateway, Marketplace | http://admin-api:8080 |
| ServiceAuth__OutgoingKey | Gateway, Marketplace, Admin, Seeder | distinct per caller |
| ServiceAuth__AllowedCallers__{caller}__Key | receiver | key matching caller, with method/path scope configured separately |
| ZaloPay__BaseUrl | Wallet | https://sb-openapi.zalopay.vn |
| ZaloPay__AppId | Wallet | Sandbox app ID |
| ZaloPay__Key1 / __Key2 | Wallet | Sandbox secrets; never client |
| ZaloPay__CallbackUrl | Wallet | https://<actual-domain>/api/v1/wallet/zalopay/callback |
| ZaloPay__RedirectUrl | Wallet (optional) | local/demo status helper if implemented, no payment authority |
| ZaloPay__ReconcileIntervalSeconds | Wallet | proposed 60; tune provider limits |
| TopUpPackages__... | Wallet | server-owned package ID, amountVnd, gemAmount; agree catalog |
| Economy__FeeBps | Marketplace | 500 proposed; snapshot at trade creation per contract |
| Economy__DefaultBandBps | Admin | 2000 proposed |
| Analytics__TimeZoneId | Admin | Asia/Ho_Chi_Minh |
| Analytics__DailySummaryTime | Admin | 00:00 local business time |
| Cors__AllowedOrigins__0 | Gateway/Auth helper route | exact local helper origin, e.g. http://localhost:5500 |
| Logging__LogLevel__Default | all | Information (Debug only local and redacted) |

JWT HMAC approach is proposed for MVP; all shared secret holders can sign tokens, so restricted container trust is required. If using asymmetric signing instead, distribute only public verification key to validators; no need to share private signing key. Group must choose before code freeze. Claims `sub`, role `Player|GM|CS`, iss/aud/exp/iat/jti per Contracts. Use Auth status check, not JWT alone for moderation; fail closed.

SQL string example for demo inside network (placeholders, not credentials): `Server=sqlserver,1433;Database=WalletDb;User Id=wallet_runtime;Password=<secret>;Encrypt=True;TrustServerCertificate=True;` last option only for isolated local demo when cert not trusted, not production. SQL bootstrap admin not distributed to app. Mongo service user restricted to MarketplaceReadDb; set authSource according to user creation DB. Redis password and private network, use key namespaces; Admin writes `price:policy:{templateId}` Marketplace reads. Redis TTL lock does not replace SQL guard; never flush while live. Timestamp UTC for data; convert business-day / app_trans_id prefix using Asia/Ho_Chi_Minh. Check runtime tzdata or explicit timezone support.

## 4. .env.example illustration (Compose-level placeholders)

Not a runnable .env file; keys and mapping to containers must be written by team. Empty placeholders should fail startup or validation, never default to weak passwords. Each receiver maps incoming allowed caller credentials and explicit scopes.

```dotenv
COMPOSE_PROJECT_NAME=marketplace
GATEWAY_PORT=8080
SQL_SA_PASSWORD=
AUTH_DB_PASSWORD=
WALLET_DB_PASSWORD=
INVENTORY_DB_PASSWORD=
MARKETPLACE_DB_PASSWORD=
ADMIN_DB_PASSWORD=
MONGO_ROOT_PASSWORD=
MONGO_MARKETPLACE_PASSWORD=
REDIS_PASSWORD=
RABBITMQ_BOOTSTRAP_PASSWORD=
RABBITMQ_WALLET_PASSWORD=
RABBITMQ_INVENTORY_PASSWORD=
RABBITMQ_MARKETPLACE_PASSWORD=
RABBITMQ_ADMIN_PASSWORD=
JWT_SIGNING_KEY=
SERVICE_KEY_GATEWAY=
SERVICE_KEY_MARKETPLACE=
SERVICE_KEY_ADMIN=
SERVICE_KEY_SEEDER=
GOOGLE_CLIENT_ID=
ZALOPAY_APP_ID=
ZALOPAY_KEY1=
ZALOPAY_KEY2=
ZALOPAY_CALLBACK_URL=
GRAFANA_ADMIN_PASSWORD=
```

ngrok host token managed by its agent config, not passed to business services. Compose maps e.g. SQL_SA_PASSWORD to `MSSQL_SA_PASSWORD` for SQL container only; `ACCEPT_EULA=Y` and Developer edition for non-production if appropriate. Mongo root/bootstrap account only for initialization, not Marketplace runtime. RabbitMQ vhost/user bootstrap with scoped permissions; don't use default guest remote. Generate secrets using secure RNG, avoid accidental special character interpolation, use correct escaping/secret file; protect and rotate as necessary. `.gitignore` includes `.env`, secret files, cert/private keys, user-secrets exports, backups; `.env.example` placeholders only. Credentials must not appear in logs, YAML, screenshots, OpenAPI examples, shell history or commits.

## 5. Gateway route policy

Exact method/path operations from `../03-Contracts/marketplace-openapi.yaml`/text (27 public operations, 14 internal; technical endpoints separate). Route unchanged path to service DNS. Deny default unmatched; no catch-all internal forwarding.

| Prefix | Destination | Access |
|---|---|---|
| /api/v1/auth | auth-api | POST login/google/refresh/logout as contract; GET me JWT |
| /api/v1/wallet | wallet-api | listed Player JWT endpoints; exact POST zalopay/callback provider MAC, no JWT |
| /api/v1/inventory/items | inventory-api | listed GET Player JWT |
| /api/v1/marketplace | marketplace-api | listed Player JWT operations; apply TradeRestricted rules |
| /api/v1/admin | admin-api | exact GM/CS policies per contract |
| /internal/v1/* | none | never public |
| /metrics, /health/*, /swagger, broker/UI ports | none via tunnel | private/local only |

Gateway must strip client-supplied internal headers (`X-Service-Key`, fake user/role etc.), propagate validated JWT and correlation, internal calls direct service DNS with scoped key. Validate JWT and account status at entry; services validate again. Rate-limit appropriate public player endpoints; callback handled independently, validate MAC. Trust forwarded headers only from known proxy path/config, use explicit HTTPS callback URL rather than arbitrary Host; if local Swagger needed expose separate loopback-only debug surface not forwarded by tunnel. Public allowlist must be enforced on Gateway even if ngrok agent runs locally.

## 6. Messaging and storage

| Queue (from Contracts) | Owner |
|---|---|
| wallet-commands | Wallet |
| inventory-commands | Inventory |
| marketplace-saga | Marketplace |
| marketplace-projection | Marketplace |
| admin-analytics | Admin |

Pin shared `Marketplace.Contracts.V1` version; configure MassTransit consume/publish topology and ACLs for required exchanges, no manual conflicting queue declaration. Durable queues/persistent messages, persistent broker volume. Restricted error queues for triage. SQL unique operation keys/outbox and inbox, retries with bounded backoff (team decides values) and state review. No secrets/JWT in messages. Named volumes: sqlserver-data, mongodb-data, rabbitmq-data, grafana-data; prometheus-data recommended; redis-data optional and never authority for money. SQL connection credentials separated; one instance five DBs as architecture. Configure image and library versions consistently; CI/repo pins, not this document.
