# Retrospective: Task 29 — Vault MEMCTL_SHARED_VAULT env var

**Date:** 2026-05-02
**Overall Assessment:** Smooth (no QC retries, single-shot pass)

## Metrics

| Metric | Value |
|--------|-------|
| QC Retry Loops | 0 |
| Final QC | 11/11 ACs (FR-1..7 + NFR-1..4) |
| Build | 0 warning, 0 error |
| Tests | 53/53 (4 new + 49 baseline) |
| Smoke | 2/2 (env var hit + walk-up wins) |
| Commits | 3 (spec+design / feat / merge) |
| Push | Deferred — PAT revoked, local merge per /sdlc fallback rule |

## What Went Well

1. **Spec body in backlog already had implementation snippet** — saved design churn. Adapted V1 walk-up assumption to V2.1 marker pair (`<env>/.obsidian/`) cleanly.
2. **`InternalsVisibleTo` clean approach** — test injection via `internal static EnvReader` standard pattern; tests assign + Dispose restore. No reflection hacks.
3. **Smoke verified end-to-end** — running built dll with env var set + cwd outside any walk-up vault returned `search_strategy: "MEMCTL_SHARED_VAULT env (shared)"` exactly as specced.
4. **Per-project security property holds** — smoke 2 confirmed walk-up wins over env var even when both set.
5. **Version lockstep automated by workflow #28** — csproj + plugin.json bumped together, tag will be enforced.

## What Didn't Go Well

1. **Git push hung silently — no PAT in cache**
   - Root cause: anh revoked PAT after v1.3.0 ship. Push blocks invisibly on credential prompt that doesn't surface in non-interactive shell.
   - Impact: Phase 5 PR creation skipped. Local merge per /sdlc fallback rule. Anh push manually sau.
   - Prevention: when push hangs >30s, fall back immediately. Skill rule already covers — followed correctly.

2. **No-op env var doc check** — task body claimed "docs already document MEMCTL_SHARED_VAULT as if implemented" but `grep MEMCTL_SHARED_VAULT backlog/wiki/ plugins/memctl-claude/README.md` returned 0 hits. Docs were actually clean. FR-7 reinterpreted as "add proper env var doc" — completed.

## Patterns Detected

### New Patterns

1. **`InternalsVisibleTo` + injectable static delegate for env-dependent code**
   - Observation: `internal static Func<string, string?> EnvReader` lets tests stub env reads without reflection or process env mutation. Default to `Environment.GetEnvironmentVariable`. Tests assign in ctor, restore in Dispose.
   - Rule candidate: any future env-var-dependent code in memctl follows this pattern (don't read `Environment.GetEnvironmentVariable` directly inline — wrap via injectable delegate).
   - Triggers: [env var, environment variable, Environment.GetEnvironmentVariable]
   - Confidence: high

### Reinforced

1. **V2.1 marker pair check** — `Directory.Exists(<path>/.obsidian)` is the consistent vault-validity check. Reused identically in env var fallback.
2. **Git push fallback to local merge** (from #25 retro) — confirmed effective again.

### Anti-Patterns

(none)

## Recommendations

### For Spec Phase
- When task body claims "docs already document X" — grep verify before specifying FR-7 doc-sync. False premise leaks into ACs.

### For Build Phase
- `InternalsVisibleTo` pattern reusable — codify in golden rules.

### For QC Phase
- Smoke tests caught the strategy string contract; unit tests verify branching. Both layers needed for env var features (state lives in process env, not args).

### For Memory System
- Promote pattern: "env-dependent code → injectable delegate, never inline `Environment.GetEnvironmentVariable`". Hit count: 1 (this task). Promote to rule on second hit.
