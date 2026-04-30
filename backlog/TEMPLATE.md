# Backlog Item Template

This is the canonical structure for backlog items in this repo. The `/sdlc` bot expects every section. Run `bash scripts/lint-backlog.sh` before starting `/sdlc` to verify.

## Hard Rules (MUST)

1. **Frontmatter complete** — id, type, title, status, priority, tags, created, updated all required.
2. **Description ≥ 3 sentences** explaining problem + impact + why now.
3. **Implementation lists every file** as `**File CREATE/MODIFY/DELETE:** path/to/file.cs` with bullet describing change.
4. **AC table FR-N + Verify column**, Verify MUST be mechanical: grep, run command, file check, dotnet test, gh api. **NEVER vague**.
   - GOOD: `dotnet test --filter MapperTests returns 0 exit, 24/24 pass`
   - GOOD: `grep -E "JsonSerializer\.(De)?serialize<" src/ returns 0 hits`
   - BAD: "looks correct"
   - BAD: "user verifies"
   - BAD: "manual review" (unless paired with specific checklist file)
5. **NFR section mandatory** with ≥ 1 entry.
6. **Dependencies explicit** — `Blocked by #N` or `none`. Soft deps separate.
7. **Out of Scope mandatory** — list deferred items by name.
8. **Risk pairs** each risk with Mitigation.
9. **Effort ≤ 16h** else split. Hours + breakdown.
10. **`[USER-ACTION-REQUIRED]`** tag on bot-blocked steps. Bot encountering this tag MUST pause + report to user, never attempt workaround.
    - Examples: PAT issuance, account creation, manual SDK install requiring admin, secret rotation, paid service signup, prod deploy approval.
    - Each tagged step MUST include: what user does, where (URL/UI path), what to paste back to bot.
11. **Step 0 fail-fast prereq** verifying env, else exit + install hint.
12. **Code examples copy-paste runnable**.
13. **No "TBD"/"Open Question"** without resolver + due date.

## Skeleton

```yaml
---
id: {N}
type: task
title: '{Imperative title — verb + object}'
status: Todo
priority: {high|medium|low}
tags: [tag1, tag2]
created: YYYY-MM-DD
updated: YYYY-MM-DD
---
```

## Description
{What is broken/missing.} {Why it matters now.} {What this task delivers.}

## Implementation

### Step 0 — Prereq fail-fast
- Verify `{tool}` available: `{command}` || exit "Install: {instruction}"

### Step 1+ — concrete steps
- **File CREATE/MODIFY/DELETE:** `path/file.cs` — {role/change}
- {commands or code blocks, copy-paste runnable}

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| FR-1 | {behavior} | `{command}` → {expected} |
| NFR-1 | {non-functional} | `{measurable check}` |

## Out of Scope
- {Deferred item}

## Dependencies
- Blocked by #N (or "none")
- Soft depend #M

## Risk

| Risk | Mitigation |
|------|-----------|
| {threat} | {countermeasure} |

## Effort
~Nh:
- {sub-step}: Mh

## User Actions Required
- [USER-ACTION-REQUIRED] {human-only step + URL + paste-back format}
