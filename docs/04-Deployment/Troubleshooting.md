# Troubleshooting & Recovery

Version 0.1.0 | planned operational runbook

## 1. Triage order

1. Identify environment, commit/tag, failing operation, UTC time, service and correlationId/TradeId/TopUpId.
2. Check `docker compose ps`, `docker compose logs --tail=100 <service>`, then relevant dependency. Inspect only authorized and redacted data. `docker compose config --quiet` validates syntax; avoid displaying expanded config with secret values. Use `docker stats` for CPU/RAM, disk/volumes on host.
3. Determine whether request was rejected before commit, persisted Pending, or definitively committed. HTTP timeout, RabbitMQ fault or container restart does not prove business failure.
4. Restore connectivity first; retry original idempotency/operation ID as contract allows; do not blind retry with new IDs. Observe traces, queue and ledger after recovery; escalate unresolved state to group.

## 2. Infrastructure / network

| Symptom | Check | Safe action |
|---|---|---|
| `docker compose up` build fails | target framework, build context, ProjectReference, entrypoint | match SDK/runtime tags; restore with repo root context; rebuild |
| SQL container exits | logs, EULA/password, host architecture/RAM/disk | use supported x64/approved image; fix env/resources; keep volume |
| SQL login failed | service login/DB mapping, connection user, bootstrap, SQL ready | migrate/bootstrap correct own DB; no sa runtime |
| Certificate error in local SQL | encryption/cert config | use trusted cert; local-only exception in config if approved |
| Mongo auth fail | user role + authSource, DB name | fix credentials without dropping db |
| Redis unavailable | network/DNS/password, memory | restore connection, rebuild cache from durable data; do not bypass lock/state guards |
| RabbitMQ connection refused | DNS, port 5672, vhost credentials, readiness | verify listener, create vhost/users/permissions; don't use guest remote |
| `localhost` inside API fails | container DNS addresses | use service name:port, not host localhost |
| host port conflict | compose ps, host processes | free port or change only host binding; update client/ngrok |
| container running but API not ready | migrations/healthcheck/consumer, dependency | repair startup order; retry with bounded backoff; no cyclic readiness |

Do not expose private ports to internet just to debug. If local inspection requires a DB/UI client, use controlled loopback override or exec, close when done. Avoid bypassing permissions.

## 3. Gateway & authentication

| Symptom | Check | Safe action |
|---|---|---|
| 404 | method, exact path, /api/v1, YARP route/allowlist | align with OpenAPI; /internal public must remain blocked |
| 502/503 | downstream readiness, DNS, Auth status lookup, Gateway logs | restore service; retry original request only if safe; 503 without durable acceptance differs from 202 |
| 401 | Bearer token expiry, issuer/audience/signing configuration | refresh/relogin; align JWT settings; never disable validation |
| 403 | role, Banned, TradeRestricted, ownership policy | use authorized seeded GM/CS or Player; respect restrictions |
| internal 401/403 | X-Service-Key mapping and scope | correct per-caller key, exact permissions; do not forward client headers |
| Google invalid token | GIS clientId vs aud, signature/issuer/expiry, clock | get new genuine ID token; align config and verify with library |
| Google origin mismatch / blocked | authorized JS origin incl port, testing user, CORS | register exact origin and allowed user; use local helper; not wildcard CORS |

## 4. ZaloPay / Wallet

| Symptom | Check | Safe action |
|---|---|---|
| create rejected | classic Sandbox endpoint, app_id, correct Key1, canonical string and serialization, clock, package | fix config; if confirmed rejection follow status contract; no manual credit |
| create timeout / duplicate app_trans_id | persisted Pending order / same key | query original app_trans_id, no blind new order |
| callback not received | actual callback_url, tunnel status, gateway method/path, public policy | fix endpoint for new orders, query original orders; no redirect-based credit |
| MAC invalid | Key2 and original `data` string, callback type | reject, investigate; do not skip MAC |
| signed callback amount/order mismatch | stored amount, provider ID, prior ledger | flag reconcile/manual review (no credit until validated) |
| already Paid, callbacks repeated | unique op/provider keys, atomic ledger | respond duplicate per contract; no additional credit |
| db error during credit | transaction outcome, SQL logs | retry/query original after checking durable state; do not force update status |

Use the same settlement logic from callback and query. If valid paid but late/unknown, reconcile; never reset Paid due to late failure. No real-money operation; no external refund endpoint in MVP.

## 5. Marketplace, Saga, CQRS

| Symptom | Check | Safe action |
|---|---|---|
| listing create fails after Inventory lock | PendingLock, operationId, lock by listingId | recover same listing; confirmed failed create releases lock; unknown outcome stays pending |
| ListingBusy/409 | SQL listing state, Redis contention, idempotency key | poll existing operation if yours; do not delete someone else's lock |
| Redis TTL elapsed mid-purchase | token release, SQL state, Saga state | honor SQL Processing; no manual reopen |
| hold fails | available balance, operation key | confirmed insufficient funds -> terminal failure/reactivate via Saga |
| inventory full | result event and hold status | let Saga release Gem and reactivate after confirmation |
| settlement rejection | durable proof no commit | reverse item then release hold as Saga orchestrates |
| settle outcome unknown | wallet ledger/trace, retries | keep non-terminal and review; never compensate on timeout alone |
| settle committed; finalize failed | inventory PendingSettlement, transfer record | forward recovery only, retry finalize same operation |
| pending cancel | Cancelled listing, unlock command and item lock | redrive unlock safely, wait for confirmation; no unlock of PendingSettlement |
| stuck Saga / `_error` queue | correlationId, state, outbox/inbox, consumer logs | inspect failure, fix root cause, redrive original message only after state check; preserve IDs; no purge |
| stale catalog | queue, outbox, Mongo version/tombstone | recover worker, replay/rebuild read model safely; command uses SQL authoritative |
| price policy cache missing | Admin PricePolicy SQL, Redis writer | rebuild from durable policy; reject listing with 503 while unavailable |
| incorrect daily stats | TradeFacts unique tradeId, day/timezone, late events | recompute projection/summary from durable facts; upsert, not naive reset |

Replaying messages is safe only when persisted idempotency is implemented and verified. If compensation itself fails, keep Saga nonterminal/review; not safe to mark Failed or make inventory available by hand. Coordinate owner services to fix data, preserve immutable ledger/audit, document any manual repair in controlled environment. No cross-DB modifications to bypass service boundaries.

## 6. Monitoring

| Symptom | Check | Safe action |
|---|---|---|
| Prometheus target DOWN | service DNS/port, /metrics mapping, network | correct scrape config/endpoint; use separate private port if needed |
| Grafana no data | datasource http://prometheus:9090, actual metrics, time range | fix config, create controlled traffic; absent is not zero |
| high p95/error rate | route/status distribution, load, Docker stats, logs | diagnose dependency bottleneck; record actual sample size; no fabricated SLA |
| metrics cardinality/memory growth | raw paths/IDs as labels | normalize route and bounded labels; use logs for individual IDs |

## 7. Incident & release checklist

Record incident ID, commit, symptom, reproduction, correlation/operation IDs (redact), expected vs observed, root cause, safe fix, evidence and residual issue. Before live demo stop ingress if state is inconsistent; identify Pending trades/top-ups, take backups/checkpoint, repair with service owners. Do not call `docker compose down -v` or purge broker queues unless intentionally resetting isolated disposable test data, confirmed by entire team; never on active sandbox demo data. Re-run end-to-end smoke workflow and verify invariants before reopening tunnel. Keep actual incident/test results separate from this proposed playbook.
