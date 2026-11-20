# 05-Testing — Verification baseline

version: 1.0.0 | Design baseline — NOT RUN

## 1. Read in order
- Test-Plan.md — strategy, environment, fixtures, milestones and exit criteria.
- Test-Cases.md — 58 planned scenarios (including 5 unit cases), positive, negative, race and recovery.
- API-Operation-Matrix.md — all 40 REST operations, linked to case IDs; request DTO/error/status from prior Contracts.
- Integration-Testing.md — E2E, all 23 message contracts, controlled failures and third-party verification.
- Performance-Testing.md — 100-buyer test, anti-oversell, local latency benchmarks and evidence protocol.
- Traceability-Matrix.md — 29 FR, 21 NFR, 15 BR, 11 acceptance groups and 7 deployment gates mapped to tests.
- Test-Execution-Register.csv — editable execution tracking (all Not Run).
- Test-Report.md — actual-results template; update only after running code.

## 2. Baseline and discrepancies to resolve before freeze
Primary source: uploaded SOA-Finals-docs.md (Requirements, Architecture, Deployment), reconciled with original architecture diagrams and prior 03-Contracts/API-Contracts.txt and marketplace-openapi.yaml (v0.1.0). In the corrected documentation bundle, AuthDb/WalletDb ERDs are restored and aligned with standalone 08/09 (PasswordHash, DisplayName, PaymentUrl); Figure 16 restored in Create Listing; Figure 22A/22B embedded in the payment section; Figure 23 restored as 23-google-login.puml; Figure 01/03/04 embedded versions synchronized with ZaloPay selection; own-resource labels clarified in the use case. Contracts and Testing are now included. OW-01/02 test owner scope independently of actor inheritance. Migration/technical fields for persistent idempotency, outbox/inbox, listing snapshots, slot reservation and moderation still need implementation review. Use current Contracts paths /api/v1 and /internal/v1; older conceptual diagram labels may omit exact endpoints. The baseline contains proposed fee/band/TTL/capacity/rate; tests use examples, not newly approved requirements. Clarify ambiguous `volume` semantics with DTO/analytics implementation; PF-03 upper target interpretation and test sample sizes proposed. No runtime or provider verification performed; 05-Testing is documentation, no compiled xUnit, Postman collection, load scripts, Docker or report evidence.

## 3. Next steps
Copy entire folder to docs/05-Testing; freeze baseline, assign P0 cases per service owner, implement tests under tests/ (not docs), execute and fill register/report. If time limited prioritize financial/item invariants, isolation, race, sandbox, deployment. Preserve all deferred scenarios, with reasons. No additional diagram is required for the test plan.

## 4. Minimum live demo sequence (not a replacement for full verification)
D1/D2 → AU-01/02 → WA-03/05 → OW-01 → MK-01 → SG-01 → SG-03 → CC-01 → CQ-01/02 → GM-01/02 → CS-01/02 → DP-02/03. Use fresh fixture for each mutation. UT-01..05 run before integration. Other P0 scenarios remain release-gate checks; unexecuted cases must be reported.
