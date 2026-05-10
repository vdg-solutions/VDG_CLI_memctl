---
id: 916c2644f2704e22
created: 2026-05-09T00:06:16.6330547Z
modified: 2026-05-09T00:06:16.6330551Z
tags:
  - golden-rule
  - skill-sync
  - memctl-docs
---


## Rule: Two SKILL.md files must stay identical

docs/memctl.md and plugins/memctl-claude/skills/memctl/SKILL.md are the SAME file maintained in two places.

**Why two places exist:**
- CI reads docs/memctl.md → publishes to memctl-releases/SKILL.md (downloaded by install script)
- CI also syncs plugins/memctl-claude/ directory to release repo
- Both must be identical or release SKILL.md will lag behind

**Enforcement:**
- .git/hooks/pre-commit diffs the two files and blocks commit if they differ
- Error tells you exactly which direction to copy

**When editing:** Always edit ONE file, then cp to the other before staging.
Preferred: edit plugins/.../SKILL.md (closer to feature code), then cp to docs/memctl.md.

**Discovered:** v1.7.2 — 4 updates (feat #48 #49 git-guidance mcp) drifted for multiple releases unnoticed.