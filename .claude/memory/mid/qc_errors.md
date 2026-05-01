
### [2026-04-19] — nuget_tls_smoke_environment
- Severity: low
- Triggers: [fetch, http, network, nuget, tls, smoke]
- Pattern: Network-dependent smoke tests (external URLs, TLS) may be UNVERIFIABLE in sandboxed build environments. Mark as UNVERIFIABLE, not FAIL. Verify by static code review instead.
- Fix: Always include static code review path verification as fallback for network-dependent tests.
- Hit count: 1

### [2026-04-19] — json_null_field_vs_absent
- Severity: high
- Triggers: [csharp, json, serialization, nullable, anonymous, hint, optional]
- Pattern: When a JSON response field should be conditionally absent (not null), Generator uses a single anonymous object with a nullable value (serializes as "field": null). Spec says absent. Root cause: not reading the acceptance criteria carefully ("field absent" != "field null").
- Fix: Use two separate anonymous object expressions — one with the field, one without. Or use a DTO with [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)].
- Hit count: 1

### [2026-04-19] — spec_exact_text_not_copied
- Severity: normal
- Triggers: [message, text, hint, output, format, exact]
- Pattern: When spec provides exact acceptance criterion text for user-facing strings, Generator paraphrases instead of copying verbatim. Results in review rejection.
- Fix: When spec says 'emit: "X"', output exactly "X", not a paraphrase.
- Hit count: 1

### [2026-04-19] — sqlite_pragma_table_info_migration
- Severity: normal
- Triggers: [sqlite, migration, csharp, dotnet, alter, column]
- Pattern: When adding columns to an existing SQLite table via ALTER TABLE, prefer checking pragma_table_info(tableName) to see if column exists before ALTER, rather than catching duplicate column exceptions. SQLite exception messages are not standardized and may vary.
- Fix: Use `SELECT COUNT(*) FROM pragma_table_info('notes') WHERE name='col_name'` before ALTER TABLE.
- Hit count: 1

### [2026-04-19] — local_function_before_type_declaration
- Severity: high
- Triggers: [csharp, dotnet, program, topLevel, localFunction, static]
- Pattern: In C# top-level statement files (Program.cs), local functions (static or not) must be placed BEFORE any `internal sealed record` or other type declarations. Placing local functions AFTER type declarations causes CS8803 compilation error.
- Fix: Move local functions to after `return await root.InvokeAsync(args)` and before the first type/record declaration.
- Hit count: 1

### [2026-04-19] — sqlite_upsert_weight_not_inserted (task 11)
- Severity: high
- Triggers: [upsert, weight, sqlite, noteindex, insert, capture, session]
- Pattern: `SqliteNoteIndex.Upsert` INSERT statement omitted the `weight` column — all new notes landed in DB with weight=0.0 regardless of what was set on the Note record. The ON CONFLICT UPDATE correctly excluded weight, but the INSERT needed to include it so initial weights (e.g., 0.5 for session notes) are persisted.
- Fix: Add `weight` to the INSERT column list and `@weight` parameter. ON CONFLICT UPDATE remains unchanged (still excludes weight to preserve user edits on re-ingest).
- Hit count: 1
- Source: QC task #11

### [2026-04-19] — auto_session_id_double_date (task 11)
- Severity: normal
- Triggers: [session_id, generate, filename, capture, date]
- Pattern: `GenerateSessionId()` returned `{date}-{hash}`, but the file path template already prepends the date as `sessions/{date}-{safeId}.md`. Result: `sessions/2026-04-19-2026-04-19-abc123.md` (double date).
- Fix: `GenerateSessionId()` returns only the random hash `Guid.NewGuid().ToString("N")[..8]`. Date is added by the path template.
- Hit count: 1
- Source: QC task #11

### [2026-04-17] — mcp_serverinfo_instructions_injection (autoresearch task 6)
- Severity: high
- Triggers: [mcp, initialize, serverInfo, instructions, identity, context]
- Pattern: MCP clients expect `serverInfo.instructions` in the `initialize` response to auto-inject vault context. Missing this means clients must explicitly call a tool to get identity — breaking L0 semantics.
- Fix: Populate `serverInfo.instructions` in `HandleInitialize()` from identity note content. Use `DefaultIgnoreCondition.WhenWritingNull` (already set) so it's omitted when no identity is configured.
- Hit count: 2
- Source: autoresearch task #6 | QC task #6 confirmed (implemented + verified correct)

### [2026-04-17] — dotnet_build_warnaserrors_invalid_flag (from retro 6)
- Severity: normal
- Triggers: [dotnet, build, warnaserrors, csproj, msbuild, qc, layer1]
- Pattern: `dotnet build --warnaserrors` fails — `--warnaserrors` is an MSBuild property, not a `dotnet build` CLI flag. Causes immediate invocation error in QC Layer 1.
- Fix: Use `dotnet build {project}` alone, or `/p:TreatWarningsAsErrors=true` if strict mode needed.
- Hit count: 1
- Source: retro analysis task #6

### [2026-04-17] — generator_commit_before_evaluator (from retro 7)
- Severity: normal
- Triggers: [git, diff, review, evaluator, build, commit, phase]
- Pattern: After Generator phase, implementation files not staged/committed → `git diff main...HEAD` shows only docs. Review and Evaluator see empty diff.
- Fix: Always `git add {impl files} && git commit` after Generator builds successfully, before entering Evaluator/review.
- Hit count: 1
- Source: retro analysis task #7

### [2026-04-17] — powershell_null_coalescing_compat (from retro 5)
- Severity: high
- Triggers: [powershell, install.ps1, ps1, windows]
- Pattern: Used PowerShell 7+ `??` null-coalescing operator when project requires PS5+ compat. Causes parse error on default Windows PS5.1.
- Fix: Replace `$x = expr ?? ""` with `$raw = expr; $x = if ($null -ne $raw) { $raw } else { "" }`
- Hit count: 1
- Source: retro analysis task #5

### [2026-05-01] — pat_scope_insufficient_for_pr_create (from retro 28)
- Severity: low
- Triggers: [pat, gh pr create, 403, sdlc, fallback, github-permissions]
- Pattern: PAT issued with minimal Contents:Write + Metadata:Read lacks Pull Requests scope, causing /sdlc phase 5 PR creation to 403. Fallback to local merge per /sdlc skill rule, but PR review record is lost.
- Fix: When issuing PAT for SDLC autonomous flow, include Pull Requests:Read+Write scope by default. Document scope ladder in docs/release-runbook.md PAT rotation section.
- Hit count: 1
- Source: retro analysis #28

### [2026-05-01] — workflow_yaml_change_qc_static_only (from retro 28)
- Severity: low
- Triggers: [workflow, github-actions, yaml, qc, smoke, real-run]
- Pattern: Workflow YAML changes pass mechanical AC grep + python yaml.safe_load but real GHA runtime semantics (if: evaluator on tag string, needs: ordering) only verified by actual workflow execution. Mechanical-only QC is necessary but not sufficient.
- Fix: For workflow YAML changes, schedule a real-tag smoke run as post-merge verification. Or use workflow_dispatch with a test ref to validate non-tag-driven paths.
- Hit count: 1
- Source: retro analysis #28

### [2026-05-01] — pat_resolver_explicit_short_circuit (from retro 31)
- Severity: low
- Triggers: [resolver, strategy, explicit-vault, walk-up, vault-locator]
- Pattern: Operators that depend on `VaultLocator.Discover().Strategy.StartsWith(...)` miss the explicit-path case — when CLI passes `--vault <path>`, resolver short-circuits to Strategy="explicit", bypassing walk-up flags. Operator-side filesystem-state checks must re-verify directly rather than trust resolver Strategy alone.
- Fix: When an operator needs filesystem-state info (layout type, structure presence), check filesystem directly. Don't rely solely on resolver Strategy.
- Hit count: 1
- Source: retro analysis #31

### [2026-05-01] — pat_design_existing_paths_unaudited (from retro 31)
- Severity: low
- Triggers: [design, paths, scope, refactor]
- Pattern: Design phase declares "X moves to Y" without grepping for existing X. When existing paths are user-global vs vault-relative, mismatch causes wasted code/revert. /design phase must grep `Path.Combine` patterns of subject identifiers before drafting integration code blocks.
- Fix: Add design checklist step: "Grep existing path computations involving the moved component". Report findings in design doc § Path computations.
- Hit count: 1
- Source: retro analysis #31
