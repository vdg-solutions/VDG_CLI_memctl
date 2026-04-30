---
id: 19
type: task
title: Tag namespace migration — dọn legacy tag (telegram, chat-*) trong vault data
status: In Progress
priority: low
tags:
- migration
- cleanup
- vault-data
- one-time
created: 2026-04-30
updated: 2026-04-30
---

## Description

`AddTurnOperator` (đã xóa) hardcode tag `telegram` + `chat-<id>` + `user-<id>` cho mỗi conversation log. User vault có thể đã chứa hàng trăm note với tag này — Claude Code list tags ra → confuse "tại sao có telegram?".

`add-turn` đã bị xóa (task ngày 2026-04-30) nên không thêm legacy tag mới. Vẫn cần script migration cho vault hiện hữu của user.

## Mục tiêu

One-time cleanup: thay legacy tag thành neutral tag, hoặc xóa.

## Phương án

**A. Replace mapping (preferred)**
- `telegram` → `conversation`
- `chat-<id>` → `thread-<id>`
- `user-<id>` → giữ hoặc xóa (tùy user)

**B. Remove all legacy tags**
- Xóa `telegram`, `chat-*`, `user-*` khỏi mọi note.

**C. Hỏi user interactive**
- CLI command list legacy tag count, hỏi user replace hay remove.

## Implementation

**File create:** `src/memctl/Operators/MigrateTagsOperator.cs`

```bash
memctl migrate-tags --dry-run                    # show preview
memctl migrate-tags --replace telegram=conversation,chat-=thread-
memctl migrate-tags --remove telegram,user-
memctl migrate-tags --interactive                # ask per tag
```

**File modify:** `src/memctl/Bootstrap/Program.cs` — register `migrate-tags` subcommand.

**Algorithm:**
1. Scan all notes in vault (use `IVaultReader.ListNotes`).
2. Per note: parse frontmatter tags.
3. Apply mapping (replace/remove).
4. Re-write frontmatter.
5. Re-index (re-embed if tag affects content scoring).
6. Print summary: N notes modified, M tags replaced, K tags removed.

## Acceptance Criteria

| ID | Criterion | Verify |
|---|---|---|
| FR-1 | `--dry-run` print preview (note count, tag changes) without modifying files | Run with dry-run flag, diff vault → no changes |
| FR-2 | `--replace old=new` thay tag trong frontmatter, giữ thứ tự tag khác | Setup test note, run, parse YAML |
| FR-3 | `--remove tag1,tag2` xóa tag khỏi frontmatter | Setup test note, run, parse YAML |
| FR-4 | `--interactive` prompt user per unique legacy tag | Test trong terminal |
| FR-5 | Re-index sau migration (tags ảnh hưởng search-tags) | Run, then `memctl search-tags <new-tag>` trả note |
| NFR-1 | Backup option: `--backup` tạo `vault.bak/` trước khi modify | Test |
| NFR-2 | Idempotent: run 2 lần không gây duplicate | Run twice, diff result |
| NFR-3 | Build pass | `dotnet build` |

## Out of Scope

- Migrate other field besides tag.
- Schedule migration (one-time only).
- Cross-vault migration.

## Dependencies

- Không depend task khác.

## Effort

~3-4h.