# Test Plan — In-game P2P Marketplace

version: 1.0.0 | Design baseline — NOT RUN

## 1. Objective and scope
Verify 29 retained FR, 21 NFR, 15 BR, T01–T11 and deployment gates D1–D7 across five ASP.NET Core APIs + YARP, RabbitMQ/MassTransit, Redis, five SQL logic DBs and MongoDB, Google Identity and ZaloPay Sandbox. Scope is backend only; no OTP, tickets, automated RMT, GM buyback/injection, real payment, actual gameplay, partial purchase, HA/SLA. Test business correctness separately from latency targets and observability. Source: SOA-Finals-docs.md (01,02,04 sections) and prior 03-Contracts v0.1.0 TXT/YAML; resolve drift before running.

## 2. Test levels / tools
| Level | Tool / environment | Responsibility |
|---|---|---|
| Unit | xUnit, fake clock/adapters, pure domain logic; 5 prioritized UT cases | Marketplace / Wallet owners |
| API/contract | Postman Collection Runner + Swagger/OpenAPI schema checks; local Gateway, private runner for internal APIs | Each owner |
| Integration | Actual isolated SQL/Mongo/Redis/RabbitMQ via Docker Compose; test-only fault injection | All, service owners |
| End-to-end | Postman + browser Google and ZaloPay Sandbox, ngrok, real broker | Auth/Wallet & Marketplace owners |
| Security | Role matrix, ownership, status, replay, invalid payload, private route | All |
| Concurrency / performance | Concurrent HTTP runner (e.g. k6 or custom script; proposed), Prometheus and timestamps | Marketplace & infra owner |
| Smoke / regression | Postman + docker compose probes; before every integration build and release | Infra + all |

## 3. Test environment and data
- Use a dedicated Compose project/network and volumes, never the shared demo database; same versions and config as candidate release. Local gateway http://localhost:8080 (override host port if needed); public HTTPS ngrok only during real provider tests. Private routes via container-network test runner and scoped X-Service-Key; no public test endpoints. Store secrets locally, not source/evidence. Run fault injection only in isolated test environment, not public Sandbox route; turn off diagnostics and fault flags for demo/release.
- Record Git commit, image tags, host CPU/RAM/OS, Docker resource limits, config (fee/band/TTL/capacity/packages), timezone, timestamps; UTC for event traces, Asia/Ho_Chi_Minh for analytics day.
- Seed stable aliases (actual UUIDs saved in local Postman environment): A buyer active total 1500 Gem held 0; B seller active total 350 held 0; C unrelated active; F full inventory + sufficient Gem; U insufficient funds (999); GM/CS seeded separately; X new Google test user. Distinct owner-specific item IDs, three tradable templates and one nontradable; default reference 1000, band 2000 bps, fee 500 bps. Set package IDs/amount/Gem from server; no assumed conversion rate. Capacity choose group-approved value and fill F exactly. Additional 100 funded buyer fixtures with free slots for CC-01. Every listing uses a fresh item/stack. Seed balances explicitly as test fixtures with matching ledger/opening balance evidence, not simulated Paid provider transactions. Ensure policy cache warm before create. Each scenario starts from independent state or explicitly named prerequisite; don't reuse sold listing. Use stable idempotency key within replay and new UUID for independent attempt.
- Capture baseline totals and holds for all affected wallets; verify supply delta in the isolated fixture, not shared database. Inventory snapshot includes itemId, ownerId, quantity, status, listingId/tradeId; unique item instance and seller reserved slot. Capture trade/listing state, ledger operation keys, outbox/messages and analytics facts. Only read each service's DB through authorized test tooling (not cross-service runtime query).

## 4. Execution and gates (11–12 day MVP)
| Window | Work | Deliverable |
|---|---|---|
| Day 1–2 | Freeze contracts/policies, fixture plan, clean deployment smoke | D1/D2, environment baseline |
| Day 3–5 | UT-01..05 and per-service APIs, ownership, seeded login; get provider credentials early | xUnit results, Postman groups |
| Day 6–8 | SG-01..08, CC, CQRS + payment callback/reconciliation | Trade traces; fault and race logs |
| Day 9–10 | GM/CS, real sandbox, deployment/monitoring, performance and regression | Evidence by scenario, defects/blocks |
| Day 11–12 | Retest release candidate, freeze code, finalize actual report/demo | Test report, residual risks |
This is a suggested execution sequence, not proof that all tests fit the time budget. Work concurrently with service implementation. All P0 cases targeted; if P1 deferred document NFR/FR gap, not a scope change without approval.

## 5. Entry / exit criteria
Entry: contract review and test data, build/migrations, readiness, message queues, seed/policy package, environment access and credential availability; cases requiring real providers blocked if credentials unavailable. Exit: all P0 executed and Pass on release candidate, no open critical/high correctness/security defects, 29 FR and 21 NFR reviewed with actual evidence or explicitly recorded gaps, T01..T11/D1..D7 evaluated, mandatory sandbox success evidenced separately from mock, 3–5 unit tests (plan 5), regression and final demo. NFR latency target misses disclosed rather than concealed; release with unresolved gaps requires group/teacher acceptance, not automatic Pass. A 202 is not completion; poll authoritative SQL API to terminal state. Recommended poll interval 1s up to 60s healthy local, extend controlled recovery window to 120s or documented deadline; record actual times. Provider payment wait starts after user action; use provider expiry/config, not arbitrary timeout that marks Paid/Failed.

## 6. Status and defects
Not Run (default), Pass (expected and evidence), Fail (observed mismatch), Blocked (missing prerequisite/infrastructure/provider), N/A only when scope formally approved and reason stated. A case with failed variant is Fail; report variants and retest. Severity: Critical = money/item duplication/loss or unauthorized data; High = core workflow or required integrations unavailable; Medium = recoverable feature/target miss; Low = documentation/cosmetic. Store reproduction, expected vs actual, commit, environment, sanitized evidence, owner and retest. No fabricated results. Use Test-Execution-Register.csv and Test-Report.md.
