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

```bash
memctl init ~/my-vault
cd ~/my-vault
```

The plugin auto-detects the vault from process cwd. Set `MEMCTL_VAULT` env var to pin a specific vault across directories.

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

**Vault not detected:** plugin uses cwd. `cd` into your vault dir before starting Claude Code, or set `MEMCTL_VAULT`.

**Capture is slow:** `memctl capture` filters short turns + skips tool-call-only turns. If still slow, check `~/.claude/logs/` for hook output.

## License

MIT
