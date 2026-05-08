# Spec #48 — memctl add: --content alias + improved unknown-flag error

## Context

memctl's primary caller is LLMs. LLMs naturally pass content via `--content` flag.
Current `memctl add` only accepts content as a positional `<text>` argument.
Passing `--content` produces a misleading error that LLMs cannot self-correct from.

## Functional Requirements

### FR-1: --content alias
`memctl add --content "<text>"` MUST behave identically to `memctl add "<text>"`.
Both syntaxes are accepted. If both are provided, `--content` wins.
If neither is provided, emit usage error to stderr and exit 1.

### FR-2: Unknown-flag error for add command
When `memctl add --<unknown>` is called, output to stderr:
```
Unknown option '--<unknown>'. Usage: memctl add <text> [--content <text>] [--title <title>] [--tags <tags>] [--file <file>]
```
Exit code 1.

### FR-3: No regression
`memctl add "<text>"` (existing positional syntax) continues to work unchanged.

### FR-4: SKILL.md updated
`plugins/memctl-claude/skills/memctl/SKILL.md` add command entry reflects `--content` option.

## Non-Functional Requirements

### NFR-1: No hot-path overhead
Change is limited to argument parsing setup. Note write and index paths unchanged.

### NFR-2: Scope
Only `memctl add` is changed. Other commands' error messages are out of scope.

## Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC-1 | `memctl add --content "text" --title "t"` → `success: true`, note written |
| AC-2 | `memctl add --unknown "x"` → stderr: `Unknown option '--unknown'...`, exit 1 |
| AC-3 | `memctl add "text"` → unchanged behaviour, `success: true` |
| AC-4 | JSON output of `--content` and positional forms is identical |
| AC-5 | SKILL.md shows `--content` in `memctl add` entry |

## Files

- `src/memctl/Bootstrap/Program.cs` — add command wiring (2 changes)
- `plugins/memctl-claude/skills/memctl/SKILL.md` — Commands table
