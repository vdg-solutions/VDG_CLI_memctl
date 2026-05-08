---
id: 46
type: task
title: Document MCP as primary integration path for non-Claude tools
status: Done
priority: low
created: 2026-05-08
updated: 2026-05-08
---

## Description

Add "Integrating with other AI tools" section to the project docs. Position `memctl mcp` as the primary zero-maintenance integration path (works with any MCP host: Cursor, Cline, VS Code MCP extension). Hook plugins (`plugins/memctl-{tool}/`) are the fallback for tools that don't support MCP — see #47 for plugin guide. Docs-only, no code changes.

## Files

- `README.md` — add "Integrating with other AI tools" section (h2 level, after existing "Usage" section)
- `plugins/memctl-claude/skills/memctl/SKILL.md` — MCP mode section: add one sentence positioning it as the agnostic integration path

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | `README.md` contains `## Integrating with other AI tools` section | grep README.md |
| AC-2 | Section shows `memctl mcp` config snippet for at least one non-Claude host (e.g. Cursor `mcp.json`) | read section |
| AC-3 | Section names hook plugins as fallback with reference to plugin guide (#47) | read section |
| AC-4 | SKILL.md MCP mode section updated with agnostic context sentence | read SKILL.md |
| AC-5 | No existing content removed or restructured — additive only | diff README.md |

## Out of scope

- Implementing MCP support for any specific tool (already works via `memctl mcp`)
- Creating any new plugin
- Code changes to the binary

## Performance

Docs-only — no runtime impact.

## Comments

**2026-05-08 06:46 user:** README + SKILL.md: document that memctl mcp is the zero-maintenance integration path for Cursor, Cline, VS Code MCP extension, etc. Hook plugins (plugins/memctl-{tool}/) are the fallback for tools that don't support MCP. Add a 'Integrating with other AI tools' section.
