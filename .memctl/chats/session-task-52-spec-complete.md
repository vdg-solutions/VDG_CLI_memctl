---
id: 7712928fe64b482e
created: 2026-05-08T10:27:38.6308444Z
modified: 2026-05-08T10:27:38.6308446Z
tags:
  - session
  - task-52
---


## Current Task: 52 — General event logging system (EventLog)
## Phase: Spec — complete
## Spec Path: docs/specs/52-spec.md
## E2E: false

## Key Requirements
- Wire EventLog.Record into Add, Delete, Weight, Decay, Organize, MigrateTags, Distill operators
- Fix SearchBm25: add AND n.archived=0 to SQL (line ~92 SqliteNoteIndex.cs)
- Keep HookLog untouched; no double-logging at Program.cs capture sites
- dryRun operators (Decay, MigrateTags): no EventLog on dryRun

## Open Questions
- None