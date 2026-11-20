# Database Design
**MVP v0.1 — Logical ERD, chưa phải migration**

## 1. Database-per-Service
Theo `a(1).txt` mục 4: một SQL Server container, 5 logical DB với connection string riêng và MongoDB cho catalog. Service chỉ truy cập DB sở hữu; không join/FK xuyên DB. ID ở service khác chỉ là external reference. Tạo credential riêng theo DB nếu khả thi; không dùng tài khoản sa cho toàn bộ runtime.


```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
left to right direction
skinparam componentStyle rectangle
component Auth as A
component Wallet as W
component Inventory as I
component Marketplace as M
component Admin as G
node "One SQL Server instance" {
 database AuthDb as ADB
 database WalletDb as WDB
 database InventoryDb as IDB
 database MarketplaceWriteDb as MDB
 database AdminAnalyticsDb as GDB
}
database "MongoDB\nMarketplaceReadDb" as R
database "Redis\nNamespaced cache" as C
A --> ADB
W --> WDB
I --> IDB
M --> MDB
M --> R
M --> C : lock:*; price:* read
G --> GDB
G --> C : price:*; analytics:*
@enduml
```


## 2. Ownership
| Data | Source of truth | Read copy / access |
|---|---|---|
| Role, account status | AuthDb | JWT claims + status API |
| Gem balance, hold, ledger, top-up | WalletDb | Wallet REST |
| Item, ownership, reservation, capacity | InventoryDb | Catalog snapshot / internal REST |
| Listing, trade history, saga steps | MarketplaceWriteDb | Mongo catalog projection / trade status REST |
| Base price, band, stats | AdminAnalyticsDb | Redis snapshot và aggregates |
| Cache/locks | Redis | Không là nguồn chuẩn của tiền, kho, listing |

## 3. Logical ERDs
Các tên bảng/field dưới đây là đề xuất. UUID/long/datetime là kiểu logic, sẽ map sang SQL Server/EF Core. Các khóa ngoại chỉ ở trong sơ đồ DB sở hữu.


### AuthDb

```plantuml
@startuml
title Auth Service - Entity Relationship Diagram
skinparam shadowing false
skinparam defaultFontName SansSerif
hide circle
skinparam linetype ortho
package "AuthDb" {
  entity Users {
    * UserId : uuid <<PK>>
    --
    Email : string
    DisplayName : string
    PasswordHash : string?
    Role : string
    Status : string
    Version : long
  }
  entity ExternalIdentities {
    * IdentityId : uuid <<PK>>
    --
    UserId : uuid <<FK>>
    Provider : string
    ProviderSubject : string <<UQ with Provider>>
  }
  entity RefreshTokens {
    * TokenId : uuid <<PK>>
    --
    UserId : uuid <<FK>>
    TokenHash : string
    ExpiresAt : datetime
    RevokedAt : datetime?
  }
}
Users ||--o{ ExternalIdentities
Users ||--o{ RefreshTokens
note right of Users
  PasswordHash nullable for Google-only accounts.
  Seeded accounts use a password hash.
  Do not auto-link accounts by email.
end note
@enduml
```


### WalletDb

```plantuml
@startuml
title Wallet Service - Entity Relationship Diagram

top to bottom direction

skinparam backgroundColor #FFFFFF
skinparam shadowing false
skinparam defaultFontName Arial
skinparam defaultFontSize 13
skinparam linetype ortho
skinparam nodesep 50
skinparam ranksep 60
skinparam roundcorner 10
skinparam ArrowColor #64748B

skinparam entity {
    BackgroundColor #DCFCE7
    BorderColor #86C99C
    FontColor #14532D
}

skinparam package {
    BackgroundColor #F8FAFC
    BorderColor #94A3B8
}

hide circle

package "WalletDb - SQL Server" {

    entity "Wallets" as Wallets {
        * UserId : uuid <<PK>>
        --
        TotalBalance : long
        HoldBalance : long
        Version : long
    }

    entity "FundHolds" as FundHolds {
        * TradeId : uuid <<PK>>
        --
        BuyerId : uuid <<FK>>
        Amount : long
        Status : string
        CreatedAt : datetime
        UpdatedAt : datetime
    }

    entity "WalletTransactions" as Transactions {
        * EntryId : uuid <<PK>>
        --
        UserId : uuid <<FK>>
        OperationKey : string <<UQ>>
        TradeId : uuid?
        TopUpId : uuid? <<FK>>
        TotalDelta : long
        HoldDelta : long
        FeeGem : long?
        Type : string
        CreatedAt : datetime
    }

    entity "TopUpOrders" as TopUpOrders {
        * TopUpId : uuid <<PK>>
        --
        UserId : uuid <<FK>>
        Provider : string
        AppTransId : string <<UQ>>
        PaymentUrl : string?
        ProviderTxnId : long? <<UQ>>
        AmountVnd : long
        GemAmount : long
        Status : string
        CreatedAt : datetime
        PaidAt : datetime?
    }

    Wallets ||--o{ FundHolds : reserves
    Wallets ||--o{ Transactions : records
    Wallets ||--o{ TopUpOrders : creates

    TopUpOrders |o--o{ Transactions : generates

    FundHolds -[hidden]right-> Transactions
    Transactions -[hidden]right-> TopUpOrders
}

note top of Wallets
    AvailableBalance =
    TotalBalance - HoldBalance

    0 <= HoldBalance <= TotalBalance
end note

note bottom of FundHolds
    Hold Status:
    - Held
    - Settled
    - Released
end note

note bottom of Transactions
    Transaction Types:
    - TopUp
    - Hold
    - SettlementDebit
    - SettlementCredit
    - Release
    - TaxBurn

    OperationKey prevents
    duplicate financial operations.
end note

note bottom of TopUpOrders
    Payment Provider: ZALOPAY

    AppTransId = app_trans_id
    ProviderTxnId = zp_trans_id
    app_user = UserId (canonical string)
    PaymentUrl may be null while Pending.

    Order Status:
    - Pending
    - Paid
    - Failed
    - Expired

    ProviderTxnId uses a filtered
    unique index (IS NOT NULL).
end note

legend bottom
    |= Symbol |= Description |
    | PK | Primary Key |
    | FK | Foreign Key |
    | UQ | Unique Constraint |
    | ? | Nullable |
endlegend

@enduml
```


### InventoryDb

```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
hide circle
skinparam linetype ortho
package "InventoryDb" {
  entity ItemTemplates {
    * TemplateId : uuid <<PK>>
    --
    Name : string
    Category : string
    Rarity : string
    Tradable : bool
  }
  entity Inventories {
    * UserId : uuid <<PK>>
    --
    Capacity : int
    ReservedSlots : int
    Version : long
  }
  entity InventoryItems {
    * ItemId : uuid <<PK>>
    --
    OwnerId : uuid <<FK>>
    TemplateId : uuid <<FK>>
    Quantity : int
    Status : string
    ListingId : uuid?
    TradeId : uuid?
  }
  entity ItemLocks {
    * ListingId : uuid <<PK>>
    --
    ItemId : uuid <<FK>>
    SellerId : uuid
    Status : string
    UpdatedAt : datetime
  }
  entity ItemTransfers {
    * TradeId : uuid <<PK>>
    --
    ItemId : uuid
    SellerId : uuid
    BuyerId : uuid
    Status : string
    Quantity : int
    UpdatedAt : datetime
  }
}
Inventories ||--o{ InventoryItems
ItemTemplates ||--o{ InventoryItems
@enduml
```


### MarketplaceWriteDb

```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
hide circle
skinparam linetype ortho
package "MarketplaceWriteDb" {
  entity Listings {
    * ListingId : uuid <<PK>>
    --
    SellerId : uuid
    ItemId : uuid
    TemplateId : uuid
    ItemName : string
    Category : string
    Rarity : string
    Quantity : int
    UnitPrice : long
    GrossGem : long
    PolicyVersion : long
    Status : string
    Version : long
    CreatedAt : datetime
    UpdatedAt : datetime
  }
  entity Trade_Histories {
    * TradeId : uuid <<PK>>
    --
    ListingId : uuid <<FK>>
    BuyerId : uuid
    SellerId : uuid
    ItemId : uuid
    TemplateId : uuid
    Quantity : int
    UnitPrice : long
    GrossGem : long
    FeeGem : long
    SellerNetGem : long
    Status : string
    FailureCode : string?
    RequiresReview : bool
    IdempotencyKey : string
    CreatedAt : datetime
    UpdatedAt : datetime
    CompletedAt : datetime?
  }
  entity Saga_States {
    * CorrelationId : uuid <<PK>>
    --
    TradeId : uuid <<FK,UQ>>
    CurrentState : string
    UpdatedAt : datetime
    FailureReason : string?
  }
  entity Saga_Steps {
    * StepId : uuid <<PK>>
    --
    TradeId : uuid <<FK>>
    Step : string
    Outcome : string
    ErrorCode : string?
    At : datetime
  }
}
Listings ||--o{ Trade_Histories
Trade_Histories ||--o| Saga_States
Trade_Histories ||--o{ Saga_Steps
@enduml
```


### AdminAnalyticsDb

```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
hide circle
skinparam linetype ortho
package "AdminAnalyticsDb" {
  entity Price_Policies {
    * TemplateId : uuid <<PK>>
    --
    BasePrice : long
    BandBasisPoints : int
    Version : long
  }
  entity Trade_Facts {
    * TradeId : uuid <<PK>>
    --
    TemplateId : uuid
    GrossGem : long
    FeeGem : long
    Quantity : int
    CompletedAt : datetime
  }
  entity Price_Policy_Audits {
    * AuditId : uuid <<PK>>
    --
    TemplateId : uuid <<FK>>
    ActorId : uuid
    BasePrice : long
    BandBasisPoints : int
    Version : long
    Reason : string
    CreatedAt : datetime
  }
  entity Market_Daily_Summaries {
    * Day, TemplateId <<PK>>
    --
    TradeCount : long
    TotalGrossGem : long
    TotalFeeGem : long
    Volume : long
    MinUnitPrice : long?
    MaxUnitPrice : long?
    ReferencePrice : long
    UpdatedAt : datetime
  }
  entity Moderation_Actions {
    * ActionId : uuid <<PK>>
    --
    OperationId : uuid <<UQ>>
    ActorId : uuid
    TargetUserId : uuid
    Action : string
    Reason : string
    Status : string
    CreatedAt : datetime
    UpdatedAt : datetime
  }
}
@enduml
```


## 4. MongoDB read model
Chỉ lưu dữ liệu đã làm phẳng từ Listing events; không có FK. Query chỉ hiển thị Active. Duy trì version và tombstone Sold/Cancelled để message cũ không làm sống lại tin đã bán/hủy; upsert điều kiện version mới hơn. Index tùy truy vấn và kết quả đo.



```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
class "Marketplace_Catalog" as Catalog <<MongoDB document>> {
  _id : ListingId
  version : long
  status : string
  sellerId : uuid
  itemId : uuid
  templateId : uuid
  itemName : string
  category : string
  rarity : string
  quantity : int
  unitPrice : long
  grossGem : long
  createdAt : datetime
  updatedAt : datetime
}
note right of Catalog
  Index candidates:
  status + unitPrice
  status + createdAt
  status + category + rarity
  sellerId + status
  Tombstones retain version after Sold/Cancelled.
end note
@enduml
```


## 5. Constraints and transactions
| Store | Ràng buộc quan trọng |
|---|---|
| AuthDb | unique (Provider, ProviderSubject), hash refresh token, nullable PasswordHash cho Google-only, DisplayName theo DTO User; login seed cần email lookup nhất quán |
| WalletDb | CHECK TotalBalance ≥ HoldBalance ≥ 0; unique hold TradeId; unique OperationKey cho ledger; unique AppTransId; unique (Provider, ProviderTxnId) khi có; PaymentUrl nullable; top-up chuyển Pending → Paid một lần |
| InventoryDb | một item instance hoặc khóa toàn stack theo ListingId; capacity check/chuyển đồ trong transaction; unique TradeId transfer; giữ slot seller cho khả năng reverse |
| MarketplaceWriteDb | conditional update WHERE status=Active; idempotency key unique theo buyer + operation; filtered unique completed trade/ListingId nếu cần; TradeId/CorrelationId duy nhất; giữ version tăng dần |
| AdminAnalyticsDb | unique TradeId ở Trade_Facts; daily summary upsert bằng (Day,TemplateId); ghi policy bền vững trước khi publish/cache |

Wallet settlement cập nhật hai ví, trạng thái hold và ledger cùng transaction; không trừ toàn bộ HoldBalance của buyer nếu còn trade khác. Phần fee được ghi rõ trong trade và ledger metadata (không credit ví hệ thống). Inventory transfer/finalize/reverse là mỗi local transaction riêng. Không giữ SQL transaction mở trong lúc chờ HTTP/broker.

## 6. Outbox / Inbox và lưu vết
Các ERD nghiệp vụ trên không vẽ bảng hạ tầng lặp lại: service phát message có OutboxMessages (MessageId, Payload, OccurredAt, PublishedAt...) và consumer có Inbox/ProcessedMessages (Consumer, MessageId, ProcessedAt...). Tên thực tế tùy cơ chế MassTransit đã chọn. Trong cùng transaction, lưu business state + outbox; dispatcher publish retry. Consumer dedupe message ID và khóa nghiệp vụ (TradeId/ListingId) vì retry có thể dùng MessageId khác. Với Mongo projection, version guard; với Redis aggregate, không coi một thao tác increment đơn lẻ là đủ chống trùng.

HTTP Idempotency: mỗi service có mutation dùng Idempotency-Key cần lưu key, actor/operation/resource scope, request fingerprint và resource/result reference bền vững trong cùng local transaction với business intent. Unique theo actor + operation + scope + key; replay khác fingerprint trả 409. Đây là logical infrastructure design, chưa chốt tên bảng/migration. Các field snapshot của listing/trade (policy version, fee/net, quantity), moderation operation id và seller slot reservation phải được map trước khi migration theo Contracts mục 08/11; ERD nghiệp vụ hiện tại không vẽ toàn bộ field kỹ thuật.

## 7. Redis namespaces
| Key | Owner | TTL / semantics |
|---|---|---|
| lock:listing:{id} | Marketplace | 10–30s, unique token; compare-and-delete; SQL status là bảo vệ cuối |
| price:policy:{templateId} | Admin write; Marketplace read-only | versioned snapshot {referencePrice, band, version}; cache miss gọi nội bộ Admin (`ADM-I01`); không mặc định giá 0 |
| analytics:{day}:{templateId} | Admin | thống kê tái tạo từ Trade_Facts; giữ đủ thời gian đối soát |

Daily job dùng ngày nghiệp vụ Asia/Ho_Chi_Minh, lưu ngày vừa kết thúc, không xóa bộ đếm khi có event đến trễ. Có thể dựng lại ngày bị ảnh hưởng từ Trade_Facts. Không cần quét SQL toàn sàn theo từng request.

## 8. Traceability và read paths
- FR-QRY-04: Marketplace trade history; Wallet ledger là endpoint riêng do Wallet sở hữu.
- FR-CS-01: Admin tổng hợp trace qua API nội bộ của Marketplace/Wallet/Inventory theo TradeId/UserId, không query database khác.
- GM analytics là nghiệp vụ; Grafana là giám sát kỹ thuật, không thay thế API thống kê thị trường.
