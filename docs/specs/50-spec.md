---
id: 50
title: Fix _memctl-backend.md vault path instruction
status: accepted
---

# Spec: Fix _memctl-backend.md vault path

## Problem

`_memctl-backend.md` instructed AI skills to init the vault at `<root>/.memctl-vault/`:

```bash
VAULT="$(git rev-parse --show-toplevel)/.memctl-vault"
memctl init --vault "$VAULT"
```

`InitVaultStructure` receives `.memctl-vault`, detects that `Path.GetFileName(".memctl-vault") != ".memctl"`, so it creates `.memctl-vault/.memctl/` — NOT at `<root>/.memctl/`. VaultLocator V2.1 walks up looking for `<dir>/.memctl/.obsidian/` — it checks `<root>/.memctl/`, not `<root>/.memctl-vault/.memctl/` → vault never found via auto-detect.

Result: any AI call without `--vault` fails silently; with a mangled `--vault` argument (from AI-generated path computation), generates paths like `reposWorkingVDG_CleanCode.memctl-vault`.

## Fix

Change vault path throughout `_memctl-backend.md`:
- `.memctl-vault` → `.memctl`
- Gitignore entry: `.memctl-vault/` → `.memctl/`
- Hard rules section: clarify vault is always `<repo_root>/.memctl`

## Acceptance Criteria

- AC-1: `_memctl-backend.md` has no occurrence of `.memctl-vault`
- AC-2: First-run setup uses `VAULT="$(git rev-parse --show-toplevel)/.memctl"`
- AC-3: `memctl init --vault "$VAULT"` with corrected VAULT creates `<root>/.memctl/.obsidian/` → VaultLocator finds it via walk-up
- AC-4: All `--vault` examples point to `<repo_root>/.memctl`
