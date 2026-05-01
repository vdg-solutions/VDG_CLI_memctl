# Vault Isolation Runbook

How to set up `memctl` vaults so memory does NOT leak across projects.

---

## Threat model

User installs the Claude Code plugin → `Stop` hook auto-captures every conversation → `UserPromptSubmit` hook auto-injects context. If all projects share one vault, a sensitive decision recorded while working on `client-A-payments` will surface as `## Memory Context` while working on `client-B-marketing`. Leak through automation, no human in the loop.

Goal: by default, memory recorded while working on project X stays scoped to project X.

---

## Vault auto-detect priority (verified)

When any `memctl` command runs without explicit `--vault`, the resolver walks this list, top-down, and uses the first hit:

| # | Source | Wins when |
|---|--------|-----------|
| 1 | `--vault <path>` CLI flag | Always (explicit override) |
| 2 | `MEMCTL_VAULT` env var | No CLI flag |
| 3 | `.memctl/` folder at cwd or any parent dir | No CLI flag, no env var |
| 4 | Error "no vault found" | Nothing matched |

**Filesystem-based isolation**: a project with its own `.memctl/` always wins over `MEMCTL_VAULT` when you `cd` into that project. No code change needed.

---

## Three-tier setup

### Tier 1 — Personal global vault (optional)

For life decisions, reusable code patterns, cross-project preferences. Anything you'd happily share across all your work.

```powershell
memctl init --vault $HOME\memctl-personal
[Environment]::SetEnvironmentVariable('MEMCTL_VAULT', "$HOME\memctl-personal", 'User')
```

Effect: any directory without a project vault falls back to the personal vault. Restart shell to pick up env var.

**Skip Tier 1 entirely if you don't want any global vault.** Just don't set `MEMCTL_VAULT`.

### Tier 2 — Per-project vault (recommended for sensitive projects)

```powershell
cd C:\repos\sensitive-project
memctl init --vault .memctl-vault
Add-Content .gitignore ".memctl-vault/"
```

Now `cd C:\repos\sensitive-project` puts you in this project's vault automatically. Outside that directory, you fall through to Tier 1 (or get "no vault" if Tier 1 not set).

`.memctl-vault/` in `.gitignore` keeps the vault local — won't leak via `git push`.

### Tier 3 — Team-shared vault (advanced)

Only when the entire team agrees to share memory AND there's no PII / secrets / customer data risk:

```powershell
cd C:\repos\team-project
memctl init --vault .memctl
# DO NOT add to .gitignore — vault is committed
git add .memctl
git commit -m "init: shared team memory vault"
```

Risk: every commit to vault is a code-review checkpoint. Team must enforce "no secrets in notes" discipline.

---

## Anti-leak checklist

Before launching `claude` in a directory:

1. **`memctl status`** — confirm which vault is active. Look at `index_path` in JSON output:
   ```json
   "index_path": ".memctl-vault\\index.db"   // ← project vault, scoped ✓
   "index_path": "C:\\Users\\you\\memctl-personal\\.memctl\\index.db"   // ← global, watch out
   ```
2. **If working on sensitive code** and `memctl status` shows global vault → STOP. Init project vault first:
   ```powershell
   memctl init --vault .memctl-vault
   ```
3. **`memctl list --limit 5`** — peek at top notes. If you see content from a different project, you're in the wrong vault.
4. **Disable hooks for one-off sensitive sessions** without changing setup:
   ```powershell
   $env:MEMCTL_DISABLE_AUTOCAPTURE=1
   $env:MEMCTL_DISABLE_AUTOINJECT=1
   claude
   ```

---

## Migration: I already have a global vault with mixed content

```powershell
# 1. Inventory what's in the global vault
memctl list --vault $HOME\memctl-personal --limit 100

# 2. For project-specific notes, move them to project vault
#    - Copy the .md note file from global vault to project vault notes/ dir
#    - Run memctl ingest --vault <project-vault> to re-index
#    - Delete from global vault

# 3. Re-index both vaults to sync
memctl ingest --vault $HOME\memctl-personal
memctl ingest --vault C:\repos\project\.memctl-vault
```

No CLI helper for migration — Obsidian-compatible markdown means manual `mv` is fine.

---

## Audit periodically

```powershell
# What vaults exist on this machine?
Get-ChildItem -Path C:\repos -Recurse -Force -Filter .memctl-vault -Directory -ErrorAction SilentlyContinue
Get-ChildItem -Path $HOME -Filter "memctl-*" -Directory

# What's in each?
memctl list --vault <each-path> --limit 10

# Prune notes that drifted to wrong vault
# (manual review — Obsidian also works for browsing)
```

---

## Plugin behavior with isolation

The Claude Code plugin's hooks (`SessionStart`, `UserPromptSubmit`, `Stop`) call `memctl status / context-inject / capture` **without** passing `--vault`. They rely on the auto-detect priority above.

So:
- `cd project-A && claude` → hooks resolve to project-A's `.memctl-vault/` (or fallback `MEMCTL_VAULT`)
- `cd project-B && claude` → hooks resolve to project-B's vault
- No plugin reconfiguration needed — isolation is handled by the resolver, not the hooks

The plugin README documents this. Future plugin changes must NOT inject `--vault` into hook commands without a strong reason — that would break isolation.

---

## TL;DR

- **Default safe setup**: don't set `MEMCTL_VAULT`. Init `.memctl-vault/` per project. Add to `.gitignore`. Each `cd` switches scope automatically.
- **Optional global**: set `MEMCTL_VAULT` only for personal/cross-project notes. Per-project vaults still override.
- **Audit before `claude`**: `memctl status` → check `index_path`. If wrong scope, init project vault.
- **Plugin hooks respect isolation** — they call memctl without `--vault`, the resolver handles scope.
