# Monitoring & Observability Guide

Version 0.1.0 | Planned — NFR-OBS-01

## 1. Objective

Monitor 5 Microservices + YARP, visibility into HTTP workload, failures and asynchronous processing. Prometheus scrapes private metrics endpoints; Grafana reads Prometheus. This technical dashboard is distinct from Admin.API market analytics (Gem volume, fees, prices), which remains a business feature. No production SLA/alerting cluster in MVP.

## 2. Instrumentation contract

Each service + Gateway implements `GET /health/live` (process alive, 200), `GET /health/ready` (dependencies necessary for core ability, 200/503, Health DTO from Contracts), `GET /metrics` (Prometheus text, internal). Liveness must not depend on external provider/DB or trigger restart storms. Readiness reports critical DB/broker/cache as appropriate; no cyclical health probes or repeated live provider payment calls. Google/ZaloPay availability should be logged and scoped to their endpoints rather than taking all local business routes down. Probe health on internal container network. Restrict `/metrics`, `/health/*` and management routes from public gateway (gateway's own endpoints need local management listener or guard; merely omitting YARP routes does not hide locally mapped endpoints). Docker healthcheck uses available client or dedicated health probe; restart policy `unless-stopped` proposed but healthcheck alone does not automatically restart unhealthy containers under Compose.

Use prometheus-net.AspNetCore in each application; middleware / MapMetrics example to implement as compatible with pinned library version. HTTP metrics instrument normalized route templates, method, status, service. Do not use raw path with UUIDs, user ID, trade ID, tokens or payment ID as labels. CorrelationID in structured logs only. Set meaningful histogram bucket ranges; count actual attempts versus durable business outcomes separately. Avoid double increments on duplicate events: outcome metric best effort per committed state transition; ledger is source of truth.

## 3. Prometheus configuration proposal

Place `infrastructure/prometheus/prometheus.yml` in repo when implemented; scrape targets use Docker DNS and private port 8080. Example (not installed):

```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s
scrape_configs:
  - job_name: marketplace-http
    metrics_path: /metrics
    static_configs:
      - targets: ['gateway:8080']
        labels: {service: gateway}
      - targets: ['auth-api:8080']
        labels: {service: auth-api}
      - targets: ['wallet-api:8080']
        labels: {service: wallet-api}
      - targets: ['inventory-api:8080']
        labels: {service: inventory-api}
      - targets: ['marketplace-api:8080']
        labels: {service: marketplace-api}
      - targets: ['admin-api:8080']
        labels: {service: admin-api}
```

If gateway metrics use separate management port, update its target. Mount config read-only (usual path `/etc/prometheus/prometheus.yml`), use named data volume, set modest retention (e.g. 7 days) depending disk budget. Bind host UI loopback only. Check config with `promtool check config` using installed/matching image binary; check Prometheus Targets. `up` measures scrape success, not readiness or business success. Validate actual metrics available from `/metrics`, then write Grafana queries. RabbitMQ UI internal/local can show queue depth/error queues; optional broker exporter not mandatory in base 12 containers. CPU/RAM via `docker stats`, not falsely labeled application metrics; optional exporters outside baseline.

## 4. Proposed metric dictionary

Names below are custom implementation proposals (standard `up` excepted), not metrics already emitted. Align with prometheus-net generated metric names or adapt queries when coding.

| Metric | Type | Labels | Meaning |
|---|---|---|---|
| up | gauge | job, instance, service | scrape availability (Prometheus generated) |
| marketplace_http_requests_total | counter | service, method, route, status_code | completed HTTP attempts |
| marketplace_http_request_duration_seconds | histogram | service, method, route, status_code | duration distribution |
| marketplace_ready | gauge | service | readiness health 1/0, if instrumented |
| marketplace_trade_outcomes_total | counter | outcome (completed, failed) | terminal transitions from Saga only |
| marketplace_saga_step_duration_seconds | histogram | step | time from command to verified result |
| marketplace_saga_inflight | gauge | state | active trade count, derived from own durable state if implemented |
| marketplace_projection_lag_seconds | histogram | projection | time event committed to read-model application |
| marketplace_payment_callbacks_total | counter | outcome | accepted/duplicate/invalid/retry callbacks |
| marketplace_message_failures_total | counter | consumer | processing faults or retry exhaustion (define semantics) |

Limit scope for 11–12 days: first 3 metrics + basic saga outcomes, add others if feasible. NFR targets mean catalog <=100ms, cache validation <5ms, CQRS sync 1–2s in healthy local benchmark are goals, not guaranteed. Cache read needs dedicated measurement if instrumented, not HTTP latency proxy. Counters may reset on restart; use rate/increase; SQL trace/ledger required for correctness.

## 5. Grafana dashboard

Local UI `http://localhost:3000`; change default admin password via secret mapping (`GF_SECURITY_ADMIN_PASSWORD`), anonymous access off; datasource URL `http://prometheus:9090`, not localhost when Grafana in Docker. Configure datasource/provisioning/dashboard JSON in `infrastructure/grafana/` when implementing; export versioned JSON with no secrets. Local UI only, no ngrok route. Recommended dashboard title `Marketplace — Service Health & Transactions`, variable service (all or selected). Refresh 15–30s; view last 15–30 minutes for demo.

| Panel | Visualization | Example PromQL (custom names above) |
|---|---|---|
| Scrape targets | stat/table | `up{job="marketplace-http"}` |
| HTTP throughput | time series | `sum by(service) (rate(marketplace_http_requests_total[5m]))` |
| HTTP 5xx/s | time series | `sum by(service) (rate(marketplace_http_requests_total{status_code=~"5.."}[5m]))` |
| P95 latency (seconds) | time series | `histogram_quantile(0.95, sum by (le,service) (rate(marketplace_http_request_duration_seconds_bucket[5m])))` |
| HTTP 5xx % | time series | `100 * sum by(service)(rate(marketplace_http_requests_total{status_code=~"5.."}[5m])) / sum by(service)(rate(marketplace_http_requests_total[5m]))` |
| Trade outcomes | time series | `sum by(outcome)(increase(marketplace_trade_outcomes_total[5m]))` |
| Callback outcomes | bar/time series | `sum by(outcome)(increase(marketplace_payment_callbacks_total[5m]))` |
| Projection p95 lag | time series | `histogram_quantile(0.95, sum by(le)(rate(marketplace_projection_lag_seconds_bucket[5m])))` |

No traffic may mean absent time series or NaN (division by zero); label no data rather than imply zero failures. Scale seconds to ms in panel units when useful. Histograms require observations and suitable buckets. Build first four required panels with real metrics, optional panels only after instrumentation. Save dashboard screenshot of generated workload and show request flow plus outcomes; screenshots not evidence of full correctness.

## 6. Structured logs and operational traces

Each service logs UTC timestamp, service, severity, normalized operation, correlationId, optional tradeId/listingId/topUpId/operationId for trace, state/errorCode and duration. Propagate X-Correlation-ID and message CorrelationId as defined in Contracts. Avoid JWT, passwords, keys, raw callback, personal data and SQL connection strings. Docker logs read with `docker compose logs --tail=100 marketplace-api wallet-api inventory-api`, not dump all unredacted. Apply bounded Docker log rotation in eventual compose. Trace endpoints authorized CS via Admin API; no raw stack exposure. For correlation across HTTP and async messages follow tradeId/operationId; no distributed tracing backend required for MVP.

## 7. Acceptance and evidence

1. All six targets show UP when healthy; verify private access and public route blocked.
2. Generate a few valid and invalid requests, dashboard updates request rate/status/latency.
3. Execute one successful trade + one controlled inventory failure; compare Saga trace, queue, ledger and optional outcome metrics; do not infer correctness from metric alone.
4. Stop/restart a test service only in controlled fixture; observe scrape status and recovery, inspect queues and pending operations.
5. Record dashboard screenshot + commit, sample duration, target status, measured results and caveats. Formal performance/load cases belong 05-Testing. No metrics have been collected in this document.

Reference: https://prometheus.io/docs/prometheus/latest/configuration/configuration/
