---
id: 8
type: task
title: Vault auto-detect and skill rename
status: In Progress
priority: high
created: 2026-04-17
updated: 2026-04-17
---

## Description

Two improvements:

1. **Vault auto-detect**: When `--vault` is omitted, walk up the directory tree from `cwd` looking for a vault marker (`.obsidian/` directory). This removes the need to hardcode the vault path in MCP config — convention is to place vault in project root.

2. **Rename docs/SKILL.md → docs/memctl.md**: Match the skill name convention for Claude Code skill files.

## Context

Currently `--vault` is required for every command including `mcp`. This forces users to hardcode absolute paths in MCP config. Auto-detection (like git's `.git/` discovery) makes the config zero-config: `["mcp"]` with no `--vault` arg.

Each project drops a vault directory (with `.obsidian/`) in its root. MCP spawns from project cwd → auto-detects vault.

## Acceptance Criteria

- `memctl mcp` without `--vault` finds vault by walking up from cwd
- `memctl mcp --vault /explicit` still works (explicit overrides auto-detect)
- Other commands (search, get, list, etc.) also benefit from auto-detect
- Clear error if no vault found: "No vault found. Create one with 'memctl init --vault <path>' or run from a directory containing a vault."
- docs/SKILL.md renamed to docs/memctl.md
- SKILL.md/memctl.md updated to reflect auto-detect (--vault now optional for mcp)
