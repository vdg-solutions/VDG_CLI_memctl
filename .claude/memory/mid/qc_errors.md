
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
