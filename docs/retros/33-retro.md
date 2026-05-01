# Retrospective: Task 33 — V2 docs ship + version 1.3.0

**Date:** 2026-05-01
**Overall Assessment:** Smooth (docs-only, version bump, sync — no code regression risk)

## Metrics

| Metric | Value |
|--------|-------|
| QC Retry Loops | 0 |
| Final QC | 8/8 ACs pass (FR-1..5, FR-8 + NFR-1..3); FR-6/7 verified post-merge via API sync |
| Review Verdict | APPROVE (inline self-review) |
| Critical Issues | 0 |
| Total commits | 3 (spec+design / build / merge) |
| Public memctl-releases sync | 3 files PUT 200 OK |

## What Went Well

1. **Skill sync script worked first try** — `bash scripts/sync-skill-to-plugin.sh` propagated docs/memctl.md → plugin/skills/memctl/SKILL.md without diff issues.
2. **Version bump atomic in single commit** — csproj + plugin.json bumped together, workflow `verify-versions` will gate next tag push.
3. **Workflow ordering manually verified** — mechanical grep failed initially (wrong regex), but line number inspection confirmed: release job (122) contains "Sync plugin source" step (163), sync-marketplace (187) depends release.
4. **API sync clean** — 3 PUT calls to public memctl-releases all 200 OK. Post-sync grep verifies V2.1 content live.
5. **Bulk sed legacy purge** — earlier wiki consolidation (#28+ retro) primed the codebase to drop legacy patterns cleanly. Plugin README + docs/memctl.md updated without lingering `.memctl-vault/` refs.

## What Didn't Go Well

1. **Initial workflow grep regex broke**
   - Root cause: `grep -B1 "name: Sync plugin source" | grep -q "release:"` returned no match because `-B1` shows the line BEFORE, not the parent job header. YAML structure has `- name:` at step level, not "release:" prefix on the line above.
   - Impact: AC FR-8 mechanical check failed; em fell back to manual line-number verify.
   - Prevention: workflow ordering verify needs YAML-aware parser, not raw grep. Use `python -c "import yaml; ..."` or `yq`. Add to wishlist as cosmetic improvement.

2. **Public sync endpoint 4xx not retried**
   - Em didn't observe 4xx in this run, but sync_file lacks retry-on-fail logic. If 1 of 3 files fails, partial sync state.
   - Impact: none this run (all 200). But fragility flagged.
   - Prevention: workflow auto-sync on next tag push handles fallback. Manual API sync is best-effort by design.

## Patterns Detected

### New Patterns

1. **YAML structural verify needs parser, not grep**
   - Observation: workflow ordering checks via grep -B/-A miss because YAML has nested structure (`- name:` inside `release:` indentation). Line-aware grep can't capture parent-child relationship.
   - Rule candidate: when verifying YAML structure (job order, dependency chains), use `python -c "import yaml; data=yaml.safe_load(...); assert ..."` not grep.
   - Triggers: [yaml, workflow, grep, structure, parser]
   - Confidence: medium

### Reinforced

1. **Skill sync script proven** (from #28 retro) — `cp` binary copy preserves CRLF/LF, frontmatter intact. Diff returns empty. Confirmed effective.
2. **Atomic version bump in 1 commit** — workflow `verify-versions` (#28) gate reliable.

### Anti-Patterns

(none)

## Recommendations

### For Spec Phase
- Future doc-only tasks: don't over-spec the design phase. The backlog item already has all path-level detail; design becomes summary + integration code blocks.

### For Build Phase
- Build clean + 49/49 baseline pre-changes confirmed — no code regression risk for docs+version task. Pattern: docs-only changes should pass NFR-3 "0 new tests" trivially.

### For QC Phase
- Workflow YAML grep checks brittle — when next workflow change ships, replace grep with python yaml.safe_load() inspection.

### For Memory System
- Add new error pattern: `pat_yaml_grep_structural_check_brittle` (hit_count: 1).
