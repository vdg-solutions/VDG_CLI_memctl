# Technical Design: V2 docs ship

**Spec:** docs/specs/33-spec.md
**Task:** 33
**Date:** 2026-05-01

## 1. Architecture Overview

3 doc rewrites + 2 version bumps + 3 API syncs. No code changes. Sequential commits to keep merge atomic.

## 2. File Changes

### Modified

| File | Change |
|------|--------|
| `docs/memctl.md` | V2.1 layout diagram + writers ownership matrix + "Upgrading from V1" manual mv flow |
| `plugins/memctl-claude/README.md` | V2.1 init examples (`memctl init --vault .`), drop `.memctl-vault/` pattern, auto-detect priority |
| `backlog/wiki/memory-pipeline.md` | Drop "verified" claim about MEMCTL_VAULT (never wired in V1) |
| `backlog/wiki/vault-layout.md` | Drop migrate-vault references; replace with manual upgrade pointer |
| `plugins/memctl-claude/skills/memctl/SKILL.md` | Auto-synced from docs/memctl.md via `bash scripts/sync-skill-to-plugin.sh` |
| `src/memctl/memctl.csproj` | `<Version>1.2.0</Version>` → `<Version>1.3.0</Version>` |
| `plugins/memctl-claude/.claude-plugin/plugin.json` | `"version": "1.2.0"` → `"version": "1.3.0"` |

### Manual API sync (post-merge, pre-tag)

3 PUT calls to `vdg-solutions/memctl-releases/contents/<path>`:
- `SKILL.md` (top-level — release archive embed)
- `plugins/memctl-claude/README.md`
- `plugins/memctl-claude/skills/memctl/SKILL.md`

After v1.3.0 tag push, workflow auto-sync (release job last step from #25 + #28) takes over.

## 3. Workflow ordering verify (FR-8)

Mechanical grep on `.github/workflows/release.yml`:

```bash
# 1. release job has "Sync plugin source" step
grep -B1 "name: Sync plugin source" .github/workflows/release.yml | grep -q "release:" 

# 2. sync-marketplace job depends on release job
grep -A1 "sync-marketplace:" .github/workflows/release.yml | grep -q "needs: release"
```

Both must pass before tag push. If either fails, fix workflow YAML before merging this child.

## 4. Implementation Order

1. Edit `docs/memctl.md` — V2.1 examples, manual upgrade
2. Edit `plugins/memctl-claude/README.md` — V2.1 init, drop legacy
3. Edit `backlog/wiki/memory-pipeline.md` — drop env var verified claim
4. Edit `backlog/wiki/vault-layout.md` — drop migrate-vault refs
5. `bash scripts/sync-skill-to-plugin.sh` — propagate skill text
6. Diff verify: `diff -q docs/memctl.md plugins/memctl-claude/skills/memctl/SKILL.md` exit 0
7. Edit csproj `<Version>1.3.0</Version>`
8. Edit plugin.json `"version": "1.3.0"`
9. Verify workflow ordering grep checks
10. `dotnet build` clean
11. `dotnet test` 49/49 pass
12. Commit + push branch + merge
13. (post-merge) API sync 3 files to public memctl-releases

## 5. Smoke (post-merge, pre-tag)

```bash
grep -oE '<Version>[^<]+' src/memctl/memctl.csproj   # expect 1.3.0
python -c "import json; print(json.load(open('plugins/memctl-claude/.claude-plugin/plugin.json'))['version'])"   # expect 1.3.0
diff -q docs/memctl.md plugins/memctl-claude/skills/memctl/SKILL.md   # expect 0
grep -c "\.memctl/" docs/memctl.md   # expect ≥ 3
```

## 6. Testing

Doc-only + version bump. No new code. NFR-3 enforces 0 new test files.
