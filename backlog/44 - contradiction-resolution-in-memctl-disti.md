---
id: 44
type: task
title: Contradiction resolution in memctl distill
status: Todo
priority: low
tags:
- memory,distill,qc
created: 2026-05-07
updated: 2026-05-07
---

## Description

`memctl distill` hiện chỉ extract memories mới từ conversations → write L2 notes. Không kiểm tra xem extraction mới có mâu thuẫn với L2 notes đã tồn tại không. Kết quả: vault có thể tích lũy contradictions theo thời gian (e.g. "always use X pattern" + "never use X pattern" từ hai conversations khác nhau).

Anthropic's dreaming feature explicitly "resolves contradictions in memory" — cần implement tương đương.

## Design

### Approach: LLM-driven contradiction check trong distill flow

Sau khi extract `DistilledNote[]` từ conversation, trước khi write L2 files:

1. Với mỗi extracted note, search vault index cho existing notes có cùng topic/tags
2. Feed extracted note + top-K related existing notes vào LLM: "Có contradiction không?"
3. LLM trả về: `{ "contradicts": bool, "existing_id": "...", "resolution": "keep_new|keep_existing|merge", "merged_content": "..." }`
4. Apply resolution:
   - `keep_new`: write extracted note, delete/archive existing
   - `keep_existing`: skip extracted note
   - `merge`: write merged content, archive existing

### New LLM method

`ILlmClient.CheckContradictionAsync(DistilledNote newNote, IReadOnlyList<Note> candidates)` → `ContradictionResult`

```csharp
public sealed record ContradictionResult(
    bool Contradicts,
    string? ExistingId,
    string Resolution,   // "keep_new" | "keep_existing" | "merge"
    string? MergedContent,
    string Rationale);
```

### Flag

`memctl distill --resolve-contradictions` (opt-in, default off) — vì tốn thêm LLM calls (1 call per extracted note).

Hoặc `memctl distill --full` = distill + contradiction resolution.

## Implementation notes

- Search candidates: `index.SearchBm25(note.Title, 5)` + filter bằng tag overlap
- Chỉ check khi có candidates trong same type (decision vs decision, lesson vs lesson)
- Log resolution decisions để user có thể review: `memctl distill --resolve-contradictions --verbose`
- Archived notes (loser của resolution) set `archived: true` + tag `superseded` — không delete

## Acceptance criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | `--resolve-contradictions` flag hoạt động, không break default distill | `memctl distill` (không flag) → unchanged behavior |
| AC-2 | LLM được gọi với candidate notes khi flag enabled | mock LLM → verify ContradictionCheckAsync called |
| AC-3 | `keep_new`: existing note bị archive (`archived: true`, tag `superseded`) | setup contradiction → distill → old note archived |
| AC-4 | `keep_existing`: extracted note bị skip, không write file mới | setup contradiction → distill → new note NOT written |
| AC-5 | `merge`: merged content được write, existing archived | setup contradiction với merge resolution → verify merged file + old archived |
| AC-6 | No contradiction: both notes written normally | non-contradicting notes → both present |
| AC-7 | Resolution logged khi `--verbose` | verbose output có "Resolved contradiction: keep_new/keep_existing/merge" |

## Files

- `src/memctl/CoreAbstractions/Entities/ContradictionResult.cs` (new)
- `src/memctl/CoreAbstractions/Ports/ILlmClient.cs` (thêm CheckContradictionAsync)
- `src/memctl/Implementations/Llm/OpenAiLlmClient.cs` (implement)
- `src/memctl/Operators/DistillOperator.cs` (wire contradiction check)
- `src/memctl/Bootstrap/Program.cs` (thêm --resolve-contradictions flag)
- `tests/memctl.Tests/Operators/DistillContradictionTests.cs` (new)

## Dependency

Sau #43 (hoặc independent — không conflict).

## Effort

~3h: ContradictionResult + ILlmClient (0.5h) + OpenAiLlmClient impl (0.75h) + DistillOperator wire (0.75h) + tests (1h)