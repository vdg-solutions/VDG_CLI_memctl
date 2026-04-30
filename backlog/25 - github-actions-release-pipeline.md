---
id: 25
type: task
title: 'GitHub Actions release pipeline — multi-platform AOT build + public release repo'
status: Todo
priority: high
tags:
  - ci
  - github-actions
  - release
  - automation
created: 2026-04-30
updated: 2026-04-30
---

## Description

Hiện tại release manual: `bash build-portable.sh <version>` rồi push tag. Thiết kế CI/CD: tag `v*` push lên private repo → GitHub Actions tự build 4 platform AOT → upload artifact lên **public release-only repo**. Source private, binary public.

## Ngữ cảnh kiến trúc

```
Private repo: vdg-solutions/VDG_CLI_memctl
├── source code (đã private)
└── .github/workflows/release.yml          ← workflow này
                ↓ trigger: push tag v*
        GitHub-hosted runners
        (windows-latest, ubuntu-latest, macos-latest, macos-13)
                ↓ dotnet publish -p:PublishAot=true
        artifacts uploaded
                ↓
Public repo: vdg-solutions/memctl-releases
└── GitHub Releases ← gh release create với binaries
```

**Anti-RE rationale:**
- Source private — chỉ owner thấy.
- AOT binary native machine code — strip IL.
- Public repo chỉ chứa binaries, install scripts, README, skill markdown — không có .cs file.
- GitHub-hosted runner ephemeral VM — destroy sau job, source không leak.

**Trust model:** Trust Microsoft GitHub Actions infrastructure. Nếu paranoid → switch sang self-hosted runner (separate task).

## Mục tiêu

- Tag `v*` push → tự động build + release trên public repo trong < 15 phút.
- 4 binary native (win-x64.exe, linux-x64, osx-arm64, osx-x64) + 1 nupkg upload lên Release.
- Release notes auto-generated từ commits since last tag.
- Public repo READOnly cho user — chỉ chứa Releases page + minimal repo content.

## Implementation

### Step 0 — Prereq fail-fast
- Verify `gh` CLI available + authenticated: `gh auth status` || exit "Install: https://cli.github.com/ then `gh auth login`".
- Verify org `vdg-solutions` exists + user has admin: `gh api orgs/vdg-solutions -q .login` || exit "Create org first".
- Verify `RELEASE_REPO_PAT` secret set on private repo: `gh secret list --repo vdg-solutions/VDG_CLI_memctl | grep RELEASE_REPO_PAT` || exit "[USER-ACTION-REQUIRED] Issue PAT — see User Actions".

### 1. Tạo public release-only repo

```bash
gh repo create vdg-solutions/memctl-releases --public --description "Released binaries for memctl CLI — source at vdg-solutions/VDG_CLI_memctl (private)"
```

Init nội dung tối thiểu:
- `README.md` — install instructions, link tới Releases, link tới skill `docs/memctl.md`
- `install.sh`, `install.ps1` — fetch latest release zip, extract, add to PATH
- `docs/memctl.md` — Claude Code skill markdown (copy từ private repo, không chứa source code)
- `LICENSE` — same as private repo

### 2. Personal Access Token cho cross-repo push

GitHub Settings → Developer settings → Personal access tokens (fine-grained):
- Repository access: chỉ `vdg-solutions/memctl-releases`
- Permissions: Contents: Write, Metadata: Read
- Lifetime: 90-365 days (renew khi cần)

Save token vào private repo: Settings → Secrets and variables → Actions → New repository secret:
- Name: `RELEASE_REPO_PAT`
- Value: <token>

### 3. Workflow file `.github/workflows/release.yml`

```yaml
name: release

on:
  push:
    tags: ['v*']
  workflow_dispatch:    # manual trigger fallback

permissions:
  contents: read

env:
  PROJECT: src/memctl/memctl.csproj
  RELEASE_REPO: vdg-solutions/memctl-releases

jobs:
  build:
    name: build-${{ matrix.rid }}
    strategy:
      fail-fast: false
      matrix:
        include:
          - { os: windows-latest, rid: win-x64,    ext: .exe }
          - { os: ubuntu-latest,  rid: linux-x64,  ext: ''   }
          - { os: macos-latest,   rid: osx-arm64,  ext: ''   }
          - { os: macos-13,       rid: osx-x64,    ext: ''   }
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'

      - name: Restore
        run: dotnet restore ${{ env.PROJECT }}

      - name: Publish AOT
        run: |
          dotnet publish ${{ env.PROJECT }} \
            -c Release \
            -r ${{ matrix.rid }} \
            -p:PublishAot=true \
            -p:DebugType=none \
            -p:DebugSymbols=false \
            -p:StripSymbols=true \
            -o publish

      - name: Package
        shell: bash
        run: |
          mkdir -p package
          cp publish/memctl${{ matrix.ext }} package/
          cp docs/memctl.md package/SKILL.md
          cp README.md package/ 2>/dev/null || true
          cp LICENSE package/   2>/dev/null || true
          cd package
          if [[ "${{ matrix.rid }}" == win-* ]]; then
            7z a -tzip ../memctl-${{ matrix.rid }}-${GITHUB_REF_NAME#v}.zip *
          else
            tar -czf ../memctl-${{ matrix.rid }}-${GITHUB_REF_NAME#v}.tar.gz *
          fi

      - uses: actions/upload-artifact@v4
        with:
          name: memctl-${{ matrix.rid }}
          path: |
            memctl-${{ matrix.rid }}-*.zip
            memctl-${{ matrix.rid }}-*.tar.gz
          retention-days: 7

  pack-tool:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.x' }
      - name: Build nupkg
        run: |
          dotnet pack ${{ env.PROJECT }} \
            -c Release \
            -p:Version=${GITHUB_REF_NAME#v} \
            -o nupkg
      - uses: actions/upload-artifact@v4
        with:
          name: memctl-nupkg
          path: nupkg/*.nupkg
          retention-days: 7

  release:
    needs: [build, pack-tool]
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4   # for release notes generation
        with:
          fetch-depth: 0

      - name: Download all artifacts
        uses: actions/download-artifact@v4
        with:
          path: artifacts

      - name: Flatten
        run: |
          mkdir release
          find artifacts -type f \( -name '*.zip' -o -name '*.tar.gz' -o -name '*.nupkg' \) -exec cp {} release/ \;
          ls -la release/

      - name: Create release on public repo
        env:
          GH_TOKEN: ${{ secrets.RELEASE_REPO_PAT }}
        run: |
          # Generate release notes from this private repo (commit history),
          # but publish to public repo
          NOTES=$(git log $(git describe --tags --abbrev=0 ${GITHUB_REF_NAME}^ 2>/dev/null || git rev-list --max-parents=0 HEAD)..${GITHUB_REF_NAME} --pretty=format:'- %s' | head -100)
          gh release create ${GITHUB_REF_NAME} \
            --repo ${{ env.RELEASE_REPO }} \
            --title "${GITHUB_REF_NAME}" \
            --notes "$NOTES" \
            release/*
```

### 4. Public repo README + install scripts

`README.md`:
```markdown
# memctl

Obsidian-compatible personal memory vault CLI.

## Install

### Native binary
Download from [Releases](https://github.com/vdg-solutions/memctl-releases/releases/latest):
- Windows: `memctl-win-x64-<version>.zip`
- Linux:   `memctl-linux-x64-<version>.tar.gz`
- macOS:   `memctl-osx-arm64-<version>.tar.gz` or `memctl-osx-x64-<version>.tar.gz`

### dotnet global tool
```
dotnet tool install -g memctl --add-source <download-nupkg-locally>
```

## Documentation
See `SKILL.md` inside each release archive.
```

`install.sh`:
```bash
#!/usr/bin/env bash
set -euo pipefail
PLATFORM="$(uname -s | tr '[:upper:]' '[:lower:]')-$(uname -m)"
case "$PLATFORM" in
  linux-x86_64)  RID=linux-x64 ;;
  darwin-arm64)  RID=osx-arm64 ;;
  darwin-x86_64) RID=osx-x64   ;;
  *) echo "Unsupported: $PLATFORM"; exit 1 ;;
esac
LATEST=$(curl -s https://api.github.com/repos/vdg-solutions/memctl-releases/releases/latest | grep tag_name | cut -d'"' -f4)
URL="https://github.com/vdg-solutions/memctl-releases/releases/download/${LATEST}/memctl-${RID}-${LATEST#v}.tar.gz"
curl -L "$URL" | tar -xz -C "$HOME/.local/bin"
chmod +x "$HOME/.local/bin/memctl"
echo "Installed memctl ${LATEST} to ~/.local/bin"
```

### 5. Test workflow

- Push tag `v1.2.1-rc1` lên private repo → workflow chạy.
- Verify: 4 binary + 1 nupkg appear ở `vdg-solutions/memctl-releases/releases/v1.2.1-rc1`.
- Download win-x64 zip, extract, run `memctl.exe --version` → prints `1.2.1-rc1+...`.
- Smoke test: `memctl status` runs, exits 0.

## Acceptance Criteria

| ID | Criterion | Verify |
|---|---|---|
| FR-1 | Push tag `v*` lên private repo trigger workflow | check Actions tab |
| FR-2 | 4 platform binaries built in parallel matrix | check job summary |
| FR-3 | Each binary < 80 MB | inspect artifact sizes |
| FR-4 | Final release page on public repo có 4 zip/tar.gz + 1 nupkg + auto-generated release notes | open release URL |
| FR-5 | Release notes chứa commit subjects since last tag | manual review |
| FR-6 | Public repo có install.sh + install.ps1 + SKILL.md (no source code) | `gh api repos/vdg-solutions/memctl-releases/contents` |
| FR-7 | `curl install.sh \| sh` từ máy clean → memctl executable hoạt động | test trên Linux Docker container |
| FR-8 | Workflow total time < 15 phút | check duration |
| NFR-1 | Third-party Actions pinned to commit SHA (security) | grep `uses:` patterns — only `actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact`, `actions/download-artifact`, official Microsoft/GitHub orgs |
| NFR-2 | Secret `RELEASE_REPO_PAT` không log ra | manual review log |
| NFR-3 | Workflow file < 200 dòng | line count |
| NFR-4 | Failed workflow trigger không leak source vào artifact | manual review fail-case |

## Out of Scope

- Self-hosted runner (separate task nếu cần zero-trust).
- Build artifact signing (Authenticode / notarization). Future task.
- Auto-update mechanism in memctl binary. Future task.
- ARM64 Linux. Future task.

## Dependencies

- **Blocked by task #24** (AOT must compile cleanly first; CI does the same compile).
- Public repo `vdg-solutions/memctl-releases` không tồn tại — em tạo trong implementation phase.
- PAT must be issued by user (cannot create programmatically).

## Risk

- **Medium**: GitHub Actions free tier 2000 min/month limit. Estimate per release: 4 platforms × 5 min build = 20 min. Plus nupkg + release job 2 min. Total ~22 min/release. 90 releases/month possible — far below limit.
- **Medium**: PAT rotation. Add to runbook: rotate every 90-365 days.
- **Low**: macOS-13 runner deprecation. GitHub may retire. Migration plan: drop osx-x64 support hoặc move to Apple Silicon emulation.

## Effort

~6-8h:
- 1h: create public repo + init README + install scripts
- 1h: PAT setup + secret config
- 2h: write workflow file + test on rc tag
- 1h: debug cross-platform shell quirks (Windows bash vs PowerShell)
- 1h: install.sh + install.ps1 testing on clean VMs
- 1-2h: documentation (README cross-links, runbook for PAT rotation)

## Notes

- Workflow này là tự động cho mọi tag `v*`. Manual trigger qua workflow_dispatch nếu cần re-run.
- Pre-release (rc, alpha, beta) tags vẫn trigger — convention: `v1.2.1-rc1` works, GitHub Release auto-flag pre-release từ tag suffix.
