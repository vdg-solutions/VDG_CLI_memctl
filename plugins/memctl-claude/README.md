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

## Init a vault (V2.1 layout, v1.3.0+)

`memctl init` uses `--vault <path>` (NOT positional arg). PowerShell does NOT expand `~` — use `$HOME` or full path.

### Recommended: per-project vault (memory stays scoped to the project)

```powershell
# In each project root
cd C:\repos\my-project
memctl init --vault .                    # creates .\.memctl\ as vault root
Add-Content .gitignore ".memctl/"
```

```bash
# Linux / macOS
cd ~/repos/my-project
memctl init --vault .
echo ".memctl/" >> .gitignore
```

`memctl init --vault <path>` creates `<path>/.memctl/` as the vault root container with V2.1 layout: `.obsidian/` config + 7 semantic subdirs (tasks/, patterns/, lessons/, decisions/, chats/, attachments/, claude-memory/) + nested runtime `.obsidian/memctl/` (auto-hidden by Obsidian).

When you `cd` into the project, the plugin's hooks auto-detect the vault by walking up looking for `.memctl/` containing `.obsidian/`. Move to another project → that project's vault. **Filesystem-based isolation** — no env vars needed.

To open the vault in Obsidian app: open `<project>/.memctl/` as the vault folder.

### Optional: personal global vault (cross-project notes)

For life decisions, code patterns reusable across all projects:

```powershell
memctl init --vault $HOME\memctl-personal
# vault root = $HOME\memctl-personal\.memctl\
```

```bash
memctl init --vault $HOME/memctl-personal
# vault root = ~/memctl-personal/.memctl/
```

Use `--vault` flag explicitly per-command, or `cd` into the personal vault dir to use it.

### Vault auto-detect priority

```
1. --vault <path> CLI flag                                       (explicit)
2. Walk-up from cwd looking for .memctl/ containing .obsidian/   (per-project, V2.1)
3. MEMCTL_SHARED_VAULT env var pointing at vault root            (shared opt-in, v1.3.1+)
4. error "no vault found"
```

Hooks call memctl WITHOUT `--vault` — they always go through the resolver, so scope follows the directory you `claude` in.

**Shared vault opt-in (v1.3.1+):** set `MEMCTL_SHARED_VAULT=<path>` where `<path>/.obsidian/` exists. Used only when walk-up exhausts — per-project vault always wins. Lets you have a personal global fallback without forcing every cwd to host one.

```bash
export MEMCTL_SHARED_VAULT=$HOME/memctl-personal/.memctl    # Linux/macOS
$env:MEMCTL_SHARED_VAULT="$HOME\memctl-personal\.memctl"   # Windows PowerShell
```

### Privacy guidance

- **Sensitive project**: per-project vault + `.gitignore`. Filesystem isolation.
- **Audit before launch**: `memctl status` → inspect `index_path` in JSON output. Confirms which vault is active.
- **One-off without leaving traces**: `$env:MEMCTL_DISABLE_AUTOCAPTURE=1; $env:MEMCTL_DISABLE_AUTOINJECT=1` then launch claude.

Full V2.1 layout doc: see plugin SKILL.md or source repo `docs/memctl.md`.

## Upgrading from v1.2.x (V1 vault layout)

**Hard cutover — no automatic migration.** V2.1 (v1.3.0+) requires fresh init. Manual upgrade per existing V1 vault:

```powershell
# Backup V1 vault
mkdir <project>\.archived-v1-vault
Move-Item <project>\.memctl   <project>\.archived-v1-vault\.memctl
Move-Item <project>\.obsidian <project>\.archived-v1-vault\.obsidian

# Init fresh V2 vault
memctl init --vault <project>

# Optional: copy notes from old root .md files
Copy-Item <project>\.archived-v1-vault\*.md  <project>\.memctl\

# Rebuild index
memctl ingest --vault <project>\.memctl
```

Add `.archived-v1-vault/` to `.gitignore`. V1 vault preserved for manual recovery.

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

**Vault not detected:** plugin walks up from cwd looking for `.memctl/` containing `.obsidian/` (V2.1 marker pair). `cd` into a project that has been `memctl init --vault .`'d.

**Wrong vault active (cross-project leak risk):** run `memctl status` and check `index_path` in the JSON output — confirms which `.memctl/` is active. Project-level `.memctl/` always wins via walk-up.

**Capture is slow:** `memctl capture` filters short turns + skips tool-call-only turns. If still slow, check `~/.claude/logs/` for hook output.

## License

MIT
