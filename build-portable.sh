#!/usr/bin/env bash
# build-portable.sh — builds self-contained portable packages for all platforms
# Usage: bash build-portable.sh [version]
# Output: dist/memctl-portable-<platform>-<version>.zip

set -euo pipefail

VERSION="${1:-$(git describe --tags --exact-match 2>/dev/null || git rev-parse --short HEAD 2>/dev/null || echo "dev")}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$SCRIPT_DIR/src/memctl/memctl.csproj"
SKILL="$SCRIPT_DIR/docs/memctl.md"
DIST="$SCRIPT_DIR/dist"

TARGETS=("win-x64" "linux-x64" "osx-arm64" "osx-x64")

mkdir -p "$DIST"
echo "Building memctl CLI $VERSION"
echo ""

for RID in "${TARGETS[@]}"; do
    echo "→ Publishing $RID..."
    OUT="$DIST/$RID"

    dotnet publish "$SRC" \
        -c Release \
        -r "$RID" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -o "$OUT" \
        --nologo -v q 2>&1 | grep -v "^$" || true

    PKG_NAME="memctl-portable-$RID"
    PKG="$DIST/$PKG_NAME"
    rm -rf "$PKG"
    mkdir -p "$PKG"

    # Copy binary + native runtime libs (ONNX Runtime, SQLite can't be single-file bundled)
    if [[ "$RID" == win-* ]]; then
        cp "$OUT/memctl.exe" "$PKG/"
        cp "$OUT"/*.dll "$PKG/" 2>/dev/null || true
    else
        cp "$OUT/memctl" "$PKG/"
        chmod +x "$PKG/memctl"
        cp "$OUT"/*.so "$PKG/"  2>/dev/null || true
        cp "$OUT"/*.dylib "$PKG/" 2>/dev/null || true
    fi

    # Copy skill doc
    cp "$SKILL" "$PKG/"

    # Zip using relative paths inside dist/
    ZIP_NAME="memctl-portable-$RID-$VERSION.zip"
    rm -f "$DIST/$ZIP_NAME"
    (
        cd "$DIST"
        python -c "
import zipfile, os
base = '$PKG_NAME'
zipname = '$ZIP_NAME'
z = zipfile.ZipFile(zipname, 'w', zipfile.ZIP_DEFLATED)
for r, _, files in os.walk(base):
    for f in files:
        full = os.path.join(r, f)
        arc = full.replace(base + os.sep, '').replace(os.sep, '/')
        z.write(full, arc)
z.close()
mb = os.path.getsize(zipname) // 1024 // 1024
print(f'  done: {zipname} ({mb} MB)')
"
    )

    rm -rf "$PKG"
done

echo ""
echo "Done. Packages:"
ls "$DIST/"memctl-portable-*-"$VERSION".zip 2>/dev/null | while read -r f; do echo "  $(basename "$f")"; done
