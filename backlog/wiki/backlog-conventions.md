# Backlog Conventions — for future bots reading `backlog/`

How a Claude Code bot should treat `backlog/` items based on `status` field.

---

## Status semantics

| Status | Bot behavior |
|--------|--------------|
| `Todo` | Pick via `/sdlc` (highest priority, lowest id tiebreaker). Drives full SDLC pipeline. |
| `In Progress` | Active SDLC pipeline. Don't re-pick. Resume only if explicit user request. |
| `In Review` | Code merged but retro/qc-dream pending, OR `--review` mode awaiting human. Don't re-pick. |
| `Done` | Immutable for re-execution. Read-only as **reference**. See "Editing Done items" below. |
| `Archived` | Same as Done — never re-execute. Excluded from lint. |

`scripts/lint-backlog.sh` skips `Done` and `Archived` automatically. `/sdlc` task picker only scans `Todo`.

---

## Editing Done items — when it's OK

`Done` does NOT mean frozen. The file IS the historical record AND a reference doc for future bots writing similar tasks. Edits are allowed and encouraged when:

1. **A code/spec snippet inside the item is wrong** and a future bot copying it would repeat the bug.
   - Example: `backlog/27` had `"source": "github:..."` (string format). Claude Code 2.1+ rejects string source — only object `{source, url, path, ref}` works. Updated snippet so the next bot building a plugin doesn't lose 30 minutes to the same 403.

2. **An external resource referenced by the item changed** (URL, repo path, API endpoint).
   - Example: marketplace owner changed from `rentaicoder` → `vdg-solutions` mid-design. Done items referencing the old name updated.

3. **A typo, dead link, or wrong file path is found.**

Edits NOT allowed:
- Changing `status` (use `bl edit <id> --status` if absolutely necessary, e.g., reverting a wrongly-marked Done).
- Rewriting requirements / ACs (history matters — write a new task for new requirements).
- Removing comments / phase notes (they document the SDLC journey for retros).

---

## Edit + push protocol

When editing a Done item to fix reference content:

1. Edit the file in place.
2. Commit directly to `main`. **Skip the full /sdlc pipeline** — this is a doc fix, not a task.
3. Commit message convention: `docs(<id>): fix <what> — <why>`. Example:
   ```
   docs(27): fix marketplace.json snippet — Claude Code 2.1 source object format
   ```
4. Push to `origin/main`.

Don't:
- Create a feature branch for trivial doc fixes.
- Run `/spec` / `/design` for content corrections.
- Bump `updated:` field unless the change is substantive (`bl` does this automatically when needed).

---

## When to ARCHIVE instead of fix

If a Done item describes a path that's been completely superseded (e.g., the entire feature was replaced 6 months later), don't try to keep the snippet current. Move the file to `backlog/archive/` and add a one-line forward-reference:

```yaml
status: Archived
archived_reason: 'Superseded by #N — see backlog/N - <title>.md'
```

Future bots see `Archived` and skip — no scrubbing needed.

---

## Why this matters for /sdlc autonomy

`/sdlc` reads completed Done items implicitly via:
- Memory tier `mid/qc_errors.md` (patterns from past tasks)
- Insights tier `long/insights.md` (meta-learnings)
- Direct backlog scan for "similar tasks" reference

If Done items contain stale snippets, the bot's reference base is poisoned. Worse, `/autoresearch` mode treats Done item snippets as ground-truth examples. One wrong snippet → bot ships wrong code → another retro entry → cycle repeats.

Keep Done items honest. They're the codified institutional memory for future autonomous runs.

---

## TL;DR for the next bot reading this

- See `Done` item with wrong snippet? Fix it. Commit `docs(<id>):` to main. No /sdlc.
- See `Done` item with outdated requirement? Don't fix — write a new `Todo` task that supersedes it.
- See `Archived` item? Trust the `archived_reason` pointer. Don't dig further.
- Never change `status: Done` → `Todo` to "redo" a task. Always create a new task.
