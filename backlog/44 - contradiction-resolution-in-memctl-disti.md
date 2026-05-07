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
public enum ContradictionResolution { KeepNew, KeepExisting, Merge }

public sealed record ContradictionResult(
    bool                    Contradicts,
    string?                 ExistingId,
    ContradictionResolution Resolution,
    string?                 MergedContent,
    string                  Rationale);
```

**Resolution là enum, không phải magic string** — type-safe, no switch-on-string anti-pattern.

### Flag

`memctl distill --resolve-contradictions` (opt-in, default off). Flag name committed — `--full` variant dropped.

### Breaking change: ILlmClient gains new method

`ILlmClient` là public interface — adding `CheckContradictionAsync` requires ALL existing implementations and test stubs to implement it. Affected:
- `OpenAiLlmClient` — full impl
- `CountingLlmClient` (in `DistillOperatorTests.cs`) — stub: return `new ContradictionResult(false, null, ContradictionResolution.KeepNew, null, "")`
- `FixedResultLlmClient` (in `DistillOperatorTests.cs`) — same stub

Error handling: if `CheckContradictionAsync` throws, log via `EventLog.Record` and treat as `Contradicts=false` (best-effort, proceed with extraction). Never abort distill on contradiction check failure.

## Implementation notes

- Search candidates: `index.SearchBm25(note.Title, 5)` filter by same type (decision/pattern/lesson)
- Only check when candidates exist — skip LLM call if no same-type candidates found
- **Validate ExistingId**: after LLM returns `ContradictionResult`, verify `ExistingId` is in the candidate list (`candidates.Any(c => c.Id == result.ExistingId)`). If not found → treat as `Contradicts=false`. Prevents LLM hallucination silently archiving wrong note.
- `archived: true` + tag `superseded` set in BOTH file frontmatter AND index (`index.Upsert`) — index must reflect archived state for `GetAll(false)` to exclude it
- `superseded` notes: set `Weight = 0` to make them decay-immune (already at floor) — prevents `memctl decay` from further modifying them
- Log resolution: `Console.Error.WriteLine($"[distill] contradiction resolved: {resolution} — {rationale}")`

## Acceptance criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | Default `memctl distill` (no flag) behavior unchanged | run without flag → no contradiction checks, no LLM calls for CheckContradiction |
| AC-2 | `--resolve-contradictions`: LLM called per extracted note with same-type candidates | mock → CheckContradictionAsync call count = number of extracted notes with candidates |
| AC-3 | `keep_new` (KeepNew): existing note archived in file (archived:true, tag superseded) AND in index | contradiction → old note: file has archived:true, index.GetAll(false) excludes it |
| AC-4 | `keep_existing` (KeepExisting): extracted note not written, no new file | contradiction KeepExisting → file count unchanged |
| AC-5 | `merge` (Merge): merged content written as new file, existing archived | contradiction Merge → new file with merged content + old archived |
| AC-6 | No contradiction (Contradicts=false): extracted note written normally | no contradiction → new note present |
| AC-7 | `superseded` archived notes have Weight=0 in index | post-archive → index.GetById shows Weight=0 |
| AC-8 | CheckContradictionAsync throws → distill continues, note extracted as normal | mock throw → distill succeeds, note written |
| AC-9 | Same-type filter: decision only checked against decisions, not lessons/patterns | decision extraction → candidates from decisions/ only |

## Files

- `src/memctl/CoreAbstractions/Entities/ContradictionResult.cs` (new — enum + record)
- `src/memctl/CoreAbstractions/Ports/ILlmClient.cs` (add CheckContradictionAsync)
- `src/memctl/Implementations/Llm/OpenAiLlmClient.cs` (implement CheckContradictionAsync)
- `src/memctl/Operators/DistillOperator.cs` (wire --resolve-contradictions)
- `src/memctl/Bootstrap/Program.cs` (add --resolve-contradictions flag)
- `tests/memctl.Tests/Operators/DistillOperatorTests.cs` (update CountingLlmClient + FixedResultLlmClient stubs)
- `tests/memctl.Tests/Operators/DistillContradictionTests.cs` (new)

## Dependency

Independent of #43 — no conflict.

## Effort

~3.5h: ContradictionResult enum+record (0.25h) + ILlmClient + stubs update (0.5h) + OpenAiLlmClient (0.75h) + DistillOperator wire (0.75h) + tests (1.25h)