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

## Memory Protocol — What to do every session

### Session start (Recall)
```bash
memctl status                           # vault healthy? model ready?
memctl ingest                           # re-index if new files added outside Claude
memctl list --limit 10                  # load top memories by importance
memctl search "<current task keywords>" # surface relevant prior decisions
```

### During session (Encode)
```bash
# After a decision, finding, or discovered pattern:
memctl add --title "Decision: use X over Y" --content "Rationale: ..."
memctl append --id <note-id> --content "Update: discovered edge case..."

# Boost notes that will matter next session:
memctl weight <id> 1.5
```

### Session end (Consolidate)
```bash
memctl add --title "Session: <date> — <task>" --content "<summary of what was done, decided, left open>"
```

### Periodic maintenance (Lint) — fully automatic after G3 ships
```bash
# Structural lint: baked into ingest — runs every session, free
memctl ingest
# → {"indexed": 47, "lint": {"orphans": 2, "broken_links": 1, "duplicates": 0}}

# Semantic lint: auto-triggered by ingest when overdue (default: every 7 days)
# Uses cheap LLM (Haiku ~$0.05/run for 100 notes) — no manual action needed
# Report saved as vault note → bot reads it next session start

# Manual semantic lint:
memctl lint --semantic --llm-url <url> --llm-model claude-haiku-4-5-20251001 --llm-key $KEY

# Coming G5:
memctl decay --days 30      # reduce weight of notes not touched in N days
```

Until G3 ships, manually ask the bot: *"Review my vault and clean up duplicates/orphans."*

> **Coming in roadmap G1+G2:** This full protocol will run automatically via Claude Code hooks — capture without remembering to capture, recall without remembering to recall.

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

`--vault` is optional if you run from inside a project with a `.obsidian/` directory — memctl auto-detects upward. Examples below use explicit `--vault` for clarity.

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
Create a new vault with `.obsidian/` and `.memctl/` structure.
```bash
memctl init --vault ./my-vault
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

### `add-turn`
Log a Telegram conversation turn. Creates `chats/<chat-id>/<date>.md` per day, appends each turn. Required flags: `--chat-id`, `--from`, `--role`, `--text`.
```bash
memctl add-turn --chat-id 123456 --from "Alice" --role user --text "how does X work?" --vault ./vault
memctl add-turn --chat-id 123456 --from "Bot" --role assistant --text "X works by..." --vault ./vault
# optional: --user-id, --timestamp (ISO 8601), --write-only (skip index update)
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
| `delete` | Delete note from disk and index (MCP only — no CLI equivalent) |
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

---

## Roadmap — Từ "có thể nhớ" → "nhớ như con người"

Bộ nhớ con người không cần effort: ký ức hình thành tự động, được recall khi liên quan, phai mờ theo thời gian nếu không dùng đến. memctl hiện tại yêu cầu bot chủ động — gọi `list`, gọi `search`, gọi `add`. Roadmap này loại bỏ từng friction point đó.

> "Cần nhớ" = decisions, findings, patterns, user preferences, bug rationale. Không phải mọi exchange — auto-capture filter signal khỏi noise. G5 (decay) là quá trình quên tự nhiên: những gì không được dùng đến sẽ chìm xuống.

### G1 — Auto-capture: ký ức hình thành tự động (P0)

**Vấn đề:** Bot phải chủ động gọi `create`/`append` để lưu memory. Nó thường quên.

**Giải pháp:** Thêm `--auto` flag vào `add-turn`. Kết hợp với Claude Code `Stop` hook — hook chạy sau mỗi response, tự động capture conversation turn vào vault mà không cần bot nhớ gì.

```json
// ~/.claude/settings.json
{ "hooks": { "Stop": [{ "hooks": [{ "type": "command", "command": "memctl add-turn --auto" }] }] } }
```

### G2 — Proactive injection: ký ức tự được recall khi liên quan (P0)

**Vấn đề:** Bot phải chủ động gọi `list`/`search` để load context — nó có thể skip.

**Giải pháp:** New command `memctl context-inject`. Đọc user prompt từ stdin, extract keywords, chạy `list + search`, format thành context block → stdout. Kết hợp với `UserPromptSubmit` hook — context được inject tự động vào mỗi conversation turn trước khi bot process.

```json
{ "hooks": { "UserPromptSubmit": [{ "hooks": [{ "type": "command", "command": "memctl context-inject" }] }] } }
```

### G3 — Lint hai tầng: vệ sinh ký ức tự động (P1)

**Tier 1 — Structural (free, baked vào ingest):** Mỗi lần `ingest` tự động health check — orphans, duplicates, broken links, isolated notes. Kết quả append vào ingest JSON output. Không cần LLM.

**Tier 2 — Semantic (cheap LLM, auto-scheduled):** Track `last_semantic_lint` trong index metadata. Khi `now - last_semantic_lint > 7 ngày` (configurable), ingest tự trigger semantic lint qua LLM rẻ (Haiku ~$0.05/100 notes). LLM tìm contradictions, stale claims, synthesis candidates. Report lưu thành vault note → bot đọc lần sau.

```bash
memctl ingest   # structural free + semantic auto khi overdue
memctl lint --semantic --llm-url ... --llm-model claude-haiku-4-5-20251001 --llm-key $KEY
```

### G4 — Source fetch: học từ nguồn bên ngoài (P2)

**Vấn đề:** Không có cách fetch raw source (URL/file) để bot synthesize vào vault.

**Giải pháp:** `memctl fetch <url>` — fetch URL, convert HTML → markdown, output to stdout. Bot đọc, synthesize, gọi `create`/`append`. memctl là fetch helper; bot là brain.

```bash
memctl fetch "https://example.com/article" | # → bot reads, creates notes
```

### G5 — Temporal decay: quên tự nhiên (P1)

**Vấn đề:** Old notes không decay → cạnh tranh với fresh notes trong search results. Vault trở nên noisy theo thời gian.

**Giải pháp:** `memctl decay --days 30` — giảm weight của notes không được access/update trong N ngày. Weight field đã có sẵn; chỉ cần decay logic. Notes quan trọng được boost tay (hoặc qua auto-capture) → immune to decay. Notes bị quên → tự nhiên chìm xuống. Đây là cơ chế giữ vault focused vào "cần nhớ" thay vì "mọi thứ".
