# Integration & End-to-End Testing

version: 1.0.0 | Design baseline — NOT RUN

## 1. Setup and API execution
Use fixtures in Test-Plan.md. Import 03-Contracts/marketplace-openapi.yaml into Postman (OAS 3.1 support depends on version), organize public/internal folders by service; manually reconcile operations in API-Operation-Matrix.md. Save collection with placeholders only. Suggested environment: baseUrl, private service DNS (private runner), buyerToken, sellerToken, unrelatedToken, gmToken, csToken, refreshToken, packageId, listingId, tradeId, itemId, templateId, topUpId, idempotencyKey, scoped keys; sensitive values local only. Generate X-Correlation-ID as UUID, carry Idempotency-Key across exact retries. Public API `Authorization: Bearer ...`; internal X-Service-Key, never exposed via ngrok. Test status, JSON schema required fields/types, errors (ProblemDetails code/status/traceId), correlation echo, Location, no body 204. Verify state beyond response. Example Postman test after purchase (assumes successful acceptance case):
```javascript
pm.test("Durably accepted (HTTP only)", () => pm.response.to.have.status(202));
const body = pm.response.json();
pm.test("Trade identifier and status URL", () => {
  pm.expect(body.tradeId).to.be.a('string');
  pm.expect(body.statusUrl).to.be.a('string');
});
pm.environment.set('tradeId', body.tradeId);
```
Do not count this alone as a completed purchase. Poll MKT-08 and inspect both source stores. Export test output redacted; never export token values. Actual collection/tests to be created in tests/Postman and tests/xUnit when code exists.

## 2. E2E scenarios and checkpoints
| Flow | Scenario IDs | Steps / authoritative evidence |
|---|---|---|
| Login → own data | AU-01..05, OW-01/02 | Seed/password and Google token → internal JWT → own wallet/kho; validate different owner/role; no raw token in logs |
| Gem issuance | WA-01..07 | WAL-03 → WAL-04 → browser pays Sandbox → callback /api/v1/wallet/zalopay/callback → WAL-05 + WAL-01/02; compare amount/ID/ledger and duplicate |
| Listing | MK-01..04, IN-02 | MKT-01 → INV lock → SQL Active → Mongo list; cancel → state Cancelled → unlock eventual; replay and lost reply |
| Successful purchase | SG-01 | MKT-03 → 202/Location → poll MKT-08; compare balance/inventory before-after; MKT-07 history, CS trace |
| Compensation/recovery | SG-02..08 | insufficient funds, full inventory, confirmed settle rejection, timeout and finalize; assert exact residual state; do not treat technical fault as rejection |
| CQRS | CQ-01..03 | Pause worker, send versioned events, read SQL detail and Mongo list; restart; stale events ignored |
| GM/CS | GM-01..04, CS-01/02 | GM policy and facts/job → CS read trace and moderation → protected workflow; current and old JWT |

## 3. Message contract verification (23 contracts)
| Command(s) | Event(s) | Cases | Assertion |
|---|---|---|---|
| HoldBuyerFundsCommand | BuyerFundsHeldEvent; BuyerFundsHoldFailedEvent | SG-01/02/07, CC-02 | Guard available, exact amount, one effect |
| TransferItemCommand | ItemTransferredEvent; ItemTransferFailedEvent | SG-01/03/04/07 | Owner/slot check, PendingSettlement, no duplicate item |
| SettleTradeCommand | TradeSettledEvent; TradeSettlementRejectedEvent | SG-01/04/05/07 | Debit/credit/fee atomically; rejection durably excludes late commit |
| RefundBuyerFundsCommand | BuyerFundsRefundedEvent | SG-03/04/07 | Release hold only; no refund after settle |
| ReverseItemTransferCommand | ItemTransferReversedEvent | SG-04/07/08 | Restore seller Locked before release; reserved slot |
| FinalizeItemTransferCommand | ItemTransferFinalizedEvent | SG-01/06/07/08 | Available to buyer only after confirmed settlement |
| UnlockItemCommand | ItemUnlockedEvent | MK-04, IN-02, SG-08 | Correct listingId and idempotent unlock |
| — | ListingCreatedEvent; ListingProcessingEvent; ListingCancelledEvent; ListingReactivatedEvent | MK-01/04, CQ-01/02 | Increasing version snapshots; projection hides non-Active |
| — | TradeCompletedEvent; TradeFailedEvent | SG-01/03/04/05/06, GM-02, CQ-02 | Terminal events only after confirmed effects; analytics dedup by TradeId |
Check payload field names/types and delivery route against Contracts §07; common correlationId (tradeId/listingId), stable operationId, occurredAt for events; native MassTransit headers. Redeliver identical MessageId AND same operationId with new MessageId (test-only harness). Assert correct queue/consumer, state+outbox commit, inbox/operation guard, no token/provider secrets, bounded retry/error queue. Do not use public test REST endpoints for consumers. Audit/optional consumer for TradeFailed is not mandatory separate service.

## 4. Fault injection matrix
| Injection point | Verification |
|---|---|
| Before/after SQL lock response | MK-03: distinguish known failure vs unknown, preserve PendingLock, recover same listing |
| After hold, before transfer | SG-03: business reject full, release confirmed |
| After transfer, before settle | SG-04: only definitive no-settle permits reverse then release |
| After settle commit, before result | SG-05: retry/query same operation, no refund; protect item |
| Finalize transient/permanent | SG-06/08: forward retry, no terminal lie; error queue/trace |
| Broker unavailable / consumer stopped | RL-01/02: persisted messages and outbox dispatch recovery |
| Redis lock TTL expiry / loss | CC-03/RL-03: SQL guard, no lost wallet state |
| Callback SQL fail / query and callback overlap | WA-05/07: local transaction, only one credit |
Use test-only in-process hooks/adapters or controlled stop/restart and barriers; no fault injection public endpoint, no destructive purge. Capture exact intended point (before commit vs after commit before acknowledgement). If unable to reproduce point, mark Blocked/Not Run and don't substitute a generic timeout as proven scenario. Stop new traffic, restore component, inspect in-flight operations and traces before proceeding. Replay only known operation with stable ID; never edit DB to force successful trade.

## 5. Sandbox validation
Google: real provider ID token generated for test client and test user, validate issuer/audience/signature/expiry, JIT Player, stable subject. Negative token fixtures can use adapter/unit harness; report as local tests separately. ZaloPay: only Sandbox credentials, actual /v2/create and /v2/query, verified Key2 over original data for callback, validate app_id/order/amount/zp_trans_id, check return_code 1/2/0/-1 as in contract. Invalid MAC/amount tests locally with synthetic signed data using test-only keys, not forged provider payment; verify true Sandbox flow separately. Callback public via ngrok Gateway with allowlist, no player JWT. No redirect-based credit. If create unknown, query original app_trans_id; don't originate new charge on retry. Record topUpId and provider transaction ID redacted, time, payment state, delta and ledger count. Provider-specific return codes subject to actual Sandbox verification; do not claim mock proves provider integration.
