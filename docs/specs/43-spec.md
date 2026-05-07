# Spec #43 — Auto-trigger distill via context-inject threshold counter

## Problem
`memctl distill` must be called manually. Need auto-recommendation to LLM when N conversations have accumulated since last distill.

## Functional Requirements

| FR | Requirement |
|----|-------------|
| FR-1 | `DistillStateStore` tracks `conversations_since_distill` in `.obsidian/memctl/distill-state.json` |
| FR-2 | `CaptureOperator.CreateNote` increments counter after writing note. `AppendNote` does NOT increment. |
| FR-3 | `ContextInjectOperator` appends `## Distill Recommendation` block when counter >= threshold |
| FR-4 | `ContextInjectOperator` returns recommendation even when vault is empty (null path) |
| FR-5 | `DistillOperator.ExecuteAsync` resets counter to 0 on success (non-dry-run) |
| FR-6 | `memctl config set distill-threshold <n>` updates threshold in state file |

## Non-Functional Requirements

| NFR | Requirement |
|-----|-------------|
| NFR-1 | All DistillStateStore methods are best-effort — swallow all exceptions, never crash callers |
| NFR-2 | State file write is atomic: temp file + `File.Move(..., overwrite: true)` |
| NFR-3 | Corrupt/missing JSON → default state (count=0, threshold=5) |
| NFR-4 | `DistillStateStore` in `Operators/` layer — same as `EventLog.cs` |

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | `distill-state.json` created on first capture | capture → file exists |
| AC-2 | Counter increments in CreateNote only, not AppendNote | same conv ID twice → counter=1 |
| AC-3 | context-inject appends recommendation when counter >= threshold | threshold=2, 2 captures → "Distill Recommendation" in output |
| AC-4 | context-inject does NOT append below threshold | counter=1, threshold=5 → no recommendation |
| AC-5 | context-inject returns recommendation even with empty vault | empty vault + threshold met → recommendation present |
| AC-6 | distill resets counter on success | distill → counter=0 |
| AC-7 | distill --dry-run does NOT reset counter | dry-run → counter unchanged |
| AC-8 | config set distill-threshold changes threshold | set 10 → recommendation at count=10 |
| AC-9 | DistillStateStore errors do not crash CaptureOperator | IO error → capture succeeds |
| AC-10 | Corrupt JSON → fallback to defaults, no crash | corrupt JSON → ShouldRecommend=false |
