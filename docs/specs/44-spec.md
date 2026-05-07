# Spec #44 — Contradiction resolution in memctl distill

## Problem

`memctl distill` extracts L2 notes without checking whether new extractions conflict with existing L2 vault notes. Over time the vault accumulates contradictions (e.g. "always use X pattern" + "never use X pattern" from different conversations).

## Functional Requirements

| FR | Requirement |
|----|-------------|
| FR-1 | `--resolve-contradictions` opt-in flag on `memctl distill`. Default behavior (no flag) unchanged. |
| FR-2 | When flag is set: for each extracted note, search index for same-type existing notes (top-5 BM25 candidates). |
| FR-3 | If candidates found, call `ILlmClient.CheckContradictionAsync(newNote, candidates)` → `ContradictionResult`. |
| FR-4 | `ContradictionResolution` is an enum: `KeepNew | KeepExisting | Merge`. No magic strings. |
| FR-5 | `KeepNew`: write extracted note, archive existing (file: `archived: true` + tag `superseded`; index: Upsert with archived state). |
| FR-6 | `KeepExisting`: skip extracted note, do not write new file. |
| FR-7 | `Merge`: write merged content as new file, archive existing. |
| FR-8 | No contradiction (`Contradicts=false`): write extracted note normally. |
| FR-9 | Validate `ExistingId` from LLM against candidate list. Invalid ID → treat as `Contradicts=false`. |
| FR-10 | `CheckContradictionAsync` throws → log via `EventLog.Record`, treat as `Contradicts=false`. Distill continues. |
| FR-11 | Archived `superseded` notes: set `Weight=0` in both file frontmatter and index (decay-immune). |
| FR-12 | No candidates found (no same-type notes in vault) → skip LLM call, write extracted note normally. |

## Non-Functional Requirements

| NFR | Requirement |
|-----|-------------|
| NFR-1 | `ContradictionResult` is a `sealed record` with `ContradictionResolution` enum — type-safe, no switch-on-string |
| NFR-2 | `ILlmClient` gains `CheckContradictionAsync` — breaking change; `CountingLlmClient` + `FixedResultLlmClient` stubs in `DistillOperatorTests.cs` must implement it |
| NFR-3 | Log resolution: `Console.Error.WriteLine($"[distill] contradiction resolved: {resolution} — {rationale}")` |
| NFR-4 | Same-type filter: decisions checked only against decisions/, patterns against patterns/, lessons against lessons/ |

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | Default `memctl distill` (no flag) behavior unchanged — no CheckContradiction LLM calls | mock → 0 calls to CheckContradictionAsync |
| AC-2 | `--resolve-contradictions`: CheckContradictionAsync called per extracted note with candidates | mock with candidates → call count = notes with same-type candidates |
| AC-3 | `KeepNew`: old note has `archived: true` + `superseded` tag in file AND index excludes it from GetAll | contradiction KeepNew → old file has archived:true, GetAll(false) excludes it |
| AC-4 | `KeepExisting`: extracted note not written | contradiction KeepExisting → file count unchanged |
| AC-5 | `Merge`: merged content written as new file, existing archived | contradiction Merge → new file + old archived |
| AC-6 | No contradiction: extracted note written normally | Contradicts=false → new note present |
| AC-7 | Archived notes have Weight=0 in index | post-archive → indexed note.Weight == 0 |
| AC-8 | CheckContradictionAsync throws → distill continues, note extracted as normal | mock throw → distill succeeds, note written |
| AC-9 | Same-type filter: decision extraction → candidates from decisions/ only | decision extracted → CheckContradiction candidates all start with decisions/ |
| AC-10 | Invalid ExistingId from LLM → treated as Contradicts=false | ExistingId not in candidates → note written normally |
