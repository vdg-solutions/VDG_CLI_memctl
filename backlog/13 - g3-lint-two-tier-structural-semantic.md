---
id: 13
type: task
title: 'G3: Lint two-tier — structural (ingest) + semantic (auto-scheduled LLM or --self)'
status: Todo
priority: medium
created: 2026-04-19
updated: 2026-04-19
---

## Description

Notes accumulate nhưng không được health-checked. Contradictions, duplicates, orphans tích lũy theo thời gian. G3 thêm lint hai tầng:

**Tier 1 — Structural lint** (free, baked vào ingest):
- Orphan notes: không có inbound links từ note nào khác
- Broken links: `[[note]]` references đến notes không tồn tại trong vault
- Duplicate candidates: cosine similarity > 0.92 giữa embeddings

**Tier 2 — Semantic lint** (LLM-based, optional):
- Contradictions: hai notes có nội dung mâu thuẫn nhau
- Stale claims: facts có thể đã outdated
- Missing cross-references: notes liên quan nhưng không link nhau
- Summary gaps: concept mentioned nhiều nơi nhưng không có page riêng

Semantic lint auto-scheduled: sau mỗi `ingest`, report bao nhiêu ngày kể từ lần semantic lint cuối. Nếu > 14 ngày, suggest gọi lệnh.

**`--self` mode**: bot tự thực hiện semantic lint mà không cần external LLM — đọc notes qua MCP/CLI, reason bằng chính Claude Code context.

## Dependencies

- G5 nên implement trước: structural lint dùng `weight` + `archived` fields để detect decay-risk
- `INoteIndex` đã expose `GetAll()`, `SearchSemantic()`, link graph — dùng trực tiếp
- Vault metadata file `.memctl/meta.json`: cần tạo nếu chưa có khi lint chạy lần đầu

## Implementation

`memctl lint`:

**Files to create/modify:**
- NEW: `src/memctl/Operators/LintOperator.cs`
- MODIFY: `src/memctl/Operators/IngestOperator.cs` — append structural lint summary + semantic lint hint to output
- MODIFY: `src/memctl/Bootstrap/Program.cs` — register `lint` subcommand

**Algorithm — Structural (always):**
1. `index.GetAll()` → all notes
2. Orphans: notes where `inbound_link_count == 0` (check link graph)
3. Broken links: notes containing `[[X]]` where X not found in index by title/path
4. Duplicates: pairs where `CosineSimilarity(a.Embedding, b.Embedding) > 0.92`
5. Decay-risk: notes where `weight in [0.05, 0.3]` AND `days_since_modified > 60` AND `inbound_link_count >= 2`

**Algorithm — Semantic (`--semantic`):**
1. Batch notes (max 50/batch) → call LLM with structured prompt
2. LLM returns: contradictions[], stale_claims[], missing_links[], summary_gaps[]

**Algorithm — Self (`--self`):**
1. Format all notes as structured markdown prompt → stdout
2. Bot reads, reasons, calls `memctl create` to save report manually

**Ingest changes:**
- Read `.memctl/meta.json` → check `last_semantic_lint` timestamp
- Append to ingest JSON output: `"semantic_lint": {"days_since": N, "overdue": bool, "hint": "..."}`

`ingest` changes:
- Sau khi index xong: check `last_semantic_lint` timestamp
- Nếu > 14 ngày (hoặc chưa từng chạy): print hint với suggested command

## Acceptance Criteria

- `memctl lint` → structural report: orphans (list), broken links (list), duplicate candidates (list với similarity score), decay-risk candidates (list)
- **Decay-risk candidates**: notes với weight trong [0.05, 0.3] AND chưa được modified > 60 ngày AND có ít nhất 2 inbound links (high centrality, low weight)
- Decay-risk report format per note: `{id, title, weight, days_since_modified, inbound_link_count}`
- Decay-risk report kèm hint: "These notes are referenced but at risk of archiving. Run: memctl weight <id> 0.8 to protect."
- `memctl lint --semantic --llm-url <url> --llm-model <model> --llm-key <key>` → full report bao gồm contradictions, stale claims, gaps
- `memctl lint --self` → in ra structured self-analysis prompt để bot đọc và reason (không call external LLM)
- Report format: JSON (default) hoặc markdown (`--format md`)
- `ingest` output cuối: "Semantic lint: {N} days since last run. Run: memctl lint --semantic ..."
- Nếu chưa từng semantic lint: "Semantic lint: never run."
- Track `last_semantic_lint` trong vault metadata file (`.memctl/meta.json` hoặc tương đương)
- `memctl lint --update-timestamp` → chỉ update timestamp mà không chạy lint (bot dùng sau self-analysis)
- Structural lint không cần vault re-ingest — đọc từ existing index
- Exit 0 khi vault không tồn tại — empty report
- LLM call timeout 30s, exit 1 nếu fail (structural results vẫn output trước đó)
- `memctl lint --save` → persist structural lint report as vault note `lint/<date>-structural.md` (enables lint history compounding across sessions)
