#!/usr/bin/env pwsh
# Append SHA256 trailer to memctl binary for self-tamper detection.
# Runtime SelfHash.Verify reads the trailer, hashes binary excluding trailer, compares.
# Idempotent: if already baked, skip.

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Binary
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Binary)) {
    Write-Error "Binary not found: $Binary"
    exit 1
}

$prefix      = "`nMEMCTL_SHA:"
$prefixBytes = [Text.Encoding]::ASCII.GetBytes($prefix)
$trailerLen  = $prefixBytes.Length + 64

$bytes = [IO.File]::ReadAllBytes($Binary)
$size  = $bytes.Length

# Idempotency check: if last $trailerLen bytes start with prefix, already baked
if ($size -ge $trailerLen) {
    $tailStart = $size - $trailerLen
    $tailHead  = [Text.Encoding]::ASCII.GetString($bytes, $tailStart, $prefixBytes.Length)
    if ($tailHead -eq $prefix) {
        Write-Output "Already baked: $Binary"
        exit 0
    }
}

# Compute SHA256 over current bytes
$sha    = [Security.Cryptography.SHA256]::Create()
$digest = $sha.ComputeHash($bytes)
$hex    = -join ($digest | ForEach-Object { $_.ToString('x2') })

# Append prefix + hex hash
$append = $prefixBytes + [Text.Encoding]::ASCII.GetBytes($hex)
$out    = New-Object byte[] ($size + $append.Length)
[Array]::Copy($bytes, 0, $out, 0, $size)
[Array]::Copy($append, 0, $out, $size, $append.Length)

[IO.File]::WriteAllBytes($Binary, $out)

Write-Output "Baked: $Binary (sha256=$hex)"
