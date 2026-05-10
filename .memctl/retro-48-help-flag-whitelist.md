---
id: 602e2f5b68584a0f
created: 2026-05-08T00:17:41.6032518Z
modified: 2026-05-08T00:17:41.6032522Z
tags:
  - qc-error
  - project-memctl
---

# retro-48-help-flag-whitelist

Task 48 retro: --content alias + unknown-flag error for memctl add. Bug caught in review: --help not in knownAddFlags whitelist — would have broken memctl add --help. Fixed before merge. Pattern: when pre-parsing args for specific commands, always include built-in System.CommandLine flags (--help, -h, -?) in the known-flags set.