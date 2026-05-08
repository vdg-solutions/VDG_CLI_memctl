---
id: 50
type: task
title: Fix _memctl-backend.md vault path instruction — use project root not .memctl-vault suffix
status: Todo
priority: normal
created: 2026-05-08
updated: 2026-05-08
---

## Description

Fix _memctl-backend.md vault path instruction — use project root not .memctl-vault suffix

## Comments

**2026-05-08 12:33 user:** ## Description

`_memctl-backend.md` hướng dẫn AI dùng:
```bash
VAULT="$(git rev-parse --show-toplevel)/.memctl-vault"
memctl init --vault "$VAULT"
```

Vấn đề: `InitVaultStructure` nhận path không có tên `.memctl` → tạo `.memctl-vault/.memctl/` bên trong. Nhưng VaultLocator auto-detect chỉ scan `<dir>/.memctl/.obsidian/` — KHÔNG tìm `.memctl-vault/.memctl/` → vault không được detect khi gọi memctl mà không có `--vault`.

## Root cause

`InitVaultStructure` logic:
```csharp
var isDirect = Path.GetFileName(trimmed) == ".memctl";
var vaultRoot = isDirect ? trimmed : Path.Combine(trimmed, ".memctl");
```
- `--vault <root>` → tạo `<root>/.memctl/` ✓ auto-detected
- `--vault <root>/.memctl` → isDirect=true → tạo `<root>/.memctl/` ✓ auto-detected  
- `--vault <root>/.memctl-vault` → isDirect=false → tạo `<root>/.memctl-vault/.memctl/` ✗ NOT auto-detected

## Fix

Trong `_memctl-backend.md`, đổi first-run setup:

```bash
# BEFORE (wrong):
VAULT="$(git rev-parse --show-toplevel)/.memctl-vault"
memctl init --vault "$VAULT"

# AFTER (correct):
VAULT="$(git rev-parse --show-toplevel)"
memctl init --vault "$VAULT"
# vault root = <project_root>/.memctl/ (auto-created by InitVaultStructure)
```

Và đổi mọi `memctl <cmd> --vault "<repo_root>/.memctl-vault"` thành `memctl <cmd> --vault "<repo_root>/.memctl"`.

## Files

- `~/.claude/skills/_memctl-backend.md` — fix VAULT computation + tất cả --vault examples

## Acceptance Criteria

- AI init vault tại VDG_CleanCode → `VDG_CleanCode/.memctl/.obsidian/` tồn tại
- `memctl status` từ VDG_CleanCode (không có --vault) → vault_found: true
- Không còn path sai kiểu `reposWorkingVDG_CleanCode.memctl-vault`
