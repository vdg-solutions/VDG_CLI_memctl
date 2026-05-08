# VDG_CLI_memctl

Obsidian-compatible personal memory vault CLI with hybrid BM25 + semantic search.

## Installation

### Online — from GitHub Releases (no .NET SDK, no build)

**Linux / macOS:**
```bash
curl -fsSL https://raw.githubusercontent.com/vdg-solutions/memctl-releases/main/install.sh | bash
```

**Windows (PowerShell):**
```powershell
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/vdg-solutions/memctl-releases/main/Install.ps1" -OutFile "$env:TEMP\memctl-install.ps1"; & "$env:TEMP\memctl-install.ps1"
```

Both scripts detect platform, fetch the latest release from GitHub Releases, verify the download, and add to PATH.

### From source (dev / CI)

```bash
# Native AOT (no .NET SDK on target required)
dotnet publish src/memctl/memctl.csproj -c Release -r win-x64 -p:PublishAot=true -o dist/aot/win-x64
dotnet publish src/memctl/memctl.csproj -c Release -r linux-x64 -p:PublishAot=true -o dist/aot/linux-x64
dotnet publish src/memctl/memctl.csproj -c Release -r osx-arm64 -p:PublishAot=true -o dist/aot/osx-arm64
```

> AOT prereqs: Windows — VS Build Tools 2022+ with "Desktop development with C++"; Linux — `clang libc6-dev`; macOS — `xcode-select --install`.

## Integrating with other AI tools

memctl is AI-tool-agnostic. Any tool that supports [MCP (Model Context Protocol)](https://modelcontextprotocol.io) can use it directly — no plugin required.

### MCP mode (recommended — zero maintenance)

Start the MCP server:

```bash
memctl mcp              # auto-detects vault from cwd
memctl mcp --vault /path/to/.memctl  # explicit vault
```

Wire it into your tool's MCP config. Example for **Cursor** (`~/.cursor/mcp.json`):

```json
{
  "mcpServers": {
    "memctl": {
      "command": "memctl",
      "args": ["mcp"]
    }
  }
}
```

Same config format works for **Cline**, **VS Code MCP extension**, **Windsurf**, and any MCP-compatible host.

MCP tools exposed: `search`, `search_semantic`, `search_tags`, `search_date`, `search_links`, `get`, `list`, `create`, `update`, `append`, `delete`, `set_weight`, `get_identity`.

### Hook plugins (fallback — for tools without MCP support)

For tools that don't support MCP, a thin hook plugin wires `memctl capture` and `memctl context-inject` into the tool's event hooks. See `plugins/memctl-claude/` as the canonical example and `docs/plugin-guide.md` for authoring instructions.
