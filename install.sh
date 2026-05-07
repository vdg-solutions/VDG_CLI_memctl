#!/usr/bin/env bash
# install.sh — installs memctl portable binary to ~/.local/bin (or --dir override)
# Usage: bash install.sh [--dir <path>]
# Requires: run bash build-portable.sh first

main() {
    set -euo pipefail

    local script_dir
    script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    local dest_dir="$HOME/.local/bin"

    # parse --dir flag
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --dir) dest_dir="$2"; shift 2 ;;
            *) echo "Unknown option: $1" >&2; exit 1 ;;
        esac
    done

    local dest="$dest_dir/memctl"

    # detect platform RID
    local os arch
    os="$(uname -s)"
    arch="$(uname -m)"
    local rid
    case "$os-$arch" in
        Linux-x86_64)  rid="linux-x64" ;;
        Linux-aarch64) rid="linux-arm64" ;;
        Darwin-arm64)  rid="osx-arm64" ;;
        Darwin-x86_64) rid="osx-x64" ;;
        *) echo "ERROR: unsupported platform $os-$arch" >&2; exit 1 ;;
    esac

    local src_dir="$script_dir/dist/$rid"
    local src_bin="$src_dir/memctl"

    # validate source binary
    if [[ ! -f "$src_bin" ]]; then
        echo "ERROR: $src_bin not found" >&2
        echo "  Run: bash build-portable.sh" >&2
        exit 1
    fi

    # validate native libs
    local native_lib_count
    native_lib_count=$(find "$src_dir" \( -name "*.so" -o -name "*.dylib" \) 2>/dev/null | wc -l)
    if [[ "$native_lib_count" -eq 0 ]]; then
        echo "ERROR: native runtime libs missing in $src_dir" >&2
        echo "  Re-run: bash build-portable.sh" >&2
        exit 1
    fi

    mkdir -p "$dest_dir"

    # rename-aside existing binary for rollback
    if [[ -f "$dest" ]]; then
        mv "$dest" "$dest.old"
        echo "Renamed existing binary → $dest.old"
    fi

    cp "$src_bin" "$dest"
    chmod +x "$dest"

    # copy native libs alongside binary
    find "$src_dir" \( -name "*.so" -o -name "*.dylib" \) -exec cp {} "$dest_dir/" \;

    # macOS quarantine fix
    if [[ "$os" == "Darwin" ]]; then
        xattr -d com.apple.quarantine "$dest" 2>/dev/null || true
        codesign --sign - --force "$dest" 2>/dev/null || true
    fi

    # verify new binary runs
    if ! "$dest" --version >/dev/null 2>&1; then
        echo "ERROR: binary verification failed" >&2
        if [[ -f "$dest.old" ]]; then
            mv "$dest.old" "$dest"
            echo "Rolled back to previous binary." >&2
        fi
        exit 1
    fi

    # clean up .old on success
    rm -f "$dest.old"

    echo "Installed: $dest"
    echo ""
    echo "To uninstall:"
    echo "  rm $dest_dir/memctl $dest_dir/libonnxruntime* $dest_dir/libe_sqlite3*"
    echo ""

    # PATH check
    if ! printf '%s\n' "$PATH" | tr ':' '\n' | grep -qx "$dest_dir"; then
        echo "Note: $dest_dir is not in PATH. Add to your shell profile:"
        echo "  export PATH=\"\$HOME/.local/bin:\$PATH\""
    fi
}

main "$@"
