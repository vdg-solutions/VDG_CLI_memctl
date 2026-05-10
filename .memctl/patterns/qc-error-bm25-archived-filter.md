---
id: 2df994d020464827
created: 2026-05-08T10:09:30.0800475Z
modified: 2026-05-08T10:09:30.0800871Z
tags:
  - qc-error
  - project-memctl
---

# qc-error-bm25-archived-filter

SearchBm25 does not filter archived=0 by default. SqliteNoteIndex.SearchBm25 joins notes_fts+notes but has no archived filter — archived notes appear in BM25 results after ingest. Fix: add AND n.archived=0 to SearchBm25 SQL query. GetAll() already does this correctly.