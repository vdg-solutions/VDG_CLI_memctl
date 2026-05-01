---
id: 17
type: task
title: Hook observability — log capture/context-inject failures cho Claude Code debug được
status: Done
priority: normal
tags:
- hooks
- observability
- plugin-ux
- claude-code
created: 2026-04-30
updated: 2026-04-30
---

## Description

NFR-002 hiện tại: hook (capture, context-inject) **never crash** — luôn exit 0 dù internal fail. Lý do: Claude Code stop hook fail = block user → bad UX. Trade-off: silent failure.

Hệ quả: user nói "tôi đã chat 20 turn, sao memory trống?" → Claude Code không biết hook fail vì sao (vault không tồn tại? embedding model thiếu? disk full? JSON parse fail?). Memory mất silent.

## Mục tiêu

- Giữ NFR-002 (hook never crash).
- Thêm cơ chế debug được khi memory missing.

## Phương án

**A. Persistent error log**

Hook fail ghi vào `<vault>/.memctl/hook.log`:
```
2026-04-30T10:23:11Z capture failed: vault not found at /tmp/cwd
2026-04-30T10:25:47Z capture failed: embedding model not initialized
2026-04-30T10:30:12Z context-inject failed: stdin parse error
```

Claude Code (hoặc user) đọc log khi nghi ngờ memory thiếu.

**B. CLI command `memctl hook-status`**

Trả tóm tắt N lần hook gần nhất: success/fail count, last error, vault state. Claude Code chủ động gọi khi khởi tạo session.

**C. Severity levels**

Phân biệt:
- **Soft fail** (vault missing, embedding chưa init) → exit 0 + log → không block.
- **Hard fail** (corrupted db, disk full) → exit 0 + log + flag in vault `.memctl/hook-broken` → Claude Code phát hiện flag và surface tới user.

## Implementation

**File modify:**
- `src/memctl/Operators/CaptureOperator.cs` — wrap try/catch, ghi error vào `<vault>/.memctl/hook.log`.
- `src/memctl/Operators/ContextInjectOperator.cs` — same.
- `src/memctl/Bootstrap/Program.cs` — capture/context-inject command, đảm bảo log path resolved cho cả vault không tồn tại.

**File create:**
- `src/memctl/Operators/HookStatusOperator.cs` — đọc log, tổng hợp status.
- Add CLI command `memctl hook-status` + MCP tool `hook_status`.

## Acceptance Criteria

| ID | Criterion | Verify |
|---|---|---|
| FR-1 | Hook capture/context-inject fail ghi 1 dòng log vào `<vault>/.memctl/hook.log` với timestamp ISO, command, error message | Trigger fail (e.g. corrupted vault) → check log file |
| FR-2 | Hook vẫn exit 0 dù fail (NFR-002 preserved) | Run hook với input invalid, assert exit code = 0 |
| FR-3 | `memctl hook-status` trả tóm tắt last 10 hook runs (success count, fail count, last error) | Run after triggering fails, parse JSON |
| FR-4 | MCP tool `hook_status` expose tương tự | MCP call, parse response |
| FR-5 | Log không grow unbounded — rotate khi >1MB hoặc keep last 1000 lines | Stress test 10000 hook calls, check log size |
| NFR-1 | Hook overhead với log <50ms | Benchmark |
| NFR-2 | Build pass | `dotnet build` |

## Out of Scope

- Live monitoring dashboard.
- Push notification khi hook broken.

## Dependencies

- Không depend task khác.

## Effort

~3-4h.

## Comments

**2026-04-30 09:17 user:** Phase 6: merged. Hook log + status command + MCP tool.
