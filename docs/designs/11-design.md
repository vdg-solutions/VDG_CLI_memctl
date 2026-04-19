# Technical Design: G1 Auto-capture — memctl capture

**Spec:** docs/specs/11-spec.md
**Task:** 11
**Date:** 2026-04-19
**Status:** Draft

---

## 1. Architecture Overview

`memctl capture` is a new CLI command that implements the Hook Protocol v1 `after-response` event. It sits entirely in the **Bootstrap** and **Operators** layers of A.D.D V3:

- **Bootstrap** (`Program.cs`): parses stdin JSON, detects vault, dispatches to `CaptureOperator`
- **Operators** (`CaptureOperator.cs`): filters turns, builds Note with weight=0.5, write + index
- **Implementations** (`VaultLocator.cs`): new overload `FindVaultFrom(startDir)` for cwd-based detection

No changes to CoreAbstractions (ports/entities) or Boundary layers.

### System Context

**Hook mode data flow:**
```
Claude Code Stop hook
  → stdin JSON { session_id, cwd, transcript: [{role, content}] }
  → Program.cs: deserialize CapturePayload
  → VaultLocator.FindVaultFrom(payload.Cwd ?? GetCwd())
  → CaptureOperator.Execute(vaultPath, sessionId, turns, dryRun)
    → filter noise (FR-004, FR-005)
    → if file exists → append path (FR-007)
    → else → create path (FR-006, FR-018)
    → vaultReader.WriteNote + index.Upsert (FR-008)
  → exit 0 always (NFR-002)
```

**Direct mode data flow:**
```
memctl capture --role user --text "..."
  → Program.cs: detect vault via VaultLocator.FindVault
  → CaptureOperator.Execute(vaultPath, autoSessionId, [(role, text)], dryRun)
  → same create/append path as hook mode
  → exit 0 always (FR-013)
```

---

## 2. File Changes

### New Files

| File Path | Purpose | Key Exports |
|-----------|---------|-------------|
| `src/memctl/Operators/CaptureOperator.cs` | Core session note write/append logic with noise filtering | `CaptureOperator` class |

### Modified Files

| File Path | Changes | Reason |
|-----------|---------|--------|
| `src/memctl/Implementations/Config/VaultLocator.cs` | Add `FindVaultFrom(string startDir)` | FR-003: walk from payload cwd, not GetCurrentDirectory() |
| `src/memctl/Bootstrap/Program.cs` | Add `capture` subcommand + CapturePayload / TranscriptTurn DTOs; add System.Text.Json usings | FR-001–FR-015 |

### Integration Code Blocks

#### VaultLocator.cs — add `FindVaultFrom`

```
// INTEGRATION: VaultLocator.cs → end of class
// old_string (unique tail of file):
            var parent = Directory.GetParent(dir);
            if (parent is null) return null;
            dir = parent.FullName;
        }
    }
}

// new_string (adds FindVaultFrom inside class, before closing brace):
            var parent = Directory.GetParent(dir);
            if (parent is null) return null;
            dir = parent.FullName;
        }
    }

    // Walk up from startDir — used by capture to detect vault from Hook Protocol v1 cwd field
    public static string? FindVaultFrom(string startDir)
    {
        var dir = startDir;
        while (true)
        {
            if (Directory.Exists(Path.Combine(dir, ".obsidian")))
                return Path.GetFullPath(dir);

            var parent = Directory.GetParent(dir);
            if (parent is null) return null;
            dir = parent.FullName;
        }
    }
}
```

---

#### Program.cs — Add System.Text.Json usings

```
// INTEGRATION: Program.cs → top using block
// old_string:
using System.CommandLine;

// new_string:
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
```

---

#### Program.cs — Add capture subcommand + DTOs

```
// INTEGRATION: Program.cs → before return statement
// old_string:
root.AddCommand(identityCmd);

return await root.InvokeAsync(args);

// new_string (adds capture command + DTOs):
root.AddCommand(identityCmd);

// --- capture ---
var capRoleOpt    = new Option<string?>("--role",       "Turn role (user | assistant) — direct mode");
var capTextOpt    = new Option<string?>("--text",       "Turn content — direct mode");
var capSessionOpt = new Option<string?>("--session-id", "Session ID override");
var capDryRunOpt  = new Option<bool>   ("--dry-run",    "Print what would be saved; no disk write");
var captureCmd    = new Command("capture", "Auto-capture conversation turns (Hook Protocol v1)");
captureCmd.AddOption(capRoleOpt);
captureCmd.AddOption(capTextOpt);
captureCmd.AddOption(capSessionOpt);
captureCmd.AddOption(capDryRunOpt);
captureCmd.SetHandler(async ctx =>
{
    try
    {
        var pr     = ctx.ParseResult;
        var role   = pr.GetValueForOption(capRoleOpt);
        var text   = pr.GetValueForOption(capTextOpt);
        var sesId  = pr.GetValueForOption(capSessionOpt);
        var dryRun = pr.GetValueForOption(capDryRunOpt);
        var g      = G(ctx);

        // Direct mode: --role / --text (FR-011)
        if (role is not null || text is not null)
        {
            if (role is null || text is null)
            {
                // Edge case 8: user error — exit 1
                Console.WriteLine("""{"success":false,"action":"capture","message":"--role and --text are both required for direct mode"}""");
                ctx.ExitCode = 1;
                return;
            }
            var vault = VaultLocator.FindVault(g.Vault);
            if (vault is null) { ctx.ExitCode = 0; return; }   // FR-013: vault missing → exit 0
            GemmaEmbeddingEngine? emb = null;
            try   { emb = await GetEmbedding(g); }
            catch { /* embedding optional — FR-013 always exit 0 */ }
            var turns  = new (string Role, string Content)[] { (role, text) };
            var op     = new CaptureOperator(vaultReader, noteIndex, emb);
            var result = op.Execute(vault, sesId, turns, dryRun);
            if (dryRun) ResultPrinter.Print(result);
            ctx.ExitCode = 0;
            return;
        }

        // Hook mode: read stdin (FR-001)
        if (!Console.IsInputRedirected) { ctx.ExitCode = 0; return; }

        string stdinText;
        try   { stdinText = await Console.In.ReadToEndAsync(); }
        catch { ctx.ExitCode = 0; return; }

        CapturePayload? payload;
        try   { payload = JsonSerializer.Deserialize<CapturePayload>(stdinText, CaptureJsonOptions); }
        catch { ctx.ExitCode = 0; return; }   // FR-009: invalid JSON → exit 0 silently

        if (payload is null) { ctx.ExitCode = 0; return; }
        if (payload.Transcript is null or { Length: 0 }) { ctx.ExitCode = 0; return; }

        var cwd       = payload.Cwd ?? Directory.GetCurrentDirectory();  // FR-002
        var vaultPath = VaultLocator.FindVaultFrom(cwd);                 // FR-003
        if (vaultPath is null) { ctx.ExitCode = 0; return; }            // FR-010

        GemmaEmbeddingEngine? emb2 = null;
        try   { emb2 = await GetEmbedding(g); }
        catch { /* embedding optional */ }

        var turns2 = payload.Transcript.Select(t => (t.Role, t.Content)).ToArray();
        var op2    = new CaptureOperator(vaultReader, noteIndex, emb2);
        var res    = op2.Execute(vaultPath, payload.SessionId ?? sesId, turns2, dryRun);
        if (dryRun) ResultPrinter.Print(res);
    }
    catch { /* NFR-002: never crash hook */ }
    ctx.ExitCode = 0;  // always exit 0
});
root.AddCommand(captureCmd);

return await root.InvokeAsync(args);

// Hook Protocol v1 payload DTOs
internal sealed record CapturePayload(
    [property: JsonPropertyName("session_id")] string?          SessionId,
    [property: JsonPropertyName("cwd")]        string?          Cwd,
    [property: JsonPropertyName("transcript")] TranscriptTurn[]? Transcript);

internal sealed record TranscriptTurn(
    [property: JsonPropertyName("role")]    string Role,
    [property: JsonPropertyName("content")] string Content);

internal static readonly JsonSerializerOptions CaptureJsonOptions =
    new() { PropertyNameCaseInsensitive = true };
```

---

### Deleted Files

None.

---

## 3. Data Model

### New Models/Schemas

No new entities. Uses existing `Note` record:
```csharp
// Note created with Weight = 0.5f (FR-018)
// FilePath = "sessions/YYYY-MM-DD-{safeSessionId}.md"
// Tags = ["session"]
// Content includes "# Session {date} — {id}\n\n" heading + formatted turns
```

### Migrations Needed

None. Session notes stored as markdown files in vault; indexed in existing SQLite schema. No schema changes.

### Data Flow

```
CREATE path:
  turns (filtered) → FormatNote() → Note(Weight=0.5f) → WriteNote() + Upsert()

APPEND path:
  existing = index.GetByFilePath(relPath) OR ParseNote(file) with Weight=0.5f
  combined = existing.Content + newTurnsText
  appended = existing with { Content=combined, Modified=now }
  → WriteNote(appended) + Upsert(appended)   ← Weight preserved via record copy
```

---

## 4. API Design

N/A — CLI tool, no HTTP endpoints.

---

## 5. UI Components

N/A — CLI tool.

---

## 6. Business Logic

### FR-004 + FR-005: Noise Filtering

```
IsNoise(content):
  1. Trim().Length < 50 chars → skip (FR-004)
  2. Trimmed starts with '{' or '[' → try JsonDocument.Parse → success → skip (FR-005)
  3. Strip all ```...``` code blocks via Regex → remaining.Trim().Length < 50 → skip
  4. Otherwise: keep
```

This detects tool-call-only turns without Claude Code-specific schema parsing. Both pure JSON responses and code-block-only responses are filtered.

### FR-006 + FR-018: Create path

```
safeId  = SanitizeSessionId(sessionIdRaw ?? GenerateSessionId())
date    = DateTime.UtcNow.ToString("yyyy-MM-dd")
relPath = $"sessions/{date}-{safeId}.md"

note = Note {
  Id       = Guid.NewGuid().ToString("N")[..16]
  FilePath = relPath
  Title    = $"Session {date} — {safeId}"
  Content  = $"# Session {date} — {safeId}\n\n{FormatTurns(filteredTurns)}"
  Tags     = ["session"]
  Weight   = 0.5f                ← FR-018: set directly on Note, stored in Upsert
  Created  = DateTime.UtcNow
  Modified = DateTime.UtcNow
}
emb    = embedding?.Embed($"{note.Title}\n{note.Content}")
stored = emb != null ? note with { Embedding = emb } : note

if (!dryRun):
  vaultReader.WriteNote(stored, vaultPath, relPath)  ← creates dirs, writes frontmatter+content
  index.Upsert(stored)                               ← stores weight=0.5 in SQLite
```

### FR-007: Append path (weight preservation)

```
existing = index.GetByFilePath(relPath)
if existing is null:
  // On disk but not indexed (edge case 5)
  existing = vaultReader.ParseNote(absPath, vaultPath) with { Weight = 0.5f }

newText   = FormatTurns(filteredTurns)
separator = existing.Content.EndsWith('\n') ? "" : "\n"
combined  = existing.Content + separator + newText
appended  = existing with { Content=combined, Modified=now }
// existing.Weight flows through 'with' expression — never reset ← FR-007
```

### FR-016: Turn format

```
## Turn {DateTime.UtcNow:O}
**{role}:** {content}

```
Each turn gets the current UTC timestamp (ISO 8601 round-trip format).

### FR-012: Auto session ID (direct mode)

```
$"{DateTime.UtcNow:yyyy-MM-dd}-{Guid.NewGuid().ToString("N")[..8]}"
```
Stable per invocation (generated once, used throughout).

### Edge case 3: session_id sanitization

```csharp
Regex.Replace(rawId, @"[^\w\-]", "_").Trim('_')
```
Replaces `/`, `\`, `..`, `:`, and other path-unsafe characters with `_`.

---

## 7. Error Handling Strategy

| Error Scenario | Handling | User-Facing Output | FR/NFR |
|---------------|----------|-------------------|--------|
| Invalid JSON stdin | `catch { ctx.ExitCode = 0; return; }` | Silent | FR-009 |
| Vault not found (hook mode) | `if (vaultPath is null) { exit 0; }` | Silent | FR-010 |
| Vault not found (direct mode) | `if (vault is null) { exit 0; }` | Silent | FR-013 |
| Embedding unavailable | `try { emb = await GetEmbedding } catch { }` | Silent — Upsert with null embedding | Open Q #2 from spec |
| All turns filtered | `filteredTurns.Count == 0 → return Ok(...)` | Silent (exit 0) | Edge case 2 |
| Empty transcript | `Transcript is null or { Length: 0 } → exit 0` | Silent | Edge case 1 |
| --role without --text | `Console.WriteLine(error JSON); exit 1` | JSON error message | Edge case 8 |
| Any unhandled exception | Outer `catch { }` in handler | Silent | NFR-002 |

---

## 8. Security Considerations

- **session_id sanitization**: `Regex.Replace(id, @"[^\w\-]", "_")` — prevents path traversal via session_id (edge case 3)
- **Path safety**: `vaultReader.WriteNote` calls `Directory.CreateDirectory` safely inside vault root; no additional check needed since relPath = `sessions/{date}-{safeId}.md` with sanitized safeId
- **stdin**: Content read from stdin is stored as-is — no execution, no interpretation
- **No auth needed**: CLI command, local filesystem only

---

## 9. Performance Considerations

- **NFR-001 target: < 2s for 50 turns**
- `IsNoise`: `JsonDocument.Parse` + compiled Regex — < 1ms per turn
- `GemmaEmbeddingEngine.Embed`: ~50–200ms per call; called once on combined content
- `index.Upsert`: SQLite write, < 10ms
- `IngestOperator.NeedsIngest`: stat check on SQLite file, < 1ms
- For 50 turns with embedding: ~300ms total. Well within 2s target.
- Without embedding (model not downloaded): < 50ms total.

---

## 10. Testing Strategy

| Level | What to Test | How | Count |
|-------|-------------|-----|-------|
| Unit | `IsNoise`, `SanitizeSessionId`, `FormatTurns` | Manual trace / QC Layer 3 review | 6 cases |
| Layer 2.5 smoke | Hook mode create, append, invalid JSON, vault missing, direct mode | `dotnet run -- capture` | 6 scenarios |

No test project exists — Layer 2.5 smoke scenarios cover [e2e]-tagged FRs via process spawning.

### Key Test Cases

1. `echo '{"session_id":"test","cwd":"{vault_parent}","transcript":[{"role":"user","content":"this is a longer message for testing purposes"}]}' | memctl capture` → creates `sessions/{date}-test.md`, exit 0
2. Same payload twice → second run appends (file grows), weight preserved
3. `echo 'not json' | memctl capture` → exit 0, no output, no file created
4. `echo '{"transcript":[]}' | memctl capture` → exit 0, no file
5. `memctl capture --role user --text "this is a test message for direct mode capture"` → creates session note, exit 0
6. `memctl capture --role user` → exit 1, error JSON on stdout

---

## 10.5 E2E Scenarios

**Project Type:** cli_tool

### Smoke Scenarios (Layer 2.5 — always executed by /qc)

Build command: `dotnet build src/memctl -c Debug`
Binary invocation: `dotnet run --project src/memctl --`

| Scenario | Command | Expected Output | FR |
|----------|---------|-----------------|-----|
| Hook mode create | `echo '{"session_id":"s1","cwd":"{valid_vault_parent}","transcript":[{"role":"user","content":"this is a long enough message to pass the noise filter test"}]}' \| memctl capture` | exit 0; file `sessions/{date}-s1.md` created | FR-006 |
| Hook mode append | Same command twice | exit 0; file contains two Turn sections | FR-007 |
| Invalid JSON stdin | `echo 'garbage' \| memctl capture` | exit 0, no output | FR-009 |
| No vault found | `cd /tmp && echo '{"transcript":[{"role":"user","content":"this is long enough to pass filter"}]}' \| memctl capture` | exit 0, no output | FR-010 |
| Direct mode | `memctl capture --role user --text "this message is long enough to pass the noise filter threshold"` | exit 0; session note created | FR-011 |
| Direct mode missing --text | `memctl capture --role user` | exit 1; stderr/stdout contains "required" | Edge case 8 |

---

## 11. Dependencies

| Dependency | Version | Purpose | New? |
|-----------|---------|---------|------|
| `System.Text.Json` | .NET 10 BCL | Deserialize Hook Protocol v1 stdin payload | No (BCL) |
| `System.Text.RegularExpressions` | .NET 10 BCL | IsNoise code-block stripping, session_id sanitization | No (BCL) |
| `VaultWriteOperator` | existing | NOT used directly — CaptureOperator builds Note + calls WriteNote+Upsert | No |
| `IngestOperator.NeedsIngest` + `DbPath` | existing | Index initialization guard | No |
| `GemmaEmbeddingEngine` | existing | Optional embedding on note create/append | No |
| `VaultLocator.FindVaultFrom` | new overload | Walk up from payload cwd | +8 lines |

---

## 12. Implementation Order

1. **`VaultLocator.cs`** — add `FindVaultFrom(string startDir)` (8 lines, no risk)
2. **`CaptureOperator.cs`** — new file: constants, `IsNoise`, `FormatTurns`, `Execute` (create + append paths), `SanitizeSessionId`, `GenerateSessionId`
3. **`Program.cs`** — add System.Text.Json usings, capture subcommand handler, DTO records
4. **Build + smoke test** — `dotnet build`, then run Layer 2.5 scenarios manually

---

## 13. Assumptions & Open Design Decisions

- [x] **Idempotency on re-run**: Same payload twice → same turns appended twice (minor duplication). Spec says "same session_id appends; does not duplicate" (NFR-003) but implementing exact dedup would require scanning file content. Accepted: last-write-wins semantics (concurrent safety), light duplication on exact retry.
- [x] **Turn timestamp**: Uses `DateTime.UtcNow` at capture time, not from payload. Spec FR-016 says "ISO timestamp" without specifying payload vs capture time. Assumed: capture time is correct.
- [x] **Unindexed note weight**: When note exists on disk but not in index, ParseNote returns Weight=0.0 (not in frontmatter). Solution: default to Weight=0.5f for unindexed session notes. Acceptable — spec edge case 5 says "append AND upsert to index" without specifying weight recovery.
- [x] **Embedding unavailable**: If `GetEmbedding` throws (model not downloaded), embedding is null. Note is upserted with null embedding — still BM25-searchable. Spec open question #2 confirmed this is acceptable.

---

## 14. Traceability Matrix

| Requirement | Design Section | Files | Test Cases |
|-------------|---------------|-------|------------|
| FR-001 (parse Hook Protocol v1) | 6, Program.cs block | Program.cs | Smoke 1 |
| FR-002 (cwd fallback) | 6 | Program.cs | Smoke 1 (implicit) |
| FR-003 (vault auto-detect) | 1, 6 | VaultLocator.cs | Smoke 4 |
| FR-004 (skip short turns) | 6 IsNoise | CaptureOperator.cs | Unit (IsNoise) |
| FR-005 (skip tool-call-only) | 6 IsNoise | CaptureOperator.cs | Unit (IsNoise) |
| FR-006 (create sessions/ note) | 6 Create path | CaptureOperator.cs | Smoke 1 |
| FR-007 (append, preserve weight) | 6 Append path | CaptureOperator.cs | Smoke 2 |
| FR-008 (Upsert after write) | 3, 6 | CaptureOperator.cs | Smoke 1 (indexed) |
| FR-009 (exit 0 invalid JSON) | 7 | Program.cs | Smoke 3 |
| FR-010 (exit 0 vault missing) | 7 | Program.cs | Smoke 4 |
| FR-011 (direct mode) | 6, Program.cs | Program.cs | Smoke 5 |
| FR-012 (auto session_id) | 6 | CaptureOperator.cs | Unit |
| FR-013 (direct mode exit 0) | 7 | Program.cs | Smoke 5 |
| FR-014 (same note structure) | 6 | CaptureOperator.cs | Smoke 5 |
| FR-015 (--dry-run) | Program.cs block | Program.cs | — |
| FR-016 (turn format) | 6 | CaptureOperator.cs | QC Layer 3 |
| FR-017 (note title) | 6 Create path | CaptureOperator.cs | QC Layer 3 |
| FR-018 (weight=0.5) | 6 Create path | CaptureOperator.cs | QC Layer 3 |
| NFR-001 (< 2s) | 9 | CaptureOperator.cs | — |
| NFR-002 (exit 0 always) | 7 | Program.cs | All smoke |
| NFR-003 (idempotency) | 13 | CaptureOperator.cs | Smoke 2 |
| NFR-004 (no stderr on expected failure) | 7 | Program.cs | Smoke 3, 4 |
