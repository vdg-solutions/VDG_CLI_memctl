# Memory Protocol V1

Single source of truth for ALL memory operations in memctl-equipped projects. Skill-agnostic. Cost-bounded. Passive event-driven (no daemon).

This doc supersedes scattered references trong skill bodies + `_memctl-backend.md`. Conflicts → protocol wins.

---

## §-1 Architectural primary: bot in session = the memory engine

Memory exists FOR LLM. **Assume LLM always available** — vault designed around bot in Claude Code session as primary actor. No-LLM standalone = edge case, not designed around.

### Hook ↔ bot architecture (honest)

**Hooks do NOT invoke bot.** Hooks are external commands (Stop, SessionStart, UserPromptSubmit). Bot is LLM in chat session, acts only during user-prompt-response cycle.

Coordination via **bot-inbox pattern**:

```
Hook fires (external)         Bot consumes (next user-prompt cycle)
──────────────────            ─────────────────────────────────────
Stop hook:                    UserPromptSubmit hook:
  capture turn                  context-inject reads bot-todo.md
  Tier 1 regex distill          if work-items pending:
  Tier 2 embedding ops            inject as part of context
  write bot-todo.md             bot sees pending work
  (work-item: "review            in next response, bot:
   tentative ADR-X for             reads flagged notes
   confidence promotion")          decides nuanced action
                                   writes via memctl add
                                   clears bot-todo entry
                                   announces in conversation
```

Latency: next user-prompt cycle. Not real-time parallel — but anh's working session natural granularity.

### Five operational guarantees

1. **No commands.** Anh never types `memctl pin/archive`. Bot detects via natural language + executes inline.
2. **Bot self-corrects.** Inbox includes audit work-items from SessionStart. Bot reviews, reverses mistakes naturally.
3. **Bot proactive announces.** "Noting as ADR-0042" trong response. Anh override natural "no, scratch that".
4. **Confidence threshold.** Hook-side regex creates `confidence-tentative` candidate. Bot in next response promotes to `confidence-proven` only if context confirms.
5. **Token discipline by convention.** Maintenance work terse (1-2 lines per op, brief announcements). No hard cap (LLM can't measure own usage); discipline via brevity rule. Heavy ops batched to /qc-dream skill events.

### Tier responsibility split

| Tier | Actor | When |
|------|-------|------|
| **Tier 1 — regex/counter/math** | External hook commands (memctl CLI) | Real-time per-event |
| **Tier 2 — embedding ops** | External hook commands (gemma model in process) | Real-time per-event |
| **Tier 3 — LLM reasoning** | Bot in session via inbox pattern | Next user-prompt-response cycle |

Vault works without bot (Tier 1+2 cover ~80% value). With bot (assumed default), Tier 3 catches up via inbox latency = next prompt.

### Edge case: bot session absent

If anh runs `memctl <cmd>` from CLI standalone (no Claude Code), Tier 3 work piles up in bot-todo. Next time anh open Claude Code, bot processes accumulated inbox in first response. No data loss; latency = "next session start".

---

## §0 Goals

Bot ≠ human → no biological memory → blank slate per session. Memory exists để **compound knowledge across sessions/tasks/projects**.

5 use cases justify:

1. **Recall past context** — "What did we decide last week about X?" → bot surfaces past decisions, doesn't re-ask user.
2. **Avoid repeated mistakes** — Hit bug 3 times → codify pattern → prevent N+1.
3. **Cross-pollinate** — Apply learned approach (proven in project A) to project B.
4. **Maintain coherence** — Bot consistent với prior commitments — no contradictions across sessions.
5. **Compound expertise** — Bot becomes BETTER at user's domain over time, not blank slate every login.

---

## §1 Core principles (anh's directives)

| # | Principle | Implication |
|---|-----------|-------------|
| 1 | Bot không bận tâm save | Memory **self-encodes** from conversation. No "I should save this" decision overhead. |
| 2 | Memory tự lọc bubble | Relevant items **auto-surface** to bot via context-inject. Ranking does work, not bot. |
| 3 | LLM moi móc nếu chưa thỏa | Bot **active recall** via search when surface miss. Optional, not default-required. |
| 4 | Cost-efficient | Hot path < 200ms. Heavy work async/offline. **No daemon required.** |

Human-memory metaphor: hippocampus auto-records, sleep consolidates, cortex surfaces, frontal control invokes deeper recall.

---

## §2 Four sub-systems

```
┌──────────────────────────────────────────────────────────────┐
│ 1. AUTO-ENCODE — bot không bận tâm                            │
│    - Capture conversation turns (raw, verbatim)               │
│    - Distill signals from turns (decisions, findings, errors) │
│    - Synthesize sources into wiki pages (Karpathy-style)      │
└──────────────────────────────────────────────────────────────┘
┌──────────────────────────────────────────────────────────────┐
│ 2. AUTO-SURFACE — memory tự bubble                            │
│    - Tiered read (Layer 0/1/2/3 token budget)                 │
│    - Pre-filter wing/room narrowing                           │
│    - Confidence × recency × diversity rerank                  │
│    - MMR avoid duplicate context                              │
└──────────────────────────────────────────────────────────────┘
┌──────────────────────────────────────────────────────────────┐
│ 3. ACTIVE RECALL — bot moi móc                                │
│    - Explicit search-tags / search-text / search-semantic    │
│    - Multi-hop wikilink expansion                             │
│    - Temporal filter ("last week", "since X")                 │
│    - Layer 3 deep search on demand                            │
└──────────────────────────────────────────────────────────────┘
┌──────────────────────────────────────────────────────────────┐
│ 4. WIKI MAINTENANCE — Karpathy llm-wiki discipline            │
│    - INDEX.md catalog (auto-maintained)                       │
│    - LOG.md chronological audit                               │
│    - Source ingestion synthesis (10-15 page updates per src)  │
│    - Lint (contradictions, orphans, gaps, missing cross-refs) │
└──────────────────────────────────────────────────────────────┘
```

---

## §3 Three compute tiers (passive, no daemon)

| Tier | Latency | Trigger | Operations |
|------|---------|---------|------------|
| **HOT** | <200ms | UserPromptSubmit hook | Read precomputed top-N cache + Layer 1+2 inject |
| **WARM** | <5s, async non-blocking | Stop hook | Capture chat + distill turn + log append + hit_count refresh |
| **COLD** | minutes, opportunistic | /retro + /qc-dream + sprint-close + every K-th Stop + manual `memctl maintain` | Consolidate, promote, decay, re-score, lint, rebuild cache |

**No daemon, no scheduled service.** Each tier piggy-backs on natural workflow events.

### Hot path budget

```
context-inject input: user prompt
1. Vault locator walk-up         <5ms
2. Embed prompt (model preloaded) <50ms
3. Read Layer 0 (identity)       <5ms
4. Read precomputed Layer 1 cache <5ms
5. Search Layer 2 (BM25+semantic) <80ms
6. MMR rerank Layer 2            <20ms
7. Format markdown               <5ms
                                  ─────
                                  ~170ms
```

Strict budget — anything blocking > 200ms moves to WARM tier.

### Warm path (post-response, async)

```
Stop hook spawns:
  capture (existing)            — write raw turn to chats/
  distill (new)                 — regex/heuristic signal extraction
  log append (new)              — append LOG.md entry
  hit_count refresh (new)       — bump access count on read notes
  pressure metrics update (new) — track unconsolidated_turns counter
```

All non-blocking. Next user prompt doesn't wait.

### Cold path (event-triggered)

Triggers (any):
- /sdlc Phase 6 end → /retro → /qc-dream
- /sprint-close (epic done)
- Every K-th Stop hook (default K=10) → light cold ops (decay, cache rebuild)
- Manual `memctl maintain --full | --quick`
- Pressure threshold breach (see §8)

Operations:
- Consolidate patterns (dedup, merge similar)
- Promote (hit_count >= 3 → rule, validated → golden)
- Decay weights (untouched × 0.95^(days/30))
- Re-score top-N
- Rebuild Layer 1 cache (precomputed)
- MMR diversity rerank build
- Lint (structural + semantic + contradiction + concept gap + missing cross-ref)
- Update INDEX.md catalog
- Append LOG.md consolidation entry

---

## §4 Storage contract

### Vault location (V2.1, canonical)

```
<project_root>/.memctl/                  ← vault root (Obsidian opens here)
```

Marker pair: `.memctl/` AND `.memctl/.obsidian/`. Walk-up resolver from cwd. **Per-project always.** No global vault, no cross-vault sync (out of scope per anh directive).

### Layout

```
<vault>/
├── .obsidian/                         ← Obsidian config (auto-hidden)
│   └── memctl/                        ← memctl runtime (nested, hidden)
│       ├── index.db                   ← SQLite — embeddings + BM25 + metadata
│       ├── hook.log                   ← Stop/UserPromptSubmit/SessionStart audit
│       ├── pressure.json              ← (NEW) maintenance trigger metrics
│       └── layer1_cache.json          ← (NEW) precomputed top-N for fast hot path
├── tasks/                             ← orchestrator role (per-phase artifact)
├── patterns/                          ← retrospective role (recurring errors)
├── lessons/                           ← consolidator role (cross-task wisdom)
├── decisions/                         ← decision-recorder role (ADRs)
├── chats/                             ← capturer role (daily rollup)
│   └── YYYY-MM-DD.md
├── attachments/                       ← non-md files (images, binaries)
├── claude-memory/
│   └── MEMORY.md                      ← top-level pointer page
├── INDEX.md                           ← (NEW) Karpathy-style catalog
├── LOG.md                             ← (NEW) chronological audit
├── README.md                          ← vault explainer (init)
├── *.md                               ← ad-hoc scratchpad
└── archive/                           ← (NEW) excluded from default search; see §18
    ├── decisions/
    ├── patterns/
    ├── lessons/
    └── chats/
        └── YYYY/
```

Models live user-global at `~/.memctl/models/embeddinggemma-300m/` (shared across vaults, not per-vault).

### Tag schema (Wing/Room/Hall, MemPalace-inspired)

```
Wing  = project (1 wing per vault by V2.1 design)
Room  = topic/domain inside wing
Hall  = memory type label
Drawer = atomic note (file)
```

Tag convention:
```
wing-{project},room-{topic},hall-{type},confidence-{level}
```

Examples:
- `wing-VDG_CLI_memctl,room-vault-layout,hall-decision,confidence-proven`
- `wing-VDG_CLI_memctl,room-auth,hall-pattern,project-X`
- `hall-lesson` (cross-room wisdom — no room tag)
- `hall-finding,room-postgres` (discovery in postgres room)

Hall types (closed set):
- `decision` — ADR
- `pattern` — recurring error
- `lesson` — distilled wisdom
- `finding` — discovery
- `task` — work artifact
- `chat` — raw conversation
- `index` — catalog page
- `source` — ingested raw material

Confidence levels (closed set):
- `proven` — validated 3+ times
- `tentative` — single occurrence, unconfirmed
- `superseded` — replaced by newer; show as historical
- `disputed` — contradicting evidence

---

## §5 Encode (write)

### Write channels

**Channel 1 — Implicit (passive, no bot decision):**
- Stop hook → `memctl capture` writes raw transcript turn to `chats/{date}.md`
- Hook chạy ngầm, bot không thấy

**Channel 2 — Explicit (bot decides):**
- Bot detects signal during work → invokes `memctl add ... --tags "..."`
- User explicit: "save this", "/memctl-save"
- Skill-driven: /design saves ADR, /retro saves pattern

**Channel 3 — Periodic (consolidation):**
- /retro chạy → distill chats → patterns
- /qc-dream chạy → consolidate patterns → lessons
- Background, opportunistic

### Bot decision rule (Channel 2)

**Default: DON'T save.** Channel 1 (auto-capture) handles raw — Channel 2 is signal extraction only.

Save triggers (signal threshold met):
- User explicit ("save", "remember", "ADR this")
- Architectural fork — bot picked one of multiple valid options + has rationale
- Discovery — bot learned new fact about codebase/user/domain (not from prompt)
- Repeated mistake — Nth occurrence (search before save to confirm count)
- Task completion boundary — phase end, summary required

Anti-triggers (DON'T save):
- Routine code work (already in chats/)
- Tool output (already in transcript)
- Speculation (low confidence, premature)
- Ephemeral state cleared per task end

### Auto-distill (Warm tier, Channel 1.5)

Em propose new mechanism: **Stop hook post-processes transcript for signal phrases**, auto-creates structured notes WITHOUT bot conscious save.

Signal patterns (regex/heuristic, no LLM):
- "we decided X because Y" / "decided to X" / "settled on X" → decision candidate
- "the issue is Z" + "fix: W" / "root cause: Z" → pattern candidate
- "discovered" / "noticed" / "turns out" → finding candidate
- "rejected X because Y" → decision-against candidate

Confidence: `tentative` by default (single occurrence). Promote to `proven` only after manual confirmation OR Nth re-mention.

Auto-distill writes to vault subdirs with `confidence-tentative` tag. Lint surfaces tentative items for user review during periodic maintenance — user confirms or discards.

### Title naming

| Subdir/Hall | Title format |
|-------------|--------------|
| `tasks/` (`hall-task`) | `task-{id}-{phase}` |
| `patterns/` (`hall-pattern`) | `pattern-{slug}` |
| `lessons/` (`hall-lesson`) | `lesson-{slug}` |
| `decisions/` (`hall-decision`) | `adr-{NNNN}-{slug}` |
| `chats/` (`hall-chat`) | `YYYY-MM-DD` |
| `*.md` (root, scratchpad) | user-supplied |
| Sources (`hall-source`) | `source-{slug}` |

### Frontmatter convention

```yaml
---
title: <human title>
weight: 1.0                     # importance (default 1.0)
tags: [wing-X, room-Y, hall-Z, confidence-W]
created: 2026-05-01T12:00:00Z
modified: 2026-05-01T12:00:00Z
hit_count: 0                    # access reinforcement
last_accessed: 2026-05-01T12:00:00Z
superseded_by: null             # or [adr-0042] if obsolete
links: [related-note-1, related-note-2]
sources: [source-id-1]          # if synthesized from raw source
---
```

---

## §6 Recall (read) — most important phần

### Tiered read (token-budgeted, MemPalace-inspired)

| Layer | Token budget | Content | Latency |
|-------|--------------|---------|---------|
| **Layer 0** | ~100 | Identity note (always) | <5ms |
| **Layer 1** | ~600 | Top 15 by combined score (precomputed cache) | <5ms (cache hit) |
| **Layer 2** | ~400 | 6 context-relevant (BM25+semantic RRF on prompt keywords) | <100ms |
| **Layer 3** | unbounded | Bot explicit search via slash/CLI | on-demand |
| **Total auto** | ~1100 | Layer 0+1+2 injected per prompt | ~170ms |

Layer 0+1+2 always inject via UserPromptSubmit hook. Layer 3 only when bot explicit invokes.

### Pre-filter narrowing

```
Detect wing from cwd        → already done by VaultLocator
Detect room from prompt:
  - Extract topic keywords (lowercase, dedup, filter stopwords)
  - Match against existing tag taxonomy `room-*`
  - If 80%+ confidence → narrow scope to that room first
  - Else: full vault search
```

Narrow → semantic search within scope → fewer false positives.

### Combined scoring

For Layer 1 cache (precomputed) and Layer 2 (live):

```
score = semantic_sim          # 0..1 (cosine)
      × log(weight + 1)       # 0..N (importance)
      × recency_decay         # 0.95^(days_since_modified / 30)
      × log(hit_count + 2)    # access frequency reinforcement
      × confidence_weight     # proven=1.0, tentative=0.7, superseded=0.0
      × diversity_penalty     # MMR penalize duplicates
```

Weight 0.0 → fully decayed, hidden from default surface (still searchable explicit).
Weight 1.0 → normal.
Weight 1.5+ → boosted, decay-resistant.

### MMR diversity rerank

After top-K candidates by score:
```
selected = []
for i in range(N):
    candidate = max(remaining,
                    key=lambda c: λ × score(c)
                                - (1-λ) × max(sim(c, s) for s in selected))
    selected.append(candidate)
    remaining.remove(candidate)
```

Default λ=0.7 (favor relevance). Prevents 5 dupes of same topic in surfaced top-N.

### Recall enrichment

| Feature | Mechanism | When applied |
|---------|-----------|-------------|
| **Temporal** | Frontmatter `created`/`modified` + query parser ("last week", "since X") | If prompt contains temporal phrase |
| **Multi-hop** | Auto-traverse wikilinks 1-hop from top-3 hits | Always (Layer 2) |
| **Supersession** | Skip notes with `superseded_by` set; show successor link instead | Always |
| **Confidence rerank** | `confidence_weight` factor in score | Always |
| **Negative recall** | Include `hall-decision-against`/`rejected` tagged notes | Default include |
| **Disambiguation** | Multiple matches same topic → list with id+title+date+confidence | When Layer 2 returns >1 strong match |

### Active recall triggers (Channel B/C)

**Channel B — Bot explicit:**
- `/memctl-recall <topic>` slash command — bot wants more depth
- `memctl search "<query>" --expand-links 2 --temporal "since 2026-04"` via Bash tool

**Channel C — Reactive (bot self-recall mid-thinking):**
- Bot reasoning detects gap → invokes `memctl search-tags "..."` via Bash
- User mentions past entity → bot searches by entity/date
- Bot about to make decision contradicting injected context → re-search to confirm

**Bot decision rule (when active recall):**
- Trust Channel A (auto-injected) by default
- Active recall only when:
  - User mentions past entity not in injected context ("our auth decision")
  - Multi-step plan needs per-step context check
  - Contradiction detected → confirm via deeper search

---

## §7 Wiki maintenance (Karpathy-inspired)

### 3-layer model

| Layer | Owner | Mutability |
|-------|-------|------------|
| **Raw sources** | User curates (URLs, PDFs, articles, chat exports) | Immutable — LLM reads, never modifies. Stored at `<vault>/attachments/sources/` or external. |
| **Wiki** | LLM owns (entity pages, concept pages, ADRs, summaries, cross-refs) | LLM creates + maintains. User reads. Stored at `<vault>/{decisions,patterns,lessons,*.md}`. |
| **Schema** | Co-evolved (skill instructions defining how LLM operates) | `~/.claude/skills/_memctl-backend.md` (canonical), `<repo>/backlog/wiki/memory-protocol.md` (this doc). |

User curates sources, asks questions. LLM does maintenance: summarize, cross-reference, file, update.

### INDEX.md catalog

Auto-maintained by /qc-dream or `memctl maintain --update-index`:

```markdown
# Vault Index

_Last updated: 2026-05-01 by qc-dream_

## Entities
- [Postgres](entities/postgres.md) — DB choice for project X · 3 notes
- [VaultLocator](entities/vault-locator.md) — V2.1 resolver · 5 notes
- [Stop hook](entities/stop-hook.md) — Plugin event · 2 notes

## Concepts
- [Per-project isolation](concepts/per-project-isolation.md) — design principle · proven
- [Token tier budget](concepts/token-tiers.md) — recall optimization · proven

## Decisions (ADRs)
- [ADR-0001](decisions/adr-0001-aot.md) — adopt Native AOT · 2026-04-29
- [ADR-0003](decisions/adr-0003-vault-v2.md) — vault layout V2.1 · 2026-05-01

## Sources
- [MemPalace article](attachments/sources/mempalace-article.md) — Medium 2026-04
- [Karpathy llm-wiki](attachments/sources/karpathy-llm-wiki.md) — Gist 2026

## Patterns
- [pat-resolver-explicit-short-circuit](patterns/pat-resolver-explicit-short-circuit.md) · hit=1
- [pat-yaml-grep-structural-check-brittle](patterns/pat-yaml-grep-structural-check-brittle.md) · hit=1

## Lessons
- [lesson-skill-sync-script-proven](lessons/lesson-skill-sync-script-proven.md)
- [lesson-pre-optimized-backlog-compresses-sdlc](lessons/lesson-pre-optimized-backlog-compresses-sdlc.md)
```

LLM updates on every ingest/distill/consolidation. Bot reads INDEX first → drills into pages.

### LOG.md chronological

Append-only:

```markdown
# Vault Log

## [2026-05-01 13:42] capture | session abc123 turn 7
## [2026-05-01 13:42] distill | extracted 1 finding from turn 7 (tentative)
## [2026-05-01 13:50] decision | ADR-0003 vault layout V2.1
## [2026-05-01 14:15] ingest-source | mempalace-article.md → 3 wiki updates
## [2026-05-01 14:30] consolidate | merged 2 patterns into pat-resolver-short-circuit
## [2026-05-01 14:30] promote | pat-yaml-grep promoted to lesson (hit=3)
## [2026-05-01 15:00] lint | found 1 contradiction in entities/postgres.md, flagged
## [2026-05-01 15:30] maintain-full | rebuilt cache, decayed 12 notes, promoted 2
```

Parseable: `grep "^## \[" LOG.md | tail -10`. Hot-path read for "recent activity" context block.

### Source ingestion synthesis

```bash
memctl ingest-source <url|file> [--wiki] [--no-synthesize]
```

LLM pipeline:
1. Read source (HTML → markdown via existing `memctl fetch`, or local file)
2. Extract entities, concepts, claims, dates
3. Write `<vault>/attachments/sources/<slug>.md` (raw archive)
4. For each entity:
   - If entity page exists → update with new info, flag contradictions
   - Else → create new entity page, link from INDEX
5. For each concept:
   - Update concept page or create
6. Append cross-references (wikilinks)
7. Append LOG.md entry
8. Bot summarizes changes to user (10-15 page touches typical)

User involvement: review summaries, guide emphasis. Schema (this doc) tells LLM workflow.

### Lint operations

Extends G3 roadmap. Run on `memctl maintain --lint` or as part of `--full`:

| Lint type | Detection | Action |
|-----------|-----------|--------|
| **Orphans** (existing G3) | Note with no inbound wikilinks | Flag for review or auto-link if obvious topic match |
| **Broken links** (existing G3) | Wikilink target missing | Either create stub or remove dead link |
| **Duplicates** (existing G3) | Notes with high semantic similarity | Suggest merge |
| **Contradictions** (NEW) | 2+ notes claim opposing facts about same entity | Surface to user, suggest reconcile or flag stale |
| **Concept gaps** (NEW) | Entity mentioned in 3+ notes without dedicated page | Suggest creating page |
| **Missing cross-refs** (NEW) | Entity in note A and B without `[[link]]` | Suggest adding link |
| **Stale claims** (NEW) | Newer source contradicts older note | Flag for supersession |

Semantic lints use cheap LLM call (~$0.05/100 notes per Karpathy roadmap G3) OR self-lint (memctl outputs notes as prompt → bot reasons → bot saves report — no external LLM).

---

## §8 Maintenance trigger detection

**Pressure metrics** stored in `<vault>/.obsidian/memctl/pressure.json`:

```json
{
  "unconsolidated_turns_count": 23,
  "patterns_pending_promotion": 1,
  "stale_notes_count": 8,
  "contradiction_flags": 0,
  "concept_gap_count": 4,
  "tentative_notes_pending_review": 7,
  "last_full_maintain": "2026-04-25T14:30:00Z",
  "days_since_full_maintain": 6,
  "last_quick_maintain": "2026-05-01T13:00:00Z",
  "hours_since_quick_maintain": 0.5,
  "vault_size_notes": 234,
  "vault_size_chats": 12
}
```

Counters incremented by Stop hook + ingest + distill. Reset by maintain operations.

### Trigger conditions

`memctl maintain --check` returns recommendation:

| Condition | Threshold | Action recommended |
|-----------|-----------|-------------------|
| `unconsolidated_turns > 50` | Chat backlog grew | Run `--quick` (distill catchup) |
| `patterns_pending_promotion >= 3` | Promotions piled up | Run `--full` (promote + reindex) |
| `days_since_full_maintain > 7` | Weekly heartbeat | Run `--full` |
| `contradiction_flags > 0` | Active issue | Run `--lint` immediately |
| `concept_gap_count > 5` | Synthesis debt | Run `--full --synthesize` |
| `tentative_notes_pending_review > 10` | Distill backlog | User review queue (not auto) |
| `vault_size_notes > 500 AND last_full_maintain > 14d` | Large vault stale | Run `--full` |
| `hours_since_quick_maintain > 24` | Daily light cleanup | Run `--quick` |

If ANY threshold breached → `memctl status` shows:

```
{
  "vault_status": "...",
  "maintenance": {
    "recommended": true,
    "reason": "3 patterns ready promote; 6 days since full maintain",
    "command": "memctl maintain --full"
  }
}
```

SessionStart hook (existing) reads pressure → emit `::warning::` annotation if `recommended: true`. User sees on every session start until they run maintain.

### Self-deciding maintain (default: ON, ubiquitous trigger)

**Single command, auto-scopes.** Memctl reads pressure → picks operation:

```
read pressure.json
if contradiction_flags > 0          → lint (LLM opt-in, otherwise structural only)
elif patterns_pending_promotion >= 3 → cheap (consolidate + promote + cache rebuild)
elif concept_gap_count > 5           → cheap + flag for synthesize (LLM opt-in)
elif days_since_full_maintain > 7    → cheap (full deterministic clean)
elif unconsolidated_turns_count > 50 → cheap (distill + decay + cache + INDEX)
elif hours_since_quick_maintain > 24 → cheap (light: cache + INDEX)
else                                 → noop, exit 0 silent
```

User never picks scope. Memctl decides.

### Universal pressure trigger (every memctl invocation)

Every `memctl <subcommand>` invocation enters via pressure-aware shim:

```
memctl status / search / add / get / list / ...
        │
        ▼
[shim] read pressure.json (~1ms)
        │
        ├── pressure breached + cheap-fixable + last_auto_maintain >= 60s ago
        │       ↓
        │   spawn `memctl maintain --auto` detached process, exit immediately
        │   (continues in background, doesn't delay main command)
        │
        ▼
execute <subcommand>  (the actual command anh asked for)
```

Effect: anh chạy bất kỳ memctl command — `status`, `search`, `add`, etc. — pressure opportunistically catches up. Hook events also still trigger via own paths. Pressure check **ubiquitous**.

Throttle: `last_auto_maintain >= 60s` prevents runaway re-trigger if anh chạy nhiều memctl calls liên tiếp.

Cost: ~1ms (stat + parse JSON). Negligible vs subcommand latency.

### Auto-trigger paths summary

| Trigger | Source | Cost | Frequency |
|---------|--------|------|-----------|
| Universal shim | Every memctl invocation | ~1ms | Every CLI call |
| Stop hook (existing) | Every assistant response | ~5s async | Per-response |
| SessionStart hook (existing) | Claude Code session start | ~5s async | Per-session |
| Bot proactive (in-session) | Skill instruction reads `data.maintenance.recommended` | varies | Per session start |
| `/memctl-maintain` slash command | User explicit | varies | On demand |
| Throttle | `last_auto_maintain >= 60s` | — | Prevents thrash |

Default: ALL paths ON. User effort = 0.

### Bot in-session maintain (default — assume LLM available)

Bot in Claude Code session processes memory work via inbox pattern. Hooks coordinate (write work-items), bot consumes (next user-prompt-response cycle).

**Skill rule (in `docs/memctl.md`):**

> Mode B default protocol — bot owns memory work end-to-end:
>
> **Per-Stop hook (post-response, batched K=5):**
> 1. Read last K turns from chats/
> 2. Distill nuanced signals (not just regex):
>    - Decisions made and rationale
>    - Discoveries/findings
>    - Recurring errors patterns
>    - Importance markers (explicit + implicit emphasis)
> 3. Save with appropriate tags + confidence-tentative if single-signal, confidence-proven if 2-signal threshold met
> 4. Update INDEX.md catalog (add new entries)
> 5. Append LOG.md entry summarizing actions
> 6. Announce in conversation if action visible: "I'm noting <X> as ADR-<NNNN>" (anh can override naturally)
>
> **Per-SessionStart (audit + maintenance):**
> 1. Read LOG.md last 10 auto-actions
> 2. Audit: any action seem wrong (saved trivial, archived important)?
> 3. Reverse mistakes via memctl (no commands needed — bot does)
> 4. Read pressure metrics → run cheap ops if breached
> 5. Continue session normally
>
> **Per-conversation natural override:**
> - Anh nói "no, don't archive that" / "unarchive X" / "this isn't important" / "we don't need this anymore"
> - Bot detects override pattern → reverses via memctl, no command typed
> - Anh nói "remember this" / "important" / "boost X" → bot pins with weight 1.5
>
> **Token budget:**
> - Maintenance ops batched per K=5 turns (not every turn)
> - Async via Stop hook spawn (don't block next prompt)
> - Cap ≤ 10% of session tokens for maintenance work
> - Heavy ops (full synthesis, lint) deferred to /qc-dream skill event (less frequent)

Mode B is DEFAULT. Bot trong session does this. No opt-in, no flag.

**Mode A fallback (no Claude Code session — `CLAUDECODE` env unset):**
- Tier 1 regex distill runs (catches obvious signals)
- Tier 2 embedding (synonyms, dedup, clustering)
- Tier 3 LLM ops skipped (or external if `--llm-url` configured)
- Lower fidelity but vault stays maintained

### `/memctl-maintain` slash command

Plugin command for user explicit trigger:

```markdown
---
description: Run vault maintenance — auto-decides scope from pressure
argument-hint: "[--force <scope>] | [--check]"
---
1. Run `memctl maintain --check` to see pressure status.
2. If pressure breached or anh passes --force <scope>:
   a. Cheap ops: `memctl maintain` (auto-scopes via pressure)
   b. LLM-required ops (semantic lint, source synthesis):
      - Read flagged notes via Read tool
      - Reason about contradictions / synthesize sources
      - Save findings via `memctl add` with tags
3. Append LOG.md summary via `memctl add ... --tags "log,maintain"`.
```

User types `/memctl-maintain` → bot runs both cheap + LLM ops in-session.

### Manual surface (3 commands, escape hatch only)

```bash
memctl maintain               # explicit run now (auto-scopes via pressure)
memctl maintain --check       # dry-run: report what WOULD do, no action
memctl maintain --force <scope>  # override: scope = quick|lint|full|synthesize|review
```

Most users never type these — hooks handle everything. `--check` for curiosity. `--force` for power-user override.

### Configuration (1 setting, default ON)

```bash
memctl config set maintenance.auto off    # disable hook auto-trigger (default: on)
```

If anh prefer 100% explicit (run only when typed), turn off.

### Surface to bot

`memctl status --vault <path>` JSON output now includes `data.maintenance` field:

```json
{
  "data": {
    "vault_indexed": true,
    "note_count": 234,
    "maintenance": {
      "recommended": true,
      "reason": "...",
      "command": "memctl maintain --full",
      "pressure": { ... }
    }
  }
}
```

UserPromptSubmit hook (context-inject) can surface high-priority maintenance recommendation as part of injected context (rare cases — em propose only if `recommended: true AND reason mentions contradiction OR concept_gap > 10`). Most cases: silent until SessionStart.

---

## §9 Passive event-driven mechanism (no daemon)

Compute tiers map directly to natural workflow events:

```
HOT  → UserPromptSubmit hook (every prompt)
WARM → Stop hook (every assistant response, async)
COLD → /sdlc Phase 6, /sprint-close, every K-th Stop hook (throttled),
        manual `memctl maintain`, optional user cron
```

No persistent process. No background service. No daemon.

Why no daemon:
- Cross-platform setup pain (Windows service / Linux systemd / macOS launchd)
- Resource overhead 24/7 even idle
- Per-project state — daemon doesn't know which cwd
- Memctl ops are bursty (when user actively works), not continuous
- User can opt-in to cron if they want strict periodic runs

Throttle: every K-th Stop hook (default K=10) triggers light cold ops:
- Decay weights
- Rebuild Layer 1 cache
- Update pressure metrics

Heavy cold ops (consolidation, semantic lint, synthesis) only on:
- Skill events (/retro, /qc-dream, /sprint-close)
- Manual `memctl maintain`
- User cron (optional)

---

## §10 Skill-agnostic role mapping (appendix)

Protocol body uses **roles**, not skill names. Skills declare which role they fulfill via front matter.

### Conceptual roles

| Role | Writes to | When |
|------|-----------|------|
| **orchestrator** | `tasks/`, `hall-task` | Task pipeline transitions |
| **retrospective** | `patterns/`, `hall-pattern` | Post-task analysis |
| **consolidator** | `lessons/`, `claude-memory/MEMORY.md`, `hall-lesson`, `hall-index` | Periodic consolidation |
| **decision-recorder** | `decisions/`, `hall-decision` | Architectural choice |
| **capturer** | `chats/`, `hall-chat` | Session-end auto |
| **distiller** | various subdirs, `confidence-tentative` | Auto post-Stop hook |
| **scratchpad-writer** | `*.md` root, user-tag | Manual save |
| **synthesizer** | `attachments/sources/`, entity/concept pages, `hall-source` | Source ingestion |
| **linter** | INDEX.md, LOG.md, lint reports | Maintenance |

### Current skill ↔ role mapping

| Skill | Roles fulfilled |
|-------|-----------------|
| `/sdlc` | orchestrator |
| `/retro` | retrospective |
| `/qc-dream` | consolidator + linter |
| `/design` | decision-recorder |
| Stop hook (`memctl capture`) | capturer + distiller (post-#35a) |
| `/memctl-save` | scratchpad-writer |
| `memctl ingest-source` (post-#35f) | synthesizer |
| `/build` | orchestrator (sub-pipeline) |
| `/autopilot` | orchestrator (multi-task) |

Future skills declare `memory-roles: [...]` in their front matter. Protocol references roles, never specific skill names. Adding new skill doesn't require protocol change.

---

## §11 Aliases (legacy → memctl translation)

For skill bodies not yet rewritten. **NEW code MUST use memctl directly — these are aliases for backward compat reading only.**

| Legacy filesystem path | memctl operation |
|------------------------|------------------|
| `.claude/memory/short/session_context.md` | `memctl search-tags "session,task-{id}"` |
| `.claude/memory/short/scratch.md` | `memctl search-tags "scratch,task-{id}"` |
| `.claude/memory/short/qc_feedback.md` | `memctl search-tags "qc-feedback,task-{id}"` |
| `.claude/memory/mid/qc_errors.md` | `memctl search-tags "hall-pattern,project-{name}"` |
| `.claude/memory/mid/qc_rules.md` | `memctl search-tags "hall-rule,project-{name}"` |
| `.claude/memory/mid/qc_scores.md` | `memctl search-tags "hall-score,project-{name}"` |
| `~/.claude/memory/long/golden_rules.md` | `memctl search-tags "hall-rule,confidence-golden"` |
| `~/.claude/memory/long/anti_patterns.md` | `memctl search-tags "hall-anti-pattern"` |
| `~/.claude/memory/long/insights.md` | `memctl search-tags "hall-lesson"` |
| `~/.claude/memory/long/dream_log.md` | Read `<vault>/LOG.md` (filtered to `consolidate|promote` entries) |
| `~/.claude/memory/long/user_preferences.md` | `memctl search-tags "hall-user-preference"` (or single identity note) |

### Operation translation

| Legacy operation | memctl equivalent |
|------------------|-------------------|
| Read `.claude/memory/short/session_context.md` | `memctl search-tags "session,task-{id}"` |
| Write to `.claude/memory/short/qc_feedback.md` | `memctl add "{feedback}" --tags "session,qc-feedback,task-{id}" --title "qc-feedback-task-{id}-attempt-{n}"` |
| Append to `.claude/memory/mid/qc_errors.md` | `memctl add "{error}" --tags "hall-pattern,project-{name},confidence-tentative" --title "pattern-{slug}"` |
| Promote rule errors→rules | `memctl add` with `--tags "hall-rule,project-{name},confidence-proven"` + delete original tentative |
| Append to `~/.claude/memory/long/dream_log.md` | Append to `<vault>/LOG.md` with `## [DATE] consolidate \| {summary}` |
| "Delete short-term files" | `memctl delete <id>` for noted ids OR retag with `archived` |

---

## §12 Enforcement

### Hard rules

1. **NEVER** `cat`, `Read`, `Write`, `Edit`, `Append`, `mkdir`, `sed` on filesystem paths under `.claude/memory/{short,mid,long}/`. Those are aliases — translate to memctl.
2. **ALWAYS** use `memctl` subcommands for memory ops.
3. Wiki content (decisions, patterns, lessons) — LLM owns, user reads. Don't manually edit unless reviewing tentative.
4. INDEX.md + LOG.md auto-maintained — DON'T hand-edit.
5. Source files in `attachments/sources/` — immutable raw, never modify.
6. Schema files (`docs/memctl.md`, `<repo>/backlog/wiki/memory-protocol.md`) — co-evolved with user, not LLM-only.
7. Vault path: `.memctl/` (V2.1). NEVER `.memctl-vault/` (legacy from skill canonical pre-V2.1).
8. Hot path operations <200ms. If hitting budget, push to WARM.
9. WARM async, never block next prompt.
10. COLD opportunistic, never daemon.

### Lint check (CI/local enforcement)

```bash
scripts/lint-memory-protocol.sh     # (post-#35d-implementation)
```

Scans repo + global skill bodies for filesystem direct access:

```bash
# Pseudo-implementation
violations=$(grep -rn "\.claude/memory/{short,mid,long}/" \
                    src/ tests/ scripts/ ~/.claude/skills/*/SKILL.md \
             | grep -v "alias\|legacy\|conceptual")
[ -z "$violations" ] || echo "FAIL: filesystem direct access found"
```

Run pre-commit (optional hook) and CI (eventually).

### Conflict resolution

If protocol conflicts với skill body or _memctl-backend.md → **protocol wins**. Update other doc to align.

If anh changes protocol direction → update this doc, then audit downstream:
- `~/.claude/skills/_memctl-backend.md`
- Skill bodies (sdlc, retro, qc-dream, etc.)
- `<repo>/docs/memctl.md` skill source
- Plugin SKILL.md (synced via script)

---

## §13 LLM dependency split — cheap vs LLM-required ops

Most maintenance runs **WITHOUT LLM** (cheap, deterministic, free). LLM only required for 2 specific ops, both opt-in.

| Operation | LLM? | Reason |
|-----------|------|--------|
| Distill signals (regex phrases like "we decided X because Y") | ❌ | Pattern match deterministic |
| Decay weights | ❌ | Math: `weight × 0.95^(days/30)` |
| Rebuild Layer 1 cache | ❌ | SQL re-query top-N |
| MMR diversity rerank | ❌ | Pure math (cosine sim) |
| INDEX.md auto-update | ❌ | Iterate notes, regenerate from frontmatter |
| LOG.md append | ❌ | Write entry |
| Promote pattern → rule (hit≥3) | ❌ | Move file + tag swap |
| Structural lint (orphans, broken links, exact dupes) | ❌ | Graph traversal + embedding similarity |
| Hit count refresh | ❌ | Counter increment |
| Pressure metrics update | ❌ | Counter |
| Confidence-based ranking | ❌ | Tag lookup |
| Supersession marker handling | ❌ | Frontmatter check |
| **Contradiction structural proxy** | ❌ | Same `entity` tag + conflicting frontmatter values (e.g., 2 notes with `entity: postgres` and `claim: chosen` vs `claim: rejected`) — flag mismatched assertions deterministically without LLM |
| **Concept gap structural** | ❌ | Entity mentioned 3+ in `[[wikilink]]` but no entity page exists → suggest creating |
| **Tentative note auto-process** | ❌ | After N=3 re-mentions of same fact → promote to `confidence-proven`. After 90d untouched → decay to `confidence-stale` then archive. |
| **SQLite vacuum** | ❌ | `PRAGMA vacuum;` quarterly when vault > 1000 notes |
| **Semantic lint (deeper contradictions)** | ✅ opt-in | Catches "claim A semantically contradicts B" beyond simple tag mismatch |
| **Source synthesis (10-15 page updates per source)** | ✅ required | Extract entities, decide which pages update, write summaries — only Mode B or external LLM |
| Concept gap → rich page draft | ✅ opt-in | LLM writes initial draft if requested |
| Disambiguation/merge nuanced | ✅ opt-in | Edge cases need LLM judgment |

### Default operating model (LLM always available)

Memory designed around bot in Claude Code session as primary actor. Per §-1, hooks and bot coordinate via inbox pattern.

**Default flow:**
- Hooks (Stop, UserPromptSubmit, SessionStart) handle Tier 1 (regex/math) + Tier 2 (embedding) directly via external `memctl` CLI calls
- Hooks write Tier 3 work-items to `<vault>/.obsidian/memctl/bot-todo.md`
- Context-inject (UserPromptSubmit) reads bot-todo, includes pending items in prompt
- Bot in next response performs Tier 3 work inline (alongside primary task), clears todo
- Token discipline: bot keeps maintenance terse, batched per K=5 turns, brief announcements
- $0 external cost — bot is the LLM

**Edge case — no bot session (CLI standalone):**
- Tier 3 work piles up in bot-todo
- Tier 1+2 keep vault functional (~80% value)
- Next session: bot processes accumulated inbox in first response
- No data loss; latency = inbox depth at session resume

**External LLM opt-in (rare):**
- Set `memctl config llm.url <url> ...` if anh want hook-side Tier 3 work without waiting for bot
- Calls cheap model (gemma 4B via VDG proxy, ~$0.05/100 notes)
- Useful for: long unattended periods (e.g., overnight cron), or shared vault scenarios

Default behavior: bot present, inbox-async pattern, $0.

**Self-lint mode (explicit, anywhere):**
- `memctl maintain --force lint --self` → memctl outputs all notes as structured prompt to stdout
- Bot/user reads, reasons, calls `memctl add` to save lint report
- Equivalent to Mode B but explicit invocation

**Opt-in external LLM (Mode A only, when CLAUDECODE not set):**
- `memctl ingest-source <url> --llm-url ... --llm-model ... --llm-key ...` (required for synthesis if not in session)
- `memctl maintain --force lint --semantic --llm-url ...`
- Or set defaults: `memctl config set llm.url ... --llm-model ... --llm-key ...`

### Cost summary

```
Mode A standalone, no LLM:
  All cheap maintenance free + offline + unlimited
  ~80% of value covered. LLM ops skipped.

Mode A standalone, opt-in external LLM:
  ~$0.05 per 100 notes (cheap model)
  Run on-demand or scheduled (user cron)
  100% of value, $$ cost

Mode B inside Claude Code:
  100% of value, $0 external cost
  Bot in session does LLM work for free
  Default behavior when bot uses memctl
```

---

## §14 Cost model summary

```
PER PROMPT (HOT):                    ~170ms     (model + SQLite + format)
PER RESPONSE (WARM, async):           <5s       (capture + distill + log + counters)
PER 10TH RESPONSE (COLD-light):      ~1-2s     (decay + cache rebuild)
PER TASK END (/retro):               ~30s      (consolidate + promote + lint)
PER SPRINT END (/qc-dream):          ~2-5min   (full consolidation + synthesis)
SOURCE INGEST (post-#35f):           ~30s-2min (LLM synthesis 10-15 pages)
MAINTAIN --quick (manual):           ~10-30s   (no LLM)
MAINTAIN --full (manual):            ~2-10min  (LLM lint + consolidation)

NO DAEMON:                            0 idle CPU/RAM
```

Bot effort:
- 0% required (auto-encode + auto-surface cover ~80% cases)
- 20% optional (active recall when bot needs deeper context)

User effort:
- Curate sources (drop into `attachments/sources/` or `memctl ingest-source <url>`)
- Ask questions (normal Claude Code use)
- Periodic `memctl maintain --full` when status shows recommended (or set auto)
- Review tentative notes queue periodically (low priority)

---

## §15 Empirical success metric

Protocol theoretical until validated with real long-term use. Track this metric to confirm "bot actually remembers":

**Recall hit rate** = of N times bot needed past context, how many were correctly surfaced (Layer 1+2+3) without explicit re-asking?

```bash
# After 1 month of use:
memctl maintain --check --report-recall

→ {
    "recall_attempts": 47,           # times bot invoked recall
    "recall_hits": 38,               # past context successfully surfaced
    "recall_misses": 9,              # bot had to re-derive or asked user
    "hit_rate": 0.81,                # 81% — good (target ≥ 0.7)
    "by_layer": {
      "layer_0_identity": "100% always available",
      "layer_1_top_weighted": "75% relevant",
      "layer_2_context_search": "85% relevant",
      "layer_3_explicit_search": "45% used (rare)"
    }
}
```

Targets:
- ≥ 0.7 hit rate after 6 months (first 6 months experimental — empirical data needed)
- < 0.5 hit rate: ranking broken, needs re-tune
- Layer 1 > 70% relevance: cache + scoring correct
- Layer 3 < 50% used: most needs covered by passive surface (good)

Anh check this metric quarterly. If bad → tune scoring weights, audit pressure thresholds, review tentative queue.

**Calibration honesty:** Day-in-life §17 numbers (0.84 hit rate at year 1) là projection, not measured. First 6 months of real use = experimental. Targets above are heuristic from MemPalace/Karpathy literature, adjusted for memctl's hybrid scoring; require empirical validation.

---

## §16 Failure modes + recovery

| Failure | Symptom | Recovery |
|---------|---------|----------|
| `index.db` corrupt | `memctl search` returns 0 always | `memctl ingest --vault X` rebuild from .md files |
| `pressure.json` stale/corrupt | maintain not auto-triggering | `memctl maintain --force quick` (rebuilds pressure) |
| Bot in Mode B skips maintenance | LLM ops never run, contradictions accumulate | User runs `/memctl-maintain` slash explicit; or `memctl maintain --force lint --self` outputs prompt for bot to consume |
| Vault size > 5000 notes, search slow | Latency creeps over 200ms hot budget | SQLite vacuum (auto in cold path quarterly when notes > 1000); archive `chats/` older than 1 year to `chats/archive/YYYY/` |
| Auto-distill missed nuanced insight | Bot's deep thought lost to chats/ noise | User explicit `/memctl-save`; or Mode B bot re-reads chats/ daily, re-extracts |
| Contradiction undetected in Mode A | Conflicting facts coexist in vault | Set `--llm-url` for periodic semantic lint OR run `memctl maintain --force lint --self` in Claude Code session monthly |
| Tentative note never confirmed/discarded | Queue grows | Auto-process: 3+ re-mentions → promote, 90d untouched → archive |
| `.memctl/` accidentally deleted | All vault gone | User backup responsibility — `git init` inside `.memctl/` for free version control if anh wants |
| Cache stale (Layer 1 not refreshed) | Top-15 surface outdated | Cold path rebuilds every K Stop hooks; or `memctl maintain --force quick` |
| Vault location ambiguous (multiple `.memctl/` in cwd path) | Wrong vault resolved | Walk-up takes nearest first (correct behavior); explicit `--vault <path>` overrides |
| Skill instruction not followed by bot | Mode B silent failure | Hook-driven shim catches cheap ops; LLM ops degrade to "skipped" notice — no silent corruption |

---

## §17 Day-in-life example (concrete flow)

### Day 1, 09:00 — Init project

```bash
cd ~/repos/my-new-project
memctl init --vault .       # creates ./.memctl/ V2.1 layout
echo ".memctl/" >> .gitignore
```

→ `.memctl/.obsidian/memctl/index.db` empty
→ `.memctl/INDEX.md` empty stub
→ `.memctl/LOG.md` empty
→ `.memctl/.obsidian/memctl/pressure.json` initialized to zeros

### Day 1, 10:30 — Discussion captured

Anh + bot discuss vault layout V2 vs V1.

→ Stop hook fires after each response
→ `memctl capture` writes turn to `chats/2026-05-01.md`
→ `memctl distill` (regex) catches "we chose `.memctl/` over `.memctl-vault/` because consistency with .git/ pattern"
→ Auto-creates `decisions/adr-0001-vault-layout.md` with `confidence-tentative` tag
→ `pressure.unconsolidated_turns_count = 1`
→ `LOG.md` appends: `## [2026-05-01 10:30] decision | adr-0001 vault layout (tentative)`

Bot doesn't think about saving. Memctl handled.

### Day 1, 14:00 — Anh referenced past decision

Anh: "what did we decide about vault layout?"

→ UserPromptSubmit hook fires
→ `memctl context-inject` reads pressure (~1ms), no maintenance breach
→ Layer 0 (identity) + Layer 1 (top-15 by weight) + Layer 2 (search "vault layout decision") injected
→ Bot prompt now starts với:
  ```
  ## Memory Context
  ### ADR-0001: Vault layout (tentative)
  Chose .memctl/ over .memctl-vault/ because consistency with .git/ pattern...
  ```
→ Bot reads, recalls, answers without re-asking anh

### Day 5, anh discussed similar topic 3 times → tentative ADR auto-promoted

→ Distill detects 3rd re-mention of "vault layout decision"
→ Auto-promote ADR confidence-tentative → confidence-proven
→ `LOG.md` appends: `## [2026-05-05] promote | adr-0001 tentative→proven (3 mentions)`

### Day 10, anh runs `memctl status`

→ Universal pressure shim runs (~1ms)
→ Pressure: `unconsolidated_turns_count=58` (> 50 threshold)
→ Spawn `memctl maintain --auto` detached
→ status command continues, returns instantly
→ Background: distill catchup, decay weights, rebuild Layer 1 cache, append LOG.md

### Day 30, vault has ~30 ADRs, ~50 patterns, ~5 lessons

→ Anh asks "what bug did we hit with auth?"
→ context-inject Layer 2 search "auth bug"
→ Pre-filter: cwd has `wing-my-project` → narrow to that wing
→ Top hit: `patterns/pat-auth-token-expiry.md` (hit_count=4, weight=1.5, confidence-proven)
→ MMR rerank: returns this + 2 different aspects (not 5 dupes of token-expiry)
→ Bot recalls past pattern, applies fix without re-deriving

### Day 90, anh launches Claude Code

→ SessionStart hook reads pressure
→ `days_since_full_maintain=8` (>7 threshold)
→ Hook outputs JSON with `data.maintenance.recommended: true, reason: "weekly heartbeat"`
→ Skill rule fires: bot reads recommendation, runs `memctl maintain` via Bash
→ Mode B detected (CLAUDECODE=1) → bot does inline:
  - Cheap ops (auto): decay weights, rebuild cache
  - Tentative review: 7 notes flagged → bot reads, promotes 4, archives 3 (no LLM external)
→ `LOG.md`: `## [2026-08-01 09:00] maintain-full | mode-B | promoted 4, archived 3, decayed 12`

### Day 365, vault has ~50 ADRs, 200 patterns, 30 lessons, 365 chats files

→ SQLite vacuum auto-runs in cold path (notes > 1000 threshold)
→ Old `chats/` files (> 1 year) auto-archive to `chats/archive/2026/`
→ Recall hit rate metric: anh runs `memctl maintain --check --report-recall`
  ```
  hit_rate: 0.84
  Layer 1 relevance: 78%
  Layer 2 relevance: 89%
  Layer 3 used: 38%
  ```
→ Above target 0.7 → protocol working empirically

### Day 1825 (5 years), vault > 5000 notes

→ Cold path runs structural lint quarterly
→ Concept gap detection: 3 entities mentioned 5+ times without dedicated page → suggest creating
→ Mode B bot reviews suggestions weekly, creates pages or dismisses
→ Compound expertise: bot answer in 2031 informed by decisions from 2026
→ Recall hit rate stable ~0.8

---

## §18 Archival — nhớ cần thiết, loại bỏ dư thừa

Vault grows over time. Without pruning, signal drowns in noise. Archival keeps signal active in default search; redundant moved to archive (still readable, still searchable on demand).

### Archive vs delete

| Action | When | Recoverable? |
|--------|------|--------------|
| **Archive** (default) | Auto via cold path | YES — move file to `archive/` subdir, exclude from default search |
| **Delete** (explicit) | User `memctl delete <id>` | NO — file removed from disk |

Default behavior: **archive, never delete.** Vault history preserved.

### Auto-archive triggers

Cold path checks each note quarterly (or when triggered manually `memctl maintain --force archive`):

| Criterion | Threshold | Action |
|-----------|-----------|--------|
| Weight decayed below floor | `weight × 0.95^(days_since_modified/30) < 0.1` | Archive |
| Superseded > 30d | `superseded_by` field set + 30 days passed | Archive |
| Tentative note untouched | `confidence-tentative` + 90 days no re-mention | Archive |
| Chats older than 1 year | `chats/YYYY-MM-DD.md` where YYYY < current_year - 1 | Move to `chats/archive/YYYY/` |
| Pattern merged into lesson | After consolidation, original pattern files → archive | Archive |
| Dupe merged | After dedup, kept primary, archived secondaries | Archive |

### Archive layout

```
<vault>/
├── archive/                      ← (NEW) excluded from default search
│   ├── decisions/                ← superseded ADRs
│   ├── patterns/                 ← merged into lessons
│   ├── lessons/                  ← stale wisdom
│   ├── chats/
│   │   └── YYYY/                 ← old daily rollups
│   └── <other-subdirs>/
└── ... (active subdirs)
```

Archived notes:
- Still parseable markdown
- Still in `index.db` (with `archived: true` flag)
- Excluded from default `memctl search` / `context-inject`
- Surface only if explicit: `memctl search --include-archive` or `memctl get <id>` direct

### Why archive (not delete)

1. **Audit trail** — anh + bot can review past decisions, even superseded ones, when researching "why did we change?"
2. **Recovery** — if archive triggered wrongly, user can `memctl unarchive <id>` to restore
3. **Long-term context** — bot in 2031 can still find 2026 decisions when explicitly searching history
4. **Storage cheap** — markdown + SQLite cheap; no need for aggressive deletion

### Anti-archive (auto-pin via natural conversation)

**Mode B default — bot detects via context, not regex.** Bot reads conversation, judges importance, pins accordingly. Mode A fallback uses regex signals.

**Bot/user không bao giờ type `memctl pin`.** Distill detects explicit signals from conversation turns and auto-pins:

| Signal phrase pattern | Action |
|----------------------|--------|
| "this is important", "remember this", "don't forget", "keep this" | weight ← 1.5 + tag `pinned` |
| "boost X", "pin X", "always surface X" | same |
| "we'll need this later", "critical decision" | same |
| User explicit slash: `/memctl-save <title> \| <content> #important` | same (hashtag triggers) |
| Pattern hit_count ≥ 3 | weight ← 1.5 (proven) |
| ADR explicitly referenced 5+ times across sessions | weight ← 2.0 (golden track) |

Distill (Tier 1+2 in §13) does this automatically post-Stop hook. **Zero user effort.**

Reverse signal (unpin):
| Signal phrase pattern | Action |
|----------------------|--------|
| "we don't need this anymore", "outdated", "scratch that", "ignore X" | weight ← 0.5, allow decay |
| Explicit supersession: "ADR-0042 supersedes ADR-0041" | older `superseded_by: [adr-0042]`, archived after 30d |

### What memctl NEVER auto-archives

- Notes with `weight >= 1.5` (auto-pinned via signals)
- Notes tagged `confidence-golden` or `pinned`
- Notes with active `[[wikilink]]` from non-archived notes (graph relevance preserved)
- `claude-memory/MEMORY.md` (top-level index always live)
- ADRs in `decisions/` (architectural choices preserved unless `superseded_by` explicitly set via signal)
- `LOG.md` (chronological audit always preserved)

### Archive size policy: never delete

Storage is cheap. Vault grows linearly với usage. **Memctl never deletes archived notes.** No "prune-archive" command. Old archives stay accessible via auto-include-archive when Layer 1+2 returns insufficient results.

### Auto-include-archive on recall miss

If hot path Layer 1+2 returns < N relevant results (default N=3), context-inject auto-extends Layer 2 to include archive scope. Bot still finds historical context when active vault doesn't suffice — no user command needed.

```
recall pipeline:
  Layer 1+2 search active scope
  if results < 3:
      extend Layer 2 to include archive
  inject merged
```

Threshold tunable via pressure config; default behavior covers compound expertise scenarios.

### Bot proactive announce + natural override (Mode B default)

Bot in session announces memory actions as part of conversation flow:

```
Bot: "Em note this as ADR-0042: V2.1 layout final" 
       (after anh + bot finalized layout decision)

Anh: "ok"  → confirms

—— OR ——

Anh: "no, don't ADR that, it's still draft"
       ↓
Bot detects override → reverses (delete or demote ADR)
```

```
Bot: "Em archived 3 stale chats from > 1yr ago"

Anh: "wait, the one about postgres still relevant"
       ↓
Bot: read LOG.md → identify the postgres-related archived → unarchive
```

```
Anh: "remember this auth flow pattern"
       ↓
Bot detects pin signal → memctl add with weight 1.5 + tag pinned
```

Anh sees activity transparently in conversation — `memctl status` query rarely needed. Override = natural language, no commands.

### Bot self-audit on SessionStart (Mode B default)

When bot enters new Claude Code session, SessionStart hook returns recent auto-actions:

```json
{
  "data": {
    "recent_auto_actions": [
      {"date": "2026-04-30 16:32", "action": "pinned", "id": "adr-0042", "reason": "signal: 'remember this'"},
      {"date": "2026-04-30 18:15", "action": "archived", "id": "chat-2025-04-30", "reason": "decay < 0.1"},
      {"date": "2026-05-01 09:00", "action": "promoted", "id": "pat-postgres-conn", "reason": "hit_count=3"}
    ]
  }
}
```

Skill rule: bot audits each, reverses suspicious. E.g.:

```
Bot reviews: "Em pinned 'kế hoạch picnic' from yesterday's chat"
   → Em judges: this is off-topic (project is memctl, not picnic)
   → Bot reverses: memctl unarchive + remove pin tag
   → Bot announces: "Em đã unpin kế hoạch picnic — không liên quan project"
```

Audit catches Mode B's own mistakes. No manual recovery needed from anh.

### Cost vs trade-offs

**Mode B trade-off (only one):** Token cost on bot session.
- Maintenance ops consume session tokens (~10% cap via batching + async)
- Reduces bot's available context for primary work slightly
- NOT $$ (no external API), NOT user effort, NOT memory blind spots

All other trade-offs (false positives, misses, bot reliability, recovery) **eliminated by bot judgment in-session.**

Mode A trade-offs (fallback): cheap ops lower fidelity. Acceptable when bot session absent.

---

## §19 Open questions / future

These don't block protocol — implementation choices, deferred to future tasks:

- Auto-classifier for distill: regex-only (cheap) vs cheap LLM call (better) vs hybrid (regex first, LLM only if signal phrase ambiguous)
- INDEX.md format: flat list vs hierarchical Wing/Room nesting
- Synthesizer LLM choice: local (gemma 4B via VDG proxy) vs cloud (Anthropic/OpenAI) — user-configurable
- Concept page schema: standardized template vs free-form
- Tentative note review UI: TUI vs Obsidian plugin vs CLI prompt
- Pressure metric weights: tunable per-project or fixed defaults

---

## §20 Implementation phases (for backlog #35 when written)

NOT a backlog item itself — pointer to future implementation work:

- **Phase A: Auto-distill** (Channel 1.5 from Stop hook) — biggest UX win
- **Phase B: Layer 1 cache + tiered hot path** — speed
- **Phase C: Decay + reinforcement** — natural forgetting/strengthening
- **Phase D: MMR diversity rerank + supersession + confidence** — quality
- **Phase E: Maintenance trigger detection (pressure metrics)** — UX
- **Phase F: Source ingestion synthesis** — Karpathy wiki maintenance
- **Phase G: INDEX.md + LOG.md auto-maintenance** — catalog discipline
- **Phase H: Lint extension (contradictions, gaps, cross-refs)** — wiki health
- **Phase I: Lint enforcement script (filesystem direct ban)** — protocol guard

Each phase is independent — ship incrementally.

---

## §21 References

- Karpathy llm-wiki gist (2026): https://gist.github.com/karpathy/442a6bf555914893e9891c11519de94f
- MemPalace (April 2026, Milla Jovovich + Ben Sigman): GitHub viral release
- Vannevar Bush "Memex" (1945): conceptual ancestor
- Memctl docs: `<repo>/docs/memctl.md` (skill), `<repo>/backlog/wiki/vault-layout.md` (V2.1 layout)
- Skill canonical (post-update): `~/.claude/skills/_memctl-backend.md`

---

## §22 Glossary

- **Vault** — `.memctl/` directory containing all memory for a project
- **Wing** — project-scoped vault (1 wing per vault by V2.1 design)
- **Room** — topic/domain inside wing (tag `room-X`)
- **Hall** — memory type label (decision, pattern, lesson, finding, etc.)
- **Drawer** — atomic note (.md file)
- **Layer 0/1/2/3** — token budget tiers for read (identity / top-15 / context-relevant / on-demand)
- **HOT/WARM/COLD** — compute tiers (per-prompt / per-response async / opportunistic)
- **Channel 1/2/3** — write paths (auto-capture / explicit / periodic)
- **Hit count** — access frequency (reinforcement)
- **Confidence** — proven / tentative / superseded / disputed
- **Pressure** — maintenance trigger metrics in `pressure.json`
- **Synthesizer** — bot role that integrates source into wiki pages (Karpathy)
- **Tunnel** — cross-vault wikilink (out of scope per anh — single-project)
