---
name: memctl
description: Your persistent memory system across sessions. Use at session start to recall prior context, during work to capture decisions and findings, at session end to consolidate. Memory persists in an Obsidian-compatible vault — searchable, weighted by importance, and shared across all projects.
allowed-tools: Bash
---

# memctl — Bot Memory System

You have persistent memory. It survives across sessions, across projects, across context resets. Use it.

**The model:** You encode what matters → vault stores it → you recall it next session → periodic consolidation keeps it clean → old memories decay unless reinforced. Like human long-term memory.

**The vault:** Obsidian-compatible markdown files on disk. Human-readable, git-friendly, searchable by meaning (semantic) and keyword (BM25). Each note has an importance weight — higher weight = surfaces first in recall.

---

## Prerequisites — install memctl binary FIRST

The Claude Code plugin ships hooks + skill, but **NOT** the binary. Bot must verify binary present before any memctl command.

```
Bot first action this session: check binary
  ├── `memctl --version`
  │     ├── prints version (e.g. "1.3.1") → proceed to First-run setup
  │     ├── version mismatch with plugin (plugin v1.3.1 vs binary v1.2.x) → tell user: upgrade binary
  │     └── command-not-found → binary missing, tell user to install:
  │           Linux/macOS:
  │             curl -fsSL https://raw.githubusercontent.com/vdg-solutions/memctl-releases/master/install.sh | sh
  │           Windows PowerShell:
  │             iwr -useb https://raw.githubusercontent.com/vdg-solutions/memctl-releases/master/install.ps1 | iex
  │           Or via dotnet:
  │             dotnet tool install -g memctl
  │           After install: restart shell so PATH update takes effect.
```

Until binary present, all hook commands fail silently (they have `|| true` guards). Bot sees no `## Memory Context` injection, sees nothing wrong, but memory is offline. ALWAYS check `memctl --version` once per session before assuming memory works.

---

## First-run setup (bot decision tree)

After binary verified, run this decision tree once per session:

```
1. memctl status                                      # check vault state
   ├── success + vault_found=true  → vault exists, proceed normally
   ├── success + vault_found=false → no vault here yet, decide next step
   └── command not found            → re-check Prerequisites above

2. IF no vault yet, ask user (only once per project):
   "No memctl vault here. Init `<cwd>/.memctl/` for this project? [Y/n]"
   ├── Y → memctl init --vault .                      # creates ./.memctl/ + adds .gitignore
   ├── shared global vault → tell user: 'export MEMCTL_SHARED_VAULT=<path>'  (v1.3.1+)
   └── skip → continue without memory; do NOT prompt again this session

3. memctl model download                              # only if status reports model_ready=false (one-time, ~310 MB)

4. memctl ingest                                      # only if vault exists but vault_indexed=false
```

Hooks (SessionStart/UserPromptSubmit/Stop) fail silently when vault missing — they exit 0 and inject nothing. Bot is the one that detects and offers init.

After init completes, every subsequent prompt receives `## Memory Context` injection automatically. Bot reads that block as part of normal context, no explicit recall needed.

### Optional shared vault (v1.3.1+)

For cross-project personal notes (life decisions, code patterns) without per-project vaults everywhere:

```bash
# Init once
memctl init --vault $HOME/memctl-personal

# Set env var so cwd without `.memctl/` falls through to it
export MEMCTL_SHARED_VAULT=$HOME/memctl-personal/.memctl     # Linux/macOS
$env:MEMCTL_SHARED_VAULT="$HOME\memctl-personal\.memctl"   # PowerShell
```

Per-project `.memctl/` always wins over env var. Sensitive vaults never leak.

---

## Disable / uninstall

Disable individual hook (graceful degrade — hook exits 0 silent):

```bash
export MEMCTL_DISABLE_AUTOCAPTURE=1     # Stop hook off (no chat capture)
export MEMCTL_DISABLE_AUTOINJECT=1      # UserPromptSubmit hook off (no context inject)
# SessionStart has no flag — it's idempotent status check
```

One-off no-trace session: set both env vars before launching `claude`.

Uninstall plugin entirely:

```bash
claude plugin uninstall memctl@vdg-solutions
```

Uninstall binary:

```bash
# If installed via curl/iwr
rm ~/.local/bin/memctl                                    # Linux/macOS
Remove-Item "$env:LOCALAPPDATA\Programs\memctl" -Recurse  # Windows

# If installed via dotnet tool
dotnet tool uninstall -g memctl
```

Vault data persists at `<project>/.memctl/` after uninstall — delete manually if not wanted.

---

## Recovery — broken vault / model / version mismatch

```bash
# Model corrupted (download interrupted, file truncated):
memctl model download --force                            # re-fetch (~310 MB)

# Vault index corrupted (SQLite corruption, ABI break post-upgrade):
rm <vault>/.obsidian/memctl/index.db                     # delete index, NOT notes
memctl ingest --vault <vault>                            # rebuild

# Notes partially deleted but vault dir survives:
memctl ingest --vault <vault>                            # re-scan + re-index existing .md files

# Plugin v1.3.x + binary v1.2.x mismatch (skill expects new commands binary lacks):
# Upgrade binary to match plugin version (re-run installer one-liner)

# Vault gone entirely (catastrophic):
# Restore from git/backup if vault tracked, else memctl init fresh + accept memory loss

# Verify recovery:
memctl status     # all green: model_ready=true, vault_indexed=true, note_count > 0
```

Notes are markdown files on disk — you never lose them unless filesystem fails. Index is rebuildable from notes.

---

## Memory Protocol — What you actually do

> Full canonical protocol (architecture, wire DTOs, hook contract, integration points) lives in the source repo and ships internally. This skill is the bot-facing summary — sufficient for using memctl in Claude Code without further docs.

### Mental model

**You don't manually capture or recall.** Hooks do that. You consume context auto-injected into every prompt + invoke `memctl search` via Bash when surface coverage insufficient. Maintenance auto-triggered on every memctl invocation (pressure-checked, throttled 60s).

### 4 sub-systems (memory types)

| Type | Stores | Subdir | Lifecycle |
|------|--------|--------|-----------|
| **Episodic** | Chat turns, session logs | `chats/` (daily YYYY-MM-DD.md) | Stop hook auto-write; archive after 1 year |
| **Semantic** | Facts, decisions, patterns, lessons | `lessons/`, `decisions/`, `patterns/` | /design + /retro + /qc-dream write |
| **Procedural** | Active rules (golden, qc-rule, anti-pattern) | tag-routed | /qc-dream promote (hit_count ≥ 3) |
| **Identity** | Who anh is, stack, preferences | one identity note | Auto-update via Mode B distill from chats |

### 3 compute tiers (every memctl call self-routes)

| Tier | Budget | What runs |
|------|--------|-----------|
| **HOT** | <500ms | regex/math, pressure check, BM25+semantic, smart retrieval (5 signals) |
| **WARM** | <5s async | embedding compute, ingest, structural lint |
| **COLD** | opportunistic | bot-in-session distill via inbox (Mode B), full dream cycle |

### Encode (writes — mostly automatic)

```bash
# Auto: Stop hook captures conversation → chats/{date}.md (you do nothing)
# Auto: UserPromptSubmit hook injects ## Memory Context (you read it)

# Manual, by SDLC role (see § Commands "add" for full syntax):
memctl add "<text>" --tags "session,task-{id}"           # session state
memctl add "<text>" --tags "qc-feedback,task-{id}"        # retry feedback
memctl add "<text>" --tags "qc-error,project-{name}"      # mid-term error pattern
memctl add "<text>" --tags "golden-rule"                  # cross-project universal
memctl add "<text>" --tags "insight"                      # meta-learning
memctl add "<text>" --tags "dream-log"                    # consolidation entry

# Boost importance (decay-resistant):
memctl weight <id> 1.5
```

### Recall (reads — Tier 3 active recall when injected context insufficient)

```bash
memctl search-tags "session,task-{id}"        # tag-precise
memctl search "<query>"                       # hybrid BM25+semantic with smart retrieval
memctl get <id-or-path>                       # full note content
memctl list --limit 10                        # top by weight
```

**Smart retrieval (5 default signals on every search):**
1. Cluster routing (vault ≥500 notes only) → 2. BM25+semantic hybrid → 3. PRF rerank (drift-guarded) → 4. PageRank boost (recency-clamped) → 5. Wikilink anchor expansion (sparse-graph guarded)

Realistic gain: ~5-10% (vault <100), ~25-35% (vault 500-2000). NOT 50-60%.

### Maintenance (single `maintain` command, self-decides scope)

```bash
memctl maintain               # auto-detects what's needed: ingest if vault changed + decay if stale
memctl maintain --dry-run     # plan without executing
memctl maintain --force       # bypass 60s throttle
```

What it does (in order):
1. Throttle check — skip if last run < 60s ago (unless `--force`)
2. **Ingest** if any vault file modified after index — re-indexes (structural lint baked-in)
3. **Decay** if `last_decay_date != today` — reduces weight of notes >30 days stale
4. Stamp `last_maintain_run` timestamp in index metadata

Output:
```json
{"actions": ["ingest", "decay"], "skipped": ["..."], "skipped_reason": null, "throttled": false, "dry_run": null}
```

Semantic lint (LLM-driven contradiction/synthesis check) NOT auto-triggered — invoke manually when bot wants deep audit:

```bash
memctl lint --semantic --self                              # bot reasons in chat
memctl lint --semantic --llm-url <url> --llm-model <model> # external LLM
```

### Tag schema (routing key)

| Tag | Purpose | Subdir/Lifecycle |
|-----|---------|------------------|
| `session,task-{id}` | SDLC pipeline state | short-term, deleted after task Done |
| `qc-feedback,task-{id}` | QC retry feedback | short-term |
| `qc-error,project-{name}` | Mid-term error patterns | promote to qc-rule at hit_count ≥ 3 |
| `qc-rule,project-{name}` | Active project rules | promote to golden-rule at strength > 5 |
| `qc-score,project-{name}` | Score history | compress oldest after 20 sessions |
| `golden-rule` | Universal cross-project | long-term, retire at strength < 0.05 |
| `anti-pattern` | Recurring agent mistakes | long-term |
| `insight` | Meta-learning | long-term |
| `dream-log` | Consolidation history | append-only |
| `user-preference` | Stack/style/identity | long-term |
| `project-context,project-{name}` | Project domain | mid-term |

---

## Vault layout (V2.1 — since v1.3.0)

`memctl init --vault <project-anchor>` creates `<project-anchor>/.memctl/` as the vault root container:

```
<project-anchor>/                ← project repo, $HOME, anywhere
├── .memctl/                     ← vault root (Obsidian opens here)
│   ├── .obsidian/               ← Obsidian config (auto-hidden in Obsidian)
│   │   └── memctl/              ← memctl runtime (nested, hidden)
│   │       ├── index.db
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

Memctl walks up from cwd looking for `.memctl/` containing `.obsidian/`. Per-project install is the natural default — projects with their own `.memctl/` always resolve to themselves first, no env var needed.

To open vault in Obsidian app: open `<project-anchor>/.memctl/` as the vault folder.

Models live user-global at `~/.memctl/models/embeddinggemma-300m/` — shared across vaults, not per-vault.

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

---

## Upgrading from V1 (pre-v1.3.0)

**Hard cutover — no automatic migration.** If you have a V1 vault (`.obsidian/` + `.memctl/` siblings at project root), do this manually:

```bash
# 1. Move V1 vault aside (preserves notes for manual recovery)
mkdir -p <project>/.archived-v1-vault
mv <project>/.memctl   <project>/.archived-v1-vault/.memctl
mv <project>/.obsidian <project>/.archived-v1-vault/.obsidian

# 2. Init fresh V2 vault
memctl init --vault <project>

# 3. (optional) Copy any .md notes from .archived-v1-vault/ into <project>/.memctl/
cp <project>/.archived-v1-vault/*.md  <project>/.memctl/  2>/dev/null || true

# 4. Rebuild index
memctl ingest --vault <project>/.memctl
```

Add `.archived-v1-vault/` to `.gitignore` so old vault doesn't leak into commits.

---

## Installation

```bash
dotnet tool install -g memctl --add-source ./nupkg
```

---

## Global Options

| Option | Required | Description |
|--------|----------|-------------|
| `--vault <path>` | No (auto-detected from cwd if omitted) | Vault directory — walks up from cwd looking for `.memctl/` containing `.obsidian/`. Required for `init`. |
| `--limit <n>` | No (default: 10) | Max results |
| `--llm-url <url>` | For add/organize | OpenAI-compatible API URL |
| `--llm-model <model>` | For add/organize | Model name |
| `--llm-key <key>` | For add/organize | API key |

---

## Pre-flight Check

**Always run `status` before the first use of a vault.** This tells you if the embedding model is downloaded and the vault is indexed — without triggering a blocking download.

`--vault` is optional if you run from inside a project with a `.memctl/` directory containing `.obsidian/` — memctl auto-detects upward. Examples below use explicit `--vault` for clarity.

```bash
memctl status --vault ./vault
→ {
    "model_ready": false,     ← model not downloaded yet
    "model_size_mb": 0,
    "vault_indexed": false,   ← vault not indexed yet
    "note_count": 0
  }
```

If `model_ready` is false → run `model download` first (one-time, ~310 MB):
```bash
memctl model download
→ {"model_ready": true, "model_size_mb": 309}
```

If `vault_indexed` is false → run `ingest`:
```bash
memctl ingest --vault ./vault
```

Re-check when ready:
```bash
memctl status --vault ./vault
→ {"model_ready": true, "vault_indexed": true, "note_count": 234}
```

---

## Commands

Commands grouped by memory operation. All output structured JSON.

### — Setup & Health —

### `status`
Check readiness. Run this first — does not require model or index to be present.
```bash
memctl status --vault ./vault
```

### `model download`
Download EmbeddingGemma ONNX model (~310 MB). One-time, stored at `~/.memctl/models/`.
```bash
memctl model download
```

### `model list`
List all downloaded models and which one is the current default.
```bash
memctl model list
→ {"default_model": "embeddinggemma-300m", "models": [{"name": "embeddinggemma-300m", "ready": true, "size_mb": 309, "is_default": true}]}
```

### `model use <name>`
Switch default model. **Requires re-indexing the vault** — dimensions change between models.
```bash
memctl model use bge-m3
memctl ingest --vault ./vault   # mandatory after switching
```

### `init`
Create a new V2.1 vault: `.memctl/` container with nested Obsidian config + 7 semantic subdirs (tasks, patterns, lessons, decisions, chats, attachments, claude-memory). Pass parent dir or direct `.memctl/` path — both work.
```bash
memctl init --vault ./my-project       # creates ./my-project/.memctl/
memctl init --vault ./my-project/.memctl   # equivalent (direct path)
```

### — Encode (write to memory) —

### `ingest`
Index all `.md` files in vault into SQLite + compute embeddings. Run after adding notes outside memctl, or first time on existing vault.
```bash
memctl ingest --vault ./my-vault
```

### `add <text>`
Add a new note. With `--llm-*` flags, auto-generates tags and wikilinks.
```bash
memctl add "Blockchain uses distributed ledger to store transactions" --vault ./vault
memctl add "ETH staking yields ~4% APY" --vault ./vault --llm-url https://api.anthropic.com/v1 --llm-model claude-haiku-4-5-20251001 --llm-key $KEY
```

### `get <id|path>`
Retrieve full note content by ID or relative file path.
```bash
memctl get abc123def456 --vault ./vault
memctl get "crypto/ethereum.md" --vault ./vault
```

### `list`
List notes, optionally filtered by tag.
```bash
memctl list --vault ./vault --limit 20
memctl list --vault ./vault --tag crypto
```

### — Recall (read from memory) —

### Search — Quick pick

| Command | When to use |
|---------|-------------|
| `search` | Default — unknown intent, general queries |
| `search-semantic` | Conceptual / meaning-based ("what notes relate to X idea") |
| `search-text` | Exact keyword, name, code, ID |
| `search-tags` | Topic cluster, known tag |
| `search-links` | Graph traversal — "what's connected to note X" |
| `search-date` | Time-scoped queries |

### `search <query>`
**Hybrid search** — RRF fusion of BM25 + semantic. Best default for general queries.
```bash
memctl search "ethereum staking" --vault ./vault
```

### `search-semantic <query>`
Pure vector similarity. Best for conceptual/meaning-based queries.
```bash
memctl search-semantic "decentralized finance" --vault ./vault --limit 5
memctl search-semantic "DeFi" --vault ./vault --scope id1,id2,id3  # restrict to scope
```

### `search-text <query>`
BM25 full-text. Best for exact keywords, names, or code.
```bash
memctl search-text "Uniswap v3" --vault ./vault
```

### `search-tags <tags>`
Filter by tags. Use `--match all` to require all tags.
```bash
memctl search-tags "crypto,blockchain" --vault ./vault
memctl search-tags "crypto,defi" --vault ./vault --match all
```

### `search-links <id>`
Traverse wikilink graph from a note (bidirectional).
```bash
memctl search-links abc123 --vault ./vault --depth 2
```

### `search-date`
Filter by creation date range.
```bash
memctl search-date --vault ./vault --from 2026-01-01 --to 2026-01-31
memctl search-date --vault ./vault --from 2026-03-01
```

### `grep <pattern>`
Raw file search. Works even without index.
```bash
memctl grep "Uniswap" --vault ./vault
memctl grep "0x[0-9a-f]{40}" --vault ./vault --regex
```

### `tags`
List all tags with note counts. Useful for understanding vault topics.
```bash
memctl tags --vault ./vault
```

### `stats`
Vault overview: note count, tag count, link count, index size.
```bash
memctl stats --vault ./vault
```

### — Inspect (observe memory structure) —

### — Maintain (memory health) —

### `weight <id> <value>`
Set importance weight. Affects `list` sort order — higher weight = appears first. Default is `1.0`. Use `0.0` to deprioritize, `> 1.0` to protect from future temporal decay.
```bash
memctl weight abc123 1.5 --vault ./vault   # high importance, decay-resistant
memctl weight abc123 0.9 --vault ./vault   # normal importance
memctl weight abc123 0.0 --vault ./vault   # deprioritize / archive
```

### `identity set <id|path>`
Designate a note as the vault identity note. Its content is injected into every MCP `initialize` response via `serverInfo.instructions`, giving every AI session immediate project context.
```bash
memctl identity set abc123 --vault ./vault
memctl identity set "project/identity.md" --vault ./vault
```

### `identity get`
Retrieve the current identity note.
```bash
memctl identity get --vault ./vault
```

### `append <id> <content>`
Append content to an existing note. Re-embeds and re-indexes the note. Accepts note ID or relative file path.
```bash
memctl append abc123 "Additional notes..." --vault ./vault
memctl append "notes/crypto.md" "More content" --vault ./vault
```

### `organize`
LLM scans all notes, writes tags and wikilinks back to frontmatter.
```bash
memctl organize --vault ./vault --llm-url ... --llm-model ... --llm-key ...
memctl organize --vault ./vault --since 2026-03-01 --llm-url ...  # only recent notes
```

---

## MCP Mode (AI Agent Integration)

Run memctl as a stdio MCP server. Exposes 13 tools to any MCP-compatible client (Claude Code, Cursor, etc.).

`--vault` is optional — memctl auto-detects the vault by walking up from the cwd where the MCP server is spawned. Place your vault (with `.memctl/.obsidian/`) anywhere along the project path and the config becomes zero-config.

```bash
memctl mcp              # auto-detects vault from cwd
memctl mcp --vault ./vault  # explicit override
```

**Claude Code config** (`~/.claude/claude_desktop_config.json` or project `.mcp.json`):
```json
{
  "mcpServers": {
    "memctl": {
      "command": "memctl",
      "args": ["mcp"]
    }
  }
}
```

**Available MCP tools:**

| Tool | Description |
|------|-------------|
| `search` | Hybrid BM25 + semantic search |
| `search_semantic` | Pure vector similarity |
| `search_tags` | Filter by tags |
| `search_date` | Filter by date range |
| `search_links` | Traverse wikilink graph |
| `get` | Retrieve note by ID or path |
| `list` | List notes by importance |
| `get_identity` | Get the vault identity note |
| `create` | Create new note, index immediately |
| `update` | Replace note content, re-index |
| `append` | Append to note, re-index |
| `delete` | Delete note from disk and index (MCP only — no CLI equivalent) |
| `set_weight` | Set note importance weight |

The `initialize` response automatically includes a session protocol in `serverInfo.instructions` — telling the agent to call `list` and `search` at session start for context.

---

## Other LLM Clients

memctl's MCP mode and CLI are client-agnostic. Auto-capture (`memctl capture`) and proactive injection (`memctl context-inject`) are shipped commands that work with any client implementing the Hook Protocol v1 below — Claude Code is just the reference implementation.

### AGENTS.md / OpenCode / Codex / Pi
Copy this skill content (the file you're reading) to `AGENTS.md` in your project root. The session protocol applies to any LLM agent with MCP support.

### Shell wrapper (any CLI-based LLM)
For clients with no hook system, a thin shell wrapper provides auto-capture + proactive injection:
```bash
#!/usr/bin/env bash
# Usage: memctl-wrap <llm-cli-command> [args...]
PROMPT=$(cat /dev/stdin)
CONTEXT=$(echo "$PROMPT" | memctl context-inject 2>/dev/null)
FULL_PROMPT="${CONTEXT}

${PROMPT}"
RESPONSE=$(echo "$FULL_PROMPT" | "$@")
echo "$RESPONSE"
memctl capture --role user --text "$PROMPT" 2>/dev/null
memctl capture --role assistant --text "$RESPONSE" 2>/dev/null
```

### MCP initialize (all MCP clients, zero config)
The MCP `initialize` response already injects session protocol into every MCP-compatible client via `serverInfo.instructions`. No additional config needed — recall instructions are delivered automatically on every session start.

---

## Hook Protocol v1

Claude Code implements G1+G2 via two hooks. Any LLM client can replicate this by implementing the same two events. This section defines the client-agnostic contract — no SDK required, just stdin/stdout.

| Event | When | Direction | memctl command |
|-------|------|-----------|----------------|
| `after-response` | AI finishes a response | client → memctl (stdin JSON) | `memctl capture` |
| `before-prompt` | User submits prompt, before AI sees it | client → memctl (stdin text) → client (stdout prepended) | `memctl context-inject` |

### Event 1: `after-response` (G1 — auto-capture)

After the AI response is complete, spawn the configured command and pipe a JSON payload on stdin:

```json
{
  "session_id": "string — unique per session, stable across turns",
  "cwd": "string — absolute working directory",
  "transcript": [
    { "role": "user",      "content": "string" },
    { "role": "assistant", "content": "string" }
  ]
}
```

- `session_id`: stable per session → turns accumulate in one note (`sessions/<date>-<session_id>.md`)
- `cwd`: used for vault auto-detection (no `--vault` flag needed)
- `transcript`: last N turns is sufficient; full history not required
- Extra fields: ignored (forward-compatible)
- Client: MUST ignore non-zero exit; MUST enforce timeout (recommended 10 s)

### Event 2: `before-prompt` (G2 — context injection)

Before sending the prompt to the AI, spawn the configured command and pipe the user's raw prompt text on stdin. If stdout is non-empty, prepend it to the prompt (or inject as a context message) before the AI call.

- stdin: plain UTF-8 text — the user's prompt, no envelope
- stdout: markdown context block, or empty string
- Timeout: 5 s recommended — treat timeout as empty output, proceed
- Client: MUST NOT block the prompt on non-zero exit or timeout

### Config format (canonical)

```json
{
  "hooks": {
    "after-response": [{ "command": "memctl capture" }],
    "before-prompt":  [{ "command": "memctl context-inject" }]
  }
}
```

### Claude Code — reference implementation

Claude Code maps: `Stop` → `after-response`, `UserPromptSubmit` → `before-prompt`. Config in `~/.claude/settings.json`:

```json
{
  "hooks": {
    "Stop":              [{ "hooks": [{ "type": "command", "command": "memctl capture" }] }],
    "UserPromptSubmit":  [{ "hooks": [{ "type": "command", "command": "memctl context-inject" }] }]
  }
}
```

### Client compliance checklist

- [ ] `after-response`: spawn command, pipe JSON, ignore exit code, enforce 10 s timeout
- [ ] `before-prompt`: spawn command, pipe prompt text, prepend non-empty stdout, enforce 5 s timeout
- [ ] Both: never crash or block the session on any hook failure
- [ ] Config: read from the canonical JSON format above

---

## JSON Output

All commands emit JSON to stdout. Frozen envelope (wire contract):

```json
{
  "schema_version": 1,
  "success": true,
  "action": "search",
  "message": "5 results",
  "data": {
    "query": "...",
    "count": 5,
    "results": [
      {
        "id": "abc123",
        "file": "crypto/ethereum.md",
        "title": "Ethereum Notes",
        "snippet": "...ETH staking yields...",
        "tags": ["crypto", "ethereum"],
        "links": ["DeFi", "Blockchain"],
        "created": "2026-01-15T10:30:00Z",
        "score": 0.91
      }
    ]
  },
  "error": null
}
```

On error: `success: false`, `data: null`, `error: { "code": "<string>", "message": "<human readable>" }`.

**Versioning policy:** `schema_version` integer (currently `1`). Adding fields → no bump (clients ignore unknown). Renaming/removing fields → bump `schema_version` (breaking change requires major version).

---

## Search Strategies

The vault has natural structure — tags, wikilinks, folders, dates — that mirrors the hierarchy BookRAG exploits in long-form documents. The key insight from BookRAG: **narrow scope first, then retrieve**, rather than searching flat across everything.

---

### Archetype 1: Hierarchical narrowing (BookRAG-inspired)
*User: "What did I write about DeFi in January?"*

Narrow by time first, then semantic within that slice — instead of searching all notes.

```bash
memctl search-date --vault ./vault --from 2026-01-01 --to 2026-01-31 --limit 50
→ {"data": {"count": 12, "results": [{"id": "n1"}, {"id": "n2"}, ...]}}

memctl search-semantic "DeFi decentralized finance" --vault ./vault --scope n1,n2,n3,n4,n5,n6,n7,n8,n9,n10,n11,n12
→ {"data": {"results": [{"id": "n4", "score": 0.91}, {"id": "n9", "score": 0.84}]}}

memctl get n4 --vault ./vault
→ {"data": {"content": "..."}}
```

---

### Archetype 2: Graph traversal (wikilink network)
*User: "How does X connect to Y in my notes?"*

Anchor on a known note, expand via link graph, then intersect with second topic.

```bash
memctl search-semantic "X" --vault ./vault --limit 3
→ {"data": {"results": [{"id": "xid", "score": 0.93}]}}

memctl search-links xid --vault ./vault --depth 2
→ {"data": {"count": 8, "results": [{"id": "l1"}, ..., {"id": "l8"}]}}

memctl search-semantic "Y" --vault ./vault --scope l1,l2,l3,l4,l5,l6,l7,l8
→ {"data": {"results": [{"id": "l3", "score": 0.88}]}}

memctl get l3 --vault ./vault
→ connection found
```

---

### Archetype 3: Tag clustering (Zettelkasten-inspired)
*User: "What are my main areas of thinking?"*

Start with structure, not content.

```bash
memctl stats --vault ./vault
→ {"data": {"note_count": 234, "tag_count": 41}}

memctl tags --vault ./vault
→ {"data": {"tags": [{"tag": "crypto", "count": 47}, {"tag": "ai", "count": 31}, ...]}}

memctl search-tags "crypto" --vault ./vault --limit 10
→ top notes in that cluster
```

---

### Archetype 4: Keyword anchor + expand
*User: "Find my notes mentioning Uniswap"*

BM25 for exact term, then expand semantically from top hit.

```bash
memctl search-text "Uniswap" --vault ./vault
→ {"data": {"results": [{"id": "u1", "score": 4.2}]}}

memctl search-links u1 --vault ./vault --depth 1
→ related notes via wikilinks

memctl search-semantic "Uniswap DEX liquidity pool" --vault ./vault --scope u1,linked-ids
→ broader conceptual cluster
```

---

### Archetype 5: Unknown query → hybrid default
*User: general question, no clear structure*

```bash
memctl search "<query>" --vault ./vault
→ RRF(BM25 + semantic) — best starting point

# Too few results → broaden:
memctl search-semantic "<query>" --vault ./vault --limit 20

# Too many irrelevant → narrow by tag:
memctl tags --vault ./vault          # find relevant tag
memctl search-tags "<tag>" --vault ./vault
memctl search-semantic "<query>" --vault ./vault --scope <tag-result-ids>
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | Not found / validation error |
| `2` | Note not found |
| `9` | Internal error |

---

## Capabilities (current state, v1.3.x)

memctl ships these capabilities today. Bot doesn't need to plan around future features — call commands directly.

### Auto-capture (`memctl capture`)
Reads Claude Code `Stop` hook payload from stdin (JSON), filters signal (drops turns <50 chars, pure tool-call turns), appends to `chats/<date>.md`. Non-Claude clients use direct mode: `memctl capture --role user --text "..."`.

### Proactive injection (`memctl context-inject`)
Reads user prompt from stdin, extracts keywords, runs hybrid search + top-by-weight, formats as `## Memory Context` markdown → stdout. Wired into Claude Code `UserPromptSubmit` hook by default.

### Two-tier lint (`memctl lint`)
- **Structural (free):** orphans, duplicates, broken links, isolated notes. Baked into `ingest` — every re-index runs structural lint.
- **Semantic (LLM-driven):** contradictions, stale claims, synthesis candidates.
  - Self-reasoning mode: `memctl lint --semantic --self` dumps notes as structured prompt → bot reasons in chat → bot saves report
  - External LLM mode: `memctl lint --semantic --llm-url <url> --llm-model <model> --llm-key <key>` (OpenAI-compat)

### Source fetch (`memctl fetch`)
Fetch URL or file, convert HTML→markdown, output to stdout. Bot synthesizes + calls `add`/`append` for vault notes.

```bash
memctl fetch "https://example.com/article"     # → markdown to stdout
```

### Temporal decay (`memctl decay`)
Reduce weight of stale notes not accessed/updated in N days.

```bash
memctl decay --days 30
```

Notes manually boosted via `memctl weight <id> 1.5` (or higher) are decay-resistant. Decay surfaces fresh content over old.

### Identity note (`memctl identity`)
Designate one note as Layer 0 vault identity. Auto-injected into every MCP `initialize` response via `serverInfo.instructions`.

```bash
memctl identity set <id>      # designate
memctl identity get           # retrieve current
```

### Hook diagnostics (`memctl hook-status`)
Show recent capture + context-inject activity for debugging.

### Tag migration (`memctl migrate-tags`)
One-time legacy tag rewrite helper. Use only when tag schema changes.
