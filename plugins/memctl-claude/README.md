# memctl — Claude Code plugin

Zero-config persistent memory for Claude Code. Auto-captures conversations, auto-injects context, no LLM-side configuration.

## What it does

- **SessionStart hook** → checks vault status (silent if missing)
- **UserPromptSubmit hook** → injects relevant memory as `## Memory Context` block before each prompt
- **Stop hook** → captures the conversation turn into the vault after each response
- **Skill** → `memctl` skill describing the protocol for explicit invocation
- **Slash commands** → `/memctl-recall`, `/memctl-save`, `/memctl-boost`, `/memctl-lint`

The LLM doesn't have to know memctl exists — context shows up automatically in every prompt.

## Prerequisites

Install the `memctl` binary first (one-time, not bundled with the plugin):

```bash
# Linux / macOS
curl -fsSL https://raw.githubusercontent.com/vdg-solutions/memctl-releases/master/install.sh | sh

# Windows PowerShell
iwr -useb https://raw.githubusercontent.com/vdg-solutions/memctl-releases/master/install.ps1 | iex
```

Or via dotnet:

```bash
dotnet tool install -g memctl
```

Verify: `memctl --version`

## Install (Claude Code marketplace)

```bash
claude plugin marketplace add vdg-solutions/claude-plugins
claude plugin install memctl@vdg-solutions
```

Restart Claude Code → hooks active in every session.

## Init a vault

`memctl init` uses `--vault <path>` (NOT positional arg). PowerShell does NOT expand `~` — use `$HOME` or full path.

### Recommended: per-project vault (memory stays scoped to the project)

```powershell
# In each project root
cd C:\repos\my-project
memctl init --vault .memctl-vault
Add-Content .gitignore ".memctl-vault/"
```

```bash
# Linux / macOS
cd ~/repos/my-project
memctl init --vault .memctl-vault
echo ".memctl-vault/" >> .gitignore
```

When you `cd` into the project, the plugin's hooks auto-detect this vault. Move to another project → that project's vault. **Filesystem-based isolation** — no env vars needed.

### Optional: personal global vault (cross-project notes)

For life decisions, code patterns reusable across all projects:

```powershell
memctl init --vault $HOME\memctl-personal
[Environment]::SetEnvironmentVariable('MEMCTL_VAULT', "$HOME\memctl-personal", 'User')
```

```bash
memctl init --vault $HOME/memctl-personal
echo 'export MEMCTL_VAULT="$HOME/memctl-personal"' >> ~/.bashrc
```

Per-project vaults (when present) **always override** the global env var — sensitive projects stay isolated even with a global vault configured.

### Vault auto-detect priority

```
1. --vault <path> CLI flag         (explicit)
2. MEMCTL_VAULT env var            (global fallback)
3. .memctl/ folder at cwd or any parent dir   (per-project)
4. error "no vault found"
```

Hooks call memctl WITHOUT `--vault` — they always go through the resolver, so scope follows the directory you `claude` in.

### Privacy guidance

- **Sensitive project**: per-project vault + `.gitignore`. Don't set `MEMCTL_VAULT` if you don't want a global fallback.
- **Audit before launch**: `memctl status` → inspect `index_path` in JSON output. Confirms which vault is active.
- **One-off without leaving traces**: `$env:MEMCTL_DISABLE_AUTOCAPTURE=1; $env:MEMCTL_DISABLE_AUTOINJECT=1` then launch claude.

Full doc: [vault-isolation-runbook.md](https://github.com/vdg-solutions/memctl-releases/blob/master/SKILL.md) (or in source repo `docs/vault-isolation-runbook.md`).

## Disable individual hooks

Set env vars (graceful degrade — hooks exit 0 silently):

```bash
export MEMCTL_DISABLE_AUTOCAPTURE=1   # disable Stop hook
export MEMCTL_DISABLE_AUTOINJECT=1    # disable UserPromptSubmit hook
export MEMCTL_ALLOW_DEBUG=1           # allow debugger attach (memctl --version still works)
```

## Slash commands

| Command | Purpose |
|---------|---------|
| `/memctl-recall [keywords]` | List top notes + optional keyword search |
| `/memctl-save <title> \| <content>` | Save a decision/finding/insight |
| `/memctl-boost <id> [weight]` | Boost importance (default weight 1.5) |
| `/memctl-lint [semantic]` | Run structural lint; add `semantic` for deep lint |

## Troubleshooting

**Hooks not firing:** restart Claude Code after install. Verify `memctl` is on `PATH` (`which memctl`).

**Vault not detected:** plugin walks up from cwd looking for `.memctl/` or `.memctl-vault/`. `cd` into a project that has been `memctl init --vault .memctl-vault`'d, or set `MEMCTL_VAULT` env var as a global fallback.

**Wrong vault active (cross-project leak risk):** run `memctl status` and check `index_path` in the JSON output. If it points at the global vault while you're working on a sensitive project, run `memctl init --vault .memctl-vault` in the project root first. See vault-isolation-runbook.md.

**Capture is slow:** `memctl capture` filters short turns + skips tool-call-only turns. If still slow, check `~/.claude/logs/` for hook output.

## License

MIT
