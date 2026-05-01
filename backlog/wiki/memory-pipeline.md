# Memory Pipeline — vault → Claude Code

How memctl vault data flows into Claude Code prompts (and back). Written for future bots reading this repo: understand the read/write paths before designing memory-related features.

---

## 3 channels

| Channel | Trigger | Direction | Writer / Reader |
|---------|---------|-----------|-----------------|
| **Inject** | UserPromptSubmit hook | vault → prompt | `memctl context-inject` reads index.db, prepends markdown |
| **Capture** | Stop hook | session → vault | `memctl capture` filters transcript turns, appends to `chats/` |
| **Save explicit** | `/memctl-save` slash | bot → vault | `memctl add` writes `.md` + reindex |

Bot doesn't have to know memctl exists — hooks run automatically, context shows up at top of prompt as `## Memory Context` block.

---

## Read path (every prompt anh submits)

```
anh gõ prompt
       │
       ▼
Claude Code: trigger UserPromptSubmit hook
       │
       │ stdin: { session_id, cwd, prompt_text }
       ▼
memctl context-inject   (binary)
       │
       │ 1. VaultLocator: walk-up cwd → .memctl/.obsidian/ → vault path; fallback MEMCTL_SHARED_VAULT (v1.3.1+)
       │ 2. extract keywords từ prompt (lowercase, dedupe, filter stopwords)
       │ 3. .obsidian/memctl/index.db query:
       │      • semantic search (embeddings cosine sim) top 6
       │      • BM25 keyword match top 6
       │      • merge by score
       │ 4. + list top-weighted notes (weight desc) top 3
       │ 5. format markdown:
       ▼
stdout:
  ## Memory Context

  ### Decision: use Postgres
  Rationale: ACID, json column support...

  ### Insight: JIT vs AOT
  Trade-off: startup vs peak throughput...

  ### Lesson: pin actions to SHA
  Why: tag mutability attack vector...
       │
       ▼
Claude Code prepend stdout vào prompt → gửi cho model
       │
       ▼
Model thấy:
  ## Memory Context
  ... (3-9 notes top relevant)

  <anh's actual prompt>
       │
       ▼
Model trả lời với context aware
```

---

## Write path — auto (after every assistant response)

```
Model trả lời xong
       │
       ▼
Claude Code: trigger Stop hook
       │
       │ stdin: { session_id, cwd, transcript: [{role, content}, ...] }
       ▼
memctl capture   (binary)
       │
       │ 1. VaultLocator walk-up → vault path
       │ 2. filter turns:
       │      • skip < 50 chars
       │      • skip tool-call-only turns
       │ 3. format markdown turn:
       │      ## Turn 2026-05-01T...
       │      **user:** ...
       │      **assistant:** ...
       │ 4. file path: <vault>/chats/2026-05-01.md  (V2.1: daily rollup)
       │ 5. append (preserve weight if existing)
       │ 6. re-index single note (incremental)
       ▼
.obsidian/memctl/index.db updated
       │
       ▼
Next prompt → context-inject sees new note → injects nếu relevant
```

---

## Write path — explicit (bot decides)

```
Bot quyết định "save this":
       │
       ▼
slash command: /memctl-save Decision: ... | Rationale: ...
       │
       ▼
Plugin command (commands/save.md) → memctl add --title "..." --content "..."
       │
       ▼
.md file written to <vault>/   (root or organized subdir)
       │
       ▼
index.db updated → next prompt picks it up
```

---

## Hierarchical memory — `claude-memory/MEMORY.md`

Top-level index. `context-inject` ALWAYS injects MEMORY.md first (sorted weight).

```
context-inject injects MEMORY.md đầu tiên
       │
       ▼
Bot reads MEMORY.md → sees pointers:
       │
       │ "See decisions/adr-0003-vault-layout.md for...
       │  See lessons/lesson-pin-actions.md for..."
       │
       ▼
Bot fetches full content qua memctl get / Read tool
```

`MEMORY.md` consolidated by `/qc-dream` on sprint close. Compact rewrite — drops stale, promotes high-weight lessons.

---

## Memory consolidation (background)

```
After task merge:
       │
       ▼
/retro analyze SDLC journey → write patterns/<slug>.md  (hit_count: 1)
       │
       ▼
/qc-dream:
       │
       │ patterns/<slug>.md hit_count >= 3 → promote to lessons/<slug>.md
       │ lessons/ duplicates merge
       │ MEMORY.md compress + rewrite (top index)
       ▼
index.db re-indexes promoted lessons
       │
       ▼
Next session, context-inject surfaces high-weight lessons trước
```

Promotion ladder:

```
patterns/<slug>.md  (hit_count >= 3)  →  lessons/<slug>.md  (cross-task wisdom)
                                              ↓
                                     (validated 3+ projects)
                                              ↓
                                  ~/.claude/memory/long/golden_rules.md
```

---

## Vault layout (V2.1 — relevant subdirs)

```
<project>/.memctl/                          ← vault root
├── .obsidian/memctl/index.db               ← SEARCH source for context-inject
├── .obsidian/memctl/models/                ← embedding model (loaded by IngestOperator)
├── chats/YYYY-MM-DD.md                     ← capture writes here (Stop hook)
├── claude-memory/MEMORY.md                 ← qc-dream writes (top index)
├── decisions/, patterns/, lessons/, tasks/ ← SDLC writers
└── *.md                                    ← ad-hoc bot saves
```

Read-side: index.db consolidates all `.md` (excluding `.obsidian/`). Write-side: each writer has dedicated subdir per ownership matrix.

See [vault-layout.md](vault-layout.md) for full layout + writer ownership matrix.

---

## Vault resolver priority (v1.3.1+)

```
1. --vault <path> CLI flag                                       (explicit)
2. Walk-up cwd → <dir>/.memctl/.obsidian/                        (per-project, V2.1)
3. MEMCTL_SHARED_VAULT env var → <env>/.obsidian/                (shared opt-in)
4. null vault — caller errors
```

Per-project always wins over env var. Sensitive vault never leaks.

## Hook control (env vars)

Disable individual hooks for one-off sensitive sessions:

```bash
# PowerShell:
$env:MEMCTL_DISABLE_AUTOCAPTURE=1   # tắt Stop hook
$env:MEMCTL_DISABLE_AUTOINJECT=1    # tắt UserPromptSubmit hook
```

Hooks exit 0 silent on missing vault or env disable — never block Claude Code session.

---

## Performance characteristics

| Operation | Typical latency | Bottleneck |
|-----------|----------------|------------|
| `context-inject` (per prompt) | < 200ms | embedding inference + SQLite query |
| `capture` (per response) | < 500ms | tokenize + embed + insert |
| `add` explicit | < 300ms | embed + insert |
| `ingest` full vault | ~1s per 100 notes | embedding batch |

Embedding model: EmbeddingGemma 300M (~295 MB), loaded once per process.

Hook timeouts in `hooks.json`:
- `SessionStart`: 3000ms
- `UserPromptSubmit`: 5000ms
- `Stop`: 10000ms

---

## Future bots: integration checklist

When designing a new memctl feature that touches memory:

1. **Read:** know that `context-inject` runs every prompt. New writes reach next prompt immediately if reindexed.
2. **Write:** prefer subdir convention from V2.1 (`tasks/`, `patterns/`, `lessons/`, `decisions/`, etc.). Don't pollute root unless ad-hoc.
3. **Reindex:** any new `.md` write must trigger ingest (incremental or full). `memctl ingest` rebuilds everything from filesystem; single-note ops should reindex just the changed note.
4. **Frontmatter:** notes use `weight: <float>` (default 1.0, boost to 1.5+ for important). `tags: [...]` for Obsidian tag pane.
5. **Filter rules:** `EnumerateMarkdownFiles` excludes `.obsidian/` (and runtime nested inside). Don't add new exclusions without updating filter.
6. **Hook integration:** if new feature needs hook trigger, add to `plugins/memctl-claude/hooks/hooks.json` + sync to public release host per [plugin-publish.md](plugin-publish.md).
