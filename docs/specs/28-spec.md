# Requirements Spec: Automate plugin sync — release workflow updates marketplace + bumps versions

**Task:** 28
**Date:** 2026-05-01
**Status:** Approved (extracted from backlog/28)

---

## 1. Overview

Release process today: bump csproj `<Version>`, tag `v*`, push → `.github/workflows/release.yml` builds 3-platform AOT + nupkg → uploads to `vdg-solutions/memctl-releases`. Two manual steps remain: (a) bump `plugins/memctl-claude/.claude-plugin/plugin.json` version, (b) update `vdg-solutions/claude-plugins/.claude-plugin/marketplace.json` plugin entry version. Forgetting either breaks task #27 NFR-3 (plugin/csproj version lockstep). This task automates both via two new workflow jobs.

## 2. User Stories

- As a maintainer, I want pushing a tag `v1.2.2` to fail fast if csproj/plugin.json versions don't match the tag, so I never ship a broken-version release.
- As a maintainer, I want pushing a production tag to auto-update the marketplace plugin entry, so users running `claude plugin update memctl@vdg-solutions` get the latest version without manual marketplace edits.
- As a maintainer, I want pre-release tags (`v1.2.2-rc1`) to NOT touch the marketplace, so RC builds don't surface to end users.

## 3. Functional Requirements

| ID | Requirement | Priority | Test | Acceptance Criteria |
|----|------------|----------|------|---------------------|
| FR-1 | New `verify-versions` job runs before `build` + `pack-tool` | Must | [unit] | `grep -q "verify-versions" .github/workflows/release.yml && grep -q "needs: verify-versions" .github/workflows/release.yml` exit 0 |
| FR-2 | Mismatch between tag base, csproj `<Version>`, plugin.json `version` fails build | Must | [unit] | Force mismatch → workflow run conclusion=failure; matching → success |
| FR-3 | `sync-marketplace` job updates `vdg-solutions/claude-plugins/.claude-plugin/marketplace.json` plugin entry version | Must | [unit] | After v1.2.2 ship, fetched marketplace.json `.plugins[0].version == "1.2.2"` |
| FR-4 | Pre-release tags (containing `-`) skip marketplace sync | Must | [unit] | Push tag `v*-rc1` → `sync-marketplace` job not started |
| FR-5 | sync-marketplace failure does NOT mark release run failed | Should | [unit] | Revoke PAT scope, push tag → `release` job success, `sync-marketplace` red, run conclusion still success |

## 4. Non-Functional Requirements

| ID | Category | Requirement | Acceptance |
|----|----------|------------|-----------|
| NFR-1 | Security | All third-party Actions pinned to commit SHA | `grep -E "^      - uses:" .github/workflows/release.yml \| grep -vE "@[0-9a-f]{40}"` returns 0 |
| NFR-2 | Performance | Workflow total time stays < 15 min (verify-versions adds < 30s) | Inspect duration of test run |
| NFR-3 | Convention | sync-marketplace commit message format = `chore: bump memctl plugin to <version>` | `gh api repos/vdg-solutions/claude-plugins/commits --jq '.[0].commit.message'` matches |

## 5. Edge Cases

1. **Version with build metadata** (`v1.2.1+build.42`): not currently supported — out of scope. Treat `+` as failure, document.
2. **Multiple plugins in marketplace.json**: only update the entry where `name == "memctl"` — leave others untouched.
3. **marketplace.json malformed JSON**: sync-marketplace job MUST fail with clear error, not silently corrupt the file.
4. **PAT lacks Contents:Write on claude-plugins**: HTTP 403 → job fails red, release artifacts already shipped from earlier `release` job.
5. **Tag re-push** (delete + recreate): workflow runs again, marketplace.json updated to same version → idempotent.

## 6. Out of Scope

- Auto-bumping csproj/plugin.json from tag (chicken-and-egg — workflow runs after tag).
- Marketplace `category` / `tags` field updates (manual via PR if changed).
- Per-release plugin asset packaging (zip plugin folder, attach to release).
- Multi-plugin marketplace (currently 1 plugin).
- Build metadata version syntax (`+build`).

## 7. Dependencies

- `#25` (release pipeline) — Done
- `#27` (plugin scaffold) — Done
- PAT `RELEASE_REPO_PAT` with `vdg-solutions/claude-plugins` access — already extended in #27

## 8. Open Questions

- (none — backlog #28 specifies all behaviors)

## 9. QC Checklist

- [ ] FR-1: verify-versions job present, build/pack-tool depend on it
- [ ] FR-2: mismatched tag fails workflow
- [ ] FR-3: marketplace.json updated post-release
- [ ] FR-4: pre-release skip filter works
- [ ] FR-5: sync-marketplace failure isolated from release job
- [ ] NFR-1: all `uses:` pinned SHA
- [ ] NFR-2: workflow < 15 min
- [ ] NFR-3: commit message convention
- [ ] docs/release-runbook.md updated to reflect automation
