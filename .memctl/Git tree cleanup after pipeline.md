---
id: 3350276361a047a0
created: 2026-05-08T01:04:50.3443829Z
modified: 2026-05-08T01:04:50.3443834Z
tags:
  - golden-rule
---

# Git tree cleanup after pipeline

Git tree must be clean after every session: check git status, commit ALL remaining files (backlog status, .memctl/*.md notes, any unstaged file) before push/tag. CRITICAL: .memctl/ gitignore must only exclude .obsidian/memctl/ (runtime index). NEVER ignore the whole vault folder — vault .md files are project memory and must be committed.