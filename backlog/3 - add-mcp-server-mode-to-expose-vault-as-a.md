---
id: 3
type: task
title: Add MCP server mode to expose vault as AI memory layer
status: Done
priority: high
created: 2026-04-17
updated: 2026-04-17
order: 4
depends_on: [2, 4]
---

## Description

Add MCP server mode to expose vault as AI memory layer

## Comments

**2026-04-17 07:13 user:** Expose memctl as an MCP server so AI agents (Claude, Cursor) can consume it directly. Implement memctl mcp command that starts a stdio-based MCP server exposing tools: search, get, list, search_semantic, search_tags, search_date, search_links. Each tool maps to an existing Operator.
