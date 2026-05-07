---
name: memctl
description: Persistent memory across conversations. Use at conversation start to recall prior context, during work to capture decisions, at end to consolidate. Obsidian-compatible vault — semantic + BM25 search, weighted by importance.
allowed-tools: Bash
---

# memctl — Bot Memory System

Encode → vault stores → recall next conversation → consolidate → stale memories decay unless reinforced.

## 3-layer memory model

| Layer | Path | Lifecycle |
|-------|------|-----------|
| L1 Raw | `chats/{date}-{id}.md` | Stop hook auto-write; decay normally |
| L2 Distilled | `decisions/` `patterns/` `lessons/` | `memctl distill` extracts; weight ≥ 1.0 |
| L3 Linked | wikilinks between L2 notes | Emergent from quality wikilinks in L2 |

## Memory types

| Type | Stores | Path |
|------|--------|------|
| Episodic | Conversation turns | `chats/{date}-{id}.md` (per-conversation) |
| Semantic | Decisions, patterns, lessons | `decisions/` `patterns/` `lessons/` |
| Procedural | Active rules | tag-routed (`golden-rule`, `qc-rule`, `anti-pattern`) |

## Conversation start

```bash
memctl --version            # verify binary first
memctl status               # vault_found, model_ready, vault_indexed
memctl ingest               # only if vault_indexed=false or new files added
memctl list --limit 10      # load top context
memctl search "<task>"      # find relevant prior decisions
```

`## Memory Context` auto-injected every prompt via UserPromptSubmit hook. No explicit recall needed.

---

## Commands

### Setup

| Command | Description |
|---------|-------------|
| `memctl init --vault <path>` | Create vault (`.memctl/` + Obsidian config + 8 subdirs) |
| `memctl status` | Check model_ready, vault_indexed, note_count |
| `memctl ingest` | Index all `.md` → SQLite + embeddings |
| `memctl model download` | Download EmbeddingGemma ONNX (~310 MB, one-time) |
| `memctl model list` | List downloaded models |
| `memctl model use <name>` | Switch model (re-ingest required) |
| `memctl hook-status` | Recent capture + context-inject activity |

### Encode

| Command | Description |
|---------|-------------|
| `memctl add "<text>"` | Add note. `--llm-*` → auto-tags + wikilinks |
| `memctl append <id> "<content>"` | Append to note, re-index |
| `memctl capture --role <r> --text <t>` | Direct-mode conversation capture |
| `memctl distill` | L1 → L2: LLM extracts long-term memory from conversations |
| `memctl distill --conversation <id>` | Distill one specific conversation |
| `memctl distill --dry-run` | Preview extractions without writing |
| `memctl distill --since <YYYY-MM-DD>` | Only conversations after date |
| `memctl organize` | LLM auto-tags + auto-links all notes. `--since <date>` for recent only |
| `memctl weight <id> <value>` | Set importance (0=archive, 1.0=normal, 1.5=decay-resistant) |
| `memctl decay --days <n>` | Reduce stale note weights |
| `memctl delete <id>` | Delete note from disk + index |

### Recall

| Command | Description |
|---------|-------------|
| `memctl search "<q>"` | Hybrid BM25+semantic (RRF). Default |
| `memctl search-semantic "<q>"` | Pure vector. Conceptual queries. `--scope <id,...>` |
| `memctl search-text "<q>"` | BM25. Exact keywords, names, code |
| `memctl search-tags "<tags>"` | Tag filter. `--match all` requires all tags |
| `memctl search-links <id>` | Wikilink graph. `--depth 2` |
| `memctl search-date --from <d> --to <d>` | Date range filter |
| `memctl get <id\|path>` | Full note content |
| `memctl list` | Top notes by weight. `--tag`, `--limit` |
| `memctl grep "<pattern>"` | Raw file search. `--regex`. No index needed |
| `memctl tags` | All tags with counts |
| `memctl stats` | Note count, tag count, link count, index size |
| `memctl fetch <url\|file>` | URL → markdown stdout. Bot synthesizes → `add` |
| `memctl lint` | Structural: orphans, broken links, duplicates |
| `memctl lint --semantic --self` | Semantic: bot reasons about contradictions in chat |

---

## Tag schema

| Tag | Use |
|-----|-----|
| `session,task-{id}` | SDLC pipeline state (short-term) |
| `qc-feedback,task-{id}` | QC retry feedback (short-term) |
| `qc-error,project-{n}` | Error patterns → promote to rule at hit_count ≥ 3 |
| `qc-rule,project-{n}` | Active project rules → promote to golden at strength > 5 |
| `golden-rule` | Cross-project universal (long-term) |
| `anti-pattern` | Recurring mistakes (long-term) |
| `insight` | Meta-learning (long-term) |
| `dream-log` | Consolidation history |
| `user-preference` | Stack, style, identity |

---

## Vault layout

```
<project>/
└── .memctl/               ← vault root (open in Obsidian)
    ├── .obsidian/
    │   └── memctl/        ← index.db, hook.log
    ├── tasks/             ← /sdlc artifacts
    ├── patterns/          ← /retro patterns
    ├── lessons/           ← /qc-dream wisdom
    ├── decisions/         ← /design ADRs
    ├── chats/             ← Stop hook conversation transcripts
    ├── events/            ← EventLog (archived: true, invisible by default)
    ├── attachments/       ← images, binaries
    └── claude-memory/     ← MEMORY.md index
```

Auto-detect: walks up from cwd for `.memctl/.obsidian/`. Per-project wins over `MEMCTL_SHARED_VAULT`.
Shared vault: `export MEMCTL_SHARED_VAULT=$HOME/memctl-personal/.memctl`

---

## Hook Protocol v1

| Event | When | Command |
|-------|------|---------|
| `after-response` (Stop) | After AI response | `memctl capture` |
| `before-prompt` (UserPromptSubmit) | Before AI sees prompt | `memctl context-inject` |

**Wire format — after-response (stdin JSON):**
```json
{
  "conversation_id": "string — stable per conversation",
  "cwd": "string — for vault auto-detect",
  "transcript": [{"role": "user|assistant", "content": "string"}]
}
```

**Claude Code config** (`~/.claude/settings.json`):
```json
{
  "hooks": {
    "Stop":             [{"hooks": [{"type": "command", "command": "memctl capture"}]}],
    "UserPromptSubmit": [{"hooks": [{"type": "command", "command": "memctl context-inject"}]}]
  }
}
```

Client rules: ignore non-zero exit; Stop timeout 10s; UserPromptSubmit timeout 5s; never block on failure.

---

## JSON envelope

```json
{"schema_version": 1, "success": true, "action": "search", "message": "5 results",
 "data": {"query": "...", "count": 5, "results": [{"id": "...", "title": "...", "score": 0.91}]},
 "error": null}
```

Error: `"success": false, "data": null, "error": {"code": "...", "message": "..."}`.

---

## Search strategies

| Archetype | When | Approach |
|-----------|------|----------|
| Hierarchical | "What did I write about X in Jan?" | `search-date` → `search-semantic --scope <ids>` |
| Graph | "How does X connect to Y?" | `search-semantic X` → `search-links` → `search-semantic Y --scope` |
| Tag cluster | "What are my main topics?" | `tags` → `search-tags` → `search-semantic --scope` |
| Keyword+expand | "Find notes mentioning X" | `search-text X` → `search-links` |
| Unknown | General query | `search "<q>"` (RRF default) |

---

## MCP mode

```bash
memctl mcp              # auto-detects vault from cwd
memctl mcp --vault <p>  # explicit override
```

MCP tools: search, search_semantic, search_tags, search_date, search_links, get, list, create, update, append, delete, set_weight, get_identity.

Claude Code config (`~/.claude/claude_desktop_config.json`): `{"mcpServers": {"memctl": {"command": "memctl", "args": ["mcp"]}}}`.

---

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Validation / not found |
| 2 | Note not found |
| 9 | Internal error |
