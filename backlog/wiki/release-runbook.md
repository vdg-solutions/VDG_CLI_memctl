# Release Runbook

End-to-end SOP for shipping memctl releases. Future bots: read this before touching tags or workflows.

---

## Architecture

| Repo | Visibility | Role |
|------|-----------|------|
| `vdg-solutions/VDG_CLI_memctl` | private | source code, workflow, backlog |
| `vdg-solutions/memctl-releases` | public | binary release host (GitHub Releases) + install scripts |
| `vdg-solutions/claude-plugins` | public | Claude Code marketplace manifest |

Push tag `v*` to private repo → workflow `.github/workflows/release.yml` runs → builds 3 native AOT binaries (win-x64, linux-x64, osx-arm64) → uploads to `memctl-releases/releases/<tag>` via `RELEASE_REPO_PAT` secret. No nupkg — AOT binaries only.

---

## Production release — manual steps

### 1. Bump versions

Both files MUST stay in sync (NFR-3 task #27):

```bash
NEW_VER="1.3.1"

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

### 5. Marketplace sync (automated)

Handled by workflow `sync-marketplace` job — updates `vdg-solutions/claude-plugins/.claude-plugin/marketplace.json` plugin entry version. Skipped on pre-release tags (`v*-rc1`/`-beta1`/`-alpha`). No manual action needed.

### 6. Monitor workflow

```bash
gh run watch --repo vdg-solutions/VDG_CLI_memctl
gh release view "v$NEW_VER" --repo vdg-solutions/memctl-releases
```

Expected: 3 assets within 10-15 min.

| Asset | Size | Platform |
|-------|------|----------|
| `memctl-win-x64-<ver>.zip` | ~5 MB | Windows (binary + 3 DLLs) |
| `memctl-linux-x64-<ver>.tar.gz` | ~5.5 MB | Linux (binary + .so libs) |
| `memctl-osx-arm64-<ver>.tar.gz` | ~5 MB | macOS Apple Silicon (binary + .dylib) |

Each archive contains: `memctl`/`memctl.exe` + native libs (`onnxruntime`, `e_sqlite3`, `onnxruntime_providers_shared`). No `.lib` linker files.

---

## Installing memctl (end-user one-liners)

### Linux / macOS

```bash
curl -fsSL https://raw.githubusercontent.com/vdg-solutions/memctl-releases/main/install.sh | bash
```

Optional: override install dir with `--dir`:

```bash
curl -fsSL https://raw.githubusercontent.com/vdg-solutions/memctl-releases/main/install.sh | bash -s -- --dir /usr/local/bin
```

### Windows (PowerShell)

```powershell
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/vdg-solutions/memctl-releases/main/Install.ps1" -OutFile "$env:TEMP\memctl-install.ps1"; & "$env:TEMP\memctl-install.ps1"
```

Optional: override install dir with `-Dir`:

```powershell
& "$env:TEMP\memctl-install.ps1" -Dir "C:\tools"
```

`irm | iex` not used — Windows PowerShell pipe semantics differ from bash; explicit download is safer.

---

## Pre-release (rc / beta / alpha)

Same as production but suffix tag:

```bash
git tag -a "v1.3.1-rc1" -m "rc1"
git push origin "v1.3.1-rc1"
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
verify-versions
    ↓
build (matrix 3-platform AOT)
    ↓
release (gh release create on memctl-releases + sync install scripts)
    ↓
sync-marketplace (skipped on pre-release tags)
```

### verify-versions (fail-fast)

Runs first, takes ~10s. Reads:
- `GITHUB_REF_NAME` → strip `v` prefix → strip pre-release suffix after `-` → `BASE_VER`
- `<Version>` from `src/memctl/memctl.csproj`
- `version` from `plugins/memctl-claude/.claude-plugin/plugin.json`

All three must equal `BASE_VER`. Mismatch → workflow fails immediately (no wasted matrix builds). Forces csproj/plugin lockstep release per task #27 NFR-3.

### sync-marketplace (post-release)

Runs after `release` job succeeds. Skipped via `if: !contains(github.ref_name, '-')` for pre-release tags. Updates plugin entry version in `vdg-solutions/claude-plugins/.claude-plugin/marketplace.json` via Contents API:
1. GET marketplace.json (get sha)
2. Decode base64, modify in-memory, re-encode
3. PUT with sha (optimistic concurrency) + commit message `chore: bump memctl plugin to <version>`

Failure isolated — does NOT roll back uploaded release artifacts. PAT scope or rate-limit issues only block marketplace update; users still get binaries.

### Sync plugin source to release repo (release job, last step)

After `gh release create` succeeds, the `release` job clones `vdg-solutions/memctl-releases` (using `RELEASE_REPO_PAT`), removes the existing `plugins/memctl-claude/` directory, copies the current source from this repo, and commits + pushes if there's a diff. Required because Claude Code marketplace clones plugin source from `memctl-releases` (public) — when the local `plugins/memctl-claude/` changes, the public copy must propagate, otherwise `claude plugin update memctl@vdg-solutions` fetches a stale plugin.

Commit message format: `chore: sync plugin source to <tag>`. Idempotent — no commit if `git diff --staged --quiet` returns true.

### Marketplace.json `source` format (Claude Code 2.1+)

```json
"source": {
  "source": "git-subdir",
  "url": "https://github.com/vdg-solutions/memctl-releases.git",
  "path": "plugins/memctl-claude",
  "ref": "master"
}
```

NOT the legacy string format `"source": "github:owner/repo"`. The source repo MUST be public — Claude Code does not authenticate when cloning marketplace sources. That's why plugin source lives at `vdg-solutions/memctl-releases/plugins/memctl-claude/` (public) and is auto-synced from the private source repo each release.

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

## Plugin publish flow (merged from plugin-publish.md)

### Two-repo split

```
PRIVATE source              PUBLIC release host          PUBLIC marketplace
vdg-solutions/              vdg-solutions/               vdg-solutions/
VDG_CLI_memctl              memctl-releases              claude-plugins

plugins/memctl-claude/  →   plugins/memctl-claude/  ←   marketplace.json
(authoritative source)      (Claude Code clones here)   (entry points memctl-releases)
```

Why:
- Source repo private — Claude Code can't clone for plugin install
- `memctl-releases` public — Claude Code clones plugin source from here on `claude plugin install memctl@vdg-solutions`
- `claude-plugins` public marketplace — `marketplace.json` declares plugin metadata + points `source.url` at memctl-releases

### Mid-release iteration (no tag, plugin-only changes)

```bash
PAT="<RELEASE_REPO_PAT>"
TMP=$(mktemp -d)
git clone "https://x-access-token:${PAT}@github.com/vdg-solutions/memctl-releases.git" "$TMP/rel"
rm -rf "$TMP/rel/plugins/memctl-claude"
cp -r plugins/memctl-claude "$TMP/rel/plugins/"
cd "$TMP/rel"
git add plugins/memctl-claude
git diff --staged --quiet || git commit -m "chore: sync plugin source (mid-release iteration)"
git push
rm -rf "$TMP"
```

Users must `claude plugin update memctl@vdg-solutions` afterward — Claude Code caches the clone.

### First-time bootstrap (new plugin family)

1. Create `plugins/<plugin-name>/` in private repo: `.claude-plugin/plugin.json`, optional `hooks/hooks.json`, `skills/`, `commands/`
2. Create or reuse public release host repo
3. Create public marketplace repo with `.claude-plugin/marketplace.json` declaring plugin entry, object source format
4. Copy plugin source private → public release host (one-time API PUT or git clone+cp+push)
5. Add marketplace: `claude plugin marketplace add <owner>/<marketplace-repo>`
6. Install: `claude plugin install <plugin>@<marketplace-name>`
7. Wire workflow auto-sync (see "Sync plugin source to release repo" step)

### Verify install end-to-end

```bash
claude plugin marketplace update vdg-solutions
claude plugin install memctl@vdg-solutions     # first time
claude plugin update memctl@vdg-solutions      # subsequent
claude plugin list | grep memctl
# Expect: memctl@vdg-solutions  Version: <x.y.z>  Status: ✔ enabled
```

Failure modes:
- "unsupported source type" → marketplace.json source is string format, must be object
- "could not clone" → source repo private OR `path` doesn't exist at `ref`

### Plugin task design checklist

Any backlog item building a Claude Code plugin MUST include:
1. Public source repo confirmation (no private-only)
2. `marketplace.json` snippet using object source format (NOT string)
3. Workflow step or runbook reference for source sync on release
4. Pointer to this runbook

If missing → design gap → fix task before /sdlc.

---

## Quick reference — common ops

| Goal | Command |
|------|---------|
| Ship production v1.3.1 | `bump versions → commit → tag v1.3.1 → push origin main v1.3.1` |
| Ship pre-release | `tag v1.3.1-rc1 → push` (no version bump in csproj/plugin needed) |
| Watch run | `gh run watch --repo vdg-solutions/VDG_CLI_memctl` |
| Inspect release | `gh release view v1.3.1 --repo vdg-solutions/memctl-releases` |
| Cancel stuck run | `gh run cancel <id> --repo vdg-solutions/VDG_CLI_memctl` |
| Re-run failed | retag (delete + recreate) — `workflow_dispatch` works too if manifest unchanged |
| List secrets | `gh secret list --repo vdg-solutions/VDG_CLI_memctl` |
