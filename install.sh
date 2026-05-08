#!/usr/bin/env bash
set -euo pipefail

REPO="vdg-solutions/memctl-releases"
DEFAULT_DIR="$HOME/.local/bin"

detect_rid() {
    local os arch
    os=$(uname -s)
    arch=$(uname -m)
    case "$os" in
        Linux)
            case "$arch" in
                x86_64)  echo "linux-x64" ;;
                aarch64) echo "linux-arm64" ;;
                *) echo "ERROR: unsupported arch: $arch" >&2; exit 1 ;;
            esac ;;
        Darwin)
            case "$arch" in
                arm64)  echo "osx-arm64" ;;
                x86_64) echo "osx-x64" ;;
                *) echo "ERROR: unsupported arch: $arch" >&2; exit 1 ;;
            esac ;;
        *) echo "ERROR: unsupported OS: $os" >&2; exit 1 ;;
    esac
}

fetch_latest_tag() {
    local tag
    tag=$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" \
        | grep '"tag_name"' | sed 's/.*"tag_name": *"\([^"]*\)".*/\1/')
    if [ -z "$tag" ]; then
        echo "ERROR: could not fetch latest release tag (network error or API rate limit)" >&2
        exit 1
    fi
    echo "$tag"
}

main() {
    local install_dir="$DEFAULT_DIR"

    while [ $# -gt 0 ]; do
        case "$1" in
            --dir) install_dir="$2"; shift 2 ;;
            *) echo "ERROR: unknown option: $1" >&2; exit 1 ;;
        esac
    done

    local rid tag ver asset_url tmpdir tmpfile
    rid=$(detect_rid)
    tag=$(fetch_latest_tag)
    ver="${tag#v}"

    asset_url="https://github.com/$REPO/releases/download/$tag/memctl-$rid-$ver.tar.gz"
    tmpdir=$(mktemp -d)
    tmpfile="$tmpdir/memctl.tar.gz"

    echo "Installing memctl $tag for $rid → $install_dir"

    curl -fsSL "$asset_url" -o "$tmpfile" || {
        echo "ERROR: download failed: $asset_url" >&2
        rm -rf "$tmpdir"
        exit 1
    }
    [ -s "$tmpfile" ] || { echo "ERROR: download empty" >&2; rm -rf "$tmpdir"; exit 1; }
    tar -tzf "$tmpfile" >/dev/null 2>&1 || { echo "ERROR: archive corrupt" >&2; rm -rf "$tmpdir"; exit 1; }

    tar -xzf "$tmpfile" -C "$tmpdir"

    mkdir -p "$install_dir"
    cp "$tmpdir/memctl" "$install_dir/memctl"
    find "$tmpdir" \( -name "*.so" -o -name "*.dylib" \) -exec cp {} "$install_dir/" \;

    chmod +x "$install_dir/memctl"

    if [ "$(uname -s)" = "Darwin" ]; then
        xattr -d com.apple.quarantine "$install_dir/memctl" 2>/dev/null || true
        codesign --sign - "$install_dir/memctl" 2>/dev/null || true
    fi

    "$install_dir/memctl" --version >/dev/null 2>&1 || {
        echo "ERROR: binary verify failed — memctl --version returned non-zero" >&2
        rm -rf "$tmpdir"
        exit 1
    }

    rm -rf "$tmpdir"

    # Sync memctl skill doc to ~/.claude/skills/memctl/SKILL.md
    local skill_dir="$HOME/.claude/skills/memctl"
    if [ -d "$skill_dir" ]; then
        local skill_url="https://raw.githubusercontent.com/$REPO/main/SKILL.md"
        if curl -fsSL "$skill_url" -o "$skill_dir/SKILL.md" 2>/dev/null; then
            echo "Skill doc synced to $skill_dir/SKILL.md"
        else
            echo "NOTE: skill doc sync failed (non-fatal)" >&2
        fi
    fi

    echo "memctl $tag installed to $install_dir"

    case ":$PATH:" in
        *":$install_dir:"*) ;;
        *) echo "NOTE: add $install_dir to PATH: export PATH=\"$install_dir:\$PATH\"" >&2 ;;
    esac
}

main "$@"
