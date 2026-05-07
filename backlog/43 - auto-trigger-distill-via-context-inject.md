---
id: 43
type: task
title: Auto-trigger distill via context-inject threshold counter
status: In Progress
priority: normal
tags:
- memory,distill,hooks
created: 2026-05-07
updated: 2026-05-07
---

## Description

`memctl distill` hiện phải gọi thủ công. Anthropic's dreaming feature auto-trigger sau N sessions. Implement tương đương: memctl tự đếm conversations kể từ lần distill cuối, khi đạt threshold thì báo LLM qua `context-inject` output — LLM thấy recommendation và tự trigger distill.

Không cần hook mới, không cần scheduler. Piggyback hoàn toàn vào existing hook flow:
- `capture` (Stop hook) → increment counter
- `context-inject` (UserPromptSubmit hook) → check + emit recommendation
- `distill` → reset counter

## Design

### State file

`.obsidian/memctl/distill-state.json` (internal runtime, không phải vault note — không ingest):

```json
{
  "conversations_since_distill": 4,
  "last_distill_at": "2026-05-07T10:00:00Z",
  "threshold": 5
}
```

Default threshold: 5. Configurable via `memctl config set distill-threshold <n>`.

### Flow

```
Stop hook → memctl capture
  → write conversation note
  → increment distill-state.json: conversations_since_distill += 1

UserPromptSubmit → memctl context-inject
  → read distill-state.json
  → if conversations_since_distill >= threshold:
      append to injected context:
      "## Distill Recommendation\n{n}/{threshold} conversations since last distill. Run `memctl distill` to consolidate L1 → L2 memory."
  → LLM sees this → triggers distill at start of next response

memctl distill (on success)
  → reset: conversations_since_distill = 0
  → update: last_distill_at = now
```

### context-inject output format

Append sau `## Memory Context` block (chỉ khi threshold met):

```
## Distill Recommendation
5/5 conversations since last distill (last: 2026-05-06T08:00:00Z).
Run `memctl distill` to consolidate L1 → L2 memory.
```

LLM tự quyết định có trigger hay không — không force, không block.

## Implementation

**`src/memctl/Operators/DistillStateStore.cs`** (NEW — `Operators/` layer, same as `EventLog.cs`)

```csharp
internal static class DistillStateStore
{
    internal static void Increment(string vaultPath) { ... }   // read JSON → +1 → write; best-effort (swallow all exceptions)
    internal static void Reset(string vaultPath) { ... }       // conversations_since_distill=0, last_distill_at=now; best-effort
    internal static bool ShouldRecommend(string vaultPath) { ... }  // return count >= threshold; returns false on any error
    internal static (int count, int threshold, DateTime? lastAt) GetState(string vaultPath) { ... }
}
```

- State file: `{vaultPath}/.obsidian/memctl/distill-state.json`
- Default threshold: 5 (used when file missing or key absent)
- **Best-effort: ALL methods catch-and-swallow exceptions** — same pattern as `EventLog.Record`. DistillStateStore MUST NOT crash CaptureOperator or ContextInjectOperator. Missing file = start from zero, not an error.
- File conflict: write to temp file + `File.Move(..., overwrite: true)` — atomic on Windows/Linux, avoids partial write corruption.

**`src/memctl/Operators/CaptureOperator.cs`**
- `CreateNote` only: `DistillStateStore.Increment(vaultPath)` sau `EventLog.Record(...)`.
- `AppendNote`: NO increment — appending to existing conversation is NOT a new conversation.

**`src/memctl/Operators/ContextInjectOperator.cs`**
- Sau khi `FormatContext(results)` returns: check `DistillStateStore.ShouldRecommend(vaultPath)`.
- If true: append `"\n## Distill Recommendation\n{count}/{threshold} conversations since last distill (last: {lastAt}).\nRun \`memctl distill\` to consolidate L1 → L2 memory.\n"` to the returned string.
- If `Execute` returns null (no vault notes): return recommendation-only string instead of null — LLM still sees it.

**`src/memctl/Operators/DistillOperator.cs`**
- Sau khi `ExecuteAsync` completes (non-dry-run, at least 1 conversation processed): `DistillStateStore.Reset(vaultPath)`

**`src/memctl/Bootstrap/Program.cs`**
- `memctl config set distill-threshold <n>` — narrow command, NOT a general config mechanism. Reads distill-state.json, updates only `threshold` key, writes back. Validates n > 0.

## Acceptance criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | `distill-state.json` tạo tự động lần đầu capture chạy | capture → file exists |
| AC-2 | Counter chỉ increment ở `CreateNote`, không increment ở `AppendNote` | capture same conversation ID twice → counter = 1, not 2 |
| AC-3 | `context-inject` append recommendation khi counter >= threshold | threshold=2, 2 new conversations → output có "Distill Recommendation" |
| AC-4 | `context-inject` KHÔNG append nếu counter < threshold | counter=1, threshold=5 → không có recommendation |
| AC-5 | `context-inject` trả về recommendation ngay cả khi vault rỗng (không có notes) | empty vault, threshold met → output có recommendation |
| AC-6 | `memctl distill` reset counter về 0 sau khi thành công | distill xong → counter = 0 |
| AC-7 | `memctl distill --dry-run` KHÔNG reset counter | dry-run xong → counter unchanged |
| AC-8 | `memctl config set distill-threshold 10` thay đổi threshold | set 10 → recommendation chỉ xuất hiện ở count=10 |
| AC-9 | DistillStateStore errors không crash CaptureOperator | inject IO error vào Increment → capture vẫn succeed, note vẫn written |
| AC-10 | File corrupt/invalid JSON → fallback về default state (count=0, threshold=5) | corrupt JSON → ShouldRecommend returns false, no crash |

## Files

- `src/memctl/Operators/DistillStateStore.cs` (new — same layer as `EventLog.cs`, same static utility pattern)
- `src/memctl/Operators/CaptureOperator.cs`
- `src/memctl/Operators/ContextInjectOperator.cs`
- `src/memctl/Operators/DistillOperator.cs`
- `src/memctl/Bootstrap/Program.cs`
- `tests/memctl.Tests/Operators/DistillStateStoreTests.cs` (new)

## Effort

~2.5h: DistillStateStore (1h) + wire into 3 operators (0.75h) + config command (0.25h) + tests (0.5h)