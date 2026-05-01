# Requirements Spec: Native AOT compatibility refactor

**Task:** #24
**Date:** 2026-04-30
**Status:** Final
**Source:** `backlog/24 - native-aot-compatibility-refactor.md` (canonical detail)

---

## 1. Overview

Compile memctl bằng Native AOT (`-p:PublishAot=true`) trên 4 platform (win-x64, linux-x64, osx-arm64, osx-x64) với 0 warning + 0 error. Refactor reflection-based APIs (System.Text.Json, YamlDotNet, DataAnnotations.Validator) sang AOT-friendly equivalents (JsonSerializerContext source-gen, hand-parse frontmatter, manual Validate methods). Anti-RE benefit: native machine code thay IL → ILSpy decompile fails.

## 2. User Stories

- As distributor, em want AOT binary để user reverse-engineer khó hơn JIT IL.
- As user, em want startup `< 200ms` thay vì ~500ms JIT.
- As maintainer, em want build pipeline fail fast nếu prereq thiếu (linker, SDK).

## 3. Functional Requirements

| ID | Requirement | Priority | Test | Acceptance Criteria |
|----|------------|----------|------|---------------------|
| FR-1 | AOT publish win-x64 clean | Must | [unit] | `dotnet publish -c Release -r win-x64 -p:PublishAot=true` exit 0, 0 warning |
| FR-2 | AOT publish linux-x64/osx-arm64/osx-x64 clean | Must | [unit] | matrix run, all exit 0 (defer non-Win to CI #25) |
| FR-3 | Zero IL2026/IL3050/IL3056 warnings | Must | [unit] | `grep -E "IL(2026\|3050\|3056)" build.log` returns 0 hits |
| FR-4 | Binary size < 80 MB win-x64 | Should | [unit] | `ls -la dist/.../memctl.exe` |
| FR-5 | Cold-start `memctl status` < 200 ms | Should | [unit] | 5x time runs, average < 200ms |
| FR-6 | All 12+ smoke commands pass post-AOT | Must | [unit] | each command returns exit 0 + valid JSON |
| FR-7 | MCP `tools/list` returns ≥15 tools | Must | [unit] | echo + dotnet run, parse response |
| FR-8 | ILSpy decompile returns no readable C# | Should | [unit] | open binary in ILSpy, manual confirm |
| FR-9 | YamlDotNet removed from csproj | Must | [unit] | `grep YamlDotNet src/memctl/memctl.csproj` returns 0 hits |
| FR-10 | Existing 24 mapper unit tests still pass | Must | [unit] | `dotnet test --filter MemctlResultMapperTests` 24/24 pass |

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance Criteria |
|----|----------|------------|---------------------|
| NFR-1 | Documentation | README có AOT prereq section | grep `## AOT Build` README.md returns hit |
| NFR-2 | Compatibility | Wire format unchanged vs v1.2.0 | snapshot diff vs `tests/.../baseline/` returns 0 differences |
| NFR-3 | Portability | Binary static-linked, no missing DLL | `memctl.exe` chạy trên fresh Windows VM |

## 5. Edge Cases & Error Scenarios

1. **VS Build Tools missing**: Step 0 prereq fail-fast với install instruction.
2. **ONNX Runtime AOT incompatibility**: fallback `<TrimmerRootAssembly Include="Microsoft.ML.OnnxRuntime"/>`.
3. **SQLite reflection JSON column**: migrate to context-generated read/write nếu fail.
4. **YAML edge case** (multi-line value, escaped colon): hand-parser handles common cases, falls back to raw string for unknowns.
5. **Existing JIT user installs**: backward compat — JIT build path không xóa, AOT additive.

## 6. Out of Scope

- Obfuscator (#26)
- GitHub Actions CI/CD (#25)
- ARM64 Linux (only x64 + macOS arm64)
- Trimming JIT-only build paths

## 7. Dependencies

- Soft depend #14 (typed DTOs — required for clean JsonSerializerContext registration) ✅ Done
- Soft depend #23 (Request DTOs exist) ✅ Done
- Hard prereq: VS Build Tools 2022 + Desktop dev C++ (user-installed)

## 8. Open Questions

None. Implementation order + decisions resolved in backlog #24.

## 9. QC Checklist

- [ ] FR-1 AOT publish win-x64 0 warning
- [ ] FR-3 IL warnings 0
- [ ] FR-6 12 smoke commands pass
- [ ] FR-7 MCP tools list ≥15
- [ ] FR-9 YamlDotNet drop
- [ ] FR-10 24 mapper tests pass
- [ ] NFR-2 snapshot diff zero
- [ ] Step 0 prereq fail-fast verified
