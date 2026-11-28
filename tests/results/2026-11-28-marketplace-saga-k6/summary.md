# Execution Evidence — Marketplace Saga, Fault Recovery & k6 Benchmarks

- **Run ID:** `2026-11-28-marketplace-saga-k6`
- **Base Commit:** `5d89d89` (+ `tests/Marketplace.API.Tests/TradeSagaFaultTests.cs` committed with this run)
- **Executor:** Tran Quang Duy
- **Date (UTC):** `2026-11-28T09:00:00Z`
- **Host:** Windows 11, Lenovo LOQ 15IAX9, Docker Compose (10 active containers + 2 init jobs), Grafana k6 (`grafana/k6` Docker image)
- **Scope:** Marketplace Saga E2E (`SG-01..03`), Saga Fault & Recovery xUnit suite (`SG-04..08`, `MK-03`, `CC-04`, `CQ-02`), 100-buyer concurrency race (`CC-01`) with authoritative SQL audit and `ADM-05` trace, and MongoDB catalog latency benchmark (`PF-01`).

## 1. xUnit Suite (`Marketplace.API.Tests` — `MK-03`, `SG-04..08`, `CC-04`, `CQ-02`)

- **Command:** `dotnet test tests/Marketplace.API.Tests --verbosity normal`
- **Result:** `Total: 28 | Passed: 28 | Failed: 0 | Skipped: 0 | Duration: 7.3s`

| Case | Test Class / Coverage | Result |
|---|---|---|
| `MK-03` | `ListingRecoveryTests` (`PendingLock` unknown reply -> `202` + idempotent replay -> recovers to `Active` when locked, or tombstones before `CreateFailed` when `NotFound`) | Pass |
| `SG-04` | `TradeSagaFaultTests.SG04_SettlementRejected_ReversesItemBeforeRefundingFundsAndReactivatesListing` (`Settling` -> `ReversingItem` -> `ReleasingFunds` -> `Failed`, listing back to `Active`) | Pass |
| `SG-05` | `ListingRecoveryTests.StalledSettlingSaga_WhenWalletAlreadySettled_RecoversForwardOnly` (settled hold recovers forward to `Finalizing` with zero refund/reverse) | Pass |
| `SG-06` | `TradeSagaFaultTests.SG06_TransientFinalizeFailure_RetriesWithoutRefundThenCompletesOnFinalized` (redrives same `OperationId` without refund -> `Completed`, listing `Sold`) | Pass |
| `SG-07` | `TradeSagaFaultTests.SG07_DuplicateAndStaleEvents_ProduceSingleBusinessEffectWithNoInvalidAdvance` (stale `BuyerFundsHeld`/`TradeSettled` and duplicate `ItemTransferFinalized` ignored; 1 `TradeCompletedEvent`) | Pass |
| `SG-08` | `ListingRecoveryTests.StalledSaga_ExhaustedRetries_FlagsRequiresReviewWithoutBlindRefund` + `TradeSagaFaultTests.SG08_FinalizingExhaustedRetries_FlagsRequiresReviewAndSupportsSafeManualRedrive` | Pass |
| `CC-04` | `CancelPurchaseRaceTests.CC04_CancelVsPurchaseRace_Across10FreshListings_ExactlyOneTransitionWins` (both with and without distributed lock across 10 listings) | Pass |
| `CQ-02` | `CatalogProjectionTests` (4 tests: `Sold`/`Cancelled` v3 tombstone rejects stale v1/v2 resurrection; higher-version non-terminal reactivation applies cleanly) | Pass |

## 2. Live Saga E2E Flows (`SG-01..03`)

Executed against live YARP Gateway (`http://127.0.0.1:8080`) with all 12 Docker Compose containers healthy (`seller`, `buyer` with `1500` Gem, `buyer.full` with `5000` Gem and `20/20` inventory slots):

| Case | Scenario | Observed Outcome |
|---|---|---|
| `SG-01` | Happy path (`1000` Gem listing, 5% fee burn) | `Trade=Completed`, `gross=1000`, `fee=50`, `sellerNet=950`, `Listing=Sold`, `SellerTotal=950`, `BuyerTotal=500 (Hold=0)`, `BuyerItems=1` |
| `SG-02` | Insufficient funds (`buyer` has `500` Gem for `1000` Gem listing) | `Trade=Failed`, `failureCode=INSUFFICIENT_FUNDS`, `Listing=Active`, `BuyerTotal=500 (Hold=0)` |
| `SG-03` | Full inventory `20/20` (`buyer.full` has `5000` Gem, full bag) | `Trade=Failed`, `failureCode=INVENTORY_FULL`, hold refunded cleanly, `Listing=Active`, `FullBuyerTotal=5000 (Hold=0)` |

## 3. Concurrency Race (`CC-01`) + Authoritative SQL Audit & `ADM-05` Trace

Executed `tests/k6/cc01-concurrent-purchase.js` with `100` distinct seeded buyers (`c0010000-...-0001..0100`, `10000` Gem each) hitting fresh listing `7cabd223-e3df-4633-9901-6f9f4309aa04` simultaneously behind a barrier (`RateLimiting__Auth__PermitLimit` temporarily raised to `200` for setup login, then restored):

- **k6 Thresholds (5/5 passed):** `cc01_accepted_202=1`, `cc01_completed_trades=1`, `cc01_conflict_409=99`, `cc01_failed_trades=0`, `cc01_unexpected_status=0`
- **HTTP Stats:** `203` total requests, `0.00%` failed, `http_req_duration`: `avg=316.22ms`, `p95=505.51ms`, `max=521.67ms`; `2.5s` race window (`6.4s` total)
- **Winning Trade:** `buyer #88` (`c0010000-0000-4000-8000-000000000088`), `tradeId=f278343d-caa9-49e5-a084-02a158b25f83`, `status=Completed`
- **SQL Server Audit:**
  - `WinnerWallet`: `TotalBalance=9000`, `HoldBalance=0` (charged once)
  - `NonWinnersUnchanged`: `UnchangedCount=99`, `TotalLingeringHold=0`
  - `SellerWallet`: `TotalBalance=1900`, `HoldBalance=0` (`950` from `SG-01` + `950` from `CC-01`)
  - `ItemOwner`: `a0010000-0000-4000-8000-000000000004` owned by `buyer #88`, `Status=Available`
- **CS Cross-Service Trace (`ADM-05`):** `GET /api/v1/admin/trades/f278343d-caa9-49e5-a084-02a158b25f83/trace` returned `200`, `partial=false`, `unavailableSources=[]`, `685ms` Saga completion (`HoldBuyerFunds` -> `TransferItem` -> `SettleTrade` -> `FinalizeItemTransfer` all `Succeeded`), 4 wallet ledger entries (`Hold`, `SettlementDebit -1000`, `SettlementCredit +950`, `TaxBurn 50`), and Inventory `Finalized`.

## 4. Catalog Read Latency Benchmark (`PF-01`)

Executed `tests/k6/pf01-catalog-search.js` against warm MongoDB read model through Gateway (`GET /api/v1/marketplace/listings`):

- **Samples & Errors:** `200` measured iterations (`400/400` checks passed: `status is 200` and `returns 20 items`), `pf01_errors=0`, `http_req_failed=0.00%` (`221` total requests including login/warmup)
- **Catalog Latency (`pf01_catalog_ms`):** `avg=10.37ms`, `min=7.65ms`, `med=9.92ms`, `p90=12.41ms`, `p95=13.26ms`, `max=24.30ms`
- **Assessment:** Passed (`<=100ms` target and `<=50ms` stretch both exceeded with `0` errors).