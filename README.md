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
