# memctl gap-fill design: elevate + voting + tier

**Autoresearch output 2026-05-31** per owner directive `$IUlfeeLPT0so1stmD28v_BkG7J7qq7zLLeneVh7jPVM`. Final v2 spec for memctl gaps #595 / #596 / #597. Pairs with VDG_CleanCode wiki `backlog/wiki/effective-memory-organization.md`.

## Background

memctl already ships:
- ✅ RRF hybrid search (`SearchOperator` fuses `SearchBm25` + `SearchSemantic` via RRF, k=60, equal weight)
- ✅ LLM-driven distillation (`DistillOperator` with L1 → L2 reference in `ContextInjectOperator:76`)
- ✅ Note entity with `Weight`, `AccessCount`, `Archived`, `LastWeightSet`

Real gaps to close:
- ❌ Explicit `tier` field on Note (currently implicit in folder structure / Type field)
- ❌ `elevate` operation (scope-layer move: cwd → agent → machine)
- ❌ Voting / consensus for layer 2 → 3 elevation

## #597 — Tier field + tier-aware distill

**Note entity change** (`src/memctl/CoreAbstractions/Entities/Note.cs`):

```csharp
public sealed record Note
{
    // ... existing fields ...
    public string? Tier { get; init; }   // null | "L0" | "L1" | "L2" | "L3"
}

public static class NoteTiers
{
    public const string L0Raw      = "L0";   // raw conversation turn / tool output
    public const string L1Atom     = "L1";   // atomic fact distilled from L0
    public const string L2Scenario = "L2";   // scenario summary clustered from L1
    public const string L3Persona  = "L3";   // stable persona trait extracted from L2

    public static readonly string[] All = [L0Raw, L1Atom, L2Scenario, L3Persona];
    public static bool IsValid(string? tier) => tier is null || All.Contains(tier);
}
```

**Frontmatter parse** (`VaultReader`): YAML key `tier: L1` parses into `Note.Tier`. Missing key → null → treated as L0 raw for distillation purposes.

**Schema** (SqliteNoteIndex `CREATE TABLE` migration): `ALTER TABLE notes ADD COLUMN tier TEXT NULL`. Existing rows default to NULL — backward compatible.

**DistillOperator extension** (`src/memctl/Operators/DistillOperator.cs`):

- Existing path keeps doing L0 → L1 (conversation → atomic facts).
- Add CLI flag `--from L0 --to L1` (default), `--from L1 --to L2`, `--from L2 --to L3`.
- L1 → L2 pass: cluster L1 atoms semantically (use existing SearchSemantic to group), LLM writes scenario summary per cluster.
- L2 → L3 pass: read all L2 scenarios, LLM extracts stable persona traits (preferences, recurring patterns).
- Each output note carries `tier:` frontmatter.

**CLI**: `memctl distill [--from <tier>] [--to <tier>]`, default both `L0 → L1`.

## #595 — Elevate primitive (scope-layer move)

**New operator** (`src/memctl/Operators/ElevateOperator.cs`, ~80 LOC):

```csharp
public sealed class ElevateOperator(IVaultReader vaultReader, INoteIndex sourceIndex)
{
    public MemctlOutcome Execute(string sourceVaultPath, string targetVaultPath, string noteId)
    {
        // 1. Read note from source vault (frontmatter + body)
        // 2. Compute target file path. If slug collision → append "-elevated-{shortHash}"
        // 3. Write note file to target vault
        // 4. Mark source note Archived=true (keep audit trail) + record "elevated_to: <targetVault>/<newPath>" in frontmatter
        // 5. Re-ingest target vault so the new note is indexed
        // 6. Return MemctlOutcome.Ok("elevate", "<noteId> moved <sourcePath> -> <targetPath>")
    }
}
```

**CLI**: `memctl elevate <note_id> --to-vault <path>` (caller provides target vault root; the source vault is the one memctl is operating on per `--vault` flag).

**Atomicity**: writes to a temp file in target vault, then `File.Move`; archive flag set in source only AFTER target write succeeds. Failed-elevation rollback: if target write throws, source note untouched.

## #596 — Voting consensus (layer 2 → 3 only)

**Storage** (`<vaultPath>/.memctl/elevations.json`):

```json
[
  {
    "id":         "note-slug",
    "fromVault":  "/abs/path/to/agent/instance/.memctl",
    "toVault":    "/abs/path/to/machine/shared/.memctl",
    "proposedAt": "2026-05-31T11:00:00Z",
    "ttlDays":    30,
    "votes":      { "warm_ferret": "yes", "apdana": "yes", "vdg_hub": "no" }
  }
]
```

**New entity** (`src/memctl/CoreAbstractions/Entities/ElevationCandidate.cs`, ~30 LOC):

```csharp
public sealed record ElevationCandidate(
    string Id,
    string FromVault,
    string ToVault,
    DateTime ProposedAt,
    int TtlDays,
    Dictionary<string, string> Votes  // voter_id -> "yes" | "no"
);
```

Registered in `MemctlJsonContext` for AOT.

**New operator** (`src/memctl/Operators/VotingOperator.cs`, ~150 LOC):

```csharp
public sealed class VotingOperator(IVaultReader vaultReader, ElevateOperator elevate, IClock clock)
{
    public MemctlOutcome Propose(string vaultPath, string noteId, string fromVault, string toVault, int ttlDays = 30);
    public MemctlOutcome Vote   (string vaultPath, string noteId, string voterId, string vote);
    public MemctlOutcome Resolve(string vaultPath);   // sweep TTL + apply quorum
    public MemctlOutcome List   (string vaultPath);   // JSON dump
}
```

**Quorum** (resolve):
- Enumerate known agents on the machine: scan `~/.vdg-agent/instances/*/` (each subdirectory name = one agent id).
- Quorum threshold = `ceil(known_agents.Count * 2 / 3)` (2/3 majority of present agents).
- For each candidate: if `votes.Values.Count(v => v == "yes") >= quorum` → call `elevate.Execute(fromVault, toVault, id)`; remove from pool.
- If `proposedAt + ttlDays < now` → drop candidate (no quorum reached in time).

**Vote dedup**: `voterId` is REQUIRED in `Vote()`. Dictionary keyed by `voterId` means a re-vote overwrites the previous one. Each `voterId` contributes at most one vote in the quorum tally.

**CLI**:
- `memctl propose-elevate <id> --to-vault <path> [--ttl-days <n>]`
- `memctl vote-elevate <id> --voter <agent-id> --vote yes|no`
- `memctl resolve-elevations`
- `memctl list-candidates`

## Cross-cutting

- **AOT**: all new types (`ElevationCandidate`, `NoteTiers` constants are not types) registered in `MemctlJsonContext.cs`. Build verified with `dotnet publish -p:PublishAot=true` → 0 IL2xxx/IL3xxx warnings.
- **File size**: each new operator/entity file kept under 350 LOC per `feedback_max_file_350_lines`.
- **Backward compat**: tier column nullable; old notes load fine. Elevation pool is empty on first call. RRF search unaffected.
- **Tests**: extend `tests/` with elevate move/collision/rollback, vote dedup/TTL/quorum, tier parse/round-trip.

## Implementation order

1. **#597 tier field** — schema + Note record + DistillOperator flag (no behavior change on default path)
2. **#595 elevate primitive** — ElevateOperator + CLI; can be tested standalone
3. **#596 voting** — depends on #595; VotingOperator calls ElevateOperator on quorum
4. Bump memctl version, push, CI/CD release workflow at `.github/workflows/release.yml` triggers
