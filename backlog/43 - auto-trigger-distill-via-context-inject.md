---
id: 43
type: task
title: Auto-trigger distill via context-inject threshold counter
status: Todo
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

**`src/memctl/Implementations/Vault/DistillStateStore.cs`** (NEW)
- `Increment(vaultPath)`: read JSON, +1, write back
- `Reset(vaultPath)`: set conversations_since_distill=0, last_distill_at=now
- `ShouldRecommend(vaultPath)`: return conversations_since_distill >= threshold
- `GetState(vaultPath)`: return full state for context-inject message
- State file path: `{vaultPath}/.obsidian/memctl/distill-state.json`
- Thread-safe: retry-on-conflict (capture có thể concurrent)
- Default threshold: 5 nếu file không tồn tại

**`src/memctl/Operators/CaptureOperator.cs`**
- Sau khi write conversation note: `DistillStateStore.Increment(vaultPath)`

**`src/memctl/Operators/ContextInjectOperator.cs`**
- Sau khi build Memory Context block: check `DistillStateStore.ShouldRecommend(vaultPath)` → append recommendation

**`src/memctl/Operators/DistillOperator.cs`**
- Sau khi ExecuteAsync hoàn thành (non-dry-run): `DistillStateStore.Reset(vaultPath)`

**`src/memctl/Bootstrap/Program.cs`**
- `memctl config set distill-threshold <n>` → write threshold vào distill-state.json

## Acceptance criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | `distill-state.json` được tạo tự động lần đầu capture chạy | run capture → file exists |
| AC-2 | `conversations_since_distill` increment sau mỗi lần capture thành công | capture 3 lần → counter = 3 |
| AC-3 | `context-inject` append recommendation khi counter >= threshold | threshold=2, capture 2 lần → output có "Distill Recommendation" |
| AC-4 | `context-inject` KHÔNG append nếu counter < threshold | counter=1, threshold=5 → không có recommendation |
| AC-5 | `memctl distill` reset counter về 0 sau khi thành công | distill xong → counter = 0 |
| AC-6 | `memctl distill --dry-run` KHÔNG reset counter | dry-run xong → counter unchanged |
| AC-7 | `memctl config set distill-threshold 10` thay đổi threshold | set 10 → recommendation chỉ ở count=10 |
| AC-8 | State file không bị ingest (không xuất hiện trong `memctl list`) | ingest xong → distill-state.json không trong index |

## Files

- `src/memctl/Implementations/Vault/DistillStateStore.cs` (new)
- `src/memctl/Operators/CaptureOperator.cs`
- `src/memctl/Operators/ContextInjectOperator.cs`
- `src/memctl/Operators/DistillOperator.cs`
- `src/memctl/Bootstrap/Program.cs`
- `tests/memctl.Tests/Operators/DistillStateStoreTests.cs` (new)

## Effort

~2.5h: DistillStateStore (1h) + wire into 3 operators (0.75h) + config command (0.25h) + tests (0.5h)