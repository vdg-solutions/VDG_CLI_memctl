---
name: memctl
description: Personal memory vault CLI backed by an Obsidian-compatible markdown vault. Use when the user asks about their notes, wants to save a thought, or needs to query their knowledge base. All commands output structured JSON.
allowed-tools: Bash
---

# memctl — Personal Memory Vault CLI

Manages an Obsidian-compatible vault of markdown notes. Each command is an atomic tool — combine them to answer complex questions about the vault.

---

## Installation

```bash
dotnet tool install -g memctl --add-source ./nupkg
```

---

## Global Options

| Option | Required | Description |
|--------|----------|-------------|
| `--vault <path>` | No (auto-detected from cwd if omitted) | Vault directory — walks up from cwd looking for `.obsidian/`. Required for `init`. |
| `--limit <n>` | No (default: 10) | Max results |
| `--llm-url <url>` | For add/organize | OpenAI-compatible API URL |
| `--llm-model <model>` | For add/organize | Model name |
| `--llm-key <key>` | For add/organize | API key |

---

## Pre-flight Check

**Always run `status` before the first use of a vault.** This tells you if the embedding model is downloaded and the vault is indexed — without triggering a blocking download.

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
Create a new vault with `.obsidian/` and `.memctl/` structure.
```bash
memctl init --vault ./my-vault
```

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

### `weight <id> <value>`
Set importance weight (0.0–1.0). Affects `list` sort order — higher weight = appears first.
```bash
memctl weight abc123 0.9 --vault ./vault   # mark as high importance
memctl weight abc123 0.0 --vault ./vault   # deprioritize
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

### `add-turn <content>`
Append a conversation turn to an existing note. Designed for logging AI session exchanges.
```bash
memctl add-turn "User: how does X work?\nAssistant: X works by..." --vault ./vault --id abc123
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

`--vault` is optional — memctl auto-detects the vault by walking up from the cwd where the MCP server is spawned. Place your vault (with `.obsidian/`) in the project root and the config becomes zero-config.

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
| `delete` | Delete note from disk and index |
| `set_weight` | Set note importance weight |

The `initialize` response automatically includes a session protocol in `serverInfo.instructions` — telling the agent to call `list` and `search` at session start for context.

---

## JSON Output

Every command returns:
```json
{
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
  }
}
```

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
