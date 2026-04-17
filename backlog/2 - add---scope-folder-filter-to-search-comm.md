---
id: 2
type: task
title: Add --scope folder filter to search commands
status: Done
priority: high
created: 2026-04-17
updated: 2026-04-17
---

## Description

Add --scope folder filter to search commands

## Comments

**2026-04-17 07:13 user:** Filter search results by folder/path prefix. Add --scope option to SearchSemantic, SearchBm25, and SearchOperator. Obsidian vault already stores file_path — exploit this to narrow search space before running vector similarity (spatial pre-filtering like MemPalace Wings/Rooms concept).

**2026-04-17 07:55 user:** Phase 1 complete: Spec created at docs/specs/2-spec.md

**2026-04-17 08:03 user:** Phase 2 complete: Design created at docs/designs/2-design.md

**2026-04-17 08:14 user:** Review: APPROVE — Score 4.7/5. 0 critical, 0 high, 1 medium (no unit test project — pre-existing). All 12 FRs verified.

**2026-04-17 08:14 user:** Pipeline complete. Merged to main.

**2026-04-17 08:16 user:** Retrospective complete. Assessment: Smooth. QC loops: 0, Review: APPROVE. 1 new insight saved to memory.
