#!/usr/bin/env bash
# Append SHA256 trailer to memctl binary for self-tamper detection.
# Idempotent: re-running on already-baked binary is a no-op.
set -euo pipefail

BIN="${1:-}"
if [[ -z "$BIN" ]]; then echo "Usage: $0 <binary>" >&2; exit 2; fi
if [[ ! -f "$BIN" ]]; then echo "Not found: $BIN" >&2; exit 1; fi

PREFIX=$'\nMEMCTL_SHA:'
PREFIX_LEN=${#PREFIX}
TRAILER_LEN=$((PREFIX_LEN + 64))

SIZE=$(wc -c < "$BIN" | tr -d ' ')

if [[ $SIZE -ge $TRAILER_LEN ]]; then
    TAIL_HEAD=$(tail -c "$TRAILER_LEN" "$BIN" | head -c "$PREFIX_LEN" || true)
    if [[ "$TAIL_HEAD" == "$PREFIX" ]]; then
        echo "Already baked: $BIN"
        exit 0
    fi
fi

# Compute SHA256 of current bytes
HASH=$(sha256sum "$BIN" 2>/dev/null | awk '{print $1}')
if [[ -z "$HASH" ]]; then
    HASH=$(shasum -a 256 "$BIN" | awk '{print $1}')
fi

if [[ ${#HASH} -ne 64 ]]; then
    echo "Hash compute failed" >&2; exit 1
fi

printf '%s%s' "$PREFIX" "$HASH" >> "$BIN"
echo "Baked: $BIN (sha256=$HASH)"
