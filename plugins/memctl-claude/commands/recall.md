---
description: Recall top memories + search vault by current task keywords
---
Run `memctl status` to confirm vault is healthy. Then run `memctl list --limit 10` to load top notes by importance. If `$ARGUMENTS` is non-empty, additionally run `memctl search "$ARGUMENTS" --limit 10`. Format the output clearly. If vault is missing, suggest `memctl init <path>`.
