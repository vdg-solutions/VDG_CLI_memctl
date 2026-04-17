#!/usr/bin/env bash
# build-tool.sh — builds dotnet tool nupkg for global install
# Usage: bash build-tool.sh [version]
# Output: nupkg/memctl.{VERSION}.nupkg

set -euo pipefail

VERSION="${1:-$(git describe --tags --exact-match 2>/dev/null || git rev-parse --short HEAD 2>/dev/null || echo "dev")}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$SCRIPT_DIR/src/memctl/memctl.csproj"
OUT="$SCRIPT_DIR/nupkg"

mkdir -p "$OUT"
rm -f "$OUT"/memctl.*.nupkg

echo "Building memctl tool package $VERSION"
echo ""

dotnet pack "$SRC" \
    -c Release \
    -p:Version="$VERSION" \
    -o "$OUT" \
    --nologo -v q 2>&1 | grep -v "^$" || true

PKG="$OUT/memctl.$VERSION.nupkg"
if [[ ! -f "$PKG" ]]; then
    echo "ERROR: expected $PKG not found" >&2
    exit 1
fi

SIZE=$(du -k "$PKG" | cut -f1)
echo "  done: memctl.$VERSION.nupkg (${SIZE} KB)"
echo ""
echo "Install:"
echo "  dotnet tool install -g memctl --add-source $OUT"
echo "  dotnet tool update  -g memctl --add-source $OUT"
