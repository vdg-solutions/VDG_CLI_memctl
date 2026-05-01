# Release Runbook

End-to-end SOP for shipping memctl releases. Future bots: read this before touching tags or workflows.

---

## Architecture

| Repo | Visibility | Role |
|------|-----------|------|
| `vdg-solutions/VDG_CLI_memctl` | private | source code, workflow, backlog |
| `vdg-solutions/memctl-releases` | public | binary release host (GitHub Releases) + install scripts |
| `vdg-solutions/claude-plugins` | public | Claude Code marketplace manifest |

Push tag `v*` to private repo → workflow `.github/workflows/release.yml` runs → builds 3 native AOT binaries (win-x64, linux-x64, osx-arm64) + nupkg → uploads to `memctl-releases/releases/<tag>` via `RELEASE_REPO_PAT` secret.

---

## Production release — manual steps (current state)

### 1. Bump versions

Both files MUST stay in sync (NFR-3 task #27):

```bash
NEW_VER="1.2.1"

sed -i "s|<Version>[^<]*</Version>|<Version>$NEW_VER</Version>|" src/memctl/memctl.csproj
sed -i "s|\"version\": \"[^\"]*\"|\"version\": \"$NEW_VER\"|" plugins/memctl-claude/.claude-plugin/plugin.json
```

### 2. Sync skill (if `docs/memctl.md` changed since last release)

```bash
bash scripts/sync-skill-to-plugin.sh
```

### 3. Verify build clean locally

```bash
dotnet build src/memctl/memctl.csproj -c Release --nologo -v q
dotnet test --nologo
```

### 4. Commit, tag, push

```bash
git add src/memctl/memctl.csproj plugins/memctl-claude/.claude-plugin/plugin.json plugins/memctl-claude/skills/memctl/SKILL.md
git commit -m "release: v$NEW_VER"
git tag -a "v$NEW_VER" -m "v$NEW_VER"
git push origin main "v$NEW_VER"
```

### 5. Update marketplace.json (manual, until task #28 ships)

```bash
PAT="<RELEASE_REPO_PAT>"
SHA=$(curl -sS -H "Authorization: token $PAT" \
  https://api.github.com/repos/vdg-solutions/claude-plugins/contents/.claude-plugin/marketplace.json \
  | python -c "import sys,json; print(json.load(sys.stdin)['sha'])")

# Edit marketplace.json plugin entry version=$NEW_VER, then PUT back with sha
```

After task #28 ships this is automated — workflow handles it.

### 6. Monitor workflow

```bash
gh run watch --repo vdg-solutions/VDG_CLI_memctl
gh release view "v$NEW_VER" --repo vdg-solutions/memctl-releases
```

Expected: 4 assets within 10-15 min.

| Asset | Size | Platform |
|-------|------|----------|
| `memctl-win-x64-<ver>.zip` | ~5 MB | Windows |
| `memctl-linux-x64-<ver>.tar.gz` | ~5.5 MB | Linux |
| `memctl-osx-arm64-<ver>.tar.gz` | ~5 MB | macOS Apple Silicon |
| `memctl.<ver>.nupkg` | ~140 MB | dotnet tool (multi-rid) |

---

## Pre-release (rc / beta / alpha)

Same as production but suffix tag:

```bash
git tag -a "v1.2.1-rc1" -m "rc1"
git push origin "v1.2.1-rc1"
```

GitHub Releases auto-flags as pre-release based on tag suffix. Workflow still runs full pipeline. Marketplace sync (task #28) skips pre-release tags.

---

## Failure recovery

### Workflow fails (build error / runner timeout)

```bash
# Inspect logs
gh run view <run-id> --log-failed --repo vdg-solutions/VDG_CLI_memctl

# Fix code, retag (delete + recreate)
git tag -d "v$NEW_VER"
git push origin --delete "v$NEW_VER"
# fix, commit, then:
git tag -a "v$NEW_VER" -m "v$NEW_VER (retry)"
git push origin "v$NEW_VER"
```

### macos-13 runner queue stuck (pre-#25 historic note)

`osx-x64` was dropped from matrix in `31beef3` (run 25175621126 stuck 35+ min). Apple Intel coverage deprioritized — `osx-arm64` covers modern macs. Add back via cross-compile if revived.

### Workflow secret expired

`RELEASE_REPO_PAT` lifetime: 7 days–1 year. Rotation steps:

1. Issue new fine-grained PAT at https://github.com/settings/personal-access-tokens/new
2. Repository access: select `vdg-solutions/memctl-releases` + `vdg-solutions/claude-plugins`
3. Permissions: `Contents: Read and write`, `Metadata: Read-only`
4. Update secret on private repo:
   ```bash
   gh secret set RELEASE_REPO_PAT --repo vdg-solutions/VDG_CLI_memctl --body "<new-token>"
   ```
5. Verify: `gh secret list --repo vdg-solutions/VDG_CLI_memctl | grep RELEASE_REPO_PAT`

---

## Workflow internals (future bot context)

`.github/workflows/release.yml` jobs:

```
verify-versions (after #28 ships)
    ↓
build (matrix 3-platform AOT)
pack-tool (dotnet pack nupkg)
    ↓
release (gh release create on memctl-releases)
    ↓
sync-marketplace (after #28; skipped on pre-release)
```

Trigger: `tags: ['v*']` or `workflow_dispatch` manual.

Third-party Actions pinned to commit SHA (NFR-1). When bumping action versions, find new SHA via:
```bash
gh api repos/actions/checkout/git/refs/tags/v4.2.2 --jq .object.sha
```

---

## Bot operating rules

- **Never auto-push tags without user approval.** User per memory rule grants auto-commit but reserves push.
- **Don't commit sensitive files.** `.claude/settings.local.json`, `.claude/scheduled_tasks.lock` ignored in `.gitignore`.
- **Don't bypass `verify-versions`.** Mismatched plugin/csproj versions break NFR-3 task #27.
- **Don't paste PAT into chat.** Use env var or secret reference.
- **Don't skip the skill sync** if `docs/memctl.md` changed — plugin distribution stale otherwise.
- **macOS-13 runner is gone.** Don't add it back without cross-team check.

---

## Quick reference — common ops

| Goal | Command |
|------|---------|
| Ship production v1.2.1 | `bump versions → commit → tag v1.2.1 → push origin main v1.2.1` |
| Ship pre-release | `tag v1.2.1-rc1 → push` (no version bump in csproj/plugin needed) |
| Watch run | `gh run watch --repo vdg-solutions/VDG_CLI_memctl` |
| Inspect release | `gh release view v1.2.1 --repo vdg-solutions/memctl-releases` |
| Cancel stuck run | `gh run cancel <id> --repo vdg-solutions/VDG_CLI_memctl` |
| Re-run failed | retag (delete + recreate) — `workflow_dispatch` works too if manifest unchanged |
| List secrets | `gh secret list --repo vdg-solutions/VDG_CLI_memctl` |
