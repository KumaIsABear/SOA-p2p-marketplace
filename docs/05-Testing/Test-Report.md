# Test Report — Execution template

version: 1.0.0 | Design baseline — NOT RUN

No implementation/test execution supplied. All actual results are pending. Do not replace placeholders with invented counts or screenshots.

## 1. Run metadata

| Field | Actual |
|---|---|
| Commit / tag | TBD |
| Date & executor | TBD |
| Host OS / CPU / RAM / Docker limits | TBD |
| Image/.NET versions, dataset, fee/band/TTL/capacity | TBD |
| Google / ZaloPay Sandbox credentials available? | TBD (no secrets) |
| Run environment / tools / sample size | TBD |

## 2. Execution summary

| Measure | Value |
|---|---|
| Planned scenarios | 58 |
| Executed | 0 (not run) |
| Pass / Fail / Blocked / N/A | 0 / 0 / 0 / 0 |
| Not Run | 58 |
| Pass rate among Pass + Fail | N/A (no execution) |
| FR/NFR validated | Not measured |
| Release gate | Not evaluated |

Update from Test-Execution-Register.csv; count each scenario once, child variants separately if desired. Coverage mapping is not executed coverage. Report pass rate denominator explicitly and list blocked/deferred.

## 3. Business / architecture result

| Gate | Actual outcome | Evidence |
|---|---|---|
| T01..T11 (each individually in RTM/register) | Not Run | TBD |
| D1..D7 (each individually in RTM/register) | Not Run | TBD |
| Successful purchase and fee audit | Not Run | TBD |
| Full inventory compensation | Not Run | TBD |
| Concurrent purchase 100 clients | Not Run | TBD |
| Object ownership isolation | Not Run | TBD |
| Google real integration | Not Run | TBD |
| ZaloPay Sandbox actual payment & callback/query | Not Run | TBD |

## 4. Performance results

| Metric | Target | Actual mean | P95 / max | Samples / errors | Assessment |
|---|---|---|---|---|---|
| Catalog HTTP | <=100ms mean | TBD | TBD | TBD | Not Run |
| Redis price read | <5ms mean | TBD | TBD | TBD | Not Run |
| CQRS lag | 1–2s target | TBD | TBD | TBD | Not Run |
| Concurrent trade | 1 Completed / 100 attempts | TBD | N/A | TBD | Not Run |

## 5. Defect register template

| Defect ID | Case | Severity | Steps / expected | Actual | Evidence | Owner | Status / retest commit |
|---|---|---|---|---|---|---|---|
| TBD | TBD | TBD | TBD | TBD | TBD | TBD | TBD |

## 6. Evidence folder suggestion
`tests/results/<run-id>/` with xUnit TRX, Postman summaries, raw concurrency/timing CSV, redacted trace and before-after snapshots, screenshots of Grafana/Prometheus targets, defects + manifest with commit and environment. No raw credentials, JWT, callback keys, PII or database dumps.

## 7. Conclusion / sign-off
Actual scope achieved: TBD. Known limitations/deferred cases: TBD. Remaining risks: TBD. Group acceptance: TBD. Release recommendation: Not evaluated. Report separately local mock results and real Sandbox/Google results. Do not claim that all FR/NFR passed based on design mappings or a 202 response.
