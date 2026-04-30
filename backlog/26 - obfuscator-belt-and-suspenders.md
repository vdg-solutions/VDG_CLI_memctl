---
id: 26
type: task
title: 'Obfuscator hardening — ConfuserEx integration belt-and-suspenders với AOT'
status: Todo
priority: low
tags:
  - hardening
  - anti-reverse-engineer
  - obfuscation
  - optional
created: 2026-04-30
updated: 2026-04-30
---

## Description

Optional follow-up to task #24 (AOT). Native AOT đã strip IL nhưng không strip:
- Embedded string literals (error messages, identity hint, search_help table, JSON property names if not stripped)
- Symbol names trong native binary nếu compile chưa strip đủ
- Embedded resources (model paths, config schemas)

Reverse engineer dùng tool như Ghidra/IDA Pro vẫn có thể recover control flow + string content từ AOT binary. Obfuscator add layer:
- String encryption (decrypt at runtime)
- Control flow flattening
- Symbol renaming
- Resource encryption

Combine với AOT = belt-and-suspenders. Không cần làm trừ khi threat model yêu cầu.

## Khi nào nên làm task này

- Threat model: đối thủ pro reverse-engineer (compete, IP theft, security research adversary).
- Sau khi #24 AOT đã ship + verify hoạt động.
- Không trước khi #24 ship — adding obfuscator vào JIT build chỉ gây flake mà không gain anti-RE thực sự (IL vẫn decompilable).

## Mục tiêu

- Strings literal trong native binary bị encrypt — `strings memctl.exe | grep -i hint` không trả ra plaintext.
- Method symbols renamed — không còn tên có nghĩa trong disassembly.
- Resource files encrypted.
- Binary vẫn chạy đúng — 0 regression.
- CI/CD vẫn build clean.

## Implementation

### Option A — ConfuserEx 2 (free, OSS, .NET community)

ConfuserEx vốn target .NET IL → áp dụng PRE-AOT compile. Workflow:
```
dotnet build -c Release  →  ConfuserEx (obfuscate IL)  →  dotnet publish -p:PublishAot=true (compile to native)
```

ConfuserEx config (`memctl.crproj`):
```xml
<project outputDir="obf" baseDir=".">
  <module path="src/memctl/bin/Release/net10.0/memctl.dll">
    <rule pattern="true" preset="aggressive">
      <protection id="rename" />
      <protection id="ctrl flow" />
      <protection id="constants" />          <!-- string encryption -->
      <protection id="resources" />
      <protection id="anti debug" />
    </rule>
  </module>
</project>
```

Run: `Confuser.CLI.exe memctl.crproj` → trả ra `obf/memctl.dll` đã obfuscated. Sau đó AOT publish từ obfuscated DLL.

### Option B — ObfuscarMembership (lightweight, less aggressive)

Chỉ rename symbols. Không encrypt string. Effort thấp hơn nhưng gain ít hơn.

### Option C — Commercial: Dotfuscator, SmartAssembly, Eazfuscator

Pricing $$. Skip unless enterprise need.

**Recommend A.**

### Pipeline integration

Thêm step trước AOT publish trong build script + CI:
```bash
# build-portable.sh additions
dotnet build src/memctl/memctl.csproj -c Release
ConfuserEx.CLI memctl.crproj
# Replace bin/Release/.../memctl.dll with obfuscated version
cp obf/memctl.dll src/memctl/bin/Release/net10.0/memctl.dll
dotnet publish src/memctl/memctl.csproj -c Release -r $RID -p:PublishAot=true ...
```

CI workflow (extend task #25):
- New job `obfuscate` between `build` and `package` per matrix
- Or run obfuscation locally và commit obfuscated assembly (avoid needing ConfuserEx in CI)

### Testing

Post-obfuscation smoke test:
- All 12+ commands run, JSON output identical to non-obfuscated build
- Reflection-dependent code paths (em dùng AOT-friendly version per #24, no reflection) — không break
- ONNX Runtime still loads model
- Strings inspection: `strings memctl.exe | grep "search_help\|hint\|memctl"` trả 0 hits của business logic (only system runtime strings)

## Acceptance Criteria

| ID | Criterion | Verify |
|---|---|---|
| FR-1 | Build pipeline tích hợp obfuscator step | grep build script |
| FR-2 | Obfuscated binary chạy đầy đủ command + MCP tools | smoke test full surface |
| FR-3 | `strings memctl.exe \| grep -E "memctl-result-mapper\|MemctlOutcome\|HookStatus"` trả 0 hits | run after build |
| FR-4 | ILSpy/Ghidra inspection: no readable method names | manual inspection |
| FR-5 | Binary size delta < 30% vs non-obfuscated AOT | compare sizes |
| FR-6 | All 24 mapper unit tests pass post-obfuscation (test against obfuscated assembly) | dotnet test với reference đã swap |
| NFR-1 | ConfuserEx config committed at repo root | check `memctl.crproj` exists |
| NFR-2 | Obfuscation preserves anti-debug detection (optional protection) | manual verify |
| NFR-3 | License compatibility — ConfuserEx MIT, OK for commercial use | review LICENSE |

## Out of Scope

- Anti-tamper / integrity check (separate task).
- Code signing / Authenticode (separate task).
- Runtime license verification (DRM — separate concern, not anti-RE).

## Dependencies

- **Blocked by task #24** (AOT) — obfuscator chỉ gain trên top of AOT-ready code.
- **Soft depend task #25** (CI/CD) — wiring obfuscator vào CI dễ hơn nếu CI đã set up.

## Risk

- **High**: Aggressive obfuscation có thể break code paths sử dụng reflection. Mitigation: task #24 đã eliminate reflection — obfuscator chỉ cần worry về remaining BCL reflection (System.Text.Json source-gen has zero runtime reflection nếu wired đúng).
- **Medium**: Anti-debug protection có thể fail trên user dev machine nếu họ debug for legitimate reasons. Mitigation: ship 2 build flavors hoặc remove anti-debug protection.
- **Low**: Binary size growth. Mitigation: profile, toggle individual ConfuserEx protections.

## Effort

~4-6h:
- 1h: install ConfuserEx, write `memctl.crproj` config
- 1h: integrate into `build-portable.sh`
- 1h: integrate into GitHub Actions workflow (task #25 extension)
- 1-2h: smoke test all command surface + adjust protections that break things
- 1h: documentation

## Notes

- This task is **optional**. Native AOT alone (#24) đủ cho 90% use case. Obfuscator chỉ thêm friction cho determined attacker.
- Realistic threat assessment: nếu code có business logic độc đáo (proprietary algorithm) → đáng làm. Nếu chỉ là tool wrapper quanh public APIs (Obsidian vault, ONNX Runtime, OpenAI) → không gain RE protection nào đáng kể, vì attacker có thể chỉ gọi same APIs trực tiếp.
- Em recommend: ship #24 + #25 trước, đánh giá xem có ai actually try reverse-engineer trong 3-6 tháng. Nếu có dấu hiệu → activate #26. Nếu không → giữ optional.
