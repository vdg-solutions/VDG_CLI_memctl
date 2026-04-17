---
id: 5
type: task
title: Enable global CLI install via dotnet tool or PATH
status: Done
priority: high
created: 2026-04-17
updated: 2026-04-17
---

## Description

Enable global CLI install via dotnet tool or PATH

## Comments

**2026-04-17 07:13 user:** Package memctl as a dotnet global tool (dotnet tool install -g memctl) so it can be called from any directory without full path. Update memctl.csproj with PackAsTool, ToolCommandName. Also consider adding to PATH via installer script for non-dotnet users.

**2026-04-17 07:24 user:** Phase 1 complete: Spec created at docs/specs/5-spec.md

**2026-04-17 07:29 user:** Phase 2 complete: Design created at docs/designs/5-design.md

**2026-04-17 07:36 user:** Phase 3 complete: Build score 4.8/5, 0 QC loops. Moving to QC.

**2026-04-17 07:45 user:** Phase 5: Review APPROVED (4.7/5). 0 issues. Merging.

**2026-04-17 07:47 user:** Retrospective complete. Assessment: Minor Issues. QC loops: 0, Review: APPROVED (4.7/5) after 1 iteration. 1 new pattern (PS7 compat), 2 insights saved.
