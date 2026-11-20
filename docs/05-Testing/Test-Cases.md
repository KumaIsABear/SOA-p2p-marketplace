# Test Cases — In-game P2P Marketplace

version: 1.0.0 | Design baseline — NOT RUN

Each row is a scenario, not an executed result. Preconditions and data are in Test-Plan.md; reset only isolated test fixtures between independent cases. P0 = release gate; P1 = additional verification (requirement remains tracked if deferred). For any mutation, capture state before/after and assert no unintended effects; API IDs refer to 03-Contracts, not FR IDs. Use exact HTTP path/payload from API-Operation-Matrix.md and the contract. Poll async instead of fixed sleep; timeout = inconclusive/fail against completion target or Blocked when fixture unavailable, never assumed success. Negative variants may be split into child test IDs (e.g. AU-01.a). All results start Not Run.


## Unit testing

| ID | Priority | Traceability | Fixture / steps | Expected result | Evidence |
|---|---|---|---|---|---|
| UT-01 | P0 | FR-MKT-01 NFR-SEC-02 T03 | Reference=1000, band=2000 bps; tính min/max; input 799,800,1200,1201 | 800–1200 inclusive; reject 799/1201, accept 800/1200 | xUnit output |
| UT-02 | P0 | FR-MKT-01 FR-WAL-05 NFR-CONS-01 NFR-SEC-02 T03 T05 | Reference=101, band=2000; gross=999, feeBps=500; thử quantity=0 và multiplication overflow | Bounds 81–121; fee=49, net=950; invalid/overflow reject, không wrap số | xUnit output |
| UT-03 | P0 | FR-WAL-04 FR-WAL-06 NFR-CONS-01 T07 | Total=1500, Hold=200; hold 1000 rồi release; thử hold 1301 ban đầu | Sau hold (1500,1200,300), release (1500,200,1300); thiếu tiền reject | xUnit output |
| UT-04 | P0 | FR-WAL-05 NFR-CONS-01 T05 | Buyer (1500,1200), seller (350,0), trade hold=1000; settle fee 50 | Buyer (500,200,300), seller (1300,0,1300); supply giảm 50; hold trade Settled; hold khác còn nguyên | xUnit output |
| UT-05 | P0 | FR-QRY-01 FR-WAL-03 NFR-CONS-04 T02 T08 | Logic idempotency cùng operation; projection v1 Active, v3 Sold, rồi v2 Processing/v1 Active | Không credit lặp; version 3 tombstone, không resurrect; test logic không thay DB integration | xUnit output |

## Identity and object authorization

| ID | Priority | Traceability | Fixture / steps | Expected result | Evidence |
|---|---|---|---|---|---|
| AU-01 | P0 | FR-AUTH-01 T01 D3 | AUTH-01 login seed Player/GM/CS; AUTH-05 bằng token tương ứng; thử password sai | 200 TokenResponse, đúng sub/role; sai 401 INVALID_CREDENTIALS | Postman; claims đã che token |
| AU-02 | P0 | FR-AUTH-01 NFR-INT-01 T01 D5 | AUTH-02 với Google ID token thật hợp lệ, login lặp cùng subject; fixture invalid audience/expired; email conflict | 200 Player cùng userId; không auto-link email; 401 INVALID_GOOGLE_TOKEN hoặc 409 IDENTITY_CONFLICT; không tạo GM/CS | Google sandbox/test user và Auth logs redacted; mock negative ghi riêng |
| AU-03 | P0 | FR-AUTH-02 T01 | AUTH-03 refresh; dùng refresh cũ; AUTH-04 logout lặp; thử refresh mới sau logout | 200 rotate, refresh cũ 401; logout 204 no body; refresh đã revoke 401; access token cũ không buộc revoke ngay | Postman + AuthDb hash/revoke metadata |
| AU-04 | P0 | FR-AUTH-04 FR-CS-02 NFR-SEC-01 T10 | AUTH-I01 bằng gateway key; thử thiếu key/sai scope; stop Auth khi gọi protected API | 200 đúng status; 401/403; Auth unavailable fail closed (503), không mutate | HTTP và log |
| AU-05 | P0 | FR-AUTH-04 FR-CS-02 NFR-SEC-01 T10 | CS ADM-07 ban P đang có JWT; AUTH-01/03, WAL-01, INV-01 với token cũ; unban; thử JWT tamper/expired | Banned 403 cả login/refresh/protected routes; JWT invalid/expired 401; unban account cho login mới, refresh cũ vẫn revoked | Postman + audit |
| OW-01 | P0 | FR-INV-01 FR-WAL-01 FR-QRY-03 FR-QRY-04 NFR-SEC-01 T11 | A và B có kho/ví khác nhau; INV-01, WAL-01/02, MKT-06/07 bằng token A; thử query userId=B và X-User-Id=B | Chỉ A (hoặc validation reject input không hỗ trợ); không lộ item/ledger/listing/trade B; thêm chiều B->A | Response đã che định danh + fixture owner snapshot |
| OW-02 | P0 | FR-WAL-02 FR-QRY-04 FR-MKT-03 NFR-SEC-01 T11 | C không tham gia trade A/B; WAL-05 của B, MKT-08 của A/B, MKT-05 non-Active của A, DELETE listing A bằng C | 404 resource tương ứng; dữ liệu/lock không đổi; Active listing công khai cho Player vẫn xem được | HTTP + state trước/sau |
| OW-03 | P0 | FR-GM-04 FR-CS-01 FR-CS-02 NFR-SEC-01 T09 T10 T11 | Player gọi ADM-01..07; GM gọi CS routes; CS gọi GM routes; GM/CS gọi player wallet/inv | 403 FORBIDDEN; không thay đổi policy/account; không lộ trace | Postman role matrix |

## Wallet and payment

| ID | Priority | Traceability | Fixture / steps | Expected result | Evidence |
|---|---|---|---|---|---|
| WA-01 | P0 | FR-WAL-01 FR-WAL-02 T02 | Player mới WAL-01/02, WAL-03; list ledger pageSize=1; gói không tồn tại WAL-04 | 200 zero wallet và packages server; ledger own; 404 PACKAGE_NOT_FOUND | HTTP + WalletDb |
| WA-02 | P0 | FR-WAL-02 NFR-CONS-04 T02 | WAL-04 packageId hợp lệ, key K; replay K; K với package khác; poll WAL-05 | 201 Pending có URL/Location khi provider OK; replay cùng TopUpId; 409 IDEMPOTENCY_CONFLICT khi payload khác; chưa cộng Gem | HTTP + order count |
| WA-03 | P0 | FR-WAL-03 NFR-INT-01 NFR-CONS-01 T02 D5 | Thực hiện order Sandbox thật; người dùng mở order_url, trả test payment; callback qua ngrok; poll | Paid, provider ID khớp, total +gemAmount, hold không đổi, 1 TopUp effect; redirect một mình không credit | Sandbox + callback/ledger before-after |
| WA-04 | P0 | FR-WAL-03 NFR-SEC-02 T02 | WAL-06 parse được nhưng MAC sai; MAC hợp lệ test fixture với amount/order/app_id sai hoặc type không hỗ trợ | 200 provider acknowledgement theo contract (-1 MAC/type, 0 discrepancy theo 22B); không credit; malformed HTTP body 400 | Local signed fixture, wallet snapshot |
| WA-05 | P0 | FR-WAL-03 NFR-CONS-04 T02 | Cùng signed callback replay song song với query cùng appTransId/zp_trans_id; thêm Message/operation khác nhưng cùng business ID | Một Paid, một TopUp ledger effect; exact duplicate return_code 2; không credit lần 2; conflicting provider ID flag/reconcile | Local integration + ledger unique; sandbox evidence riêng |
| WA-06 | P0 | FR-WAL-02 FR-WAL-03 NFR-REL-02 T02 | Provider create timeout sau persist; WAL-04 trả 202; retry cùng key; callback bị miss, job query | Cùng Pending/TopUpId; query confirmed paid mới credit; terminal confirmed rejected/expired mới đổi trạng thái; unknown giữ Pending | Adapter fault fixture + query log |
| WA-07 | P1 | FR-WAL-03 NFR-REL-01 NFR-CONS-01 T02 | Inject lỗi SQL trước commit Paid/credit/ledger, rồi phát lại callback | Không có partial credit/paid; return_code 0 khi transient; lần sau commit đúng một lần | SQL transaction, callback logs |

## Inventory and listing

| ID | Priority | Traceability | Fixture / steps | Expected result | Evidence |
|---|---|---|---|---|---|
| IN-01 | P0 | FR-INV-01 FR-INV-02 T03 | INV-I01 seeder key + grant user/template/qty; replay; template sai, full capacity; INV-01 | 201 item một lần, 404 TEMPLATE_NOT_FOUND, 409 INVENTORY_FULL khi đầy, không tăng slot sai | Private integration + inventory snapshot |
| IN-02 | P0 | FR-INV-03 FR-INV-04 NFR-CONS-04 T03 T04 | INV-I02 metadata; INV-I03 lock listing L, replay; lock L2 cùng item; INV-I05; INV-I04 unlock L; gửi lại lock cũ | 200 same lock; L2 409 ITEM_LOCKED; unlock đúng L, tombstone; lock cũ không tái khóa; wrong listing/transfer pending reject | Private integration + item state |
| MK-01 | P0 | FR-MKT-01 FR-MKT-02 FR-QRY-02 T03 | Seller item Available, template policy reference=1000 band=20%; MKT-01 body itemId/quantity=1/unitPrice=1000, key; MKT-05 | 201 Active, item Locked đúng listingId, giá snapshot, event version; exact replay không thêm listing | HTTP + DB/event |
| MK-02 | P0 | FR-MKT-01 FR-MKT-02 FR-INV-03 NFR-SEC-02 T03 | Item của B, nontradable, locked, quantity mismatch; unitPrice=799/1201; policy cache miss và Admin unavailable | 409 ITEM_NOT_OWNED/ITEM_NOT_TRADABLE/ITEM_LOCKED/QUANTITY_MISMATCH; 422 PRICE_OUT_OF_BOUNDS; 503 PRICE_POLICY_UNAVAILABLE; không Active/lock rò | Postman + inventory |
| MK-03 | P1 | FR-MKT-02 FR-INV-04 NFR-CONS-02 NFR-REL-02 T03 | Persist PendingLock, REST lock commit rồi mất reply/activation lỗi; replay cùng key; mô phỏng lỗi xác định | 202 PendingLock khi unknown; recover cùng ID Active hoặc confirmed compensated CreateFailed; không unlock mù, không orphan lock | Fault fixture + listing/lock trace |
| MK-04 | P0 | FR-MKT-03 FR-INV-04 FR-QRY-01 T04 | MKT-02 owner Active; replay; poll unlockStatus và catalog; thử Processing | 200 Cancelled, eventual Available và listing hidden; replay không side effects; Processing 409 LISTING_BUSY hoặc state conflict hợp lệ | HTTP, events, SQL/Mongo |
| MK-05 | P0 | FR-MKT-04 BR-09 NFR-SEC-02 T05 | Seller tự purchase listing mình MKT-03; thiếu key; thử gửi price/quantity từ client | 409 SELF_PURCHASE; missing key 400; extra fields reject/ignore nhất quán, không thay snapshot; không tạo trade trái rule | HTTP + state |

## Distributed transaction

| ID | Priority | Traceability | Fixture / steps | Expected result | Evidence |
|---|---|---|---|---|---|
| SG-01 | P0 | FR-MKT-04 FR-MKT-05 FR-WAL-04 FR-WAL-05 FR-INV-05 FR-QRY-04 NFR-CONS-01 T05 D4 | Buyer A (1500,0), seller B (350,0), listing gross=1000 fee=50, đủ slot; MKT-03 rồi MKT-08 poll | 202 durable trade; hold→transfer→settle→finalize; buyer (500,0), seller (1300,0), supply -50; item A Available; Sold, Completed; 1 history/effect | Before/after balances, ownership, trace, broker |
| SG-02 | P0 | FR-WAL-04 FR-MKT-05 NFR-CONS-02 T07 | Buyer có 999 Gem mua listing 1000 | 202 nếu accepted; Failed INSUFFICIENT_FUNDS; listing Active, không hold/transfer/fee, seller item Locked | Trade trace + DB |
| SG-03 | P0 | FR-WAL-06 FR-INV-05 FR-MKT-05 NFR-CONS-02 T07 D4 | Buyer đủ tiền nhưng kho đầy, purchase rồi poll | Held rồi transfer rejected INVENTORY_FULL, release; Total như trước, Hold như trước; listing Active, seller item Locked, trade Failed chỉ sau release | Saga steps + ledger + inventory |
| SG-04 | P0 | FR-WAL-05 FR-WAL-06 FR-INV-05 FR-MKT-05 NFR-CONS-02 T07 | Transfer commit PendingSettlement, inject definitive settlement rejection với durable rejection guard | Reverse item về seller Locked trước release; seller slot đã reserve; không settle muộn; totals/hold restore, listing Active, Failed | Fault fixture + event sequence |
| SG-05 | P0 | FR-MKT-05 FR-WAL-05 FR-INV-05 NFR-CONS-02 NFR-REL-02 T07 | Settle commit nhưng drop reply/timeout; giữ worker trước recovery; retry/query cùng operation | Không refund hoặc reverse mù, không reopen listing; PendingSettlement đến khi xác định, sau settle forward finalize, Completed một lần hoặc flagged requiresReview nếu chưa rõ | Trace, wallet settlement record |
| SG-06 | P0 | FR-INV-05 FR-MKT-05 NFR-CONS-02 NFR-REL-02 T07 | Settle succeeded; inject transient finalize failure, rồi khôi phục | Không refund; item PendingSettlement không dùng/bán; retry finalize → Available, Sold/Completed; không credit lại | Fault + state and ledger |
| SG-07 | P1 | FR-WAL-04 FR-WAL-05 FR-WAL-06 FR-INV-05 NFR-CONS-04 T07 | Redeliver từng command/result cùng operation, test khác MessageId; thử release sau Settled, stale success event | Mỗi business operation một effect, no invalid state advance; settled hold không Released, không nhân item | Consumer integration + counts |
| SG-08 | P1 | FR-MKT-05 NFR-REL-02 T07 | Permanent fault finalize/reverse/release/unlock hết retry, sau chẩn đoán redrive cùng operation | Error queue/trace requiresReview hoặc trạng thái đang recovery; chưa Failed/Completed sai; redrive an toàn | Broker + trace |

## Concurrency

| ID | Priority | Traceability | Fixture / steps | Expected result | Evidence |
|---|---|---|---|---|---|
| CC-01 | P0 | FR-MKT-04 FR-MKT-05 NFR-CONC-01 T06 | 100 funded buyers đủ slot, cùng listing mới, distinct keys; barrier đồng thời; poll winner | Healthy: 1 Completed, 99 conflicts (busy/not active); rate limit/timeout ghi riêng, không giả conflict; không duplicate item/settle | Raw requests + all trade/ledger snapshots |
| CC-02 | P0 | FR-WAL-04 FR-MKT-04 NFR-CONC-02 T06 | Một buyer 1500 Gem, hai listings 1000 mỗi cái, different keys; concurrent claims | Tối đa một settle, hold guard ngăn overspend; trade còn lại Failed nếu đã accept; Total/Hold không âm | Both trade traces + wallet |
| CC-03 | P0 | FR-MKT-04 NFR-CONC-02 NFR-REL-03 T06 | Pause saga sau SQL Processing >20s (TTL nếu đã giữ; nếu release ngay test acquire mới); competing purchase; compare-delete token A sau token B | SQL guard chặn claim mới, Processing không tự Active; token B không bị xóa; một Completed tối đa | Redis/SQL trace |
| CC-04 | P0 | FR-MKT-03 FR-MKT-04 NFR-CONC-03 T04 T06 | Race owner cancel vs buyer purchase cùng Active, chạy 10 fresh listings (đề xuất) | Chỉ một transition thắng; cancel thắng→eventual unlock, không trade accepted; mua thắng→không unlock khi Processing | Race logs + inventory |

## Read model

| ID | Priority | Traceability | Fixture / steps | Expected result | Evidence |
|---|---|---|---|---|---|
| CQ-01 | P0 | FR-QRY-01 FR-QRY-02 NFR-CONS-03 T08 | Create, Processing, Reactivated, Cancelled/Sold; pause projection; list Mongo vs detail SQL | Chỉ Active projected, stale browse có thể xảy ra nhưng purchase guard SQL đúng; khi restart projection hội tụ | SQL commit/project timestamps |
| CQ-02 | P0 | FR-QRY-01 NFR-CONS-04 T08 | Inject version 3 Sold/Cancelled, sau đó v1/v2 và duplicate v3; test Reactivated v4 cho listing phù hợp khác | Không resurrect terminal; v mới hợp lệ của listing khác apply; version/tombstone giữ lại | Mongo docs + events |
| CQ-03 | P0 | FR-QRY-02 FR-QRY-03 FR-QRY-04 T08 T11 | Seed khác category/rarity/price; MKT-04 search/filter/sort/page; MKT-06 own default Active/filter; MKT-07 side=buy/sell/all; invalid pageSize=101,min>max | 200 DTO, deterministic tie-break, đúng totalCount/filter/owner; invalid 400 VALIDATION_ERROR | Postman response snapshot |

## Administration

| ID | Priority | Traceability | Fixture / steps | Expected result | Evidence |
|---|---|---|---|---|---|
| GM-01 | P0 | FR-GM-04 FR-MKT-01 NFR-SEC-03 T09 | ADM-03 list; ADM-04 GM with basePrice=1000 band=2000 expectedVersion; replay, stale version, invalid basePrice/band; old listing | 200 version tăng, SQL/Redis sync, audit; 409 VERSION_CONFLICT; 422 INVALID_PRICE_POLICY hoặc 400 schema invalid; old price unchanged | Policy snapshots + audit |
| GM-02 | P0 | FR-GM-01 FR-GM-03 NFR-PERF-03 T09 | Fresh template two completed trades q=1 gross=1000 fee=50; q=2 gross=2200 fee=110; duplicate event | Gross 3200, qty 3, fee 160; reference floor(3200/3)=1066, min unit=1000 max=1100; duplicate no increase; volume trade count/qty theo DTO | Facts, Redis, ADM-01 |
| GM-03 | P1 | FR-GM-02 FR-GM-03 NFR-PERF-03 T09 | Job 00:00 Asia/Ho_Chi_Minh với controlled clock; event late thuộc ngày trước; rerun; ADM-02 from/to | Upsert 1 row/day/template, late fact đúng ngày, không reset mất số; history [from,to) | Job logs + summaries |
| GM-04 | P0 | FR-GM-03 NFR-INT-01 T09 | ADM-01 GM; WAL-I02 supply, MKT-I03 listing stats; một source stop | Healthy đúng totalGem/active count; failure partial=true/null không giả 0; volume khác supply | API + source read-only evidence |
| CS-01 | P0 | FR-CS-01 NFR-SEC-03 T10 | ADM-06 by userId; ADM-05 trade SG-01/03; MKT-I01/I02, WAL-I01, INV-I06 trực tiếp scoped test; stop Inventory | Trace đúng step/hold/ledger/transfer; partial phân biệt missing record/lỗi; missing trade 404 | Trace + source responses |
| CS-02 | P0 | FR-CS-02 FR-AUTH-04 NFR-SEC-03 T10 | ADM-07 restrict P bằng key; replay, thử target GM/CS; AUTH-I02 chỉ Admin; P đọc/hủy/nạp, thử create/purchase; đã accept trade | 200 + actionId/audit; replay same action; invalid target 409; restricted create/purchase 403 TRADE_RESTRICTED, đọc/hủy/nạp allowed; accepted saga continues | Postman, audit, trace |

## Reliability, deployment and contracts

| ID | Priority | Traceability | Fixture / steps | Expected result | Evidence |
|---|---|---|---|---|---|
| RL-01 | P0 | FR-MKT-05 NFR-REL-01 NFR-CONS-04 T11 D4 | Stop consumer trước publish; accepted trade; restart; restart broker giữ volume ở fixture riêng | Durable message được xử lý, no lost accepted intent hoặc duplicate effect; trạng thái không terminal sai | Queue depth/outbox/trace |
| RL-02 | P1 | FR-MKT-02 FR-MKT-05 NFR-REL-01 T11 | Stop broker sau local state/outbox commit hoặc dispatcher disabled; restart | Outbox pending bền vững; publish sau recovery; idempotent consumers | Outbox rows, broker/events |
| RL-03 | P1 | FR-GM-01 FR-GM-04 FR-MKT-01 NFR-REL-03 T09 T11 | Xóa riêng test price/analytics keys (không FLUSHALL), Redis unavailable, restart/rebuild | Price cache miss gọi Admin; không fallback 0; nếu cả hai lỗi 503; facts→aggregate đúng; SQL state không mất | Redis keys + SQL/fault logs |
| DP-01 | P0 | NFR-SCAL-01 NFR-SCAL-02 T11 D1 D2 | Clean isolated compose project, migration/seed, restart seed; inspect health + DB credentials | 4 infra healthy, 6 apps ready, 12 containers baseline; seed idempotent; DB owner isolation/no cross-db query; known provider unavailable scoped | compose ps, probes, migration output |
| DP-02 | P0 | NFR-SEC-01 NFR-SCAL-02 NFR-OBS-01 T11 D7 | Test ngrok /internal/v1, /metrics, /health/*, /swagger, ports; internal key mismatch; verify valid allowlist local/public | Public paths denied/not routed (no sensitive body), DB/broker/UI not exposed; authorized private path only | curl/Postman, binding config redacted |
| DP-03 | P0 | NFR-OBS-01 NFR-SEC-03 T11 D6 | Generate valid/invalid traffic; scrape Prometheus 6 targets; view Grafana; stop test service/restart | 6 UP when healthy, request/error/latency panel real data; down recovery; no secrets or UUID labels; correlation logs trace | Targets/dashboard/log screenshots |
| CT-01 | P0 | NFR-INT-01 NFR-TEST-01 T11 | Run 41 operations manifest, schema/headers/Location/204/no body, invalid UUID/page/key, auth matrix; compare shared message definitions | Status/DTO/ProblemDetails codes as contract; callback exception; same version + payload/queue, no public consumer endpoint | Postman collection/validation and xUnit output |

## Latency

| ID | Priority | Traceability | Fixture / steps | Expected result | Evidence |
|---|---|---|---|---|---|
| PF-01 | P1 | FR-QRY-02 NFR-PERF-01 T08 | Warm catalog fixture; GET MKT-04 query benchmark described in Performance-Testing | Mean <=100ms target (50ms stretch); report p95/errors/data and verify Mongo read path; failure reported | Raw latency and route/query trace |
| PF-02 | P1 | FR-MKT-01 NFR-PERF-02 T03 | Measure Redis price read only with seeded key (not whole HTTP) | Mean <5ms target; record p95/errors/cache hit and environment | Instrumentation sample |
| PF-03 | P1 | FR-QRY-01 NFR-CONS-03 T08 | Measure SQL commit to Mongo apply for listing events, healthy broker | Target 1–2s; report mean/p95/max and stale count; no SLA | Correlated timestamps |
