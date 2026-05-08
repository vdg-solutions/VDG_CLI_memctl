---
id: 48
type: task
title: Improve memctl add error message for unknown flags
status: Todo
priority: normal
created: 2026-05-08
updated: 2026-05-08
---

## Description

LLM-first fix for `memctl add` CLI ergonomics. Two changes in one commit:

1. **Add `--content` as an alias** for the positional `<text>` argument — LLMs naturally use `--content` flag; making it work eliminates the error class entirely.
2. **Improve unknown-flag error** — when any unrecognized option is passed, print `Unknown option '--x'. Usage: memctl add <text> [--title <title>] [--tags <tags>] [--file <file>]` instead of the misleading `Unrecognized command or argument <value>`.

**Why both:** alias prevents the error; better message handles any remaining unknown flags on other commands.

## Files

- `src/memctl/Bootstrap/Program.cs` — `memctl add` command definition: add `--content` option wired to same handler as positional `<text>`; improve error output for unknown options
- `plugins/memctl-claude/skills/memctl/SKILL.md` — Commands table: update `memctl add` signature to show `--content` as accepted flag (golden-rule: command signature change → SKILL.md update)

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| AC-1 | `memctl add --content "text" --title "t"` succeeds identically to `memctl add "text" --title "t"` | both return `success: true`, same note written |
| AC-2 | `memctl add --unknown "x"` prints `Unknown option '--unknown'. Usage: memctl add ...` to stderr, exits 1 | run command, check stderr + exit code |
| AC-3 | `memctl add "text"` (existing positional syntax) still works — no regression | existing usage unchanged |
| AC-4 | `memctl add --content "text"` and `memctl add "text"` both produce identical JSON output | diff outputs |
| AC-5 | SKILL.md `memctl add` entry updated to show `--content` option | read SKILL.md |

## Out of scope

- Fixing error messages for other commands (`memctl search`, `memctl get`, etc.) — separate task
- Changing JSON output schema

## Performance

Argument parsing adds one extra `Option<string>` registration — negligible, not on hot path. No change to note write or index path.

## Comments

**2026-05-08 06:52 user:** LLM-first UX fix: when an unknown flag is passed (e.g. --content), current error prints 'Unrecognized command or argument <entire content string>' — LLM cannot tell whether the flag name or the value is wrong. Fix: detect unknown options before argument parsing and emit 'Unknown option --content. Usage: memctl add <text> --title ... --tags ...' Also consider adding --content as an alias for the positional <text> argument to prevent this class of error entirely.
