---
id: 38
type: epic
title: Memory & Observation layer — chats, events, consolidation
status: Done
priority: high
children:
- 36
- 37
- 39
tags:
- memory,events,storage
created: 2026-05-07
updated: 2026-05-07
---

## Description

memctl hiện có `capture` + `context-inject` nhưng thiếu hai thứ: (1) conversation turns đang ghi sai folder, (2) không có general event store — mọi hoạt động của bot (operator runs, errors, hook fires) đều không được lưu lại có cấu trúc.

Epic này bổ sung lớp Memory & Observation đầy đủ: conversation lưu đúng chỗ, events tổng quát có schema, cả hai searchable qua vault.

## Children

- **#36** Fix conversation storage mismatch `sessions/` → `chats/` *(prerequisite — làm trước)*
- **#37** General event logging system — `EventLog` + `events/` folder
- **#39** `memctl distill` — LLM-driven conversation-to-memory extraction *(Layer 1 → Layer 2)*

## Dependency order

`#36` → `#37` → `#39`

`#36` làm trước vì sửa vault init (tránh conflict với #37). `#39` làm sau vì distill đọc từ `chats/` (cần #36 đúng trước).

## Out of scope

- Cross-vault lesson sync — wishlist
- Scheduled/automatic distillation — manual `memctl distill` only in this epic

## Comments

**2026-05-07 10:40 user:** Epic complete. All children done: #36 (conversation rename), #37 (EventLog), #39 (distill). Auto-closed.
