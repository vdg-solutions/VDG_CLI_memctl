---
id: 11
type: task
title: 'G1: Auto-capture — memctl capture + Claude Code Stop hook'
status: Done
priority: high
created: 2026-04-19
updated: 2026-04-19
---

## Description

Bot phải chủ động gọi `create`/`append` để lưu memory — nó thường quên. Auto-capture loại bỏ hoàn toàn dependency này bằng cách dùng Claude Code `Stop` hook: sau mỗi response, hook tự động capture conversation turn vào vault.

`add-turn` hiện tại là Telegram-specific (bắt buộc `--chat-id`, `--from`, `--role`). G1 cần command mới: `memctl capture`.

## Dependencies

- `VaultWriteOperator.cs` đã có (task #7) — dùng `ExecuteCreate` + `ExecuteAppend`
- `GemmaEmbeddingEngine` đã có — inject vào `CaptureOperator`
- G5 nên implement trước nếu có thể (để weight=0.5 được decay đúng cách), nhưng G1 có thể ship độc lập

## Implementation

New command `memctl capture`:

**Files to create/modify:**
- NEW: `src/memctl/Operators/CaptureOperator.cs`
- MODIFY: `src/memctl/Bootstrap/Program.cs` — register `capture` subcommand

**Algorithm:**
1. Check: `--role`/`--text` flags present → direct mode (không đọc stdin JSON)
2. Else: đọc stdin JSON → parse Hook Protocol v1 payload: `session_id`, `cwd`, `transcript`
3. Nếu `cwd` absent: fallback về `Directory.GetCurrentDirectory()`
4. Vault auto-detect từ `cwd` (VaultLocator đã có)
5. Nếu vault không tồn tại: exit 0, silent
6. Filter turns: skip < 50 chars, skip tool-call-only turns (turns không có human-readable text)
7. Format content: `## Turn {timestamp}\n**{role}:** {content}\n`
8. File path: `sessions/{date}-{session_id}.md`
9. Nếu file chưa tồn tại: `VaultWriteOperator.ExecuteCreate` với `weight=0.5`
10. Nếu file đã tồn tại: `VaultWriteOperator.ExecuteAppend` (preserve existing weight)
11. Exit 0 luôn luôn

Hook config (user docs):
```json
// ~/.claude/settings.json
{
  "hooks": {
    "Stop": [{ "hooks": [{ "type": "command", "command": "memctl capture" }] }]
  }
}
```

## Acceptance Criteria

- `memctl capture` đọc Hook Protocol v1 `after-response` payload từ stdin (JSON): `{ session_id, cwd, transcript: [{role, content}] }`
- Extra fields trong payload bị ignore (forward-compatible với richer payloads như Claude Code Stop hook)
- Thiếu field `cwd`: fallback về process cwd để auto-detect vault
- Tự động tạo `sessions/<date>-<session_id>.md` nếu chưa có
- Append turn vào session note nếu file đã tồn tại
- Filter: skip turns rỗng hoặc < 50 chars sau khi strip whitespace
- Filter: skip turns chỉ chứa tool call results (không có human text)
- Session note được tạo với initial weight = 0.5 (không phải default 0.0)
- Re-capture vào note đã tồn tại: KHÔNG overwrite weight hiện tại (append-only, preserve weight)
- Session note re-indexed sau mỗi write (single-note upsert)
- Exit 0 khi vault không tồn tại — silent, không in error
- Exit 0 kể cả khi stdin không phải JSON hợp lệ — degrade gracefully
- `memctl capture --dry-run` → print what would be saved, no write
- Docs: ví dụ Stop hook config trong docs/memctl.md
- `memctl capture --role <user|assistant> --text "<content>"` → direct input mode (no stdin JSON required)
- Direct mode: tạo cùng session note structure như hook mode, dùng current date + random suffix cho session_id
- Direct mode: enables shell wrapper integration cho non-Claude Code clients
- Exit 0 trong mọi direct-mode invocation kể cả khi vault missing
