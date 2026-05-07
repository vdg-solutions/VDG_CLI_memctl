---
id: 39
type: task
title: memctl distill — LLM-driven conversation-to-memory extraction
status: Done
priority: high
parent: 38
tags:
- memory,distillation,llm
created: 2026-05-07
updated: 2026-05-07
---

## Description

Bot xử lý conversation như con người xử lý ký ức: không lưu lại mọi thứ đã xảy ra, mà chắt lọc ra điều đáng nhớ. `memctl distill` là bước chuyển đổi từ raw transcript (Layer 1) thành long-term memory (Layer 2), với LLM là người quyết định cái gì có giá trị.

### 3-layer memory model

```
Layer 1 — Raw      chats/{date}-{id}.md            transcript thô, decay bình thường
Layer 2 — Distilled decisions/ patterns/ lessons/  ký ức thật sự, weight ≥ 1.0
Layer 3 — Linked   wikilinks giữa Layer 2 notes    memory graph, traverse via search-links
```

Layer 3 không phải storage mới — kết quả emergent của Layer 2 notes có wikilinks chất lượng.

## Command

```bash
memctl distill                               # distill tất cả conversations chưa distill
memctl distill --conversation <id-or-path>  # distill một conversation cụ thể
memctl distill --dry-run                    # xem LLM sẽ extract gì, không ghi disk
memctl distill --since 2026-05-01           # chỉ distill conversations sau ngày này
```

Requires `--llm-url` + `--llm-model` (hoặc set qua config). No `--llm-*` → error with clear message.

## LLM contract

`ILlmClient` cần method mới:

```csharp
Task<DistillResult> DistillAsync(
    string conversationContent,
    IReadOnlyList<Note> existingNotes,
    CancellationToken ct = default);
```

**Input to LLM:**
1. Conversation content — truncated to **16,000 characters** (no tokenizer in memctl; at ~4 chars/token this ≈ 4,000 tokens)
2. Existing note titles + tags (top 50 by weight, one line each) — LLM links into existing notes
3. System prompt: extract only high-signal items, write in 3rd-person declarative, no fluff

**LLM output — structured JSON** (`json_object` response format, same as `EnrichAsync`):

```json
{
  "extractions": [
    {
      "type": "decision|pattern|lesson",
      "title": "Never mock the database in integration tests",
      "content": "Full markdown content...",
      "tags": ["testing", "database", "integration"],
      "links": ["IngestOperator", "SqliteNoteIndex"],
      "weight": 1.2,
      "rationale": "why this is worth remembering"
    }
  ]
}
```

**Weight clamping**: LLM-returned weight clamped to `[1.0, 1.5]`. Any value < 1.0 → set to 1.0. Any value > 1.5 → set to 1.5. This prevents arbitrary LLM inflation.

**Link validation**: after LLM returns links, filter out any link targets not found in current vault index (prevents hallucinated wikilinks to non-existent notes).

**Folder mapping** by type:
- `decision` → `decisions/`
- `pattern` → `patterns/`
- `lesson` → `lessons/`

## Distilled flag write-back

Conversation note after distill is marked via new `MarkAsDistilled` method (see impl Step 3):

```yaml
distilled: true
distilled_at: 2026-05-07T10:00:00Z
distilled_notes: ["decisions/auth-approach.md", "patterns/retry-on-timeout.md"]
```

`DistillOperator` skips notes with `distilled: true` in frontmatter (idempotent). The flag must be in the file (not just the index) so it survives `memctl ingest` rebuilds.

**Problem**: `ObsidianVaultReader.UpdateFrontmatter` only handles tags/links. Need a new method.
**Solution**: Add `MarkAsDistilled(string absolutePath, DateTime distilledAt, string[] distilledNoteRelPaths)` to `IVaultReader` — reads file, injects/replaces the three fields in frontmatter, writes back. Pattern: same as `UpdateFrontmatter` but appends `distilled`, `distilled_at`, `distilled_notes` fields.

## Implementation steps

### Step 1 — Entities

**CREATE** `src/memctl/CoreAbstractions/Entities/DistillResult.cs`:

```csharp
internal sealed record DistilledNote(
    string   Type,
    string   Title,
    string   Content,
    string[] Tags,
    string[] Links,
    float    Weight,
    string   Rationale);

internal sealed record DistillResult(DistilledNote[] Extractions);
```

### Step 2 — ILlmClient

**MODIFY** `src/memctl/CoreAbstractions/Ports/ILlmClient.cs` — add:
```csharp
Task<DistillResult> DistillAsync(
    string conversationContent,
    IReadOnlyList<Note> existingNotes,
    CancellationToken ct = default);
```

### Step 3 — IVaultReader + ObsidianVaultReader

**MODIFY** `src/memctl/CoreAbstractions/Ports/IVaultReader.cs` — add:
```csharp
void MarkAsDistilled(string absolutePath, DateTime distilledAt, string[] distilledNoteRelPaths);
```

**MODIFY** `src/memctl/Implementations/Vault/ObsidianVaultReader.cs` — implement `MarkAsDistilled`:
- Read full file text
- Find frontmatter block (reuse `FrontmatterPattern`)
- Remove existing `distilled`/`distilled_at`/`distilled_notes` lines if present (idempotent)
- Append the three fields before closing `---`
- Write back

### Step 4 — OpenAiLlmClient

**MODIFY** `src/memctl/Implementations/Llm/OpenAiLlmClient.cs` — implement `DistillAsync`:
- Build system prompt with extraction instructions
- Truncate conversation to `MaxDistillInputChars = 16_000`
- Format existing notes context: top 50 by weight, one line per note: `{title} [{tags}]`
- Call LLM with `json_object` response format (same pattern as `EnrichAsync`)
- Deserialize to `DistillResult`
- Clamp weights: `Math.Clamp(note.Weight, 1.0f, 1.5f)`
- Filter links: remove any link not found via `index.SearchBm25(link, 1).Count == 0`

### Step 5 — DistillOperator

**CREATE** `src/memctl/Operators/DistillOperator.cs`:

```
Execute(vaultPath, conversationId?, since?, dryRun):
  1. index.Initialize(DbPath(vaultPath))
  2. Load candidate notes:
     - If conversationId specified: load that one note from chats/
     - Else: index.GetAll() filtered to FilePath.StartsWith("chats/")
  3. Filter out distilled: parse frontmatter from disk for each candidate
     - If distilled: true → skip
  4. Filter by --since: parse date from filename "chats/{date}-{id}.md"
     - date prefix is first 10 chars of filename after "chats/"
  5. For each remaining note:
     - Get top-50 notes by weight from index (exclude chats/)
     - Call llmClient.DistillAsync(note.Content, top50)
     - If dry-run: print extractions, continue
     - Write each extraction as vault note (correct folder by type)
     - index.Upsert each extracted note
     - vaultReader.MarkAsDistilled(absPath, DateTime.UtcNow, [relPaths])
     - index.Upsert updated conversation note
  6. Report: N conversations processed, M notes extracted
```

### Step 6 — Program.cs

**MODIFY** `src/memctl/Bootstrap/Program.cs` — add `memctl distill` command:
- Options: `--conversation <id>`, `--dry-run`, `--since <date>`
- Wire: `new DistillOperator(vaultReader, index, llmClient).Execute(...)`
- Require `--llm-url` + `--llm-model`; show error if missing

## Tests

**CREATE** `tests/memctl.Tests/Operators/DistillOperatorTests.cs`:

1. `Distill_SkipsAlreadyDistilledConversations`
   - Create fake conversation note with `distilled: true` in frontmatter
   - Run `DistillOperator.Execute`
   - Verify `ILlmClient.DistillAsync` never called (mock verifies 0 calls)

2. `Distill_WeightClampedToMaxBound`
   - Mock `ILlmClient.DistillAsync` returns extraction with `weight: 3.0`
   - Run operator
   - Read written note from disk → verify weight stored ≤ 1.5

3. `Distill_DryRunWritesNoFiles`
   - Run with `dryRun: true`
   - Verify no new files created in vault directory
   - Verify no calls to `vaultReader.MarkAsDistilled`

## Acceptance criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| FR-1 | `memctl distill` reads all non-distilled `chats/` notes | run distill → each chats/ note processed once |
| FR-2 | LLM extracts 0-N items per conversation | mock LLM returning 0 → no notes written |
| FR-3 | Each extraction written to correct folder by type | decision → `decisions/`, pattern → `patterns/`, lesson → `lessons/` |
| FR-4 | Extracted notes have weight ≥ 1.0 | inspect written note weight field |
| FR-5 | LLM weight clamped to [1.0, 1.5] | LLM returns 3.0 → stored as 1.5 |
| FR-6 | Conversation note marked `distilled: true` in file | read file after distill → frontmatter has distilled: true |
| FR-7 | `distilled: true` survives `memctl ingest` rebuild | distill → ingest → distill again → note skipped |
| FR-8 | `--dry-run` prints plan, writes nothing | no files changed after dry-run |
| FR-9 | `--since` filters by conversation date | conversations before date skipped |
| FR-10 | Idempotent: re-running skips distilled conversations | run twice → second run processes 0 notes |
| NFR-1 | Hallucinated wikilinks filtered out | LLM returns link to non-existent note → link dropped |
| NFR-2 | Input capped at 16,000 chars | long conversation truncated without error |
| NFR-3 | No `--llm-*` → clear error message | missing llm config → descriptive error, exit 1 |

## Files

- CREATE: `src/memctl/CoreAbstractions/Entities/DistillResult.cs`
- MODIFY: `src/memctl/CoreAbstractions/Ports/ILlmClient.cs`
- MODIFY: `src/memctl/CoreAbstractions/Ports/IVaultReader.cs`
- MODIFY: `src/memctl/Implementations/Vault/ObsidianVaultReader.cs`
- MODIFY: `src/memctl/Implementations/Llm/OpenAiLlmClient.cs`
- CREATE: `src/memctl/Operators/DistillOperator.cs`
- MODIFY: `src/memctl/Bootstrap/Program.cs`
- CREATE: `tests/memctl.Tests/Operators/DistillOperatorTests.cs`

## Risks

| Risk | Mitigation |
|------|-----------|
| LLM hallucinating wikilinks to non-existent notes | Validate links against index; drop unknown links |
| Large conversations exceed char cap → truncated context | 16,000 char cap with warning; full transcript still on disk |
| `UpdateFrontmatter`-like file rewrite races with concurrent write | `distill` is single-threaded; no concurrent vault writes |
| LLM assigns weight > 1.5 → inflated memory graph | Clamp enforced in operator, not in LLM client |
| `--since` date parse fails on non-standard filenames | Parse by filename prefix; fall back to `Note.Created` if parse fails |

## Effort

~5h: entities + ILlmClient (0.5h) + IVaultReader/MarkAsDistilled (1h) + OpenAiLlmClient impl (1h) + DistillOperator (1.5h) + Program.cs wire (0.25h) + tests (0.75h)

## Comments

**2026-05-07 10:34 user:** Pipeline complete. Merged to main. 70/70 tests passing.
