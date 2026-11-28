# Test Report — Execution Report

version: 1.2.0 | In Progress — Partial Execution (Unit Tests, Auth/Gateway/Admin E2E, Marketplace Saga/Fault/CQRS & k6 CC-01/PF-01 completed; Wallet ZaloPay & Inventory results pending teammate merge)

Only verified runs executed on real containers, Grafana k6, and xUnit assemblies are recorded below. Scenarios owned by Tuấn (`WA-*`, `IN-*`), remaining Marketplace HTTP/concurrency checks (`MK-01..02`, `MK-04..05`, `CC-02..03`, `CQ-01`, `CQ-03`, `PF-02..03`), and staging/monitoring gates (`DP-*`, `RL-*`, `CT-01`) remain `Not Run` until their evidence is merged.

## 1. Run metadata

| Field | Actual |
|---|---|
| Commit / tag | `e4f422e` (Auth/GW/Admin + `DEF-02` fix) & `5d89d89` (+ `TradeSagaFaultTests.cs` for Marketplace Saga/k6 run) |
| Date & executor | `2026-11-28` UTC — Ly Tin Hoa (TV 3: Auth / Gateway / Admin & CI) & Tran Quang Duy (TV 1: Marketplace / Saga / k6) |
| Host OS / CPU / RAM / Docker limits | Windows 11 hosts — i7-1255U / 16 GB RAM (Auth/GW/Admin run) & Lenovo LOQ 15IAX9 (Marketplace Saga/k6 run), Docker Desktop (default engine limits) |
| Image/.NET versions, dataset, fee/band/TTL/capacity | `.NET 9` (`mcr.microsoft.com/dotnet/sdk:9.0` & `aspnet:9.0`), SQL Server `2022-CU16`, MongoDB `8.0`, Redis `7.4.12`, RabbitMQ `4.0`; seeded 5 users + 4 templates (+ 100 transient `CC-01` buyers); fee `500 bps` (5%), band `2000 bps` (±20%), lock TTL `20s`, inventory capacity `20` |
| Google / ZaloPay Sandbox credentials available? | Google OAuth 2.0 Web Client ID: **Yes** (real GIS test user verified locally); ZaloPay Sandbox: **Not configured on these hosts** (owned by Tuấn) |
| Run environment / tools / sample size | Local Docker Compose (10 running services + 2 init jobs), xUnit (`28` tests in `Marketplace.API.Tests` + `Wallet.API.Tests` + `Admin.API.Tests`), PowerShell `HttpClient` E2E scripts, `tools/google-login-demo/index.html`, Grafana k6 (`grafana/k6`: `200` `PF-01` iterations, `100`-VU `CC-01` barrier race) |

## 2. Execution summary

| Measure | Value |
|---|---|
| Planned scenarios | 58 |
| Executed | 30 (`UT-01..05`, `AU-01..05`, `OW-01..03`, `MK-03`, `SG-01..08`, `CC-01`, `CC-04`, `CQ-02`, `GM-01`, `GM-02`, `GM-04`, `CS-02`, `PF-01`) |
| Pass / Fail / Blocked / N/A | 30 / 0 / 0 / 0 |
| Not Run | 28 |
| Pass rate among Pass + Fail | 100.0% (`30 / 30`) |
| FR/NFR validated | Partial — Auth, Gateway RBAC/fail-closed status, Object Ownership, Admin GM Policy/Analytics, CS Moderation, core domain unit invariants, full Distributed Trade Saga & fault recovery (`SG-01..08`), 100-buyer concurrency (`CC-01`, `CC-04`), CQRS tombstone guard (`CQ-02`), and Mongo catalog latency (`PF-01`) (`26/48` P0 + `4/10` P1 passed) |
| Release gate | **In Progress** (pending `WA-*`, `IN-*`, remaining `MK-*`/`CC-02..03`/`CQ-01,03`, `RL-*`, `DP-*`, `CT-01` runs) |

Counts match `docs/05-Testing/Test-Execution-Register.csv` 1:1.

## 3. Business / architecture result

| Gate | Actual outcome | Evidence |
|---|---|---|
| T01..T11 (each individually in RTM/register) | Partial (`T01`, `T05`, `T07`, `T09` passed; `T03`, `T04`, `T06`, `T08`, `T10`, `T11` partial; `T02` pending Tuấn) | `docs/05-Testing/Test-Execution-Register.csv` |
| D1..D7 (each individually in RTM/register) | Partial (`D3` seed login, `D4` live Saga happy/compensation, `D5` Google real login passed; `D1/D2/D6/D7` & ZaloPay `D5` pending) | `tests/results/2026-11-28-auth-gw-admin/summary.md` & `tests/results/2026-11-28-marketplace-saga-k6/summary.md` |
| Successful purchase and fee audit | **Pass** (`SG-01` live E2E: `Completed`, `gross=1000`, `fee=50`, `sellerNet=950`, `Listing=Sold`, `Seller=950`, `Buyer=500 (Hold=0)`, `BuyerItems=1`; `ADM-05` trace verified `TaxBurn=50` and 4 ledger entries) | `tests/results/2026-11-28-marketplace-saga-k6/summary.md` |
| Full inventory compensation | **Pass** (`SG-03` live E2E: `20/20` full-bag buyer held then rejected `INVENTORY_FULL`, refunded to `5000 (Hold=0)`, listing reactivated to `Active`) | `tests/results/2026-11-28-marketplace-saga-k6/summary.md` |
| Concurrent purchase 100 clients | **Pass** (`CC-01` k6 race: `1 Completed` (`buyer #88`) / `99 Conflict 409` / `0` errors; SQL audit confirmed `99` non-winners untouched with `0` lingering holds, single `Available` owner, and `ADM-05` trace `partial=false`) | `tests/results/2026-11-28-marketplace-saga-k6/summary.md` |
| Object ownership isolation | **Pass** (`OW-01` 9/9, `OW-02` 7/7, `OW-03` 18/18 — zero cross-user leakage or RBAC bypass) | `tests/results/2026-11-28-auth-gw-admin/summary.md` |
| Google real integration | **Pass** (`AU-02`: real Google ID token -> JIT `Player`, repeat same `userId`, `401` on invalid/forged/internal tokens, `409` no email auto-link, `403` banned) | `tests/results/2026-11-28-auth-gw-admin/summary.md` |
| ZaloPay Sandbox actual payment & callback/query | Not Run (`WA-03..06` owned by Tuấn) | Pending `WA-03..06` merge |

## 4. Performance results

| Metric | Target | Actual mean | P95 / max | Samples / errors | Assessment |
|---|---|---|---|---|---|
| Catalog HTTP (`PF-01`) | <=100ms mean (50ms stretch) | `10.37ms` | `13.26ms` / `24.30ms` | `200` iterations (`400/400` checks) / `0` errors | **Pass** (meets target & stretch) |
| Redis price read (`PF-02`) | <5ms mean | TBD | TBD | TBD | Not Run |
| CQRS lag (`PF-03`) | 1–2s target | TBD | TBD | TBD | Not Run |
| Concurrent trade (`CC-01`) | 1 Completed / 100 attempts | `316.22ms` HTTP (`685ms` Saga) | `505.51ms` / `521.67ms` | `100` buyers (`203` reqs) / `0` errors (`1` `202` + `99` `409`) | **Pass** (SQL & `ADM-05` audited) |

## 5. Defect register

| Defect ID | Case | Severity | Steps / expected | Actual | Evidence | Owner | Status / retest commit |
|---|---|---|---|---|---|---|---|
| DEF-01 | AU-02 | Medium | `POST /api/v1/auth/google` with malformed 3-segment string (`invalid.google.token`); expected `401 INVALID_GOOGLE_TOKEN` | Returned `500 INTERNAL_ERROR` due to uncaught JSON reader exception inside `Google.Apis.Auth` | `tests/results/2026-11-28-auth-gw-admin/summary.md` | Ly Tin Hoa | Fixed & Retested Pass (`GoogleTokenValidator.cs`) |
| DEF-02 | AU-05 | Medium | CS bans Player via `ADM-07`, then banned user calls `POST /api/v1/auth/refresh`; expected `403 ACCOUNT_BANNED` while banned, and `401 INVALID_REFRESH_TOKEN` after unban | Returned `401 INVALID_REFRESH_TOKEN` while banned because `RevokedAt` was checked before `AccountStatus.Banned` | `tests/results/2026-11-28-auth-gw-admin/summary.md` | Ly Tin Hoa | Fixed & Retested Pass (`AuthService.cs`, verified on `2026-11-28`) |

## 6. Evidence folder
- `tests/results/2026-11-28-auth-gw-admin/summary.md` stores redacted execution summaries for xUnit (`UT-01..05`), real Google Sign-In (`AU-02`), and live Gateway/Auth/Admin E2E checks (`AU-01`, `AU-03..05`, `OW-01..03`, `GM-01..02`, `GM-04`, `CS-02`).
- `tests/results/2026-11-28-marketplace-saga-k6/summary.md` stores redacted execution summaries for `Marketplace.API.Tests` (`28/28`, covering `MK-03`, `SG-04..08`, `CC-04`, `CQ-02`), live Saga E2E (`SG-01..03`), `CC-01` 100-buyer race + SQL audit + `ADM-05` trace, and `PF-01` catalog benchmark.
- No raw credentials, JWTs, callback keys, PII, or database dumps are committed.

## 7. Conclusion / sign-off
- **Actual scope achieved (so far):** `30/58` planned scenarios executed and passed (`100.0%` of executed), covering all 5 core xUnit suites (`UT-01..05`), full Identity & Access (`AU-01..05`), full Object Ownership & RBAC (`OW-01..03`), Admin GM/CS policy, analytics, and moderation (`GM-01`, `GM-02`, `GM-04`, `CS-02`), full Distributed Trade Saga & fault recovery (`SG-01..08`), `MK-03`, `CC-01`, `CC-04`, `CQ-02`, and `PF-01`. Both defects discovered during execution (`DEF-01`, `DEF-02`) were fixed and re-verified.
- **Known limitations / deferred cases:** Tuấn's Wallet/ZaloPay/Inventory runs (`WA-01..07`, `IN-01..02`), remaining Marketplace/Admin sub-cases (`MK-01..02`, `MK-04..05`, `CC-02..03`, `CQ-01`, `CQ-03`, `GM-03`, `CS-01`, `PF-02..03`) remain `Not Run` pending merge and full dry-run.
- **Remaining risks:** End-to-end monitoring (`DP-03`), clean-slate 12-container staging (`DP-01`), ngrok public boundary audit (`DP-02`), durability (`RL-01..03`), and contract manifest sweep (`CT-01`) are still pending.
- **Group acceptance / release recommendation:** Not evaluated — awaiting Tuấn's `WA-*`/`IN-*` merge and final Demo Dry-run.