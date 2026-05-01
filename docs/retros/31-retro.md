# Retrospective: Task 31 — V2 foundation

**Date:** 2026-05-01
**Overall Assessment:** Smooth with mid-flight scope reduction (anh dropped legacy V1 back-compat)

---

## Metrics

| Metric | Value |
|--------|-------|
| QC Retry Loops | 0 (passed first try after scope reduction) |
| Final QC | 7/7 ACs pass (FR-1, 2, 4, 5, 6, 7 + all NFR) |
| Review Verdict | APPROVE (inline self-review) |
| Critical Issues | 0 |
| Spec Completeness | 90% (FR-3/8/9 dropped mid-flight per anh's directive) |
| Design Accuracy | 85% (model path stayed user-global, not vault-nested as initially designed) |
| Total commits | 4 (spec+design / build / spec-update / merge) |

---

## What Went Well

1. **Mechanical refactor caught regressions early** — building after each file change kept regressions contained. 5 operators touched without breaking 42 baseline tests.
2. **Test fixture pattern proven** — IDisposable tmp cleanup pattern from prior tasks (#11, #12) reused without friction.
3. **Build-test-fix cycle disciplined** — every code change followed by `dotnet build` then `dotnet test` (when applicable). Caught 1 LegacyV1 test bug immediately.
4. **Anh's mid-flight scope reduction handled cleanly** — dropping legacy V1 detection meant reverting 4 file modifications (VaultStatus DTO + Mapper + StatusOperator + Bootstrap) plus deleting 1 test file. Reverts done atomically; build stayed clean.

## What Didn't Go Well

1. **Initial design over-scoped models per-vault**
   - Root cause: em didn't audit existing `MemctlConfig.cs` + `GemmaEmbeddingEngine.cs` before writing design — assumed models lived per-vault.
   - Impact: design said move models into `<vault>/.obsidian/memctl/models/`. Reality: models live `~/.memctl/models/` user-global (1 copy shared across vaults).
   - Prevention: design phase MUST grep for existing path computations before declaring "this moves to X". Add to design template: "trace existing paths" step.

2. **LegacyV1 detection bug caught only by test failure**
   - Root cause: StatusOperator initial impl trusted `discovery.Strategy.StartsWith("legacy v1")`. But test passed `explicitVault: root` → resolver short-circuits to "explicit" strategy → legacy never detected.
   - Impact: 1 retry on the affected test.
   - Prevention: explicit-path bypass is generic resolver design — any operator-side check that depends on resolver strategy must consider the explicit-path case. Add to memory rule: "resolver Strategy field is null/short-circuit for explicit --vault, don't depend on it for explicit-path workflows".

3. **Scope reduction late in build phase**
   - Root cause: anh thought through V2 design AFTER em had already coded legacy detection.
   - Impact: 1h work reverted (4 files modified, 1 test file deleted).
   - Prevention: not really preventable — legitimate design iteration. But em could've asked anh "is back-compat needed?" earlier. Add to design checklist: "is back-compat scope confirmed?"

## Patterns Detected

### New Patterns

1. **Resolver short-circuits Strategy for explicit-path**
   - Observation: VaultLocator.Discover with explicitPath returns Strategy="explicit", bypasses any walk-up flag. Operators relying on Strategy.StartsWith(...) miss this case.
   - Rule candidate: when an operator needs filesystem-state info (legacy layout, vault structure), it should re-check directly rather than trust resolver Strategy alone.
   - Triggers: [resolver, strategy, explicit-vault, walk-up]
   - Confidence: medium (single occurrence)

2. **Mid-flight scope reduction recipe**
   - Observation: dropping a feature mid-build = revert in dependency order: tests → DTO/Mapper → operator → entity → bootstrap. Each layer reverted independently before next.
   - Rule candidate: when dropping cross-layer feature, revert top-down (test → boundary → operator → core → bootstrap). Build between each layer revert.
   - Confidence: low (single occurrence; needs reinforcement)

### Reinforced

1. **Build after every file change** (golden rule from #14 retro) — held this task. Caught 0 regressions because of disciplined incremental builds.
2. **Test fixture IDisposable pattern** (insight from #14) — confirmed effective again.

### Anti-Patterns

(none — task executed cleanly post-scope-reduction)

---

## Recommendations

### For Spec Phase
- Always grep existing path computations BEFORE declaring "X moves to Y". `grep -rn "path/segment" src/` is cheap; design errors are expensive.

### For Design Phase
- Confirm back-compat scope explicitly with user BEFORE coding back-compat features. "Need legacy support? Y/N" is a 1-line question with huge scope implications.

### For Build/QC Phase
- Existing pattern (build after each file) confirmed effective. Continue.

### For Memory System
- Add `pat_resolver_explicit_short_circuit` to mid-term qc_errors with hit_count: 1.
- Insight added to long-term: "Existing user-global paths take precedence over per-vault designs unless explicit refactor justification given".
