---
id: 10
type: task
title: 'G5: Temporal decay — memctl decay --days N'
status: Todo
priority: medium
created: 2026-04-19
updated: 2026-04-19
---

## Description

Old notes không decay → cạnh tranh với fresh notes trong search/list results. Vault trở nên noisy. Notes được boost tay hoặc qua auto-capture thì immune. Notes bị quên tự nhiên chìm xuống. Đây là cơ chế giữ vault focused vào "cần nhớ" thay vì "mọi thứ".

## Dependencies

- **Implement trước G1**: G1 auto-capture tạo notes ở weight=0.5 — decay cần đúng behavior trước khi G1 ship
- `SqliteNoteIndex.SetWeight` clamp đã được nâng lên [0.0, 2.0] (đã fix trong session này)
- Schema migration: thêm `archived INTEGER NOT NULL DEFAULT 0` vào notes table
- `Note.cs`: thêm `public bool Archived { get; init; } = false;`
- `Note.cs`: thêm `public DateTime? LastWeightSet { get; init; }` (để detect manual boost gần đây)

## Implementation

New command `memctl decay --days 30`:

**Files to create/modify:**
- NEW: `src/memctl/Operators/DecayOperator.cs`
- MODIFY: `src/memctl/Implementations/Index/SqliteNoteIndex.cs` — schema migration + filter archived trong `GetAll()`
- MODIFY: `src/memctl/CoreAbstractions/Entities/Note.cs` — thêm `Archived`, `LastWeightSet`
- MODIFY: `src/memctl/Bootstrap/Program.cs` — register `decay` subcommand

**Algorithm:**
1. Scan tất cả notes trong index (kể cả already-archived để update `already_archived` count)
2. Tính `days_since_modified = (now - note.Modified).Days`
3. Nếu `days_since_modified > --days` AND `LastWeightSet` không gần đây (> --days): apply decay
4. Normal tier (weight ≤ 1.0): `weight *= decay_factor` (default 0.9/period)
5. Protected tier (weight > 1.0): `weight *= pow(decay_factor, 1.0/3)` — 3× chậm hơn
6. Nếu `weight < 0.05`: set `archived = 1`
7. Upsert weight + archived vào index
8. Report JSON

## Acceptance Criteria

- `memctl decay --vault ./vault --days 30` → JSON report: `{decayed: N, archived: M, unchanged: K, already_archived: P}`
- Note với weight=1.0, không update trong 30 ngày → weight giảm: `weight *= 0.9`
- Note với weight > 1.0 (protected tier) → decay chậm hơn 3×: `weight *= pow(0.9, 1/3)` per period
- `SetWeight` nhận giá trị [0.0, 2.0]; giá trị > 2.0 bị clamp về 2.0
- Note với `LastWeightSet` < --days KHÔNG decay (manual boost gần đây = protected)
- Note với weight < 0.05: set `archived = 1` trong notes table (schema: `archived INTEGER NOT NULL DEFAULT 0`)
- Archived notes VẪN xuất hiện trong tất cả search commands (search, search-semantic, search-text, search-date, grep)
- Archived notes KHÔNG xuất hiện trong `list` mặc định; xuất hiện khi dùng `list --include-archived`
- `memctl decay --dry-run` → report mà không thay đổi weights
- Idempotent: chạy 2 lần cùng ngày → kết quả giống nhau
- `--decay-factor <float>` optional flag, default 0.9
