# System Architecture
**Dự án:** In-game P2P Marketplace — MVP v0.1

## 1. Architectural drivers
- Business: chỉ Gem, Direct Purchase, phí sàn, biên độ giá và quyền sở hữu tài sản.
- SOA: service contract rõ ràng, loose coupling, khả năng phối hợp qua REST và messaging, tích hợp bên thứ ba.
- NFR: local ACID, eventual consistency, idempotency, chống overselling/double spending, khả năng quan sát và triển khai đồng nhất.
- Constraint: 3 người/11–12 ngày; không frontend hoàn chỉnh; một SQL Server container, 5 database logic; 5 service + YARP; 1 payment sandbox, Google login.

Nguồn: `a(1).txt` mục 3–6; Analyst FR/NFR; Idea mục công nghệ. Google và Sandbox là bổ sung đã thảo luận, chưa xác nhận credentials hoặc yêu cầu chính thức của giảng viên.

## 2. System context


```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
left to right direction
actor Player
actor "Game Master" as GM
actor "Customer Support" as CS
rectangle "In-game P2P Marketplace\nBackend system" as System
cloud "Google Identity" as Google
cloud "ZaloPay Sandbox" as Pay
rectangle "Core Game / Seeder" as Seed
Player --> System : Login, top-up, trade
GM --> System : Price policy, analytics
CS --> System : Trace, restrictions
System --> Google : Identity verification
System --> Pay : Create Order / Query Order
Pay --> System : Payment Callback
Seed --> System : Provision items (internal)
@enduml
```


## 3. Service decomposition và trách nhiệm
| Service | Modules / FR gốc | Trách nhiệm và public capabilities | Nội bộ |
|---|---|---|---|
| Auth.API | 1; AUTH-01,02,04 | Login, refresh, logout, Google Sign-In | Kiểm tra trạng thái, đổi status/role có quyền |
| Wallet.API | 2; WAL-01–06 | Balance, top-up, ledger, provider callback | Hold, settle, release; đối soát top-up |
| Inventory.API | 3; INV-01–05 | Inventory của mình | Seed, lock/unlock, transfer/reverse/finalize |
| Marketplace.API | 4+5; MKT-01–05, QRY-01–04 | Listing, cancel, purchase, browse/filter, my listings, trade status/history | Saga orchestration, projection, trace |
| Admin.API | 6+7; GM-01–04, CS-01–02 | Stats, price policy, trace, restrict account | Analytics consumer, daily job, aggregate internal REST |
| Gateway | Cross-cutting | Một điểm truy cập; routing; JWT ở route được bảo vệ | Không business logic, không business DB |

29 FR gốc còn lại sau loại AUTH-03, GM-05, GM-06. Google login và Sandbox thay đổi chi tiết AUTH-01/WAL-02–03; các kỹ thuật bổ sung không tự động là FR độc lập. Với API CRUD nội bộ, mỗi service kiểm tra quyền chứ không chỉ dựa vào gateway.

## 4. Container architecture



```plantuml
@startuml
title In-game P2P Marketplace - Container Architecture

top to bottom direction

skinparam backgroundColor #FFFFFF
skinparam shadowing false
skinparam defaultFontName Arial
skinparam defaultFontSize 13
skinparam componentStyle rectangle
skinparam linetype ortho
skinparam nodesep 45
skinparam ranksep 55
skinparam ArrowColor #64748B
skinparam roundcorner 10

skinparam rectangle {
    BackgroundColor #F8FAFC
    BorderColor #94A3B8
}

skinparam component {
    BackgroundColor #DBEAFE
    BorderColor #60A5FA
    FontColor #1E3A8A
}

skinparam database {
    BackgroundColor #DCFCE7
    BorderColor #86C99C
    FontColor #14532D
}

skinparam actor {
    BackgroundColor #FFFFFF
    BorderColor #475569
}

' ==========================
' ACTORS
' ==========================

together {
    actor "Player" as Player
    actor "Game Master" as GM
    actor "Customer Support" as CS
}

Player -[hidden]right-> GM
GM -[hidden]right-> CS

' ==========================
' EXTERNAL SERVICES
' ==========================

cloud "Google Identity\nServices" as Google #F1F5F9

cloud "ZaloPay Sandbox" as Payment #F1F5F9

' ==========================
' SYSTEM BOUNDARY
' ==========================

rectangle "In-game P2P Marketplace System" {

    component "API Gateway\nYARP" as Gateway #DDD6FE

    together {
        component "Auth.API\n\nAuthentication\nIdentity & RBAC" as Auth

        component "Inventory.API\n\nItem Ownership\nInventory Management" as Inventory

        component "Marketplace.API\n\nCommand & Query\nSaga Orchestration" as Marketplace

        component "Admin.API\n\nMarket Analytics\nCustomer Support" as Admin

        component "Wallet.API\n\nWallet Management\nPayment Integration" as Wallet
    }

    ' Service alignment

    Auth -[hidden]right-> Inventory
    Inventory -[hidden]right-> Marketplace
    Marketplace -[hidden]right-> Admin
    Admin -[hidden]right-> Wallet

    ' ==========================
    ' DATA LAYER
    ' ==========================

    together {
        database "AuthDb" as AuthDb
        database "InventoryDb" as InventoryDb
        database "MarketplaceWriteDb" as MarketDb
        database "AdminAnalyticsDb" as AdminDb
        database "WalletDb" as WalletDb
    }

    AuthDb -[hidden]right-> InventoryDb
    InventoryDb -[hidden]right-> MarketDb
    MarketDb -[hidden]right-> AdminDb
    AdminDb -[hidden]right-> WalletDb

    ' ==========================
    ' SHARED INFRASTRUCTURE
    ' ==========================

    together {
        queue "RabbitMQ\nMessage Broker" as RabbitMQ #FFEDD5

        database "MongoDB\nMarketplaceReadDb" as MongoDB

        database "Redis\nDistributed Lock\nCache & Analytics" as Redis #FEE2E2
    }

    RabbitMQ -[hidden]right-> MongoDB
    MongoDB -[hidden]right-> Redis

    MarketDb -[hidden]down-> MongoDB

    ' ==========================
    ' GATEWAY ROUTING
    ' ==========================

    Gateway -down-> Auth
    Gateway -down-> Inventory
    Gateway -down-> Marketplace
    Gateway -down-> Admin
    Gateway -down-> Wallet

    ' ==========================
    ' DATABASE OWNERSHIP
    ' ==========================

    Auth -down-> AuthDb
    Inventory -down-> InventoryDb
    Marketplace -down-> MarketDb
    Admin -down-> AdminDb
    Wallet -down-> WalletDb

    ' ==========================
    ' ASYNCHRONOUS MESSAGING
    ' ==========================

    Marketplace <..> RabbitMQ
    Wallet <..> RabbitMQ
    Inventory <..> RabbitMQ
    RabbitMQ ..> Admin

    ' ==========================
    ' CACHE AND READ DATABASE
    ' ==========================

    Marketplace --> MongoDB
    Marketplace --> Redis
    Admin --> Redis
}

' ==========================
' CLIENT ACCESS
' ==========================

Player -down-> Gateway
GM -down-> Gateway
CS -down-> Gateway

' ==========================
' THIRD-PARTY INTEGRATION
' ==========================

Auth -left-> Google : OIDC
Wallet -right-> Payment : ZaloPay REST API

' ZaloPay payment callback is routed
' through API Gateway to Wallet.API.
' Detailed in Payment Sequence Diagram.

legend bottom
    |= Color / Symbol |= Description |
    |<#DDD6FE>    | API Gateway |
    |<#DBEAFE>    | Microservice |
    |<#DCFCE7>    | Database |
    |<#FFEDD5>    | Message Broker |
    |<#FEE2E2>    | Distributed Cache |
    |<#F1F5F9>    | External Service |
    | Solid Arrow | Synchronous / Data Access |
    | Dashed Arrow | Asynchronous Messaging |

    SQL databases are logically isolated
    on a single SQL Server instance.
endlegend

@enduml
```


### 4.1. Thành phần chung
| Công nghệ | Vai trò |
|---|---|
| ASP.NET Core / C# | REST handlers, application/domain/infrastructure |
| YARP | Reverse proxy, routing policy |
| RabbitMQ + MassTransit | Command/event transport, consumer và saga state machine |
| SQL Server | Authoritative state, local transaction, outbox/inbox |
| MongoDB | Denormalized catalog, không dùng để phê duyệt mua |
| Redis | Short-lived listing locks, versioned price cache, analytics |
| OpenAPI/Swagger | REST contract; message contracts tách riêng (giai đoạn tiếp theo) |
| Docker Compose / ngrok | Local demo / public HTTPS gateway có kiểm soát |
| Prometheus / Grafana | Thu thập métrics / dashboard |

## 5. Marketplace internal components
Command và Query cùng deployable để tiết kiệm RAM; không được tuyên bố có thể scale riêng Query ở MVP. Query browse không chạm MarketplaceWriteDb; trade status/history đọc SQL của chính Marketplace để cung cấp kết quả chuẩn.



```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
skinparam componentStyle rectangle
package "Marketplace.API (one deployable)" {
 component "Command API" as Cmd
 component "Command Handlers" as Handler
 component "Trade Saga / State Machine" as Saga
 component "Outbox Dispatcher" as Outbox
 component "Query API" as Query
 component "CQRS Projection Worker" as Worker
 component "Trade Trace / Status API" as Trace
}
queue RabbitMQ as MQ
database MarketplaceWriteDb as W
database MarketplaceReadDb as R
database Redis
Cmd --> Handler
Handler --> Redis
Handler --> W : local state + outbox
Saga --> W : persisted state + outbox
Outbox --> W : pending messages
Outbox --> MQ : publish
MQ --> Saga : replies
MQ --> Worker : listing events
Worker --> R : versioned projection
Query --> R : browse / filter
Trace --> W : trade status / history
@enduml
```


## 6. Communication design
| Use case | Producer → Consumer | Phương thức |
|---|---|---|
| Protected request | Client → Gateway → Service | REST + JWT |
| Google login | Client/Auth ↔ Google | OIDC; verify token/code theo flow chọn |
| Top-up | Wallet ↔ provider | ZaloPay REST Create/Query + signed callback |
| Create listing | Marketplace → Inventory | REST lock; idempotent theo ListingId |
| Cancel listing | Marketplace → Inventory / Projection | RabbitMQ `UnlockItemCommand` (tới Inventory) + `ListingCancelledEvent` (tới Projection) |
| Purchase | Marketplace → Wallet/Inventory | RabbitMQ saga commands + result events |
| Market read sync | Marketplace → CQRS consumer | RabbitMQ versioned events |
| Analytics | Marketplace → Admin | RabbitMQ TradeCompletedEvent |
| Price lookup | Marketplace → Redis / Admin | Đọc `price:policy:{templateId}` trên Redis; cache miss gọi fallback `ADM-I01` (internal REST) |
| Restrict/trace | Admin → Auth/Marketplace/Wallet/Inventory | Internal REST; không đọc chéo DB |
| Explicit account status | Marketplace → Auth | Internal REST trước khi chấp nhận lệnh mua/đăng bán |



```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
left to right direction
skinparam componentStyle rectangle
component Marketplace as M
component Auth as A
component Wallet as W
component Inventory as I
component Admin as G
queue RabbitMQ as B
M --> A : status check (REST)
M --> I : LockItem / unlock / trace (REST)
M --> W : trade trace (REST)
M --> G : price policy fallback ADM-I01 (REST)
G --> A : moderation (REST)
G --> M : trade trace (REST)
G --> W : ledger trace (REST)
G --> I : transfer trace (REST)
M --> B : saga commands / listing events
B --> W : hold / settle / release
B --> I : transfer / reverse / finalize / unlock
W --> B : result events
I --> B : result events
B --> M : saga replies / projection events
B --> G : TradeCompleted
@enduml
```


## 7. Security boundary
- JWT do Auth phát hành; role Player/GM/CS nội bộ; Buyer/Seller không phải role riêng. Service xác minh issuer, audience, expiry, chữ ký; internal REST dùng service credential, không expose ở gateway.
- Google callback/login public theo flow; xác minh issuer/audience/expiry, subject ổn định, nonce/state nếu flow yêu cầu; không gán quyền GM/CS từ Google claims. Không tự liên kết tài khoản theo email không xác minh.
- ZaloPay callback public có kiểm tra chữ ký, order ID, amount, trạng thái provider; return URL chỉ hiển thị kết quả, không cộng Gem. Không log token/secret.
- API Gateway chỉ route public allowlist; seeder, command nội bộ, broker/DB/metrics không expose qua ngrok. Hạn chế truy cập Swagger public nếu cần; ngrok chỉ để demo.
- Auth sở hữu trạng thái ban; kiểm tra tại thời điểm nhận lệnh, không hủy ngang Saga đã bắt đầu.

## 8. Traceability và các giới hạn
| Driver | Giải pháp | Bằng chứng demo |
|---|---|---|
| FR-MKT-04 / NFR-CONC | Redis lock + conditional SQL update | 100 request: tối đa 1 completed trade |
| FR-MKT-05 / NFR-CONS | Saga + local transactions | Success / full inventory + release hold |
| FR-QRY-01 / NFR-PERF | CQRS | SQL write → MongoDB projection |
| FR-WAL-03 / NFR-CONS-04 | signed callback, unique provider transaction | Duplicate callback không cộng trùng |
| FR-GM-01–04 | Consumer + cache + daily job | Summary + change policy |
| NFR-SCAL-02 / OBS-01 | Compose + metrics | Run, dashboard, health |

Không cam kết throughput hoặc latency trước khi benchmark; đo query p95, sync lag và số giao dịch completed. Một broker/SQL/Redis instance không phải HA; chỉ chứng minh retry/recovery khi service dừng.
