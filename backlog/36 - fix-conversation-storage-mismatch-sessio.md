---
id: 36
type: task
title: 'Fix conversation storage mismatch: sessions/ → chats/'
status: Done
priority: high
parent: 38
tags:
- storage,capture,vault
created: 2026-05-07
updated: 2026-05-07
---

## Description

`CaptureOperator` writes conversation turns to `sessions/{date}-{id}.md` but vault init creates `chats/` and Obsidian `daily-notes.json` points to `chats/`. These must align.

## Changes

- `CaptureOperator.cs:33` — change `sessions/` → `chats/`
- `ObsidianVaultReader.InitVaultStructure` — add `sessions/` removal or confirm `chats/` is the canonical name
- Verify `daily-notes.json` folder matches

## Acceptance criteria

- `memctl capture` writes to `chats/{date}-{id}.md`
- Obsidian Daily Notes opens files from `chats/`
- Vault init does not create a `sessions/` folder