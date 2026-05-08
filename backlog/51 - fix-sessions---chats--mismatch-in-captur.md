---
id: 51
type: task
title: Fix sessions/ → chats/ mismatch in CaptureOperator
status: Done
priority: normal
created: 2026-05-08
updated: 2026-05-08
---

## Description

Fix sessions/ → chats/ mismatch in CaptureOperator

## Comments

**2026-05-08 13:46 user:** CaptureOperator.cs line 33 writes to sessions/{date}-{id}.md but vault init creates chats/ and Obsidian daily-notes.json points to chats/. One-line fix: change the path prefix from sessions/ to chats/.

**2026-05-08 17:25 user:** No-op — sessions/ → chats/ fix already applied in feat(36). CaptureOperator.cs:33 already correct.
