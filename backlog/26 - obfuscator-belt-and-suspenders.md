---
id: 26
type: task
title: 'Anti-RE hardening — string encoding + anti-debug + anti-tamper on AOT'
status: Todo
priority: high
tags:
- hardening
- anti-reverse-engineer
- anti-debug
- anti-tamper
created: 2026-04-30
updated: 2026-04-30
---

## Description

Native AOT (#24 shipped) strip IL nhưng còn để string literals dạng plaintext + symbol metadata trong native binary. Threat: ai đó decompile + Python rewrite memctl trong 1 ngày từ string literals + control flow + .NET BCL call sequence visible. Task này add 3 layer hardening lên trên AOT: (1) encode string literals nội bộ tại compile-time, (2) self-hash anti-tamper khi startup, (3) anti-debug attach detection.

Public surface (CLI commands, JSON wire keys, MCP protocol, --help) giữ plaintext — tools/users/AI client cần đọc được.

## Implementation

### Step 0 — Prereq fail-fast
- Verify task #24 AOT shipped + green: `git log main --oneline | grep -q 'aot'` || exit "Blocked by #24".
- Verify `dotnet build -c Release` clean: `dotnet build src/memctl/memctl.csproj -c Release --nologo -v q` || exit "Fix build first".
- Verify MSVC linker present: `where /R "C:\Program Files (x86)\Microsoft Visual Studio" link.exe` returns hits.

### Step 1 — Roslyn source generator for string encoding

- **File CREATE:** `src/Memctl.SourceGen/StringEncodeGenerator.cs` — Roslyn `IIncrementalGenerator`. Scans for calls to `Memctl.Hardening.S.Of("plaintext")` literal. At compile-time, encodes plaintext with XOR + per-string nonce (derived from string content hash). Replaces with `S.Decode(byte[]{...}, byte)`.
- **File CREATE:** `src/Memctl.SourceGen/Memctl.SourceGen.csproj` — net10.0, references `Microsoft.CodeAnalysis.CSharp` 4.x.
- **File CREATE:** `src/memctl/Hardening/S.cs` — runtime decoder. `S.Decode(byte[] buf, byte key)` returns `Encoding.UTF8.GetString(...)` post-XOR.

### Step 2 — String encoding callsite migration (manual selection)

Encode strings in: SQL queries, regex patterns, identity hint content, search_help markdown, file path templates, log messages, MCP method routing.

NEVER encode: `JsonPropertyName` attribute args, System.CommandLine option/arg names, public exception messages already in user-visible flows, README content.

- **File MODIFY:** `src/memctl/Implementations/Index/SqliteNoteIndex.cs` — wrap SQL strings via `S.Of`.
- **File MODIFY:** `src/memctl/Operators/IdentityOperator.cs` — wrap identity content template.
- **File MODIFY:** `src/memctl/Implementations/Mcp/McpServerAdapter.cs` — wrap method routing string keys ("initialize", "tools/list", "tools/call", "ping") + tool descriptions.
- **File MODIFY:** ~20 callsites total via grep audit.

### Step 3 — Anti-debug

- **File CREATE:** `src/memctl/Hardening/AntiDebug.cs`:
  ```csharp
  using System.Diagnostics;
  using System.Runtime.InteropServices;

  internal static class AntiDebug
  {
      [DllImport("kernel32.dll")] private static extern bool IsDebuggerPresent();

      public static void Check()
      {
          if (Environment.GetEnvironmentVariable("MEMCTL_ALLOW_DEBUG") == "1") return;
          if (Debugger.IsAttached || (OperatingSystem.IsWindows() && IsDebuggerPresent()))
              Environment.FailFast("");
      }
  }
  ```
- **File MODIFY:** `src/memctl/Bootstrap/Program.cs:1` — invoke `AntiDebug.Check()` first line of Main.

### Step 4 — Anti-tamper (self-hash)

- **File CREATE:** `src/memctl/Hardening/SelfHash.cs`:
  ```csharp
  internal static class SelfHash
  {
      // BAKED at build via MSBuild target — replaced post-compile.
      private const string EXPECTED = "__SELF_HASH_PLACEHOLDER__";

      public static void Verify()
      {
          if (EXPECTED.StartsWith("__SELF_HASH_PLACE")) return;  // dev build, skip
          var path = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
          using var fs = File.OpenRead(path);
          var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(fs));
          if (!hash.Equals(EXPECTED, StringComparison.OrdinalIgnoreCase))
              Environment.FailFast("");
      }
  }
  ```
- **File MODIFY:** `src/memctl/Bootstrap/Program.cs` — call `SelfHash.Verify()` after AntiDebug.
- **File CREATE:** `scripts/bake-selfhash.ps1` — runs after AOT publish, computes SHA256 of memctl.exe, replaces placeholder via byte search-and-replace in the binary itself (in-place edit).

### Step 5 — Csproj wiring

- **File MODIFY:** `src/memctl/memctl.csproj` — add `<ProjectReference Include="..\Memctl.SourceGen\Memctl.SourceGen.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`.

### Step 6 — Build + verify

```bash
dotnet publish src/memctl/memctl.csproj -c Release -r win-x64 -p:PublishAot=true -o publish-aot
pwsh scripts/bake-selfhash.ps1 -Binary publish-aot/memctl.exe
publish-aot/memctl.exe status   # should run clean
```

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| FR-1 | AOT publish 0 IL warnings post-source-gen | `grep -cE "IL2026\|IL3050" build.log` returns 0 |
| FR-2 | Source generator emits encoded strings | `grep "S.Of(" src/memctl/**/*.cs` shows ≥15 callsites |
| FR-3 | Decoded strings round-trip exact match | new test `tests/memctl.Tests/Hardening/StringEncodeTests.cs` 5+ tests pass |
| FR-4 | Public interface strings unchanged | `grep -r "JsonPropertyName" src/memctl/Boundary` returns same count pre/post |
| FR-5 | CLI surface unchanged | `memctl --help` output identical to v1.2.0 (diff < 5 lines) |
| FR-6 | MCP tools/list response identical | snapshot diff vs v1.2.0 returns 0 lines |
| FR-7 | Anti-debug check runs first | `Debugger.IsAttached` OR `IsDebuggerPresent` triggers `Environment.FailFast` (verify with debugger attached test) |
| FR-8 | Anti-tamper baked hash matches binary | tamper test: flip 1 byte in memctl.exe → run → exit non-zero |
| FR-9 | `MEMCTL_ALLOW_DEBUG=1` env var bypass | set var + attach debugger → memctl runs normally (dev escape hatch) |
| FR-10 | All 42 existing tests still pass | `dotnet test --nologo` 42/42 |
| FR-11 | Cold-start regression < 50ms vs v1.2.0 | `time memctl status` 5x average; baseline 164ms; max 214ms |
| NFR-1 | Encoded strings not visible via `strings` | binary scan for "DROP TABLE\|CREATE TABLE\|SELECT " in `publish-aot/memctl.exe` returns 0 hits |
| NFR-2 | Anti-debug code is AOT-clean | 0 reflection in Hardening/* |
| NFR-3 | scripts/bake-selfhash.ps1 idempotent | run 2x same binary, second run no-op |
| NFR-4 | README documents `MEMCTL_ALLOW_DEBUG` | grep README returns hit |

## Out of Scope

- Code signing / Authenticode (separate task).
- DRM / license server (separate concern).
- Anti-decompile native binary (out of scope — AOT alone, no Themida/VMProtect).
- BitMono / ConfuserEx integration (evaluated; not used — manual code AOT-safer).
- Encoding `JsonPropertyName` / CLI option strings (public surface, must stay plain).

## Dependencies

- Blocked by #24 (AOT shipped). Done.
- Soft depend #25 (CI extension to bake self-hash + run anti-debug verify test).

## Risk

| Risk | Mitigation |
|------|-----------|
| Source gen breaks AOT compile | Test via `dotnet publish` after each new encoded callsite; rollback callsite |
| Self-hash baking corrupts binary | Use byte search-and-replace, not arbitrary edit; backup pre-bake; verify via file probe post-bake |
| Anti-debug false positive on Windows Defender / EDR | `MEMCTL_ALLOW_DEBUG=1` escape; ship 2 flavors if too many reports |
| Determined reverser patches out check | Accept — anti-debug only blocks casual inspection; defense in depth, not absolute |
| String encoding XOR weak | Acceptable for hobby-grade. NetReactor + AES is overkill cho threat model |

## Effort

~8-10h:
- 2h: Roslyn source generator (StringEncodeGenerator.cs)
- 1h: runtime decoder S.cs
- 2h: pick + wrap ~20 callsites
- 1h: AntiDebug + SelfHash code
- 1h: bake-selfhash.ps1 script
- 1h: tests (StringEncodeTests, tamper test)
- 1h: AOT verify, smoke, snapshot diff
- 1h: docs

## User Actions Required

- (none — fully bot-actionable since #24 prereqs met)
