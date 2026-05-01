#!/usr/bin/env bash
# Sync docs/memctl.md into the Claude Code plugin skills dir.
# Single source of truth: docs/memctl.md → plugins/memctl-claude/skills/memctl/SKILL.md
set -euo pipefail

SRC="docs/memctl.md"
DST="plugins/memctl-claude/skills/memctl/SKILL.md"

[[ -f "$SRC" ]] || { echo "Source missing: $SRC" >&2; exit 1; }
mkdir -p "$(dirname "$DST")"
cp "$SRC" "$DST"
echo "Synced: $SRC -> $DST"
