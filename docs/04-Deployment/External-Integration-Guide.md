# External Integration Guide

Version 0.1.0 | Planned | Google Identity + ZaloPay Sandbox

## 1. Public endpoint via ngrok

Run gateway locally on host loopback `127.0.0.1:8080` per Deployment Guide; configure ngrok host agent using your account's authtoken privately. Use a stable assigned HTTPS domain if available; otherwise update CallbackUrl when the public address changes. No public tunnels for SQL, MongoDB, Redis, RabbitMQ UI, Grafana, Prometheus or service ports.

```powershell
# after the gateway is running and route policy verified
ngrok http 8080
```

Copy actual HTTPS URL shown by the agent. Use Postman baseUrl for authorized public APIs. Wallet callback URL: `https://<actual-ngrok-domain>/api/v1/wallet/zalopay/callback`. Configure explicit method-aware gateway routes; only allow contract paths. Do not enable public Swagger, health, metrics, error page or generic reverse proxy. POST callback must bypass Player JWT, but service verifies MAC. If adding tunnel access rules, verify they let ZaloPay server reach callback without interactive auth. Inspect redacted traffic; do not share inspector logs containing credentials/tokens or callback payloads. Treat URL as public, not secret. Stop tunnel after demo; if address changes, update pending-order callback expectations and reconcile in Wallet, do not generate duplicate top-ups.

## 2. Google Identity Services setup

1. In Google Cloud console select/create project, configure Google Auth Platform/consent screen, create OAuth client ID type Web application. If external/testing, add intended test users as required. Do not request extra Google API scopes for this login feature.
2. Register exact authorized JavaScript origin of the minimal local browser helper, e.g. `http://localhost:5500`; if using public HTTPS helper add actual origin only if needed. Origins include scheme/host/port, not path. For this design use GIS JavaScript callback (popup) returning ID credential; no authorization code flow or Google backend redirect endpoint required. If team later chooses redirect UX, explicitly register matching redirect URI and update contract/CSRF handling.
3. Configure Auth `Google__ClientId` to same client ID; helper uses Google Identity Services library. In JavaScript callback submit JSON to gateway `POST /api/v1/auth/google`, body `{ "idToken": "<Google ID credential>" }`. Use HTTPS for remote demo, strict origin allowlist/CORS for separate local helper, no persistent token logging/storage. Use appropriate CSRF protections; no optional nonce unless server challenge implemented according to contract. No client secret required by this ID-token flow.
4. Auth validates signature against Google keys (library), issuer, configured audience and expiry; map issuer+sub to account, never auto-link via email. New Google user defaults Player only. Return internal JWT + refresh token, use internal JWT for all protected endpoints. Staff accounts seeded and use `/api/v1/auth/login` (or explicit pre-linked identity approved by team). Banned account cannot acquire or use protected session. Validate dependency handling: Google outage does not block seeded login.
5. Test login through browser helper (not just hardcoded token): call `/api/v1/auth/me`, then wallet/inventory/catalog. Verify wrong audience/expired tokens rejected. Capture screenshots only of results without tokens/IDs. This guide does not implement helper page.

## 3. ZaloPay Sandbox configuration

Use classic ZaloPay API v2 (do not mix API versions) and Sandbox app credentials acquired from developer portal. Keep app_id, Key1 and Key2 in Wallet config; keys are server-only. Base URL `https://sb-openapi.zalopay.vn`; official create `/v2/create` and query `/v2/query`. Sandbox only; follow provider's actual instructions for test account/payment method. Do not use personal payment or production credentials. Define server-owned package IDs/prices/Gem rates with team; do not trust client-supplied amount.

| Direction | Endpoint | Auth / action |
|---|---|---|
| Player → Gateway | GET /api/v1/wallet/top-up-packages | Player JWT |
| Player → Gateway | POST /api/v1/wallet/top-ups | Player JWT + Idempotency-Key, packageId |
| Wallet → ZaloPay | POST /v2/create | Key1 HMAC; form encoded, amount persisted |
| Player → ZaloPay | order_url returned by provider | Sandbox payment UI |
| ZaloPay → ngrok → Gateway → Wallet | POST /api/v1/wallet/zalopay/callback | Key2 MAC; no JWT |
| Wallet → ZaloPay | POST /v2/query | Key1 MAC; reconcile Pending |
| Player → Gateway | GET /api/v1/wallet/top-ups/{topUpId} | Player JWT, owner only |

Before starting Wallet, set `ZaloPay__CallbackUrl` to actual public HTTPS callback. If configuring provider portal callback, ensure same URL; if create request uses callback_url, follow API v2. Verify connection/outbound DNS, clock (app_time Unix ms, app_trans_id date prefix Vietnam time), correct app credentials and matching signature rules. Use 201 when provider order URL obtained; 202 if original order persisted but provider response unknown. Create-order success is not payment success. Do not mark paid from redirect/return URL. Never log Key1/Key2 or raw MAC input containing personal data.

## 4. Callback, reconciliation and durable data

Follow Figure 22A/22B and WAL-04/05/06 in API Contracts. Valid callback data MAC over exact original data string with Key2, verify app_id, order, user, amount, provider txn ID. Callback/query both use same local Wallet SQL transaction: lock order, guard operation `topup:{topUpId}` and unique provider transaction, credit Gem, ledger, Paid+PaidAt. Exact duplicate acknowledges without second credit; conflicting signed data flagged and reconciled. HTTP response format follows provider and WAL-06, not internal ProblemDetails schema (except malformed JSON handling per contract): return_code 1 success, 2 duplicate, 0 retry/unresolved, -1 invalid MAC/type; check actual sandbox behavior. Never expose callback signing secrets to browser.

Pending job uses `/v2/query` with Key1, configured interval (initial proposal 60s, tune provider limits), around timeout; provider rejection/terminal expiry only when proven. Timeout/unknown remains Pending and reviewed; late verified payment reconciled. Query and callback can arrive simultaneously. Do not manually credit DB or resend with new app_trans_id for unknown order. No external refund in MVP; Saga RefundBuyerFunds is release of Gem hold, distinct.

## 5. Verification and evidence

- Obtain real Google ID credential, internal token and protected endpoint result.
- Choose package, POST create with idempotency key; save TopUpId/app_trans_id (redact user identifiers), open provider Sandbox URL and pay using test method; verify Paid and exact Gem delta/ledger.
- Verify callback reached correct gateway path and MAC accepted; replay only controlled test event using actual signed payload or local fixture without exposing it. Test query recovery if callback unavailable; confirm no duplicate credit. For negative tests no payment mutation; details belong 05-Testing.
- If ngrok URL changes, update config before new requests and keep original pending order IDs for query. If provider registration/credential unavailable, show local mock explicitly labeled, and record actual external integration as unverified. Do not claim mock proves real API integration.

References: Google setup https://developers.google.com/identity/gsi/web/guides/get-google-api-clientid ; verification https://developers.google.com/identity/gsi/web/guides/verify-google-id-token ; ZaloPay v2 https://developers.zalopay.vn/v2/general/overview.html ; gateway guide https://developers.zalopay.vn/v2/docs/gateway/guide.html ; ngrok https://ngrok.com/docs/start. Exact provider configuration must be verified with team account.
