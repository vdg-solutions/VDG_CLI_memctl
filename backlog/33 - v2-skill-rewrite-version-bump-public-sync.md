---
id: 33
type: task
title: 'V2 docs ship — skill rewrite + plugin README + version 1.3.0 + public memctl-releases sync'
status: Todo
priority: high
parent: 30
tags:
  - docs
  - skill
  - plugin
  - version
  - sync
  - release
  - layout-v2
created: 2026-05-01
updated: 2026-05-01
---

## Description

Final child of epic #30. Rewrite skill + plugin docs để khớp V2 layout, bump csproj + plugin.json `1.2.0 → 1.3.0`, sync public memctl-releases (top-level SKILL.md + plugin source). Verify workflow `sync-marketplace` job ordering ensures plugin source push completes BEFORE marketplace bump (no race window where users fetch new marketplace.json pointing at unchanged plugin source).

This child gates epic completion — once shipped, anh push tag `v1.3.0`, workflow auto-builds + auto-syncs marketplace + plugin source, end users `claude plugin update memctl@vdg-solutions` get V2 binary + V2 plugin source. Existing V1 vaults surface loud `legacy v1` warning (from #31) until user runs `memctl migrate-vault` (from #32).

## Implementation

### Step 0 — Prereq fail-fast
- Verify `#31` Done: `bl show 31 | grep -q '^status: Done'` || exit "Blocked by #31"
- Verify `#32` Done: `bl show 32 | grep -q '^status: Done'` || exit "Blocked by #32"
- Verify `#28` Done (workflow verify-versions enforces lockstep): `bl show 28 | grep -q '^status: Done'` || exit "Blocked by #28"
- Verify build clean + all 57 tests still pass: `dotnet test tests/memctl.Tests/memctl.Tests.csproj --nologo` || exit "Fix tests first"

### Step 1 — Rewrite docs/memctl.md (skill, single source of truth)

- **File MODIFY:** `docs/memctl.md`
- Remove all V1 examples: `memctl init ~/my-vault`, `<vault>/.memctl-vault`, `cd ~/my-vault`. Replace with V2 examples.

V2.1 example block to insert:
```markdown
## Vault layout (V2.1 as of v1.3.0)

`memctl init --vault <project-anchor>` creates `<project-anchor>/.memctl/` as the vault root container:

```
<project-anchor>/                ← can be a project repo, your $HOME, anywhere
├── .memctl/                     ← vault root (Obsidian opens here)
│   ├── .obsidian/               ← Obsidian config (auto-hidden in Obsidian)
│   │   └── memctl/              ← memctl runtime (nested, hidden)
│   │       ├── index.db
│   │       ├── models/embeddinggemma-300m/
│   │       └── hook.log
│   ├── tasks/                   ← /sdlc per-phase artifacts
│   ├── patterns/                ← /retro error patterns
│   ├── lessons/                 ← /qc-dream wisdom
│   ├── decisions/               ← /design ADRs
│   ├── chats/                   ← Stop hook daily-rollups (YYYY-MM-DD.md)
│   ├── attachments/             ← images, binaries
│   ├── claude-memory/MEMORY.md  ← top-level index
│   └── *.md                     ← ad-hoc user notes
├── src/                         ← project files OUTSIDE .memctl/ are NOT indexed
└── README.md                    ← also not indexed
```

### Writer ownership

| Subdir | Writer | Mutate |
|--------|--------|--------|
| `tasks/` | /sdlc orchestrator | append per phase (task-{id}-{phase}.md) |
| `patterns/` | /retro post-merge | mutate hit_count |
| `lessons/` | /qc-dream | dedupe + merge |
| `decisions/` | /design | append-only ADR (adr-{NNNN}-{slug}.md) |
| `chats/` | Stop hook (`memctl capture`) | append into daily file |
| `attachments/` | tool/hook output | append-only |
| `claude-memory/MEMORY.md` | /qc-dream consolidation | rewrite (compress) |

Memctl walks up from the cwd looking for `.memctl/` containing `.obsidian/`. Per-project install is the natural default — projects with their own `.memctl/` always resolve to themselves first, no env var needed.

To open the vault in Obsidian app: open `<project-anchor>/.memctl/` as the vault folder.

## Migrate from V1 (pre-v1.3.0)

V1 placed `.obsidian/` and `.memctl/` as siblings at the project root, polluting the index with non-memory `.md` files (READMEs, docs/, src/). V2 fixes this. Migrate existing V1 vaults:

```bash
memctl migrate-vault --vault <V1-vault-path> --dry-run    # preview
memctl migrate-vault --vault <V1-vault-path>              # copy notes to <vault>/.memctl-v2/
memctl ingest --vault <V1-vault-path>/.memctl-v2          # rebuild index
# verify notes are in .memctl-v2/
# manually clean up V1 artifacts and rename .memctl-v2 -> .memctl
```

V1 vault is read-only during migrate — safe to retry, no data loss.
```

- Replace remaining session-protocol examples that reference vault paths.

### Step 2 — Rewrite plugin README

- **File MODIFY:** `plugins/memctl-claude/README.md`
- Drop legacy "per-project vault subdir" pattern using `.memctl-vault/` workaround.
- Replace with V2 init:

```bash
# Per-project (default — vault scoped to the project):
cd C:\repos\my-project
memctl init --vault .                    # creates ./.memctl/ as vault root
Add-Content .gitignore ".memctl/"

# Personal global (cross-project notes — opt-in):
memctl init --vault $HOME\memctl-personal
[Environment]::SetEnvironmentVariable('MEMCTL_VAULT', "$HOME\memctl-personal\.memctl", 'User')
```

Update auto-detect priority section to V2 markers:
```
1. --vault <path> CLI flag
2. Walk-up from cwd looking for `.memctl/` containing `.obsidian/`
3. Walk-up legacy V1 (.obsidian/ + .memctl/ siblings) — surfaces "run memctl migrate-vault" warning
4. error "no vault found"
```

### Step 3 — Update vault-isolation-runbook.md

- **File MODIFY:** `docs/vault-isolation-runbook.md`
- Section "Three-tier setup" → V2 examples (per-project default, personal global opt-in, team-shared advanced).
- Drop "verified" claim about MEMCTL_VAULT — it was never wired in V1, may revisit in #29 after V2 lands.
- Add "Migration from V1" section pointing at `memctl migrate-vault`.

### Step 4 — Sync skill to plugin

```bash
bash scripts/sync-skill-to-plugin.sh
diff docs/memctl.md plugins/memctl-claude/skills/memctl/SKILL.md   # expect 0
```

### Step 5 — Version bump 1.2.0 → 1.3.0 (atomic)

- **File MODIFY:** `src/memctl/memctl.csproj` — `<Version>1.3.0</Version>`
- **File MODIFY:** `plugins/memctl-claude/.claude-plugin/plugin.json` — `"version": "1.3.0"`
- Both atomic in single commit; workflow `verify-versions` (from #28) enforces match on next tag push.

### Step 6 — Public memctl-releases sync (manual API until tag triggers auto-sync)

Two files to push:

```bash
PAT="<RELEASE_REPO_PAT>"

# 1. Top-level SKILL.md (release archive embed — workflow copies this into each binary archive)
sync_file() {
  local local_path="$1" remote_path="$2" repo="$3" msg="$4"
  local sha=$(curl -sS -H "Authorization: token $PAT" \
    "https://api.github.com/repos/$repo/contents/$remote_path" \
    | python -c "import sys,json; d=json.load(sys.stdin); print(d.get('sha',''))")
  python -c "
import json, base64
content = open('$local_path', 'rb').read()
payload = {'message': '$msg', 'content': base64.b64encode(content).decode()}
$([ -n "$sha" ] && echo "payload['sha'] = '$sha'")
open('./payload.json','w').write(json.dumps(payload))
"
  curl -sS -X PUT -H "Authorization: token $PAT" -H "Accept: application/vnd.github+json" \
    -H "Content-Type: application/json" --data-binary @./payload.json \
    "https://api.github.com/repos/$repo/contents/$remote_path" \
    -o /dev/null -w "$remote_path=%{http_code}\n"
  rm -f payload.json
}

# Top-level SKILL.md (mirror of docs/memctl.md)
sync_file docs/memctl.md SKILL.md vdg-solutions/memctl-releases "docs: sync skill V2 layout examples"

# Plugin README + skill inside plugin source
sync_file plugins/memctl-claude/README.md plugins/memctl-claude/README.md vdg-solutions/memctl-releases "docs(plugin): V2 layout examples"
sync_file plugins/memctl-claude/skills/memctl/SKILL.md plugins/memctl-claude/skills/memctl/SKILL.md vdg-solutions/memctl-releases "docs(plugin): sync skill V2"
```

Idempotent — re-running on already-synced content is no-op (sha matches, GitHub returns 200 unchanged).

### Step 7 — Verify workflow sync ordering

Read `.github/workflows/release.yml` and confirm:

1. `release` job has `Sync plugin source to release repo` step (added in commit `43881e3` per #25 follow-up).
2. `sync-marketplace` job has `needs: release` (depends on full release job completion).
3. Therefore: marketplace.json bump CANNOT execute until plugin source push inside release job has completed.

Mechanical check:
```bash
grep -A1 "sync-marketplace:" .github/workflows/release.yml | grep -q "needs: release" && echo "ordering OK"
grep -B1 "name: Sync plugin source" .github/workflows/release.yml | grep -q "release:" && echo "sync-step inside release job OK"
```

If either check fails, file is misordered — fix workflow before tagging v1.3.0.

### Step 8 — Build + test verify

```bash
dotnet build src/memctl/memctl.csproj -c Release --nologo -v q       # 0 warning, 0 error
dotnet test tests/memctl.Tests/memctl.Tests.csproj --nologo          # 57 from #31+#32 (no new tests in #33)
```

### Step 9 — Smoke (post-merge, pre-tag)

```bash
# Verify version bump consistent
grep -oE '<Version>[^<]+' src/memctl/memctl.csproj
python -c "import json; print(json.load(open('plugins/memctl-claude/.claude-plugin/plugin.json'))['version'])"
# Both should print 1.3.0

# Verify skill sync
diff docs/memctl.md plugins/memctl-claude/skills/memctl/SKILL.md
# Expect: empty diff

# Verify public sync
curl -sS https://raw.githubusercontent.com/vdg-solutions/memctl-releases/master/SKILL.md | grep -c "\.memctl/"
# Expect: ≥3 (V2 layout mentions)
```

### Step 10 — User actions (post-merge)

- [USER-ACTION-REQUIRED] After this child merges, anh push tag `v1.3.0`:
  ```bash
  git tag -a v1.3.0 -m "v1.3.0 — vault layout V2"
  git push origin v1.3.0
  ```
- Workflow runs ~10 min: build → release → sync plugin source → bump marketplace.json.
- Verify: https://github.com/vdg-solutions/memctl-releases/releases/tag/v1.3.0 has 4 assets.
- Verify: `gh api repos/vdg-solutions/claude-plugins/contents/.claude-plugin/marketplace.json --jq '.content' | base64 -d | python -c "import sys,json; d=json.load(sys.stdin); print(d['plugins'][0]['version'])"` returns `1.3.0`.

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| FR-1 | docs/memctl.md contains V2 layout diagram + migrate-vault instructions | `grep -c "Vault layout (V2" docs/memctl.md` returns 1; `grep -c "migrate-vault" docs/memctl.md` returns ≥2 |
| FR-2 | docs/memctl.md has 0 references to legacy `~/my-vault` or `.memctl-vault/` workaround patterns | `grep -cE "memctl init ~|\.memctl-vault" docs/memctl.md` returns 0 |
| FR-3 | plugins/memctl-claude/README.md uses V2 init examples + drops `.memctl-vault/` | `grep -c "memctl init --vault \." plugins/memctl-claude/README.md` returns ≥1; `grep -c "\.memctl-vault" plugins/memctl-claude/README.md` returns 0 |
| FR-4 | Skill synced — diff docs/memctl.md and plugin SKILL.md returns empty | `diff -q docs/memctl.md plugins/memctl-claude/skills/memctl/SKILL.md` exit 0 |
| FR-5 | csproj `<Version>` === plugin.json `version` === 1.3.0 | `grep -oE '<Version>1\.3\.0</Version>' src/memctl/memctl.csproj`; `grep -E '"version": "1\.3\.0"' plugins/memctl-claude/.claude-plugin/plugin.json` |
| FR-6 | Public memctl-releases SKILL.md (top-level) reflects V2 examples | `curl -sS https://raw.githubusercontent.com/vdg-solutions/memctl-releases/master/SKILL.md \| grep -c "\.memctl/"` returns ≥3 |
| FR-7 | Public memctl-releases plugin source README synced | `curl -sS https://raw.githubusercontent.com/vdg-solutions/memctl-releases/master/plugins/memctl-claude/README.md \| grep -c "memctl init --vault \."` returns ≥1 |
| FR-8 | Workflow ordering: sync-marketplace `needs: release` AND release job contains "Sync plugin source" step | mechanical grep checks in Step 7 both pass |
| FR-9 | docs/vault-isolation-runbook.md "Three-tier setup" updated to V2 | `grep -c "Vault layout (V2" docs/vault-isolation-runbook.md` returns ≥1 |
| NFR-1 | Build clean: 0 warning, 0 error | `dotnet build -c Release --nologo -v q 2>&1 \| grep -cE "warning\|error"` returns 0 |
| NFR-2 | All 57 tests still pass (no regression from #31+#32) | `dotnet test --nologo` "Passed: 57" |
| NFR-3 | No new test files added in this child (docs+version only) | `git diff main..HEAD --name-only \| grep -c "tests/.*Tests.cs"` returns 0 |
| NFR-4 | Version bump atomic — both files in same commit | `git log --name-only --pretty=format:"%H %s" -10 \| grep -A2 "release: v1.3.0\|chore: bump" \| grep -E "csproj\|plugin.json" \| wc -l` returns ≥2 in same commit |

## Out of Scope

- Pushing tag `v1.3.0` itself — anh does manually after merge per /sdlc rule "user pushes tags".
- Marketplace.json version bump — handled by workflow `sync-marketplace` job (#28) auto on tag push.
- Re-issuing PAT — assumed valid (memctl-release-pipeline) for full sync run.
- Adding new features beyond V2 layout completion. Scope locked to docs + version + sync.

## Dependencies

- **Blocked by #31 (V2 foundation)** — docs reference V2 features that must exist in code first.
- **Blocked by #32 (migrate-vault command)** — docs document migrate-vault; command must exist.
- **Blocked by #28 (workflow lockstep enforcement)** — version bump requires `verify-versions` job in workflow.

## Risk

| Risk | Mitigation |
|------|-----------|
| Manual public memctl-releases sync misses a file → users get partial V2 experience | Step 6 syncs 3 files explicitly (top SKILL.md, plugin README, plugin SKILL.md). Verify FR-6 + FR-7 confirm post-sync. Workflow auto-sync (release job) catches anything missed on next tag. |
| Workflow `sync-marketplace` race condition (em flagged earlier) | Step 7 verifies `needs: release` ordering — sync-marketplace cannot start until release job (including plugin source push) completes. Mechanical grep confirms before tag. |
| Version bump committed without skill+plugin docs sync → CI runs `verify-versions` for tag and passes, but plugin source on memctl-releases is stale | Step 5 + Step 4 + Step 6 all in single PR — atomic merge. PR checklist verifies all 3 done before merge. |
| User on v1.2.x with V1 vault upgrades to v1.3.0 → loud warning fires (from #31) but user doesn't read warnings | Doc CHANGELOG entry for v1.3.0 leads with "BREAKING: vault layout changed. Run `memctl migrate-vault` to upgrade." Plugin README adds upgrade-from-1.2 section. README on memctl-releases also flagged. |
| Skill sync script creates trailing whitespace / line-ending diffs (Windows CRLF vs LF) | Em verifies skill sync via `diff -q` — exact byte match. Use `cp` (binary copy) not text rewrite. Existing `scripts/sync-skill-to-plugin.sh` already does `cp`. |
| Public sync API call fails partway (3 files, 1 fails) → inconsistent state | Step 6 retries each file independently; failures logged via `%{http_code}`. User reruns just the failed file. |

## Effort

~3h:
- 1h: rewrite docs/memctl.md V2 examples + migrate-vault section
- 0.5h: rewrite plugin README + drop legacy patterns
- 0.5h: update vault-isolation-runbook.md three-tier section
- 0.25h: skill sync script + diff verify
- 0.25h: version bump csproj + plugin.json
- 0.25h: workflow ordering verification (mechanical grep)
- 0.25h: public memctl-releases sync (3 files via API)

## User Actions Required

- [USER-ACTION-REQUIRED] After this child merges to main, anh push tag `v1.3.0` (em không tự push tag per /sdlc rule). Workflow auto-completes release.

## Notes

- This child is "docs+version" — no behavior changes (all behavior in #31 + #32). Rewrites are mechanical.
- Skill text appears in multiple places: `docs/memctl.md` (source) → `plugins/memctl-claude/skills/memctl/SKILL.md` (synced via script) → `vdg-solutions/memctl-releases/SKILL.md` (synced via API or workflow). All 3 must agree post-this-child.
- Workflow `sync-marketplace` (#28) on next tag push auto-bumps marketplace.json plugin version to 1.3.0 — no manual marketplace edit needed.
- After v1.3.0 ships and is verified, em can revisit #29 (MEMCTL_SHARED_VAULT env var) — V2 makes per-project natural; env var fallback may still be useful for personal global vault. Anh decides.
