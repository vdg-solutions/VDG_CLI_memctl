---
description: Run vault structural + semantic lint — catch orphans, broken links, stale duplicates
---
Run `memctl ingest` (structural lint baked in). If `$ARGUMENTS` contains the word `semantic` or `deep`, additionally run `memctl lint --semantic --self`, reason about the output, and save the report via `memctl add --title "Lint report <date>" --content "<summary>"`.
