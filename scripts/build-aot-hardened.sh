#!/usr/bin/env bash
# Build hardened AOT memctl binary.
# Pipeline: dotnet publish -p:PublishAot=true -p:Obfuscate=true → MSBuild target runs BitMono between Compile + IlcCompile → bake SelfHash trailer.
#
# Usage: bash scripts/build-aot-hardened.sh [rid]   (default rid: win-x64)

set -euo pipefail

RID="${1:-win-x64}"
PROJ="src/memctl/memctl.csproj"
OUT="dist/aot-hardened/$RID"

command -v dotnet           >/dev/null || { echo "dotnet missing"; exit 1; }
command -v bitmono.console  >/dev/null || { echo "bitmono.console missing — install: dotnet tool install -g BitMono.GlobalTool"; exit 1; }

if [[ "$RID" == win-* ]]; then
  ext=".exe"
  export PATH="/c/Program Files (x86)/Microsoft Visual Studio/Installer:$PATH"
else
  ext=""
fi

echo "[1/2] AOT publish + BitMono obfuscation ($RID)..."
rm -rf "$OUT"
dotnet publish "$PROJ" -c Release -r "$RID" -p:PublishAot=true -p:Obfuscate=true -o "$OUT"

BIN_OUT="$OUT/memctl$ext"
[[ -f "$BIN_OUT" ]] || { echo "Publish output missing: $BIN_OUT"; exit 1; }

echo "[2/2] Bake SelfHash sentinel..."
python3 -c "
import hashlib, sys
p = sys.argv[1]
with open(p, 'rb') as f: data = f.read()
h = hashlib.sha256(data).hexdigest()
with open(p, 'ab') as f: f.write(b'\nMEMCTL_SHA:' + h.encode())
print(f'baked hash: {h}')
" "$BIN_OUT"

ls -la "$BIN_OUT"
echo ""
echo "Hardened binary: $BIN_OUT"
