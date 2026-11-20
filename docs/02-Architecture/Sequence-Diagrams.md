# System Behavioral Design — Sequence Diagrams
**MVP v0.1**

## 1. Notation and common assumptions
Diagrams omit Gateway in internal traffic and simplify broker dispatcher/ack flow where needed. Every public request goes through Gateway with auth policy; outbound messages representing SQL commits are published from the outbox dispatcher, not directly by SQL. For readability, a few event sends show service → MQ rather than drawing outbox per event. All mutation operations are idempotent and correlate ListingId/TradeId/TopUpId as appropriate. State diagrams are in Design-Patterns.md. No final endpoint names or JSON schemas are frozen here.

Priority P0 = indispensable for trading; P1 = required by proposed integration / admin scope. Source: `a(1).txt` section 5, Analyst FR and state matrix; Google/Sandbox and explicit recovery handling are new design proposals.

## 2. Create Listing (P0)
Persist a PendingLock intent for recovery; final Active still occurs only after confirmed Inventory lock, preserving synchronous REST requirement. Confirmed failure unlocks; unknown outcome is resolved by retry/query. Duplicate create must return original operation result.


```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
actor Seller
participant "Marketplace.API" as M
participant "Auth.API" as A
database Redis as R
participant "Inventory.API" as I
database "MarketplaceWriteDb" as DB
queue RabbitMQ as MQ
Seller -> M : Create listing (via Gateway)
M -> A : Check trade permission
A --> M : Allowed
M -> R : Read price snapshot
R --> M : Price / band / version
M -> M : Validate price and request
M -> DB : Persist PendingLock intent
M -> I : LockItem(listingId, itemId, sellerId)
alt confirmed success
 I --> M : Item locked + snapshot
 M -> DB : Commit Active + outbox
 alt committed
  M --> Seller : Listing created
  DB -> MQ : ListingCreated (outbox dispatcher)
 else confirmed activation failure
  M -> I : UnlockItem(listingId) / retry
  I --> M : Unlocked
  M -> DB : CreateFailed
  M --> Seller : Creation failed
 end
else confirmed lock rejection
 I --> M : Business rejection
 M -> DB : CreateFailed
 M --> Seller : Rejected
else timeout / unknown
 M --> Seller : Pending / inspect status
 note over M,I : Resolve lock state by ListingId; do not blindly unlock.
end
@enduml
```


## 3. Cancel Listing (P0)
Cancellation changes SQL state and publishes event, Inventory unlock is asynchronous. A user may see item locked briefly. Retry failed consumers; do not show item available until confirmed.



```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
actor Seller
participant Marketplace as M
database Redis as R
database MarketplaceWriteDb as DB
queue RabbitMQ as MQ
participant Inventory as I
participant "CQRS Worker" as Q
Seller -> M : Cancel own listing (via Gateway)
M -> R : Try acquire listing lock
alt lock busy
 M --> Seller : Conflict
else acquired
 M -> DB : conditional Active -> Cancelled + outbox
 alt claim won
  M -> R : release own lock token
  M --> Seller : Cancelled (unlock pending)
  DB -> MQ : UnlockItemCommand + ListingCancelledEvent (dispatcher)
  MQ -> I : UnlockItemCommand (inventory-commands)
  MQ -> Q : ListingCancelledEvent (marketplace-projection)
  I -> I : local unlock / dedupe
 else not Active or not owner
  M -> R : release own lock token
  M --> Seller : Reject
 end
end
@enduml
```


## 4. Direct Purchase — Happy Path (P0)
The buyer receives 202 only after durable acceptance. A Purchase may later fail if not enough Gem or capacity. Buyer polls trade status. Price and fee are determined by server snapshot; not trusted from request.



```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
actor Buyer
participant Marketplace as M
participant Auth as A
database Redis as R
database MarketplaceWriteDb as DB
queue RabbitMQ as MQ
participant Wallet as W
participant Inventory as I
Buyer -> M : Purchase listing (via Gateway)
M -> A : Check permission
A --> M : Allowed
M -> R : TryAcquire(token)
R --> M : Granted
M -> DB : Atomic claim, Trade + Saga + outbox
M -> R : release token after durable claim
M --> Buyer : 202 Accepted + TradeId
DB -> MQ : HoldBuyerFundsCommand (dispatcher)
MQ -> W : HoldBuyerFundsCommand
W -> W : Reserve Gem + ledger + outbox
W -> MQ : BuyerFundsHeldEvent
MQ -> M : BuyerFundsHeldEvent
M -> DB : Transferring + outbox
DB -> MQ : TransferItemCommand
MQ -> I : Transfer item (reserve capacity)
I -> I : Local transfer to PendingSettlement
I -> MQ : ItemTransferredEvent
MQ -> M : ItemTransferredEvent
M -> DB : Settling + outbox
DB -> MQ : SettleTradeCommand
MQ -> W : SettleTradeCommand
W -> W : Debit hold, credit seller net, burn fee
W -> MQ : TradeSettledEvent
MQ -> M : TradeSettledEvent
M -> DB : Finalizing + outbox
DB -> MQ : FinalizeItemTransferCommand
MQ -> I : FinalizeItemTransferCommand
I -> I : Mark item Available; release seller slot
I -> MQ : ItemTransferFinalizedEvent
MQ -> M : ItemTransferFinalizedEvent
M -> DB : Sold + Completed + outbox
DB -> MQ : TradeCompletedEvent
Buyer -> M : GET trade status
M --> Buyer : Completed
@enduml
```


## 5. Saga Compensation (P0)
Two confirmed failure scenarios after holding funds. A rejection before hold succeeds simply fails trade and reactivates Listing. After successful settlement, only forward recovery is allowed. A timeout does not justify compensation.



```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
participant "Marketplace Saga" as S
queue RabbitMQ as MQ
participant Wallet as W
participant Inventory as I
database MarketplaceWriteDb as DB
note over S,W : Funds already held
S -> MQ : TransferItemCommand
MQ -> I : Transfer item
alt Inventory full (confirmed no transfer)
 I -> MQ : ItemTransferFailedEvent
 MQ -> S : Failure
 S -> MQ : RefundBuyerFundsCommand
else Transferred, settlement permanently rejected
 I -> MQ : ItemTransferredEvent
 MQ -> S : Success
 S -> MQ : SettleTradeCommand
 MQ -> W : Settle
 W -> MQ : TradeSettlementRejectedEvent (not committed)
 MQ -> S : Confirmed rejection
 S -> MQ : ReverseItemTransferCommand
 MQ -> I : Restore seller locked item
 I -> MQ : ItemTransferReversedEvent
 MQ -> S : Reversed
 S -> MQ : RefundBuyerFundsCommand
end
MQ -> W : Release hold for TradeId
W -> W : Atomic release + ledger (idempotent)
W -> MQ : BuyerFundsRefundedEvent
MQ -> S : Confirmed refund
S -> DB : Failed + Listing Active + outbox
DB -> MQ : ListingReactivated / TradeFailed
note over S,W : Unknown outcomes: retry or reconcile; no blind refund.
@enduml
```


## 6. Concurrent purchase (P0)
Redis reduces contention. SQL conditional state guards ensure at most one successful claim even when lock expires; same SQL condition protects cancel vs purchase.



```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
actor BuyerA as A
actor BuyerB as B
participant "Marketplace instance A" as M1
participant "Marketplace instance B" as M2
database Redis as R
database MarketplaceWriteDb as DB
A -> M1 : Purchase same ListingId
B -> M2 : Purchase same ListingId
M1 -> R : SET NX PX tokenA
R --> M1 : OK
M2 -> R : SET NX PX tokenB
R --> M2 : Lock busy
M2 --> B : 409 Conflict
M1 -> DB : UPDATE WHERE Status=Active
DB --> M1 : 1 row + Trade/Saga committed
M1 --> A : 202 Accepted
... lock released or expires ...
B -> M2 : Retry purchase
M2 -> R : Acquire tokenB
R --> M2 : OK
M2 -> DB : UPDATE WHERE Status=Active
DB --> M2 : 0 rows (Processing/Sold)
M2 -> R : Compare-delete tokenB
M2 --> B : 409 Conflict
@enduml
```


## 7. CQRS Synchronization (P0)
Version guard and tombstones protect against late/duplicated events. Reading Mongo is not authorization to purchase.



```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
participant "Marketplace Command" as C
database MarketplaceWriteDb as DB
participant "Outbox Dispatcher" as O
queue RabbitMQ as MQ
participant "Projection Worker" as W
database MongoDB as M
actor Player
participant "Marketplace Query" as Q
C -> DB : Commit listing change + outbox
O -> DB : Read unsent outbox
O -> MQ : Publish event (ListingId, Version, Snapshot)
MQ -> W : Deliver / redeliver
W -> M : Atomic version-guarded upsert
note right of M : Sold/Cancelled tombstone retained
W -> MQ : ACK after success
Player -> Q : Browse (via Gateway)
Q -> M : status=Active + filter + pagination
M --> Q : catalog view
Q --> Player : results (may lag)
@enduml
```


## 8. Top-up via ZaloPay Sandbox (P1)
ZaloPay Sandbox là provider đã chốt. Figure 22A bao gồm khởi tạo đơn nội bộ, gọi Create Order và thanh toán; Figure 22B mô tả callback, reconciliation và kiểm tra trạng thái. Hai sơ đồ dùng chung TopUpId và app_trans_id. Redirect không xác nhận thanh toán; callback/query phải đối soát và dùng chung local atomic idempotent settlement. Chi tiết payload và mã phản hồi xem `../03-Contracts/API-Contracts.txt` (WAL-04..06). Không có tiền thật.

### Figure 22A — Create Order & Payment

```plantuml
@startuml
title Figure 22A - ZaloPay: Create Order and Payment

skinparam backgroundColor #FFFFFF
skinparam shadowing false
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam maxMessageSize 44
skinparam ParticipantPadding 10
skinparam BoxPadding 8
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam ArrowColor #64748B

autonumber

actor Player
participant "API Gateway" as G #DDD6FE
participant "Wallet.API" as W #DBEAFE
database "WalletDb" as D #DCFCE7
participant "ZaloPay\nSandbox" as Z #F1F5F9

== Phase 1 - Internal top-up ==

Player -> G : POST /api/v1/wallet/top-ups\nJWT, Idempotency-Key, packageId
G -> W : Authorized request

W -> W : Validate package; generate IDs\nSnapshot VND and Gem amounts

W -> D : Create/reuse Pending order\nTopUpId + app_trans_id

D --> W : Persisted order

break Exact replay of existing order
    W --> G : Existing order / status
    G --> Player : Same TopUpId
end

== Phase 2 - Provider order ==

W -> W : Build request and MAC (Key1)
W -> Z : POST /v2/create
Z --> W : return_code, order_url

alt Created successfully

    W -> D : Save payment URL if still relevant

    W --> G : 201 Created + TopUpOrder
    G --> Player : Payment URL + TopUpId

    == Phase 3 - Sandbox payment ==

    Player -> Z : Open order_url and pay

    Z --> Player : Payment result / redirect

    note over W,Z
        Redirect does not credit Gem.
        Callback / reconciliation: Figure 22B.
    end note

else Confirmed creation rejection

    W -> D : Mark Failed if not Paid

    W --> G : 502 PAYMENT_CREATE_REJECTED
    G --> Player : Order creation rejected

else Unknown result / duplicate app_trans_id

    W -> D : Keep Pending; schedule query

    W --> G : 202 Accepted + status URL

    G --> Player : Poll original TopUpId

end

@enduml
```

### Figure 22B — Callback & Reconciliation

```plantuml
@startuml
title Figure 22B - ZaloPay: Callback and Reconciliation
skinparam backgroundColor #FFFFFF
skinparam shadowing false
skinparam defaultFontName Arial
skinparam defaultFontSize 12
skinparam maxMessageSize 44
skinparam ParticipantPadding 10
skinparam BoxPadding 8
skinparam sequenceMessageAlign center
skinparam responseMessageBelowArrow true
skinparam ArrowColor #64748B
autonumber
actor Player
participant "API Gateway" as G #DDD6FE
participant "Wallet.API" as W #DBEAFE
database "WalletDb" as D #DCFCE7
participant "ZaloPay\nSandbox" as Z #F1F5F9
note over W,D
    Continued from Figure 22A.
    Same TopUpId and app_trans_id.
end note

== Phase 4 - Payment callback ==
opt Callback received
    Z -> G : POST /api/v1/wallet/zalopay/callback\n(data, mac, type)
    G -> W : Forward (no Player JWT)
    W -> W : Verify Key2 MAC and type;\nvalidate order, amount and IDs
    alt Invalid MAC / unsupported type
        W --> G : return_code = -1
    else Valid MAC and matching payment
        ref over W,D : Atomic idempotent settlement\nLock order; credit Gem + ledger + Paid\nOr detect exact duplicate
        D --> W : Commit / duplicate / transient error
        W --> G : return_code = 1 / 2 / 0
    else Signed but conflicting data
        W -> D : Record discrepancy; no credit
        W --> G : return_code = 0; reconcile
    end
    G --> Z : HTTP 200 + return_code, return_message
end

== Phase 5 - Reconciliation ==
opt Unresolved order / callback missed
    W -> D : Read Pending order
    W -> Z : POST /v2/query (MAC Key1)
    Z --> W : Status + amount + zp_trans_id
    alt Confirmed paid and matching
        ref over W,D : Same atomic idempotent settlement
    else Confirmed terminal failure
        W -> D : Failed / Expired if not Paid
    else Pending / unknown / mismatch
        W -> D : Keep Pending; retry or flag review
    end
end

== Phase 6 - Check top-up status ==
Player -> G : GET /api/v1/wallet/top-ups/{topUpId}
G -> W : Authorized owner request
W -> D : Read order state
D --> W : TopUpOrder
W --> G : 200 OK
G --> Player : Pending / Paid / Failed / Expired
@enduml
```

## 9. Google Sign-In (P1)
Proposed ID-token flow using a minimal browser login helper (not full UI); if team selects authorization-code flow, change diagram/contracts accordingly. Google subject is canonical external identity; local role only.



```plantuml
@startuml
title Figure 23 - Google Sign-In
skinparam shadowing false
skinparam defaultFontName SansSerif
actor Player
participant Browser
participant Google
participant "Auth.API" as A
database AuthDb as DB
Player -> Browser : Sign in with Google
Browser -> Google : Identity authorization (OIDC)
Google --> Browser : ID token (selected flow)
Browser -> A : POST /api/v1/auth/google via Gateway\n{idToken}
A -> A : Verify signature, iss, aud, exp, nonce if used
alt valid and account permitted
 A -> DB : Resolve (Provider, subject); create Player if new
 A -> DB : Store refresh token hash
 A --> Browser : Internal JWT + refresh token
else invalid or banned
 A --> Browser : Reject
end
@enduml
```


## 10. Market Analytics (P1)
Trade facts ensure reconstructable stats. For MVP after new fact insert, recompute affected item/day only, then update Redis atomically/versioned to avoid stale concurrent updates; can serialize by item/day. This is deliberate simplification of pure Redis incremental aggregation in Analyst. Daily snapshot includes reconciliation for late events. GM business statistics are separate from Grafana.



```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
participant Marketplace as M
queue RabbitMQ as MQ
participant "Admin Analytics Consumer" as A
database AdminAnalyticsDb as DB
database Redis as R
participant "Daily Job (00:00)" as J
actor GM
M -> MQ : TradeCompletedEvent
MQ -> A : tradeId, template, gross, fee, qty, time
A -> DB : Idempotent Trade_Facts insert
A -> R : Refresh versioned aggregate / reference price
note over A,R : Rebuild affected day from facts on retry; avoid duplicate increments
GM -> A : Query analytics / update price policy
A -> DB : Persist policy / read historical summary
A -> R : Update/read policy snapshot
A --> GM : metrics and price policy
J -> R : Read previous day's aggregate
J -> DB : Upsert daily summary / reconcile facts
@enduml
```


## 11. Customer Support (P1)
Admin uses authorized internal calls, never DB cross-query. Status restriction applies to new operations. Partial trace is marked rather than falsely reported complete.



```plantuml
@startuml
skinparam shadowing false
skinparam defaultFontName SansSerif
actor CS
participant "Admin.API" as A
participant "Marketplace.API" as M
participant "Wallet.API" as W
participant "Inventory.API" as I
participant "Auth.API" as U
database AdminAnalyticsDb as D
CS -> A : Trade trace (TradeId)
A -> M : Query trade / saga steps
M --> A : Trade / CorrelationId / steps
A -> W : Query ledger for TradeId
W --> A : Hold / settle / refund
A -> I : Query item transfer for TradeId
I --> A : Item operation history
A --> CS : Combined trace (partial errors labeled)
CS -> A : Restrict account (reason)
A -> D : Audit request
A -> U : Internal status change (operation ID)
U --> A : Updated / rejected
A -> D : Record outcome
A --> CS : Status result
@enduml
```


## 12. State/data example
Example only: buyer has 1,200 Gem total, 0 hold; item costs 1,000 Gem; fee 5% (not approved). Seller starts at 0 Gem. Single trade, no concurrent wallet operations in example.
| Step | Listing | Buyer Total / Hold / Available | Seller Gem | Item |
|---|---|---|---:|---|
| Listed | Active | 1200 / 0 / 1200 | 0 | Seller Locked |
| Claim | Processing | 1200 / 0 / 1200 | 0 | Seller Locked |
| Hold | Processing | 1200 / 1000 / 200 | 0 | Seller Locked |
| Transfer | Processing | 1200 / 1000 / 200 | 0 | Buyer PendingSettlement |
| Settle | Processing | 200 / 0 / 200 | 950 | Buyer PendingSettlement |
| Finalize | Sold | 200 / 0 / 200 | 950 | Buyer Available |
| Alternative: full inventory then refund | Active | 1200 / 0 / 1200 | 0 | Seller Locked |

## 13. Acceptance test matrix
| Scenario | Expected invariant |
|---|---|
| Duplicate callback | One top-up credit only |
| Duplicate purchase key | Same TradeId; no second hold |
| 100 buyers / one listing | At most one completed trade; losers conflict (if buyer winner valid) |
| Cancel vs purchase | Exactly one valid transition from Active |
| Insufficient funds | No hold/transfer, Listing reactivated |
| Inventory full | Hold released; seller item remains Locked |
| Duplicate settle event | One payout only |
| Lock TTL expires | Processing remains; second claim rejected |
| Broker/consumer temporarily unavailable | Durable state; resume or trace error; no blind refund |
| Mongo out-of-order | Old event cannot resurrect Sold/Cancelled |
| Banned/TradeRestricted | New purchase/listing denied |
| Daily job repeated | Same daily key upserted, no duplicate totals |
