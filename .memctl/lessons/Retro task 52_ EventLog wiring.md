---
id: 500bd7532193462f
created: 2026-05-08T10:51:34.2027547Z
modified: 2026-05-08T10:51:34.2027551Z
tags:
  - retro
  - task-52
  - golden-rule
---


## Retro: task 52 — EventLog wiring

### What went well
- autoresearch brief was accurate: 10/10 preemptively caught the NeedsIngest double-logging trap + SearchBm25 archived filter
- Implementation was clean: 1 line per operator, dryRun guards correct, no layer violations

### Patterns discovered (new)
1. **NeedsIngest test interference**: EventLog.Record writes .md to vault → mtime > DB → NeedsIngest re-fires on next operator call → multiple event files in tests. Fix: use content-presence assertions (Assert.Contains with predicate) instead of Assert.Single when IngestOperator is also called.
2. **SearchSemantic archived filter gap**: SearchBm25 and SearchSemantic must both filter archived=0. Hybrid search operators (SearchOperator) fuse both paths — fixing only one still leaks archived notes through the other path.

### Score: 5/5