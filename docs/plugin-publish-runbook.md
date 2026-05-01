# Plugin Publish Runbook

How to push the `memctl` Claude Code plugin from this private source repo to the public marketplace where Claude Code can install it.

Cross-references: backlog/27 (plugin scaffold), backlog/28 (release pipeline automation), docs/release-runbook.md (full release SOP).

---

## Two-repo flow

```
PRIVATE source              PUBLIC release host          PUBLIC marketplace
vdg-solutions/              vdg-solutions/               vdg-solutions/
VDG_CLI_memctl              memctl-releases              claude-plugins
                                                         
plugins/memctl-claude/  →   plugins/memctl-claude/  ←   marketplace.json
(authoritative source)      (Claude Code clones here)   (entry points at memctl-releases)
```

Why split:
- Source repo is **private** — Claude Code can't clone for plugin install.
- `memctl-releases` is **public** — Claude Code clones plugin source from here when user runs `claude plugin install memctl@vdg-solutions`.
- `claude-plugins` is **public marketplace** — `marketplace.json` declares plugin metadata + points `source.url` at `memctl-releases`.

---

## marketplace.json `source` format (Claude Code 2.1+)

Required object format:

```json
"source": {
  "source": "git-subdir",
  "url": "https://github.com/vdg-solutions/memctl-releases.git",
  "path": "plugins/memctl-claude",
  "ref": "master"
}
```

Legacy `"source": "github:owner/repo"` rejected with:

```
✘ This plugin uses a source type your Claude Code version does not support.
```

Source repo MUST be public — Claude Code does not authenticate during clone.

---

## Push protocols

### Tag-driven (production release)

Automated by `.github/workflows/release.yml`:

1. Bump `<Version>` in `src/memctl/memctl.csproj` AND `version` in `plugins/memctl-claude/.claude-plugin/plugin.json` (both must match — `verify-versions` job enforces).
2. Commit, tag `v<version>`, push to private repo.
3. Workflow runs:
   - `verify-versions` — block on csproj/plugin/tag mismatch
   - `build` — 3-platform AOT
   - `pack-tool` — nupkg
   - `release` — upload artifacts to `memctl-releases/releases/v<version>` AND **clone memctl-releases + sync `plugins/memctl-claude/` + commit + push** (last step in release job)
   - `sync-marketplace` — bump `marketplace.json` plugin entry version (skipped on pre-release tags)

After workflow completes, the public chain is consistent:
- `memctl-releases/master/plugins/memctl-claude/` matches private source
- `claude-plugins/master/.claude-plugin/marketplace.json` `version` field matches tag

### Mid-release iteration (no tag)

If you're iterating on plugin files (hooks, commands, README) WITHOUT a version bump:

```bash
# Manual sync — plugin files only, no version change
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

### First-time bootstrap (already done for memctl)

Documented for future plugins:

1. Create source dir under `plugins/<plugin-name>/` in private repo with: `.claude-plugin/plugin.json`, optional `hooks/hooks.json`, `skills/`, `commands/`.
2. Create public release host repo (or reuse if same plugin family).
3. Create public marketplace repo with `.claude-plugin/marketplace.json` declaring the plugin entry. Use the object source format.
4. Copy plugin source from private → public release host (one-time API PUT or manual `git clone + cp + push`).
5. Add marketplace to Claude Code: `claude plugin marketplace add <owner>/<marketplace-repo>`.
6. Install: `claude plugin install <plugin>@<marketplace-name>`.
7. Wire workflow to auto-sync future tags (see `.github/workflows/release.yml` "Sync plugin source to release repo" step).

---

## When the bot reads backlog #27 (or any plugin task)

Any backlog item building a Claude Code plugin MUST include:

1. Public source repo confirmation (don't put plugin in a private-only repo without a public mirror).
2. `marketplace.json` snippet using object source format (NOT string).
3. Workflow step or runbook reference for source sync on release.
4. Pointer to this runbook (`docs/plugin-publish-runbook.md`).

If a plugin task is missing any of these, treat as design gap — fix the task before /sdlc.

---

## Verify install end-to-end

After any push (manual or workflow):

```bash
# Refresh marketplace cache
claude plugin marketplace update vdg-solutions

# Install or update
claude plugin install memctl@vdg-solutions          # first time
claude plugin update memctl@vdg-solutions           # subsequent

# Verify
claude plugin list | grep memctl
# Expect: memctl@vdg-solutions  Version: <x.y.z>  Status: ✔ enabled
```

If install fails with "unsupported source type": check `marketplace.json` source is object format.
If install fails with "could not clone": check source repo is public AND `path` exists at `ref`.

---

## TL;DR

- Private source repo for development.
- Public release host repo for plugin source + binary releases. **Plugin clones from here.**
- Public marketplace repo for `marketplace.json` discovery.
- Workflow auto-syncs plugin source on every tag push (release.yml release job last step).
- Mid-release iteration: manual `git clone + cp + push` to release host repo.
- `marketplace.json` `source` MUST be object `{source, url, path, ref}`, NOT string.
