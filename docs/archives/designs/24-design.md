# Technical Design: Native AOT compatibility refactor

**Spec:** docs/specs/24-spec.md
**Task:** #24
**Date:** 2026-04-30
**Status:** Final

---

## 1. Architecture Overview

3 reflection-heavy subsystems converted to AOT-friendly equivalents while preserving public API + wire format. No A.D.D layer changes; modifications confined to existing layers (Boundary, Bootstrap, Operators, Implementations).

### System Context

```
JIT (current)                     AOT (target)
─────────────                     ─────────────
JsonSerializer.Serialize<T>  →    JsonSerializer.Serialize(value, MemctlJsonContext.Default.T)
YamlDotNet DeserializerBuilder →  FrontmatterParser.Parse(string)
DataAnnotations.Validator    →    request.Validate() returns IReadOnlyList<string>
```

---

## 2. File Changes

### New Files

| File | Purpose | Key Exports |
|------|---------|-------------|
| `src/memctl/Boundary/MemctlJsonContext.cs` | Source-gen JSON context for all DTOs/requests | `partial class MemctlJsonContext : JsonSerializerContext` |
| `src/memctl/Implementations/Vault/FrontmatterParser.cs` | Hand-parse Obsidian-style YAML frontmatter | `static Dictionary<string,object?> Parse(string raw)` |

### Modified Files

| File | Changes | Reason |
|------|---------|--------|
| `src/memctl/memctl.csproj` | Add AOT props; drop `YamlDotNet` PackageReference | FR-9 + AOT enable |
| `src/memctl/Bootstrap/ResultPrinter.cs:14` | Use context typeinfo | FR-3 |
| `src/memctl/Bootstrap/Program.cs:671` | Use `MemctlJsonContext.Default.CapturePayload` | FR-3 |
| `src/memctl/Implementations/Config/MemctlConfig.cs:27,39` | Context-bound (de)serialize | FR-3 |
| `src/memctl/Implementations/Index/SqliteNoteIndex.cs:24,25,386,387` | Use `MemctlJsonContext.Default.StringArray` | FR-3 |
| `src/memctl/Implementations/Mcp/McpServerAdapter.cs:47,53,392` | Context-bound serialize | FR-3 |
| `src/memctl/Implementations/Llm/OpenAiLlmClient.cs:56` | Define `OpenAi*Dto` typed records, register in context | FR-3 |
| `src/memctl/Operators/LintOperator.cs:236` | Replace anonymous request with typed record + context | FR-3 |
| `src/memctl/Implementations/Vault/ObsidianVaultReader.cs:6,7,17` | Replace YamlDotNet with `FrontmatterParser` | FR-9 |
| `src/memctl/Boundary/Requests/RequestValidator.cs:15` | Call `request.Validate()` switch dispatch | AOT-friendly |
| `src/memctl/Boundary/Requests/SetWeightRequest.cs` | Add `Validate()` method | AOT-friendly |
| `src/memctl/Boundary/Requests/DecayRequest.cs` | Add `Validate()` method | AOT-friendly |
| `src/memctl/Boundary/Requests/AddNoteRequest.cs` | Add `Validate()` method | AOT-friendly |

---

## 3. MemctlJsonContext registration

Register every serializable type once. Includes:

```csharp
[JsonSerializable(typeof(MemctlResult))]
[JsonSerializable(typeof(string[]))]                  // SqliteNoteIndex tags/links columns
[JsonSerializable(typeof(JsonElement))]               // McpServerAdapter raw read
[JsonSerializable(typeof(MemctlConfig))]
[JsonSerializable(typeof(CapturePayload))]
[JsonSerializable(typeof(SetWeightRequest))]
[JsonSerializable(typeof(DecayRequest))]
[JsonSerializable(typeof(AddNoteRequest))]
[JsonSerializable(typeof(OpenAiChatRequest))]         // new — OpenAiLlmClient
[JsonSerializable(typeof(OpenAiChatResponse))]
[JsonSerializable(typeof(LintSemanticRequest))]       // new — LintOperator
[JsonSerializable(typeof(LintSemanticResponse))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class MemctlJsonContext : JsonSerializerContext { }
```

Decision: single naming policy per context. Where snake_case needed (LLM API), introduce second context `MemctlSnakeCaseContext` partial class with its own options block.

---

## 4. FrontmatterParser design

```csharp
namespace Memctl.Implementations.Vault;

public static class FrontmatterParser
{
    public static Dictionary<string, object?> Parse(string raw)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(raw)) return dict;
        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
            var sep = trimmed.IndexOf(':');
            if (sep <= 0) continue;
            var key   = trimmed[..sep].Trim();
            var value = trimmed[(sep + 1)..].Trim();
            dict[key] = ParseValue(value);
        }
        return dict;
    }

    private static object? ParseValue(string s)
    {
        if (s.Length == 0) return "";
        // [a, b, c] inline array
        if (s.StartsWith("[") && s.EndsWith("]"))
        {
            var inner = s[1..^1];
            if (inner.Length == 0) return Array.Empty<string>();
            return inner.Split(',').Select(x => x.Trim().Trim('"', '\'')).ToArray();
        }
        // quoted string
        if ((s.StartsWith('"') && s.EndsWith('"')) || (s.StartsWith('\'') && s.EndsWith('\'')))
            return s[1..^1];
        // bool
        if (s.Equals("true", StringComparison.OrdinalIgnoreCase))  return true;
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        // numeric
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        return s;
    }
}
```

ObsidianVaultReader caller pulls typed values via `dict.TryGetValue("tags", out var v)` + cast.

Multi-line YAML lists (`tags:\n  - a\n  - b`) — out of scope; vault uses inline `[a, b]` per memctl convention.

---

## 5. Validate() pattern

Each Request class gains:

```csharp
public IReadOnlyList<string> Validate()
{
    var errs = new List<string>();
    if (string.IsNullOrWhiteSpace(Id)) errs.Add("id: required");
    if (Weight is < 0.0f or > 2.0f)    errs.Add("weight: must be in [0.0, 2.0]");
    return errs;
}
```

`RequestValidator.Validate(object request)` becomes:

```csharp
public static IReadOnlyList<string> Validate(object request) => request switch
{
    SetWeightRequest s => s.Validate(),
    DecayRequest     d => d.Validate(),
    AddNoteRequest   a => a.Validate(),
    _ => throw new InvalidOperationException($"No validator for {request.GetType().Name}")
};
```

DataAnnotations `[Required]` etc. attributes left on properties for IDE/doc but no runtime evaluation.

---

## 6. Csproj AOT props

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>false</InvariantGlobalization>
  <DebugType>none</DebugType>
  <DebugSymbols>false</DebugSymbols>
  <StripSymbols>true</StripSymbols>
  <IlcGenerateStackTraceData>false</IlcGenerateStackTraceData>
  <TrimmerSingleWarn>true</TrimmerSingleWarn>
</PropertyGroup>
```

---

## 7. Implementation Order

1. Step 0 — verify linker (already done — MSVC v14.50)
2. Create `MemctlJsonContext.cs` baseline (only top-level types)
3. Migrate ResultPrinter + Program.cs:671 + Config + SqliteNoteIndex JSON callsites
4. Build JIT — verify 0 IL2026 warnings on these
5. Define OpenAi*Dto + LintSemantic*Dto records, migrate OpenAiLlmClient + LintOperator
6. Migrate McpServerAdapter (more complex — JsonElement raw read needs special handling)
7. Create `FrontmatterParser.cs` + tests
8. Replace YamlDotNet usage in `ObsidianVaultReader.cs`, drop NuGet ref
9. Add `Validate()` methods to Request DTOs, refactor `RequestValidator`
10. Add AOT props to csproj
11. `dotnet publish -c Release -r win-x64 -p:PublishAot=true` → expect 0 warning
12. Smoke test 12 commands + MCP tools/list
13. Snapshot diff vs v1.2.0 — wire format unchanged

---

## 8. Testing Strategy

| Level | What | Where | Count |
|-------|------|-------|-------|
| Unit | FrontmatterParser edge cases | `tests/memctl.Tests/Vault/FrontmatterParserTests.cs` | ~10 |
| Unit | Validate() per Request | `tests/memctl.Tests/Requests/ValidateTests.cs` | ~6 |
| Unit | Existing 24 mapper tests | unchanged | 24 |
| Integration | AOT publish exit 0 + size check | `scripts/aot-smoke.sh` | 4 platforms |
| E2E (Layer 2.5) | 12 commands smoke post-AOT | `scripts/aot-smoke.sh` | 12 scenarios |

---

## 9. Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| ONNX Runtime AOT incompat | TrimmerRootAssembly fallback; if hard fail, defer to Linux/macOS CI build |
| SQLite AOT JSON column | Use `string[]` context entry; if reflection still kicks in, hand-serialize |
| Hand-parser misses YAML edge case | Unit test against existing vault notes pre-merge; rollback to YamlDotNet if regression |
| Build time blow-up (AOT 5-10x slower) | Accept; CI runs in parallel matrix |

---

## 10. Traceability

| Requirement | Section | Files |
|-------------|---------|-------|
| FR-1, FR-2 | §6 csproj | `memctl.csproj` |
| FR-3 | §3 context | `MemctlJsonContext.cs`, ~12 callsites |
| FR-9 | §4 parser | `FrontmatterParser.cs`, `ObsidianVaultReader.cs` |
| FR-10 | §8 testing | unchanged mapper tests |
| NFR-2 | §7 step 13 | snapshot diff |
