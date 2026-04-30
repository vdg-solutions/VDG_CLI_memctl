# VDG_CLI_memctl

Obsidian-compatible personal memory vault CLI with hybrid BM25 + semantic search.

## Installation

### Option A — dotnet global tool (requires .NET 10 SDK)

```bash
# build the package
bash build-tool.sh

# install globally
dotnet tool install -g memctl --add-source ./nupkg

# upgrade
dotnet tool update -g memctl --add-source ./nupkg

# uninstall
dotnet tool uninstall -g memctl
```

### Option B — portable binary (no .NET SDK required)

```bash
# build portable binaries for all platforms
bash build-portable.sh
```

**Linux / macOS:**
```bash
bash install.sh
```

**Windows (PowerShell):**
```powershell
.\install.ps1
```

> If blocked by execution policy: `Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser`

### Option C — Native AOT (anti-reverse-engineer + fast startup)

Native AOT compiles to machine code, strips IL, ~10x faster cold start (~150ms vs 500ms JIT).

**Prereq (one-time per host):**

| OS | Install |
|----|---------|
| Windows | VS Build Tools 2022/2026 + "Desktop development with C++" workload (MSVC + Win11 SDK ~4GB). Also requires `vswhere.exe` at `C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe` (download from https://github.com/microsoft/vswhere/releases). Add Installer dir to PATH so `vcvars` internal calls work. |
| Linux  | `apt install -y clang libc6-dev` |
| macOS  | `xcode-select --install` |

**Build:**

```bash
# Windows (PowerShell — set PATH so vswhere resolves):
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;$env:PATH"
dotnet publish src/memctl/memctl.csproj -c Release -r win-x64 -p:PublishAot=true -o dist/aot/win-x64

# Linux / macOS:
dotnet publish src/memctl/memctl.csproj -c Release -r linux-x64  -p:PublishAot=true -o dist/aot/linux-x64
dotnet publish src/memctl/memctl.csproj -c Release -r osx-arm64  -p:PublishAot=true -o dist/aot/osx-arm64
```

Output: ~13MB single binary (vs ~75MB JIT single-file). 0 IL2026/IL3050 warnings expected.
