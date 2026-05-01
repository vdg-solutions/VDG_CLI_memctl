# Technical Design: Automate plugin sync into release pipeline

**Spec:** docs/specs/28-spec.md
**Task:** 28
**Date:** 2026-05-01
**Status:** Approved (extracted from backlog/28 Implementation)

---

## 1. Architecture Overview

Extend `.github/workflows/release.yml` (currently 138 lines, 3 jobs: `build`, `pack-tool`, `release`) with 2 new jobs:

```
verify-versions (NEW, runs first)
    ↓
build (matrix 3-platform AOT)  ← needs: verify-versions
pack-tool (dotnet pack)         ← needs: verify-versions
    ↓
release (gh release create)     ← needs: [build, pack-tool]
    ↓
sync-marketplace (NEW)          ← needs: release; if: !contains(ref_name, '-')
```

`verify-versions` is fail-fast: 5-second job that aborts the run if csproj/plugin/tag versions disagree, preventing wasted 10-minute build runs on misconfigured releases.

`sync-marketplace` runs after artifacts are shipped — its failure does NOT roll back the release (the `release` job already succeeded by then). Pre-release tags (suffix `-rc1`/`-beta1`/`-alpha`) skip this job via `if:` filter.

## 2. File Changes

### Modified Files

| File | Changes | Reason |
|------|---------|--------|
| `.github/workflows/release.yml` | Add `verify-versions` job at top of `jobs:` block; add `needs: verify-versions` to `build` and `pack-tool` jobs; add `sync-marketplace` job after `release` | FR-1, FR-2, FR-3, FR-4, FR-5 |
| `docs/release-runbook.md` | Replace Step 5 (manual marketplace update) with reference to automated job; replace forward-references "after task #28 ships" with past-tense; add "Workflow internals — verify-versions + sync-marketplace job chain" section | Spec §9 checklist |

### Integration Code Blocks

```yaml
# INTEGRATION: .github/workflows/release.yml — add verify-versions job
# Insert AFTER line 15 ("jobs:") and BEFORE existing build job (line 16)

  verify-versions:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2
      - name: Compare tag/csproj/plugin versions
        shell: bash
        run: |
          TAG_VER="${GITHUB_REF_NAME#v}"
          BASE_VER="${TAG_VER%%-*}"
          CSPROJ_VER=$(grep -oE '<Version>[^<]+' src/memctl/memctl.csproj | sed 's|<Version>||')
          PLUGIN_VER=$(python3 -c "import json; print(json.load(open('plugins/memctl-claude/.claude-plugin/plugin.json'))['version'])")

          echo "tag=$TAG_VER base=$BASE_VER csproj=$CSPROJ_VER plugin=$PLUGIN_VER"

          if [ "$CSPROJ_VER" != "$BASE_VER" ]; then
            echo "::error::csproj <Version> $CSPROJ_VER != tag base $BASE_VER"; exit 1
          fi
          if [ "$PLUGIN_VER" != "$BASE_VER" ]; then
            echo "::error::plugin.json version $PLUGIN_VER != tag base $BASE_VER"; exit 1
          fi
          echo "::notice::version sync verified — $BASE_VER"
```

```yaml
# INTEGRATION: .github/workflows/release.yml — modify build job
# Change line 16 from "build:" to "build:\n    needs: verify-versions"

  build:
    needs: verify-versions
    name: build-${{ matrix.rid }}
    # ... rest unchanged ...
```

```yaml
# INTEGRATION: .github/workflows/release.yml — modify pack-tool job
# Change line 77 to add needs

  pack-tool:
    needs: verify-versions
    runs-on: ubuntu-latest
    # ... rest unchanged ...
```

```yaml
# INTEGRATION: .github/workflows/release.yml — append sync-marketplace job
# Insert AFTER existing release job (after line 137)

  sync-marketplace:
    needs: release
    if: ${{ !contains(github.ref_name, '-') }}
    runs-on: ubuntu-latest
    steps:
      - name: Update marketplace.json plugin entry
        env:
          GH_TOKEN: ${{ secrets.RELEASE_REPO_PAT }}
        shell: bash
        run: |
          set -euo pipefail
          VER="${GITHUB_REF_NAME#v}"
          REPO="vdg-solutions/claude-plugins"
          API="https://api.github.com/repos/$REPO/contents/.claude-plugin/marketplace.json"

          curl -fsSL -H "Authorization: token $GH_TOKEN" "$API" -o /tmp/cur.json
          SHA=$(python3 -c "import json; print(json.load(open('/tmp/cur.json'))['sha'])")
          python3 -c "
          import json, base64, sys
          d = json.load(open('/tmp/cur.json'))
          mp = json.loads(base64.b64decode(d['content']).decode())
          changed = False
          for p in mp['plugins']:
              if p['name'] == 'memctl' and p.get('version') != '$VER':
                  p['version'] = '$VER'
                  changed = True
          if not changed:
              print('marketplace already at $VER, skip', file=sys.stderr)
              sys.exit(0)
          open('/tmp/new.json', 'w').write(json.dumps(mp, indent=2) + '\n')
          "
          [ -f /tmp/new.json ] || { echo "no update needed"; exit 0; }

          B64=$(base64 -w 0 /tmp/new.json)
          PAYLOAD=$(printf '{"message":"chore: bump memctl plugin to %s","content":"%s","sha":"%s"}' "$VER" "$B64" "$SHA")
          curl -fsSL -X PUT -H "Authorization: token $GH_TOKEN" -H "Accept: application/vnd.github+json" \
            "$API" -d "$PAYLOAD" -o /tmp/resp.json
          grep -q '"content"' /tmp/resp.json
          echo "::notice::marketplace.json bumped to $VER"
```

## 3. Data Model

No persistent storage. Runtime data:
- `/tmp/cur.json`: API response containing base64-encoded marketplace.json
- `/tmp/new.json`: locally-patched marketplace.json
- Environment vars: `GITHUB_REF_NAME` (tag), `GH_TOKEN` (PAT secret)

## 4. API Design

GitHub Contents API (REST v3):

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/repos/{owner}/{repo}/contents/{path}` | Fetch current marketplace.json + sha |
| PUT | `/repos/{owner}/{repo}/contents/{path}` | Update marketplace.json with new content + commit message |

Auth: `Authorization: token <PAT>` (RELEASE_REPO_PAT secret).

## 5. UI Components

N/A — workflow file only.

## 6. Business Logic

### Version comparison logic (verify-versions)

1. Strip `v` prefix from `GITHUB_REF_NAME` → `TAG_VER`
2. Strip pre-release suffix (everything after first `-`) → `BASE_VER`
   - Example: `v1.2.1-rc1` → tag=`1.2.1-rc1` → base=`1.2.1`
   - Example: `v1.2.1` → tag=`1.2.1` → base=`1.2.1`
3. Read `<Version>` from `src/memctl/memctl.csproj`
4. Read `version` field from `plugins/memctl-claude/.claude-plugin/plugin.json`
5. Both must equal `BASE_VER`. Any mismatch → `exit 1` with `::error::` annotation.

### Marketplace sync logic (sync-marketplace)

1. Strip `v` from `GITHUB_REF_NAME` → `VER`
2. GET marketplace.json + decode base64 content
3. Loop `plugins[]` array, find entry where `name == "memctl"`, set `version = VER`
4. If no change needed (version already matches) → exit 0 silent
5. Re-encode + PUT with sha (optimistic concurrency) + commit message
6. Verify response contains `"content"` field — fail otherwise

## 7. Error Handling

| Scenario | Handling | FR |
|----------|----------|-----|
| csproj missing `<Version>` | grep returns empty → comparison fails → workflow fails | FR-2 |
| plugin.json malformed JSON | python json.load raises → workflow fails with traceback | FR-2 |
| Tag has no `v` prefix | `GITHUB_REF_NAME#v` no-op, comparison still works (e.g., `1.2.1` works directly) | FR-2 |
| PAT lacks claude-plugins access | curl PUT returns 403, `grep -q '"content"'` fails → sync-marketplace red | FR-5 |
| marketplace.json plugin entry missing | python loop finds no `memctl` entry, no change → exit 0 silent (release still succeeded) | FR-5 |
| Concurrent tag pushes (race) | Second run sees stale sha → API returns 409 conflict → curl `-fsSL` fails → red | FR-5 |
| Pre-release tag (`v1.2.2-rc1`) | `if:` evaluator skips entire job → run shows job as "Skipped" | FR-4 |

## 8. Security

- Secret `RELEASE_REPO_PAT` injected only into `sync-marketplace` job env. Never logged.
- API URL hardcoded — no SSRF.
- JSON manipulated via `python3 json` — no shell injection from plugin entry content.
- Commit message includes only sanitized version string.

## 9. Performance

- `verify-versions`: ubuntu-latest checkout + bash + 1 python invocation = ~10-15s.
- `sync-marketplace`: 2 curl calls + 1 python = ~5s.
- Total workflow time delta: < 30s. Stays well under NFR-2 15-min budget.

## 10. Testing

| Level | Test | Method |
|-------|------|--------|
| Unit (workflow) | verify-versions detects mismatch | bump csproj only, push tag, observe red |
| Unit (workflow) | sync-marketplace updates marketplace.json | push production tag, fetch marketplace.json, assert version |
| Unit (workflow) | pre-release skip | push v*-rc1, observe sync-marketplace not started |
| Unit (workflow) | sync-marketplace failure isolated | revoke PAT scope, push tag, observe release green + sync red |

## 10.5 E2E Scenarios

**Project Type:** ci_workflow (no application output)

### Smoke scenarios (Layer 2.5)

| Scenario | Trigger | Expected | FR |
|----------|---------|----------|-----|
| Matched-version production tag | push v1.2.1 | all jobs green, marketplace.json plugin version=1.2.1 | FR-1, FR-3 |
| Mismatched csproj | push v1.2.2 with csproj=1.2.0 | verify-versions red, no build runs | FR-2 |
| Pre-release tag | push v1.2.1-rc1 | all jobs except sync-marketplace green; sync-marketplace skipped | FR-4 |
| PAT scope missing | push v1.2.1 with revoked claude-plugins permission | release green, sync-marketplace red, run conclusion=failure (overall) but artifacts on memctl-releases | FR-5 |

Smoke scenarios are validated post-merge by triggering tags on a test branch.

## 11. Dependencies

| Dependency | Where | Purpose | New? |
|-----------|-------|---------|------|
| python3 | ubuntu-latest runner | JSON parse/patch | No (preinstalled) |
| curl | ubuntu-latest runner | API calls | No (preinstalled) |
| base64 | ubuntu-latest runner | Encode marketplace.json | No (preinstalled) |
| `actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683` | verify-versions | Read csproj + plugin.json | No (already pinned) |

No new external dependencies. NFR-1 satisfied — no new actions to pin.

## 12. Implementation Order

1. Edit `.github/workflows/release.yml`:
   - Insert `verify-versions` job after line 15
   - Add `needs: verify-versions` to `build` (line 16)
   - Add `needs: verify-versions` to `pack-tool` (line 77)
   - Append `sync-marketplace` job after line 137
2. Edit `docs/release-runbook.md`:
   - Update Step 5 to reference automated workflow
   - Update forward-reference notes
   - Add "Workflow internals" subsection
3. Local syntax check: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))"` exit 0
4. AC verification commands (run BEFORE pushing branch):
   - FR-1: `grep "verify-versions" .github/workflows/release.yml | wc -l` ≥ 3
   - NFR-1: `grep -E "^      - uses:" .github/workflows/release.yml | grep -vE "@[0-9a-f]{40}" | wc -l` = 0
5. Commit + push branch + open PR
6. Real workflow smoke (post-merge): bump versions, push test tag

## 13. Assumptions & Open Decisions

- Assumption: `python3` is on `ubuntu-latest` runner (confirmed — preinstalled by GitHub).
- Decision: use `python3 -c` inline rather than separate script file — keeps workflow self-contained, avoids file-permission issues.
- Decision: marketplace sync is best-effort — its failure does not roll back already-uploaded release artifacts. Trades atomicity for resilience.
- Decision: no auto-bump of csproj/plugin.json. Bumping happens manually in dev shell before tag — workflow only enforces consistency.

## 14. Traceability

| Requirement | Section | Files | Test |
|-------------|---------|-------|------|
| FR-1 | §2 (verify-versions job) | `.github/workflows/release.yml` | `grep` AC |
| FR-2 | §6 version logic | `.github/workflows/release.yml` | mismatched-tag smoke |
| FR-3 | §6 sync logic | `.github/workflows/release.yml` | post-release marketplace.json fetch |
| FR-4 | §7 pre-release filter | `.github/workflows/release.yml` `if:` | rc1 tag smoke |
| FR-5 | §7 isolation | `.github/workflows/release.yml` job order | revoke-PAT smoke |
| NFR-1 | §11 deps | `.github/workflows/release.yml` | grep AC |
| NFR-2 | §9 perf | n/a | duration inspection |
| NFR-3 | §6 commit msg | sync-marketplace step | `gh api` AC |
