# Test Report — Execution Report

version: 1.1.0 | In Progress — Partial Execution (Unit Tests + Auth/Gateway/Admin E2E completed; Marketplace Saga/k6 and Wallet ZaloPay results pending teammate merge)

Only verified runs executed on real containers and xUnit assemblies are recorded below. Scenarios owned by other team members (`SG-*`, `WA-*`, `IN-*`, `MK-*`, `CC-*`, `CQ-*`, `PF-*`) and upcoming staging/monitoring tasks (`DP-*`, `RL-*`, `CT-01`) remain `Not Run` until their evidence is merged.

## 1. Run metadata

| Field | Actual |
|---|---|
| Commit / tag | `e4f422e` (+ `AuthService.RefreshAsync` `DEF-02` fix committed alongside this report) |
| Date & executor | `2026-11-28` UTC — Ly Tin Hoa (TV 3: Auth / Gateway / Admin & CI) |
| Host OS / CPU / RAM / Docker limits | Windows 11, Intel Core i7-1255U, 16 GB RAM, NVMe SSD, Docker Desktop (default engine limits) |
| Image/.NET versions, dataset, fee/band/TTL/capacity | `.NET 9` (`mcr.microsoft.com/dotnet/sdk:9.0` & `aspnet:9.0`), SQL Server `2022-CU16`, MongoDB `8.0`, Redis `7.4.12`, RabbitMQ `4.0`; seeded 5 users + 4 templates; fee `500 bps` (5%), band `2000 bps` (±20%), lock TTL `20s`, inventory capacity `20` |
| Google / ZaloPay Sandbox credentials available? | Google OAuth 2.0 Web Client ID: **Yes** (real GIS test user verified locally); ZaloPay Sandbox: **Not configured on this host** (owned by Tuan) |
| Run environment / tools / sample size | Local Docker Compose (10 running services + 2 init jobs), xUnit (`24` unit tests across 3 assemblies), PowerShell `HttpClient` E2E script (`45` assertions), `tools/google-login-demo/index.html` (`http://localhost:5500`) |

## 2. Execution summary

| Measure | Value |
|---|---|
| Planned scenarios | 58 |
| Executed | 17 (`UT-01..05`, `AU-01..05`, `OW-01..03`, `GM-01`, `GM-02`, `GM-04`, `CS-02`) |
| Pass / Fail / Blocked / N/A | 17 / 0 / 0 / 0 |
| Not Run | 41 |
| Pass rate among Pass + Fail | 100.0% (`17 / 17`) |
| FR/NFR validated | Partial — Auth, Gateway RBAC/fail-closed status, Object Ownership, Admin GM Policy/Analytics, CS Moderation, and core domain unit invariants (`17/48` P0 scenarios passed) |
| Release gate | **In Progress** (pending `SG-*`, `WA-*`, `IN-*`, `MK-*`, `CC-*`, `CQ-*`, `RL-*`, `DP-*`, `CT-01` runs) |

Counts match `docs/05-Testing/Test-Execution-Register.csv` 1:1.

## 3. Business / architecture result

| Gate | Actual outcome | Evidence |
|---|---|---|
| T01..T11 (each individually in RTM/register) | Partial (`T01`, `T09`, `T10` partial, `T11` ownership passed; remaining pending teammate runs) | `docs/05-Testing/Test-Execution-Register.csv` |
| D1..D7 (each individually in RTM/register) | Partial (`D3` seed login & `D5` Google real login passed; `D1/D2/D4/D6/D7` pending) | `tests/results/2026-11-28-auth-gw-admin/summary.md` |
| Successful purchase and fee audit | Not Run (`SG-01` owned by Duy; `UT-04` settlement math passed in xUnit; 1 live completed trade `7e52fda9-ceac-4705-bce8-f1c21f64d2e1` observed as `OW-02` fixture) | Pending `SG-01` merge |
| Full inventory compensation | Not Run (`SG-03` owned by Duy) | Pending `SG-03` merge |
| Concurrent purchase 100 clients | Not Run (`CC-01` owned by Duy) | Pending `CC-01` merge |
| Object ownership isolation | **Pass** (`OW-01` 9/9, `OW-02` 7/7, `OW-03` 18/18 — zero cross-user leakage or RBAC bypass) | `tests/results/2026-11-28-auth-gw-admin/summary.md` |
| Google real integration | **Pass** (`AU-02`: real Google ID token -> JIT `Player`, repeat same `userId`, `401` on invalid/forged/internal tokens, `409` no email auto-link, `403` banned) | `tests/results/2026-11-28-auth-gw-admin/summary.md` |
| ZaloPay Sandbox actual payment & callback/query | Not Run (`WA-03..06` owned by Tuan) | Pending `WA-03..06` merge |

## 4. Performance results

| Metric | Target | Actual mean | P95 / max | Samples / errors | Assessment |
|---|---|---|---|---|---|
| Catalog HTTP | <=100ms mean | TBD | TBD | TBD | Not Run (pending Duy `PF-01` merge) |
| Redis price read | <5ms mean | TBD | TBD | TBD | Not Run (pending Duy `PF-02` merge) |
| CQRS lag | 1–2s target | TBD | TBD | TBD | Not Run (pending Duy `PF-03` merge) |
| Concurrent trade | 1 Completed / 100 attempts | TBD | N/A | TBD | Not Run (pending Duy `CC-01` merge) |

## 5. Defect register

| Defect ID | Case | Severity | Steps / expected | Actual | Evidence | Owner | Status / retest commit |
|---|---|---|---|---|---|---|---|
| DEF-01 | AU-02 | Medium | `POST /api/v1/auth/google` with malformed 3-segment string (`invalid.google.token`); expected `401 INVALID_GOOGLE_TOKEN` | Returned `500 INTERNAL_ERROR` due to uncaught JSON reader exception inside `Google.Apis.Auth` | `tests/results/2026-11-28-auth-gw-admin/summary.md` | Ly Tin Hoa | Fixed & Retested Pass (`GoogleTokenValidator.cs`) |
| DEF-02 | AU-05 | Medium | CS bans Player via `ADM-07`, then banned user calls `POST /api/v1/auth/refresh`; expected `403 ACCOUNT_BANNED` while banned, and `401 INVALID_REFRESH_TOKEN` after unban | Returned `401 INVALID_REFRESH_TOKEN` while banned because `RevokedAt` was checked before `AccountStatus.Banned` | `tests/results/2026-11-28-auth-gw-admin/summary.md` | Ly Tin Hoa | Fixed & Retested Pass (`AuthService.cs`, verified on `2026-11-28`) |

## 6. Evidence folder
`tests/results/2026-11-28-auth-gw-admin/summary.md` stores redacted execution summaries for xUnit (`UT-01..05`), real Google Sign-In (`AU-02`), and live Gateway/Auth/Admin E2E checks (`AU-01`, `AU-03..05`, `OW-01..03`, `GM-01..02`, `GM-04`, `CS-02`). No raw credentials, JWTs, callback keys, PII, or database dumps are committed.

## 7. Conclusion / sign-off
- **Actual scope achieved (so far):** `17/58` planned scenarios executed and passed (`100.0%` of executed), covering all 5 core xUnit suites (`UT-01..05`), full Identity & Access (`AU-01..05`), full Object Ownership & RBAC (`OW-01..03`), and Admin GM/CS policy, analytics, and moderation (`GM-01`, `GM-02`, `GM-04`, `CS-02`). Both defects discovered during execution (`DEF-01`, `DEF-02`) were fixed and re-verified.
- **Known limitations / deferred cases:** `GM-03` (controlled-clock `00:00` job verification) and `CS-01` (stop-inventory partial trace sub-step) remain `Not Run` pending full dry-run; Duy's Saga/Concurrency/CQRS/Performance runs (`SG-*`, `CC-*`, `CQ-*`, `MK-*`, `PF-*`) and Tuấn's Wallet/ZaloPay/Inventory runs (`WA-*`, `IN-*`) have not yet been merged into this report.
- **Remaining risks:** End-to-end monitoring (`DP-03`), clean-slate 12-container staging (`DP-01`), and ngrok public boundary audit (`DP-02`) are still pending.
- **Group acceptance / release recommendation:** Not evaluated — awaiting completion of remaining teammate integration runs and final Demo Dry-run.
