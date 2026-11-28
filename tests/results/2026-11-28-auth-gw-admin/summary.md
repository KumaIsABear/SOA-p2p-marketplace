# Execution Evidence — Auth / Gateway / Admin & Core Unit Tests

- **Run ID:** `2026-11-28-auth-gw-admin`
- **Base Commit:** `e4f422e` (+ `AuthService.RefreshAsync` `DEF-02` fix committed with this run)
- **Executor:** Ly Tin Hoa
- **Date (UTC):** `2026-11-28T08:30:00Z`
- **Host:** Windows 11, Intel Core i7-1255U, 16 GB RAM, NVMe SSD, Docker Compose (10 active containers + 2 init jobs)
- **External Credentials:** Google OAuth 2.0 Web Client ID configured locally in `.env` (real Google Identity Services test user verified); ZaloPay Sandbox credentials not configured on this host (owned by Tuan).

## 1. xUnit Core Test Suite (`UT-01..UT-05`)

Executed inside `mcr.microsoft.com/dotnet/sdk:9.0` matching `.github/workflows/ci.yml`:

- **Command:** `dotnet test Marketplace.sln -c Release --no-build --verbosity normal`
- **Assemblies:** `Marketplace.API.Tests`, `Wallet.API.Tests`, `Admin.API.Tests`
- **Result:** `Total tests: 24 | Passed: 24 | Failed: 0 | Skipped: 0 | Warnings: 0 | Errors: 0`

## 2. Google Identity Integration (`AU-02`)

Executed via `tools/google-login-demo/index.html` (`http://localhost:5500`) -> YARP Gateway (`http://127.0.0.1:8080`) -> `Auth.API`:

- Real Google ID token -> `HTTP 200`, JIT-provisioned `Player` account (`PasswordHash = NULL`, `Provider = https://accounts.google.com`, `Status = Active`, `Version = 1`).
- Repeat sign-in with same Google subject -> `HTTP 200` returning the same `userId` (no duplicate user created).
- Protected route checks (`AUTH-05`, `MKT-04`, `AUTH-03`, `AUTH-04`) succeeded using the issued internal HS256 JWT.
- Negative checks: malformed 3-segment token (`invalid.google.token`), non-JWT string (`not-even-a-jwt`), forged RS256 token (wrong `aud`/expired), and internal HS256 JWT all returned `401 INVALID_GOOGLE_TOKEN`; empty `idToken` returned `400 VALIDATION_ERROR`.
- Unlinked existing email check (deleted `ExternalIdentities` row only) -> `409 IDENTITY_CONFLICT` (no auto-link by email).
- Banned Google user check -> `403 ACCOUNT_BANNED`; restored to `Active` -> `200`.

## 3. E2E Moderation, Ownership & RBAC Matrix (`AU-05`, `OW-01..03`, `CS-01` happy path)

Executed via PowerShell HTTP suite against live YARP Gateway (`http://127.0.0.1:8080`) with all 6 APIs and 4 infra containers healthy:

| Case | Checks | Result | Notes |
|---|---:|---|---|
| `AU-05` | 10/10 | Pass | CS `ADM-07` banned Player C (`200`); `AUTH-01`, `AUTH-03`, `WAL-01`, `INV-01` all returned `403 ACCOUNT_BANNED`; CS unban (`200`); old refresh token stayed `401 INVALID_REFRESH_TOKEN`; new login `200`; tampered & expired JWTs returned `401 UNAUTHENTICATED`. |
| `OW-01` | 9/9 | Pass | Spoofed `?userId=` and `X-User-Id` (`A->B` and `B->A`) on `INV-01`, `WAL-01`, `WAL-02`, `MKT-06`, `MKT-07` returned only authenticated caller data with zero cross-user leakage. |
| `OW-02` | 7/7 | Pass | Owner B read own `WAL-05` (`200`); non-participant C got `404` on B's `WAL-05` (`TOPUP_NOT_FOUND`), Completed A/B trade `MKT-08` (`TRADE_NOT_FOUND`, TradeId `7e52fda9-ceac-4705-bce8-f1c21f64d2e1`), A's Cancelled `MKT-05` (`LISTING_NOT_FOUND`), and A's Active `DELETE MKT-02` (`LISTING_NOT_FOUND`, listing untouched); public Active `MKT-05` returned `200`. |
| `OW-03` | 18/18 | Pass | Full RBAC matrix (`Player -> ADM-01..07`, `GM -> ADM-05..07 / WAL-01 / INV-01`, `CS -> ADM-01..04 / WAL-01 / INV-01`) all returned `403 FORBIDDEN`. |
| `CS-01` (partial scope) | 1/1 | Partial | `ADM-05` full trace on real Completed trade `7e52fda9-ceac-4705-bce8-f1c21f64d2e1` returned `200`, `partial: false`, non-null `wallet` and `inventory`. Stop-inventory fault sub-step deferred to full dry-run. |

## 4. Prior Verified Auth, Gateway & Admin Runs (`AU-01`, `AU-03`, `AU-04`, `GM-01`, `GM-02`, `GM-04`, `CS-02`)

- `AU-01` / `AU-03`: Seed login (`Player`, `GM`, `CS`), `/me`, wrong password `401`, refresh rotation (`200` / old `401`), and logout (`204` / reuse `401`) verified through Gateway.
- `AU-04`: `AUTH-I01` scoped service key verified (`200` valid, `401` invalid/missing); stopping `auth-api` returned `503 Unhealthy` on Gateway `/health/ready` and `503 DEPENDENCY_UNAVAILABLE` on protected routes.
- `GM-01`: `AD-01..AD-15` verified `ADM-03`, `ADM-04`, Redis warm-up/sync, `ADM-I01` cache-miss repopulation, `409 IDEMPOTENCY_CONFLICT`, `409 VERSION_CONFLICT`, `422 INVALID_PRICE_POLICY`, and 6 SQL rows in `Price_Policy_Audits`.
- `GM-02`: `AC-02..AC-03` verified `TradeCompletedConsumer` deduplication (`2` unique trades + `1` duplicate -> `gross=3200`, `volume=3`, `fee=160`, `referencePrice=1066`, bounds `853..1279`).
- `GM-04`: `ADM-01` verified degraded (`wallet-api` absent -> `partial=true`, `gemSupply=null`, `unavailableSources=["wallet"]`) and healthy (`partial=false`, `unavailableSources=[]`, live `gemSupply` & `activeListingCount`).
- `CS-02`: `ADM-07` restrict/restore + idempotent replay (`actionId`/`version` matched), `409 INVALID_ACCOUNT_TARGET` on GM, `404 USER_NOT_FOUND`, `Moderation_Actions` audit rows, and `TradeRestricted` enforcement (`MKT-04` allowed `200`, `MKT-01`/`MKT-03` blocked `403 TRADE_RESTRICTED`).

## 5. Defects Found & Fixed

- **DEF-01 (`AU-02`):** Malformed 3-segment string (`invalid.google.token`) caused `Google.Apis.Auth` to throw a JSON reader exception returning `500 INTERNAL_ERROR`. Fixed in `src/Services/Auth.API/Infrastructure/Security/GoogleTokenValidator.cs` with structural Base64Url/JSON pre-validation and exception handling; retest returned `401 INVALID_GOOGLE_TOKEN`.
- **DEF-02 (`AU-05`):** Calling `POST /api/v1/auth/refresh` for a banned account returned `401 INVALID_REFRESH_TOKEN` instead of `403 ACCOUNT_BANNED` because `RevokedAt` was checked before `AccountStatus.Banned`. Fixed in `src/Services/Auth.API/Application/Services/AuthService.cs`; retest confirmed `403 ACCOUNT_BANNED` while banned and `401 INVALID_REFRESH_TOKEN` after unban.
