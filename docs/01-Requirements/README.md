# 01-Requirements — Requirements baseline

Version 0.1.0 | Proposed, pending group approval

Read in order:

1. `Project-Scope.md` — bối cảnh, phạm vi, assumptions, deliverables.
2. `Business-Requirements.md` — actors, rules, economic policies, workflows.
3. `Functional-Requirements.md` — 29 retained FRs, acceptance criteria and traceability to API/message contracts.
4. `Non-Functional-Requirements.md` — measurable quality targets, verification criteria.
5. `diagrams/01-use-case-overview.puml` — optional overview; actors outside business boundary, daily timer is a logical actor, not separately deployed service.

Priority: a(1).txt for MVP/reduction; original idea and Analyst for business requirements; user later chose ZaloPay Sandbox + Google Sign-In, 11–12 day implementation; Architecture and Contracts v0.1 for proposed refinements. The 29 original retained FR IDs are preserved; BR-13..15 and NFR-INT-01/TEST-01 added for traceability rather than new business features. NFR-OBS-01 added per a(1).txt. Existing docs are not overwritten.

Group freeze checklist: approve fee, price band/weighted-average rule, whole-stack scope, inventory capacity, top-up packages/rates, status policies, seeded login and external credentials; confirm actual rubric and test budgets. Review ERDs and diagrams against Contracts; some files v0.1 predate provider selection. No runtime tests or PlantUML rendering performed. 04-Deployment and 05-Testing now contain proposed guides and test plans, not runtime evidence.
