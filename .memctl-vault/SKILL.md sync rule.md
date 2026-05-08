---
id: 3b6c5329747a4156
created: 2026-05-07T23:48:53.3158452Z
modified: 2026-05-07T23:48:53.3158455Z
tags:
  - golden-rule
---

# SKILL.md sync rule

Any code change that adds, removes, or modifies commands, options, flags, hook behavior, or wire format must also update plugins/memctl-claude/skills/memctl/SKILL.md before committing. SKILL.md is the install artifact loaded when someone installs the memctl plugin into a fresh Claude Code environment. If it drifts from the binary, a fresh install gets wrong docs. Rule reinforced after tasks 43 and 44 were merged without updating SKILL.md.