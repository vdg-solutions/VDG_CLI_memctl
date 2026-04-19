## SDLC Pipeline: Batch run — all Todo tasks
## Current Task: 10 — G5: Temporal decay — memctl decay --days N
## Phase: Design — spec complete
## Branch: feature/10-g5-temporal-decay
## Mode: auto
## Autoresearch: true
## E2E: false
## ProjectType: cli_tool
## Queue: [10, 13, 9]

## Spec Path: docs/specs/10-spec.md

## Key Findings from Codebase
- Note.cs: sealed record, currently has Weight (float), AccessCount (int) — need Archived (bool), LastWeightSet (DateTime?)
- INoteIndex: GetAll() returns IReadOnlyList<Note> ordered by weight DESC, access_count DESC — need GetAll(bool includeArchived) overload
- SqliteNoteIndex: MigrateAddColumn pattern for idempotent migrations; SetWeight clamps [0,2] at index level but WeightOperator clamps [0,1]
- SetWeight must also write last_weight_set = UtcNow
- ListOperator: passes GetAll() → needs includeArchived param
- Search methods: no archive filter needed — archived notes always searchable
- WeightOperator: clamp to 1.0 currently; needs update to 2.0 for protected tier to be usable via weight command
- MemctlOutcome.Ok("decay", message, data) pattern for report

## Open Questions (high priority)
- Q1: Idempotency via metadata last_decay_date storage? Algorithm as-is would double-decay on second run
- Q3: WeightOperator clamp update scope (CLI guard vs index level)

## Task #12: DONE — merged to main 2026-04-19
