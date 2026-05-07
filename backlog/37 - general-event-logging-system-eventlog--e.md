---
id: 37
type: task
title: General event logging system (EventLog + events/ folder)
status: Done
priority: normal
parent: 38
tags:
- events,logging,vault
created: 2026-05-07
updated: 2026-05-07
---

## Description

`HookLog` captures only 2 hook events in a flat TSV file — not searchable, no schema, hidden from vault. Need general event store: all operators write structured events as vault notes under `events/`, findable via normal search.

**Key design decision:** Events stored as `Note` objects with `archived: true` from creation → invisible to `context-inject` / `list` / search by default, but queryable via `--folder events` or `--include-archived`. Zero performance cost: no embedding computed for event notes.

## Schema

Event note frontmatter (extends normal frontmatter):

```yaml
id: abc123
type: hook_fired | operator_run | error | model_loaded
severity: info | warn | error
source: capture | add | ingest | organize | lint | distill | ...
conversation_id: optional
payload: single-line summary (no newlines)
timestamp: 2026-05-07T06:30:00Z
tags: [event, error]        # always tag: event + severity
archived: true              # always — keeps events out of context-inject
```

Body: `{source} {severity} — {payload}` (one line, no heading).

## Implementation

### Step 1 — Add `events/` to vault init
- **MODIFY** `ObsidianVaultReader.InitVaultStructure` — add `"events"` to the folder array (line ~63)

### Step 2 — `EventLog` static class
- **CREATE** `src/memctl/Operators/EventLog.cs`:

```csharp
internal static class EventLog
{
    internal static void Record(
        string vaultPath, string type, string severity,
        string source, string payload, string? conversationId = null)
    {
        try
        {
            var ts      = DateTime.UtcNow;
            var id      = Guid.NewGuid().ToString("N")[..16];
            var relPath = $"events/{ts:yyyy-MM-dd}-{source}-{id[..6]}.md";
            var absPath = Path.Combine(vaultPath, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);

            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"id: {id}");
            sb.AppendLine($"type: {type}");
            sb.AppendLine($"severity: {severity}");
            sb.AppendLine($"source: {source}");
            sb.AppendLine($"payload: \"{payload.Replace("\"", "'")}\"");
            sb.AppendLine($"timestamp: {ts:O}");
            if (conversationId is not null)
                sb.AppendLine($"conversation_id: {conversationId}");
            sb.AppendLine("tags:");
            sb.AppendLine("  - event");
            sb.AppendLine($"  - {severity}");
            sb.AppendLine("archived: true");
            sb.AppendLine("---");
            sb.AppendLine();
            sb.Append($"{source} {severity} — {payload}");

            File.WriteAllText(absPath, sb.ToString(), Encoding.UTF8);

            // Intentionally NOT calling index.Upsert — events are disk-only until next ingest.
            // HookLog.Record still called for backward compat (hook-status command reads it).
        }
        catch { /* best-effort; never crash callers */ }
    }
}
```

### Step 3 — Wire into operators (info events only on success; error events on failure)

- **MODIFY** `CaptureOperator.cs` — after successful write: `EventLog.Record(vaultPath, "operator_run", "info", "capture", $"{turns.Count} turns → {relPath}")`
- **MODIFY** `IngestOperator.cs` — after complete: `EventLog.Record(vaultPath, "operator_run", "info", "ingest", $"{indexed} indexed, {pruned} pruned")`
- **MODIFY** `Program.cs` capture hook error path — replace `HookLog.Record(... false ...)` with both `HookLog.Record` (compat) + `EventLog.Record(... "error" ...)`

**Do NOT wire** into `AddOperator`, `SearchOperator`, `GetOperator` — read/write per-note ops are too granular, would flood `events/`.

### Step 4 — `memctl search --folder events`
- Already works — `SearchOperator` accepts `--folder` prefix. No code change needed.
- `memctl search "error" --folder events` → finds error events.

### Step 5 — Tests (3 cases)
- **CREATE** `tests/memctl.Tests/Operators/EventLogTests.cs`
  1. `Record_WritesFileToEventsFolder`: call `EventLog.Record` → file exists at `events/{date}-capture-{id}.md`
  2. `Record_FrontmatterContainsAllFields`: parse written file → id, type, severity, source, payload, timestamp, archived=true all present
  3. `Record_SilentOnInvalidVaultPath`: call with non-existent vault path → no exception thrown

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| FR-1 | `EventLog.Record` writes note to `events/` | file exists after call |
| FR-2 | Event note has `archived: true` — excluded from `memctl list` | `memctl list` shows no event notes |
| FR-3 | `memctl search "error" --folder events` returns error events | query returns ≥1 result after error recorded |
| FR-4 | `memctl grep "operator_run" --vault ...` finds event notes | raw grep works |
| FR-5 | capture errors produce event note with severity=error | trigger hook failure → file in events/ |
| FR-6 | ingest success produces event note with severity=info | run ingest → file in events/ |
| NFR-1 | No embedding computed for event notes | `EventLog` never calls embedding engine |
| NFR-2 | `EventLog.Record` never throws | called with bad path → silent |
| NFR-3 | `HookLog` kept as-is — `hook-status` command unaffected | `memctl hook-status` still works |
| NFR-4 | `dotnet test` passes | 0 regressions |

## Files
- MODIFY: `src/memctl/Implementations/Vault/ObsidianVaultReader.cs`
- CREATE: `src/memctl/Operators/EventLog.cs`
- MODIFY: `src/memctl/Operators/CaptureOperator.cs`
- MODIFY: `src/memctl/Operators/IngestOperator.cs`
- MODIFY: `src/memctl/Bootstrap/Program.cs` (capture error path only)
- CREATE: `tests/memctl.Tests/Operators/EventLogTests.cs`

## Out of scope
- Wiring into Add/Search/Get operators (too granular, floods events/)
- `memctl events` dedicated command — use `search --folder events` or `grep`
- Removing HookLog — kept for backward compat with `hook-status` command
- Embedding event notes — intentionally skipped

## Risks

| Risk | Mitigation |
|------|-----------|
| `events/` floods disk on busy vault | Only operator-level events (not per-note ops); ~5-10 files/day typical usage |
| `archived: true` frontmatter parsed correctly by `ObsidianVaultReader` | Verify `FrontmatterParser` handles bool values; `Note.Archived` field already exists |
| File naming collision (same source, same second) | ID suffix `{id[..6]}` in filename makes collision negligible |

## Effort
~3h: vault init (0.25h) + EventLog class (0.75h) + wire 2 operators (0.5h) + tests (1h) + verify (0.5h)

## Comments

**2026-05-07 10:28 user:** Pipeline complete. Merged to main. 67/67 tests passing.
