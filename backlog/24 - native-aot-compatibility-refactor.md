---
id: 24
type: task
title: Native AOT compatibility refactor — JsonSerializerContext, YamlDotNet static, manual validation
status: Done
priority: high
tags:
- aot
- hardening
- anti-reverse-engineer
- performance
- architecture
created: 2026-04-30
updated: 2026-04-30
---

## Description

Compile memctl bằng **Native AOT** (`-p:PublishAot=true`) để strip IL → giảm khả năng reverse-engineer + cải thiện startup time + giảm RAM. Dry-run AOT publish hiện tại fail vì:

1. **Platform linker** — `error: Platform linker not found.` Cần Visual Studio Build Tools với C++ workload (Windows). Linux/macOS dùng `clang`/`ld` sẵn có.
2. **~30 IL2026/IL3050 compatibility warnings** trong codebase — reflection-based APIs không AOT-friendly. Phải refactor mỗi vị trí dùng:
   - `System.Text.Json.JsonSerializer.Serialize<T>(...)` / `Deserialize<T>(...)` (~12 chỗ)
   - `YamlDotNet.Serialization.DeserializerBuilder` (1 chỗ — `ObsidianVaultReader.cs:17`)
   - `System.ComponentModel.DataAnnotations.Validator.TryValidateObject(...)` (1 chỗ — `RequestValidator.cs:15`)

## Mục tiêu

- `dotnet publish -p:PublishAot=true -r <rid>` chạy clean, 0 warning, 0 error trên cả 4 platform (win-x64, linux-x64, osx-arm64, osx-x64).
- Binary là native machine code, không chứa IL → decompile bằng ILSpy/dotPeek không trả ra C# source.
- Không regression behavior — mọi command CLI + MCP tool vẫn pass smoke test post-AOT.
- Binary size ≤ JIT single-file build hiện tại (~75 MB target; AOT thường giảm 30-50%).
- Startup time < JIT (target: cold-start `memctl status` < 100ms thay vì ~500ms JIT).

## Ngữ cảnh

Sau task #14 Contract First, mọi DTO đã typed → JsonSerializer source generator áp dụng được sạch sẽ (mỗi DTO đăng ký 1 lần ở `JsonSerializerContext`). Nếu chưa có #14, refactor này khó hơn nhiều vì anonymous Data shapes không thể đăng ký type.

## Implementation

### Step 0 — Prereq fail-fast
- Windows: `where link.exe` || exit "[USER-ACTION-REQUIRED] Install VS Build Tools 2022 + 'Desktop development with C++' workload".
- Linux: `which clang ld` || exit "Install: `apt install -y clang libc6-dev`".
- macOS: `xcode-select -p` || exit "Install: `xcode-select --install`".
- Verify .NET SDK 10.x: `dotnet --version | grep ^10` || exit "Install .NET 10 SDK".

### 1. Prerequisites (one-time setup, document in README)

**Windows build host:**
```
Visual Studio Build Tools 2022
└── Workloads: Desktop development with C++
    └── Components: MSVC v143 x64/x86, Windows 10/11 SDK
```
Verify: `where link.exe` trả về path.

**Linux (Ubuntu/Debian):** `apt install -y clang libc6-dev`
**macOS:** `xcode-select --install`

### 2. JsonSerializer migration → JsonSerializerContext source generation

**File CREATE:** `src/memctl/Boundary/MemctlJsonContext.cs`

```csharp
using System.Text.Json.Serialization;
using Memctl.Boundary.Requests;

namespace Memctl.Boundary;

[JsonSerializable(typeof(MemctlResult))]
[JsonSerializable(typeof(NoteDto))]
[JsonSerializable(typeof(NoteListResultDto))]
[JsonSerializable(typeof(SearchResultDto))]
[JsonSerializable(typeof(SearchTagsResultDto))]
[JsonSerializable(typeof(SearchLinksResultDto))]
[JsonSerializable(typeof(SearchDateResultDto))]
[JsonSerializable(typeof(TagDto))]
[JsonSerializable(typeof(TagsListResultDto))]
[JsonSerializable(typeof(StatsDto))]
[JsonSerializable(typeof(GrepHitDto))]
[JsonSerializable(typeof(GrepListResultDto))]
[JsonSerializable(typeof(WeightChangeDto))]
[JsonSerializable(typeof(DecayReportDto))]
[JsonSerializable(typeof(VaultStatusDto))]
[JsonSerializable(typeof(CaptureReportDto))]
[JsonSerializable(typeof(IngestReportDto))]
[JsonSerializable(typeof(OrganizeReportDto))]
[JsonSerializable(typeof(ModelInfoDto))]
[JsonSerializable(typeof(ModelEntryDto))]
[JsonSerializable(typeof(ModelListDto))]
[JsonSerializable(typeof(ModelSelectionDto))]
[JsonSerializable(typeof(VaultRefDto))]
[JsonSerializable(typeof(LintReportDto))]
[JsonSerializable(typeof(LintStructuralDto))]
[JsonSerializable(typeof(LintOrphanDto))]
[JsonSerializable(typeof(LintBrokenLinkDto))]
[JsonSerializable(typeof(LintDuplicateDto))]
[JsonSerializable(typeof(LintDecayRiskDto))]
[JsonSerializable(typeof(HookStatusDto))]
[JsonSerializable(typeof(HookLogEntryDto))]
[JsonSerializable(typeof(MigrateTagsReportDto))]
[JsonSerializable(typeof(SetWeightRequest))]
[JsonSerializable(typeof(DecayRequest))]
[JsonSerializable(typeof(AddNoteRequest))]
[JsonSerializable(typeof(MemctlConfig))]   // for config persistence
[JsonSerializable(typeof(CapturePayload))] // for hook stdin parse
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class MemctlJsonContext : JsonSerializerContext { }
```

**File MODIFY:**
- `src/memctl/Bootstrap/ResultPrinter.cs`: `JsonSerializer.Serialize(result, JsonOpts)` → `JsonSerializer.Serialize(result, MemctlJsonContext.Default.MemctlResult)`
- `src/memctl/Implementations/Config/MemctlConfig.cs`: `Deserialize<MemctlConfig>(json, opts)` → `Deserialize(json, MemctlJsonContext.Default.MemctlConfig)`
- `src/memctl/Implementations/Index/SqliteNoteIndex.cs`: similar swap for tag/link arrays — define `string[]` source-gen entry
- `src/memctl/Implementations/Mcp/McpServerAdapter.cs`: tool response wrapping uses context
- `src/memctl/Implementations/Llm/OpenAiLlmClient.cs`: chat completion request/response — define DTO types, register in context
- `src/memctl/Operators/LintOperator.cs:236` — semantic LLM JSON parse: register response type
- `src/memctl/Bootstrap/Program.cs:671` — `CapturePayload` parse uses context

### 3. YamlDotNet → StaticDeserializerBuilder or hand-parse

**Decision matrix:**
- Option A: YamlDotNet StaticDeserializerBuilder (requires source generator package). Effort medium, keeps API similar.
- Option B: Hand-parsed frontmatter using regex/line scan. memctl frontmatter is shallow (id, title, tags, links, created, modified, weight, archived) — em ước ~50 LOC.
- Option C: Replace YamlDotNet with `YamlSourceGen` (community) or `VYaml` (AOT-friendly).

Recommend **B** — frontmatter shape is fixed (no nested YAML, no advanced features), hand-parser is small and AOT-clean. Drop YamlDotNet dependency entirely.

**File MODIFY:** `src/memctl/Implementations/Vault/ObsidianVaultReader.cs` — replace `DeserializerBuilder().Build().Deserialize<...>` with `ParseFrontmatter(string)` returning `Dictionary<string, object>` or directly populating Note record.

**Csproj:** drop `<PackageReference Include="YamlDotNet" />`.

### 4. DataAnnotations Validator → per-Request manual Validate()

**File MODIFY:** Each Request DTO gains `Validate()` method returning `IReadOnlyList<string>` of error messages.

```csharp
public sealed class SetWeightRequest
{
    public string Id     { get; init; } = "";
    public float  Weight { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errs = new List<string>();
        if (string.IsNullOrWhiteSpace(Id))           errs.Add("id: required");
        if (Id.Length > 256)                         errs.Add("id: max 256 chars");
        if (Weight is < 0.0f or > 2.0f)              errs.Add("weight: must be in [0.0, 2.0]");
        return errs;
    }
}
```

`RequestValidator.Validate()` becomes thin wrapper that calls `request.Validate()` and formats outcome.

DataAnnotations attributes remain on DTOs as documentation but no longer evaluated at runtime. Or remove entirely.

### 5. ONNX Runtime AOT verification

ONNX Runtime claims AOT support since 1.18. memctl uses 1.22 — should be fine. Verify post-build:
- `memctl model download` → loads model
- `memctl ingest` → embeds notes
- `memctl search` → semantic search round-trip
If failure: file issue with onnxruntime, fallback to ILCompiler exclusion `<TrimmerRootAssembly Include="Microsoft.ML.OnnxRuntime" />` in csproj.

### 6. SQLite (Microsoft.Data.Sqlite) AOT verification

Microsoft.Data.Sqlite 9.0 supports AOT. Verify by running existing tests post-build. If reflection-based JSON columns fail → migrate JSON column read/write to context-generated.

### 7. Csproj modifications

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>false</InvariantGlobalization>  <!-- need date parsing -->
  <DebugType>none</DebugType>
  <DebugSymbols>false</DebugSymbols>
  <StripSymbols>true</StripSymbols>
  <IlcGenerateStackTraceData>false</IlcGenerateStackTraceData>
  <TrimmerSingleWarn>true</TrimmerSingleWarn>
</PropertyGroup>
```

### 8. build-portable.sh update

Add `-p:PublishAot=true` to `dotnet publish` invocation. Drop `-p:PublishSingleFile=true` (AOT is already single file by nature, conflicting flag).

### 9. tests/memctl.Tests AOT compatibility

Test project hiện tại dùng xUnit reflection. Tests **không cần** AOT — chỉ build product cần AOT. Test project giữ JIT.

## Acceptance Criteria

| ID | Criterion | Verify |
|---|---|---|
| FR-1 | `dotnet publish -c Release -r win-x64 -p:PublishAot=true` exits 0 | run command, check exit code |
| FR-2 | Same on linux-x64, osx-arm64, osx-x64 | matrix run |
| FR-3 | Zero AOT warnings (IL2026, IL3050, IL3056) | grep build output |
| FR-4 | Resulting binary < 80 MB win-x64 (target: ~50 MB) | `ls -la dist/.../memctl.exe` |
| FR-5 | Cold-start `memctl status` < 200 ms (vs ~500ms JIT) | `time memctl status` 5x, average |
| FR-6 | All 12 smoke commands pass post-AOT (init/add/search/list/stats/status/weight/decay/ingest/tags/grep/lint/capture/identity/model list/migrate-tags/hook-status) | run each, parse JSON, assert success |
| FR-7 | MCP `tools/list` returns 15 tools post-AOT | echo + dotnet run |
| FR-8 | ILSpy decompile of AOT binary returns no readable C# (only disassembly) | open in ILSpy, confirm |
| FR-9 | YamlDotNet removed from csproj | `git diff src/memctl/memctl.csproj` |
| FR-10 | All existing 24 mapper unit tests still pass | `dotnet test` |
| NFR-1 | Build doc in README updated with AOT prereqs (VS Build Tools) | review |
| NFR-2 | No regression in wire format (snapshot diff zero against pre-AOT v1.2.0 outputs) | run each command, diff JSON |
| NFR-3 | Binary verified static-linked (no missing DLL on fresh Windows) | run on clean Windows VM |

## Out of Scope

- Obfuscator integration (task #26).
- GitHub Actions CI/CD release pipeline (task #25).
- Multi-arch ARM64 Linux (only x64 + macOS arm64 for v1).
- Trimming non-AOT JIT build paths.

## Dependencies

- Soft depend task #14 (typed DTO contract — required for clean JsonSerializerContext registration).
- Soft depend task #23 (Request DTOs already exist — easier validation refactor).

## Risk

- **High**: ONNX Runtime AOT may have edge cases (model loading via reflection, dynamic ops). Mitigation: test exhaustively post-build; fall back to JIT-only on platforms where AOT broken.
- **Medium**: YamlDotNet removal touches `ParseNote` hot path. Mitigation: parallel implementation, gate via flag, swap atomically.
- **Medium**: AOT linker errors on Linux/macOS may surface only on those runners. Mitigation: do CI matrix testing early.
- **Low**: Binary size regression. Mitigation: profile with `dotnet-symbol`/`size`; toggle individual trimming flags.

## Effort

~10-14h:
- 1h: setup VS Build Tools + linker verification per platform
- 4h: JsonSerializerContext source-gen migration (define context + ~12 callsite swaps + smoke test)
- 2h: YamlDotNet → hand-parsed frontmatter (write parser + replace usage + diff verify against original)
- 1h: DataAnnotations → per-Request Validate()
- 2h: ONNX/SQLite verification + fallback config
- 2h: build-portable.sh update + AOT csproj props + binary size profiling
- 1h: README AOT prereqs doc
- 1h: smoke test full command surface + snapshot diff

## Notes

- Em đã trial AOT publish trên main pre-task, gặp đúng 2 blocker categories (linker + IL2026/IL3050). Tasks này địa chỉ cả hai.
- Nếu ONNX Runtime AOT fails → fallback strategy: ship 2 build flavors (AOT-stripped JIT-only, full AOT) per platform. Document trade-off cho user.

## Comments

**2026-04-30 19:39 user:** Phase 0: env probe pass. MSVC v14.50.35717 found. Starting /sdlc pipeline.

**2026-04-30 20:09 user:** Phase 6 complete: merged to main. AOT win-x64 0 warnings, 13MB binary, 164ms cold-start, 42/42 tests, wire format unchanged.
