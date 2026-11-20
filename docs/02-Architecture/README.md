# 02-Architecture — In-game P2P Marketplace

**Trạng thái:** Proposed baseline v0.1 — bản thiết kế cho nhóm review trước khi freeze contracts. Không phải code triển khai / kết quả kiểm thử.

## Cách sử dụng
| Tài liệu | Nội dung | PlantUML |
|---|---|---|
| Domain-Driven-Design.md | Subdomains, 6 bounded contexts, aggregate roots, defaults | 01–02 |
| System-Architecture.md | System context, 5 services + Gateway, component/communication | 03–06 |
| Database-Design.md | Ownership, 5 ERD logic, Mongo model | 07–13 |
| Design-Patterns.md | Saga, CQRS, lock, outbox, state machines | 14–15 |
| Sequence-Diagrams.md | 10 luồng nghiệp vụ, 11 sequence diagrams (22A/22B), test matrix | 16–25 |
| Deployment-Architecture.md | Compose topology, monitoring, deployment limits | 26 |

Mỗi tài liệu nhúng nguyên code PlantUML. Thư mục diagrams/ chứa cùng code dưới dạng .puml độc lập. Có thể copy vào repository tại docs/02-Architecture/; dùng PlantUML plugin/renderer để render. Các sơ đồ không dùng !include từ internet. Chưa render kiểm chứng bằng PlantUML runtime.

## Nguồn và thứ tự ưu tiên
1. a(1).txt: chốt phạm vi, service, database, sync/async, deployment và testing.
2. SOA - Analyst(1).md: BR-01–05, 29 FR gốc giữ lại, NFR và nghiệp vụ.
3. SOA - Live-Service Story-telling Visual Novel Game System _ Game Item Marketplace(1).md: ý tưởng kinh tế ảo, stack, actors.
4. Trao đổi mới: Google Sign-In + ZaloPay Sandbox; 11–12 ngày, 3 người.

## Quyết định cần review
- Giữ 7 module / 6 context / 5 service + Gateway; Payment adapter trong Wallet, không mở service riêng.
- Provider đã chốt ZaloPay Sandbox; Google login đề xuất ID-token flow, provider credentials chưa xác minh. Không dùng tiền thật.
- 5% fee là ví dụ, 20% band là mặc định đề xuất; nguyên Gem, mua toàn listing, cách tính reference price theo ngày. Cần nhóm chốt.
- PendingLock intent, atomic SQL state claim, PendingSettlement item và finalize sau settlement, outbox/idempotency, versioned projection là guard bổ sung; không hạ Saga thành 3 API synchronous.
- Price policy bền vững SQL + Redis read snapshot; analytics lưu Trade_Facts để tái tạo số liệu (khác incremental-only gốc). Có thể giảm P1 nếu nhóm không kịp, nhưng cập nhật scope/FR và thống nhất với giảng viên trước.

## Next step
Review Domain / service responsibilities / data ownership cùng `../03-Contracts/API-Contracts.txt` và `../03-Contracts/marketplace-openapi.yaml` (đã có bản v0.1). Đối chiếu `../04-Deployment/` và `../05-Testing/` rồi freeze baseline trước implementation. Có 27 file PlantUML kiến trúc (Figure 22 tách 22A/22B).
