# Memory Protocol V1

Single source of truth for ALL memory operations in memctl-equipped projects. Skill-agnostic. Cost-bounded. Passive event-driven (no daemon).

This doc supersedes scattered references trong skill bodies + `_memctl-backend.md`. Conflicts → protocol wins.

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
└── *.md                               ← ad-hoc scratchpad
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

### Auto-trigger (opt-in, off by default)

```bash
memctl config set maintenance.auto-quick.threshold 50      # auto-run --quick at 50 turns
memctl config set maintenance.auto-full.day-of-week sunday # auto-run --full Sunday 00:00
memctl config set maintenance.auto-trigger off             # default: notify-only
```

Default = notify-only. User decides when to run. Auto opt-in for users wanting hands-off.

### Manual commands

```bash
memctl maintain --check         # report pressure metrics + recommendation, no action
memctl maintain --quick         # cheap ops: decay, cache rebuild, INDEX update
memctl maintain --lint          # structural + semantic lint
memctl maintain --full          # everything: consolidate + promote + lint + cache + synthesize
memctl maintain --review        # interactive: process tentative_notes queue
```

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

## §13 Cost model summary

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

## §14 Open questions / future

These don't block protocol — implementation choices, deferred to future tasks:

- Auto-classifier for distill: regex-only (cheap) vs cheap LLM call (better) vs hybrid (regex first, LLM only if signal phrase ambiguous)
- INDEX.md format: flat list vs hierarchical Wing/Room nesting
- Synthesizer LLM choice: local (gemma 4B via VDG proxy) vs cloud (Anthropic/OpenAI) — user-configurable
- Concept page schema: standardized template vs free-form
- Tentative note review UI: TUI vs Obsidian plugin vs CLI prompt
- Pressure metric weights: tunable per-project or fixed defaults

---

## §15 Implementation phases (for backlog #35 when written)

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

## §16 References

- Karpathy llm-wiki gist (2026): https://gist.github.com/karpathy/442a6bf555914893e9891c11519de94f
- MemPalace (April 2026, Milla Jovovich + Ben Sigman): GitHub viral release
- Vannevar Bush "Memex" (1945): conceptual ancestor
- Memctl docs: `<repo>/docs/memctl.md` (skill), `<repo>/backlog/wiki/vault-layout.md` (V2.1 layout)
- Skill canonical (post-update): `~/.claude/skills/_memctl-backend.md`

---

## §17 Glossary

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
