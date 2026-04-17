#!/usr/bin/env bash
# install.sh — installs memctl portable binary to ~/.local/bin
# Usage: bash install.sh
# Requires: run bash build-portable.sh first

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEST_DIR="$HOME/.local/bin"
DEST="$DEST_DIR/memctl"

# detect platform RID
OS="$(uname -s)"
ARCH="$(uname -m)"
case "$OS-$ARCH" in
    Linux-x86_64)  RID="linux-x64" ;;
    Linux-aarch64) RID="linux-arm64" ;;
    Darwin-arm64)  RID="osx-arm64" ;;
    Darwin-x86_64) RID="osx-x64" ;;
    *) echo "ERROR: unsupported platform $OS-$ARCH" >&2; exit 1 ;;
esac

SRC_DIR="$SCRIPT_DIR/dist/$RID"
SRC_BIN="$SRC_DIR/memctl"

# validate binary
if [[ ! -f "$SRC_BIN" ]]; then
    echo "ERROR: $SRC_BIN not found" >&2
    echo "  Run: bash build-portable.sh" >&2
    exit 1
fi

# validate native libs
NATIVE_LIB_COUNT=$(find "$SRC_DIR" \( -name "*.so" -o -name "*.dylib" \) 2>/dev/null | wc -l)
if [[ "$NATIVE_LIB_COUNT" -eq 0 ]]; then
    echo "ERROR: native runtime libs missing in $SRC_DIR" >&2
    echo "  Re-run: bash build-portable.sh" >&2
    exit 1
fi

# warn on overwrite (idempotent — proceed regardless)
if [[ -f "$DEST" ]]; then
    echo "Warning: memctl already installed at $DEST — overwriting"
fi

mkdir -p "$DEST_DIR"
cp "$SRC_BIN" "$DEST"
chmod +x "$DEST"

# copy native libs alongside binary
find "$SRC_DIR" \( -name "*.so" -o -name "*.dylib" \) -exec cp {} "$DEST_DIR/" \;

echo "Installed: $DEST"
echo ""
echo "To uninstall:"
echo "  rm $DEST_DIR/memctl $DEST_DIR/libonnxruntime* $DEST_DIR/libe_sqlite3*"
echo ""

# PATH reminder
if ! echo ":$PATH:" | grep -q ":$DEST_DIR:"; then
    echo "Note: $DEST_DIR is not in PATH. Add to your shell profile:"
    echo "  export PATH=\"\$HOME/.local/bin:\$PATH\""
fi
