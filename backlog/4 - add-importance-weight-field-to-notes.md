---
id: 4
type: task
title: Add importance weight field to notes
status: Done
priority: high
created: 2026-04-17
updated: 2026-04-17
---

## Description

Add importance weight field to notes

## Comments

**2026-04-17 07:13 user:** Add optional weight/importance field to Note entity and SQLite schema. Weight is user-assignable (via memctl weight <id> <value>) or derived from access frequency. Used to prioritize notes when loading context into AI agents (tiered loading concept).
