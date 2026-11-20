# Requirements Traceability Matrix (RTM)

version: 1.0.0 | Design baseline — NOT RUN

Mapping design coverage only; one FR may need several executions. BR mappings are explicit; FR/NFR/T/D mapping derived from Test-Cases. Evidence and actual status are in Test-Execution-Register.csv after execution.

## Functional Requirements (29)

| ID | Scenario IDs |
|---|---|
| FR-AUTH-01 | AU-01, AU-02 |
| FR-AUTH-02 | AU-03 |
| FR-AUTH-04 | AU-04, AU-05, CS-02 |
| FR-CS-01 | OW-03, CS-01 |
| FR-CS-02 | AU-04, AU-05, OW-03, CS-02 |
| FR-GM-01 | GM-02, RL-03 |
| FR-GM-02 | GM-03 |
| FR-GM-03 | GM-02, GM-03, GM-04 |
| FR-GM-04 | OW-03, GM-01, RL-03 |
| FR-INV-01 | OW-01, IN-01 |
| FR-INV-02 | IN-01 |
| FR-INV-03 | IN-02, MK-02 |
| FR-INV-04 | IN-02, MK-03, MK-04 |
| FR-INV-05 | SG-01, SG-03, SG-04, SG-05, SG-06, SG-07 |
| FR-MKT-01 | UT-01, UT-02, MK-01, MK-02, GM-01, RL-03, PF-02 |
| FR-MKT-02 | MK-01, MK-02, MK-03, RL-02 |
| FR-MKT-03 | OW-02, MK-04, CC-04 |
| FR-MKT-04 | MK-05, SG-01, CC-01, CC-02, CC-03, CC-04 |
| FR-MKT-05 | SG-01, SG-02, SG-03, SG-04, SG-05, SG-06, SG-08, CC-01, RL-01, RL-02 |
| FR-QRY-01 | UT-05, MK-04, CQ-01, CQ-02, PF-03 |
| FR-QRY-02 | MK-01, CQ-01, CQ-03, PF-01 |
| FR-QRY-03 | OW-01, CQ-03 |
| FR-QRY-04 | OW-01, OW-02, SG-01, CQ-03 |
| FR-WAL-01 | OW-01, WA-01 |
| FR-WAL-02 | OW-02, WA-01, WA-02, WA-06 |
| FR-WAL-03 | UT-05, WA-03, WA-04, WA-05, WA-06, WA-07 |
| FR-WAL-04 | UT-03, SG-01, SG-02, SG-07, CC-02 |
| FR-WAL-05 | UT-02, UT-04, SG-01, SG-04, SG-05, SG-07 |
| FR-WAL-06 | UT-03, SG-03, SG-04, SG-07 |

## Non-Functional Requirements (21)

| ID | Scenario IDs |
|---|---|
| NFR-CONC-01 | CC-01 |
| NFR-CONC-02 | CC-02, CC-03 |
| NFR-CONC-03 | CC-04 |
| NFR-CONS-01 | UT-02, UT-03, UT-04, WA-03, WA-07, SG-01 |
| NFR-CONS-02 | MK-03, SG-02, SG-03, SG-04, SG-05, SG-06 |
| NFR-CONS-03 | CQ-01, PF-03 |
| NFR-CONS-04 | UT-05, WA-02, WA-05, IN-02, SG-07, CQ-02, RL-01 |
| NFR-INT-01 | AU-02, WA-03, GM-04, CT-01 |
| NFR-OBS-01 | DP-02, DP-03 |
| NFR-PERF-01 | PF-01 |
| NFR-PERF-02 | PF-02 |
| NFR-PERF-03 | GM-02, GM-03 |
| NFR-REL-01 | WA-07, RL-01, RL-02 |
| NFR-REL-02 | WA-06, MK-03, SG-05, SG-06, SG-08 |
| NFR-REL-03 | CC-03, RL-03 |
| NFR-SCAL-01 | DP-01 |
| NFR-SCAL-02 | DP-01, DP-02 |
| NFR-SEC-01 | AU-04, AU-05, OW-01, OW-02, OW-03, DP-02 |
| NFR-SEC-02 | UT-01, UT-02, WA-04, MK-02, MK-05 |
| NFR-SEC-03 | GM-01, CS-01, CS-02, DP-03 |
| NFR-TEST-01 | CT-01 |

## Contract acceptance (11)

| ID | Scenario IDs |
|---|---|
| T01 | AU-01, AU-02, AU-03 |
| T02 | UT-05, WA-01, WA-02, WA-03, WA-04, WA-05, WA-06, WA-07 |
| T03 | UT-01, UT-02, IN-01, IN-02, MK-01, MK-02, MK-03, PF-02 |
| T04 | IN-02, MK-04, CC-04 |
| T05 | UT-02, UT-04, MK-05, SG-01 |
| T06 | CC-01, CC-02, CC-03, CC-04 |
| T07 | UT-03, SG-02, SG-03, SG-04, SG-05, SG-06, SG-07, SG-08 |
| T08 | UT-05, CQ-01, CQ-02, CQ-03, PF-01, PF-03 |
| T09 | OW-03, GM-01, GM-02, GM-03, GM-04, RL-03 |
| T10 | AU-04, AU-05, OW-03, CS-01, CS-02 |
| T11 | OW-01, OW-02, OW-03, CQ-03, RL-01, RL-02, RL-03, DP-01, DP-02, DP-03, CT-01 |

## Deployment gates (7)

| ID | Scenario IDs |
|---|---|
| D1 | DP-01 |
| D2 | DP-01 |
| D3 | AU-01 |
| D4 | SG-01, SG-03, RL-01 |
| D5 | AU-02, WA-03 |
| D6 | DP-03 |
| D7 | DP-02 |

## Business Rules (15)

| ID | Scenario IDs |
|---|---|
| BR-01 | WA-01, MK-05, SG-01 |
| BR-02 | UT-02, UT-04, SG-01, GM-02 |
| BR-03 | UT-01, UT-02, MK-02, GM-01 |
| BR-04 | IN-02, MK-01, SG-06, CC-03 |
| BR-05 | SG-02, SG-03, SG-04, SG-05, SG-06 |
| BR-06 | UT-03, UT-04, CC-02 |
| BR-07 | CC-01, CC-04 |
| BR-08 | OW-01, OW-02, MK-02 |
| BR-09 | MK-05 |
| BR-10 | AU-05, CS-02 |
| BR-11 | GM-01, GM-02, GM-03, RL-03 |
| BR-12 | IN-01, SG-03, SG-04 |
| BR-13 | WA-03, WA-04, WA-05, WA-06, WA-07 |
| BR-14 | MK-01, MK-02, MK-05, SG-01 |
| BR-15 | OW-01, OW-02, GM-01, CS-01, CS-02, DP-03 |
