#!/usr/bin/env bash
# Lint backlog items against TEMPLATE.md hard rules.
# Run before /sdlc invocation. Exit 0 = SDLC-ready.

set -euo pipefail
fail=0
checked=0

for f in backlog/*.md; do
  [[ "$f" =~ /(README|TEMPLATE)\.md$ ]] && continue
  [[ "$(basename "$f")" =~ ^[0-9]+\  ]] || continue
  # skip done/archived items — only lint actionable
  grep -qE '^status:\s*Done'     "$f" && continue
  grep -qE '^status:\s*Archived' "$f" && continue
  # skip epics — they're parent rollups; ACs/Step0/NFR live in children
  grep -qE '^type:\s*epic' "$f" && continue
  checked=$((checked+1))

  miss() { echo "  MISS [$1] $f"; fail=1; }

  grep -q '^id:'        "$f" || miss "frontmatter:id"
  grep -q '^status:'    "$f" || miss "frontmatter:status"
  grep -q '^priority:'  "$f" || miss "frontmatter:priority"
  grep -q '^created:'   "$f" || miss "frontmatter:created"
  grep -q '^## Description'    "$f" || miss "section:Description"
  grep -q '^## Implementation' "$f" || miss "section:Implementation"
  grep -q '^## Acceptance'     "$f" || miss "section:Acceptance"
  grep -q '^## Out of Scope'   "$f" || miss "section:OutOfScope"
  grep -q '^## Dependencies'   "$f" || miss "section:Dependencies"
  grep -q '^## Risk'           "$f" || miss "section:Risk"
  grep -q '^## Effort'         "$f" || miss "section:Effort"
  grep -qE '\| FR-[0-9]+'      "$f" || miss "AC:FR rows"
  grep -qE '\| NFR-[0-9]+'     "$f" || miss "AC:NFR rows"
  grep -qE '^### Step 0'       "$f" || miss "Implementation:Step0 prereq"
  ! grep -qiE '\b(TBD|open question)\b' "$f" || miss "vague:TBD/open-question"
done

if [[ $fail -eq 0 ]]; then
  echo "OK — $checked backlog items SDLC-ready"
else
  echo "FAIL — fix above before /sdlc"
  exit 1
fi
