# SDLC Artifact Archive

Frozen per-task spec/design/retro documents from completed (status: Done) backlog tasks.

## Layout

```
docs/archives/
├── specs/<id>-spec.md      ← /spec output (Phase 1)
├── designs/<id>-design.md  ← /design output (Phase 2)
└── retros/<id>-retro.md    ← /retro output (Phase 6)
```

Filename pattern: `<task-id>-<artifact-type>.md`. Sequential by task id.

## Lifecycle

```
Task Todo → /sdlc N
        ↓
   docs/specs/<N>-spec.md      (active during pipeline)
   docs/designs/<N>-design.md
   docs/retros/<N>-retro.md
        ↓ task merged + Done
        ↓ (manual or post-Phase-6 cleanup)
   docs/archives/<type>/<N>-<type>.md   (frozen here)
```

`/sdlc` writes new artifacts to `docs/specs/`, `docs/designs/`, `docs/retros/` (active workspace). When task ships:
1. Mark backlog task `status: Done`
2. Move `docs/specs/<id>-spec.md` → `docs/archives/specs/<id>-spec.md` (and design/retro)
3. Commit `chore: archive task <id> SDLC artifacts`

Auto-archive on /sdlc Phase 6 = future improvement (see `backlog/wishlist.md`).

## Frozen rule

Archived artifacts are **frozen historical records**. They document what was true at task ship time. Per [backlog-conventions.md](../../backlog/wiki/backlog-conventions.md):

- **Don't edit** for cosmetic updates (renamed paths, deprecated tool names) — bulk-update only if a new task explicitly reopens the work.
- **Don't delete** — they're traceability for /retro patterns + /qc-dream lessons that reference this task.
- **Stale internal refs OK** — paths like `docs/release-runbook.md` may now point at moved files (`backlog/wiki/release-runbook.md`); future bots should follow the link target, not auto-update history.

## Why archive instead of leaving in `docs/`?

- `docs/specs/` and `docs/designs/` show only IN-FLIGHT tasks → easy to spot what's actively in /sdlc pipeline.
- Done artifacts decluttering — 27+ done specs would bury 1-2 active during sprint.
- Trail of consolidation: ops + memory in `backlog/wiki/`, history in `docs/archives/`, active in `docs/`.

## Special files

| File | Reason kept |
|------|-------------|
| `archives/specs/wire-format-v1.md` | Reference spec for #14 wire format contract — pre-V3, frozen as design decision context |
