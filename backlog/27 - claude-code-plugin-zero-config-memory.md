---
id: 27
type: task
title: Claude Code plugin — zero-config auto memory via hooks + skill + commands
status: Done
priority: high
tags:
- claude-code
- plugin
- auto-memory
- hooks
- distribution
- dx
created: 2026-04-30
updated: 2026-05-01
---

## Description

Hiện tại user phải tự config `~/.claude/settings.json`, dán hook config tay, copy `docs/memctl.md` thành skill, install binary qua `dotnet tool install -g memctl` hoặc tải zip. 4 bước manual + dễ sai. Mỗi bước skip đều làm bot quên gọi `memctl` → memory dead.

Mục tiêu: 1 lệnh install Claude Code plugin → auto-capture conversation + auto-inject context + skill discoverable + slash commands sẵn sàng. **LLM không cần biết memctl tồn tại** — hook chạy ngầm, context tự xuất hiện, capture tự lưu sau mỗi turn. Bot chỉ thấy "Memory Context" block trên prompt và làm việc bình thường.

Task này deliver: `plugins/claude-code/` directory chứa plugin manifest theo spec Claude Code plugin (`.claude-plugin/plugin.json`), hook config tự động wire `UserPromptSubmit` (context-inject) + `Stop` (capture) + `SessionStart` (status check + ingest), 1 skill (`memctl.md` đã có — reuse), 4 slash commands (`/memctl-recall`, `/memctl-save`, `/memctl-boost`, `/memctl-lint`), README install instructions. Cuối cùng publish lên public release repo (#25) hoặc marketplace `vdg-solutions/claude-plugins` để user `claude plugin marketplace add` 1 lệnh xong.

## Implementation

### Step 0 — Prereq fail-fast

- Verify task #11 + #12 shipped (capture + context-inject commands exist):
  - `bl show 11 | grep -q '^status: Done'` || exit "Blocked by #11"
  - `bl show 12 | grep -q '^status: Done'` || exit "Blocked by #12"
- Verify `memctl --version` works on dev box: `memctl --version` || exit "Install memctl first: dotnet tool install -g memctl"
- Verify Claude Code installed: `claude --version` || exit "Install Claude Code: https://docs.claude.com/claude-code"
- Verify plugin spec familiar — fetch + read:
  - `curl -fsSL https://docs.claude.com/en/docs/claude-code/plugins.md -o /tmp/plugins-spec.md && grep -q "plugin.json" /tmp/plugins-spec.md` || exit "Read plugin spec first"

### Step 1 — Plugin scaffold

- **File CREATE:** `plugins/memctl-claude/.claude-plugin/plugin.json`
  ```json
  {
    "name": "memctl",
    "version": "1.0.0",
    "description": "Zero-config persistent memory for Claude Code — auto-capture, auto-inject, vault-backed",
    "author": {
      "name": "vdg-solutions",
      "url": "https://github.com/vdg-solutions/memctl-releases"
    },
    "homepage": "https://github.com/vdg-solutions/memctl-releases",
    "license": "MIT",
    "keywords": ["memory", "vault", "obsidian", "context", "persistence"]
  }
  ```

- **File CREATE:** `plugins/memctl-claude/README.md` — install instructions, what it does, where vault lives, how to disable hooks, troubleshooting.

### Step 2 — Hooks wiring (the magic)

- **File CREATE:** `plugins/memctl-claude/hooks/hooks.json` — declarative hook config Claude Code merges into user settings on plugin enable:
  ```json
  {
    "hooks": {
      "SessionStart": [
        {
          "matcher": "startup|resume",
          "hooks": [
            {
              "type": "command",
              "command": "memctl status --json 2>/dev/null || true",
              "timeout": 3000
            }
          ]
        }
      ],
      "UserPromptSubmit": [
        {
          "hooks": [
            {
              "type": "command",
              "command": "memctl context-inject",
              "timeout": 5000
            }
          ]
        }
      ],
      "Stop": [
        {
          "hooks": [
            {
              "type": "command",
              "command": "memctl capture",
              "timeout": 10000
            }
          ]
        }
      ]
    }
  }
  ```

- **Behavior contract** (verify trong AC bằng manual smoke):
  - SessionStart: chạy `memctl status` lần đầu → nếu vault không tồn tại thì exit 0 silent; nếu tồn tại in JSON status. Không block session.
  - UserPromptSubmit: stdout của `memctl context-inject` được prepend vào prompt → bot thấy `## Memory Context` block automatic.
  - Stop: stdin nhận transcript JSON, `memctl capture` parse + save → exit 0 luôn (graceful degrade).

### Step 3 — Skill (reuse existing)

- **File CREATE:** `plugins/memctl-claude/skills/memctl/SKILL.md` — symlink hoặc copy của `docs/memctl.md` (đã có sẵn, validated). Frontmatter `name: memctl`, `description: ...` đã đúng format Claude Code skill.
- **File CREATE:** `scripts/sync-skill-to-plugin.sh` — 1-liner copy `docs/memctl.md → plugins/memctl-claude/skills/memctl/SKILL.md` để 1 source of truth khi update skill text:
  ```bash
  #!/usr/bin/env bash
  set -euo pipefail
  cp docs/memctl.md plugins/memctl-claude/skills/memctl/SKILL.md
  echo "Synced skill: docs/memctl.md → plugin"
  ```

### Step 4 — Slash commands (explicit power-user invocation)

Mặc dù hooks đã handle 90% use case auto, vẫn cần slash commands cho lúc user muốn trigger explicit:

- **File CREATE:** `plugins/memctl-claude/commands/recall.md`
  ```markdown
  ---
  description: Recall top memories + search vault by current task keywords
  ---
  Run `memctl status` then `memctl list --limit 10`. If user provided arguments, also run `memctl search "$ARGUMENTS" --limit 10`. Format results clearly. If vault missing, suggest `memctl init`.
  ```

- **File CREATE:** `plugins/memctl-claude/commands/save.md`
  ```markdown
  ---
  description: Save current decision/finding/insight to vault as new note
  argument-hint: "<title> | <content>"
  ---
  Parse $ARGUMENTS as `<title> | <content>`. Run `memctl add --title "<title>" --content "<content>"`. If $ARGUMENTS empty, ask user what to save. Echo the resulting note id.
  ```

- **File CREATE:** `plugins/memctl-claude/commands/boost.md`
  ```markdown
  ---
  description: Boost note importance (weight) — surfaces it first in next recall
  argument-hint: "<note-id> [<weight>]"
  ---
  Parse $ARGUMENTS as `<id> [<weight>]`. Default weight 1.5 if omitted. Run `memctl weight <id> <weight>`. Confirm the new weight.
  ```

- **File CREATE:** `plugins/memctl-claude/commands/lint.md`
  ```markdown
  ---
  description: Run vault structural + semantic lint — catch orphans, broken links, stale duplicates
  ---
  Run `memctl ingest` (structural lint baked in). If $ARGUMENTS contains "semantic" or "deep", also run `memctl lint --semantic --self` and reason about the output, then save the report via `memctl add --title "Lint report <date>"`.
  ```

### Step 5 — Plugin marketplace repo

- **External repo CREATE (manual):** `vdg-solutions/claude-plugins` — public repo serving as marketplace.
- **File CREATE in marketplace repo:** `.claude-plugin/marketplace.json`:
  ```json
  {
    "name": "vdg-solutions",
    "description": "VDG Solutions Claude Code plugins",
    "owner": {
      "name": "vdg-solutions",
      "url": "https://github.com/vdg-solutions"
    },
    "plugins": [
      {
        "name": "memctl",
        "description": "Zero-config persistent memory for Claude Code via memctl vault CLI",
        "author": { "name": "vdg-solutions", "url": "https://github.com/vdg-solutions/memctl-releases" },
        "category": "memory",
        "tags": ["memory", "vault", "obsidian", "context"],
        "version": "1.2.0",
        "homepage": "https://github.com/vdg-solutions/memctl-releases",
        "source": {
          "source": "git-subdir",
          "url": "https://github.com/vdg-solutions/memctl-releases.git",
          "path": "plugins/memctl-claude",
          "ref": "master"
        }
      }
    ]
  }
  ```
  > **Source format note:** Claude Code 2.1+ requires `source` as object `{source, url, path, ref}` (`git-subdir` source type), NOT string `"github:owner/repo"`. The source repo MUST be PUBLIC — Claude Code clones via HTTPS without auth. That's why `path` points to `plugins/memctl-claude` inside `memctl-releases` (public release host) rather than the private source repo.
- **Per-release copy:** workflow `release.yml` (#25) extends to copy `plugins/memctl-claude/` vào `vdg-solutions/memctl-releases/plugins/memctl-claude/` mỗi tag.

### Step 6 — Install UX

User chỉ cần 2 lệnh:
```bash
# 1. Install memctl binary (one-time, prereq)
curl -fsSL https://github.com/vdg-solutions/memctl-releases/releases/latest/download/install.sh | sh

# 2. Add marketplace + install plugin
claude plugin marketplace add vdg-solutions/claude-plugins
claude plugin install memctl@vdg-solutions
```

Sau đó: restart Claude Code → hooks active → mọi conversation tự capture + tự inject context. Zero further config.

### Step 7 — Smoke test in clean Claude Code session

Manual checklist (lưu thành `plugins/memctl-claude/SMOKE.md`):
1. Fresh `~/.claude/` (rename ra backup), install Claude Code from scratch.
2. Install memctl binary + plugin theo Step 6.
3. `memctl init ~/test-vault` để có vault.
4. `cd ~/test-vault && claude` → start session.
5. SessionStart hook: kiểm tra log Claude Code không có error từ `memctl status`.
6. Type prompt "what did we discuss last week" → verify request payload có `## Memory Context` block từ `memctl context-inject` (kể cả empty với vault rỗng).
7. Conversation 3-4 turns về một topic. Quit session.
8. Inspect `~/test-vault/sessions/` → có file `<date>-<session_id>.md` chứa transcript.
9. Restart Claude Code, hỏi cùng topic → context xuất hiện trong prompt từ session trước.
10. `/memctl-recall` slash command → bot list top 10 notes.

## Acceptance Criteria

| ID | Criterion | Verify |
|----|-----------|--------|
| FR-1 | `plugins/memctl-claude/.claude-plugin/plugin.json` valid JSON, schema khớp Claude Code plugin spec | `cat plugins/memctl-claude/.claude-plugin/plugin.json \| jq -e '.name == "memctl" and .version'` exit 0 |
| FR-2 | `hooks.json` declares 3 events (SessionStart, UserPromptSubmit, Stop) | `jq -e '.hooks \| keys \| length == 3' plugins/memctl-claude/hooks/hooks.json` exit 0 |
| FR-3 | `hooks.json` UserPromptSubmit invokes `memctl context-inject` | `jq -e '.hooks.UserPromptSubmit[0].hooks[0].command == "memctl context-inject"' plugins/memctl-claude/hooks/hooks.json` exit 0 |
| FR-4 | `hooks.json` Stop invokes `memctl capture` | `jq -e '.hooks.Stop[0].hooks[0].command == "memctl capture"' plugins/memctl-claude/hooks/hooks.json` exit 0 |
| FR-5 | Skill markdown exists at `plugins/memctl-claude/skills/memctl/SKILL.md` với frontmatter `name: memctl` | `head -5 plugins/memctl-claude/skills/memctl/SKILL.md \| grep -q '^name: memctl'` exit 0 |
| FR-6 | 4 slash commands present | `ls plugins/memctl-claude/commands/*.md \| wc -l` returns 4 |
| FR-7 | Slash commands có frontmatter `description` field | `grep -L '^description:' plugins/memctl-claude/commands/*.md \| wc -l` returns 0 |
| FR-8 | `scripts/sync-skill-to-plugin.sh` chạy thành công | `bash scripts/sync-skill-to-plugin.sh && diff docs/memctl.md plugins/memctl-claude/skills/memctl/SKILL.md` exit 0 |
| FR-9 | README documents install qua marketplace + manual fallback | `grep -E 'plugin marketplace add\|plugin install memctl' plugins/memctl-claude/README.md \| wc -l` returns ≥ 2 |
| FR-10 | SMOKE.md có 10 step manual checklist | `grep -cE '^[0-9]+\.' plugins/memctl-claude/SMOKE.md` returns ≥ 10 |
| FR-11 | E2E smoke pass: clean Claude Code session với plugin → conversation → restart → context inject visible | manual run SMOKE.md, paste verification output as PR comment |
| NFR-1 | Plugin directory < 50 KB total (no binaries bundled) | `du -sk plugins/memctl-claude \| awk '{print $1}'` returns < 50 |
| NFR-2 | Hook timeout < 10s mỗi hook (Claude Code default 60s nhưng UX yêu cầu nhanh) | `jq '.hooks \| .. \| objects \| select(.timeout) \| .timeout' plugins/memctl-claude/hooks/hooks.json` all values ≤ 10000 |
| NFR-3 | Plugin manifest version khớp memctl binary version (single-source-of-truth) | `jq -r .version plugins/memctl-claude/.claude-plugin/plugin.json` == `grep -oE '<Version>[^<]+' src/memctl/memctl.csproj \| sed 's/<Version>//'` |
| NFR-4 | All hook commands graceful degrade khi vault missing (exit 0, không block) | `cd /tmp/empty && echo '' \| memctl context-inject; echo $?` returns 0; `cd /tmp/empty && echo '{}' \| memctl capture; echo $?` returns 0 |

## Out of Scope

- Auto-install memctl binary (yêu cầu user chạy `curl install.sh` riêng — plugin chỉ wire hooks, không bundle native binary vì cross-platform binary distribution đã do #25 release pipeline lo).
- Auto-update plugin (Claude Code marketplace tự handle qua `claude plugin update`).
- Vault selection UI (vault auto-detect by cwd — đã có `VaultLocator`).
- MCP server registration trong plugin (separate use case — MCP đã có qua `memctl mcp` binary, không cần plugin wrapper).
- Telemetry / opt-in usage analytics. Future.
- Multi-vault profile switching. Future.
- Plugin settings UI (decay schedule, capture filter rules). Future — currently env vars + memctl flags.

## Dependencies

- **Blocked by #11** (`memctl capture` command must exist) — Done.
- **Blocked by #12** (`memctl context-inject` command must exist) — Done.
- **Soft depend #25** (release pipeline) — plugin sync vào public repo dễ hơn khi #25 ship; có thể manual sync trước.
- Public repo `vdg-solutions/claude-plugins` chưa tồn tại — task này tạo trong Step 5.

## Risk

| Risk | Mitigation |
|------|-----------|
| Claude Code plugin spec thay đổi format trước khi publish | Step 0 fetch spec mới nhất; pin version Claude Code tested với; document version supported trong README. |
| User chưa install memctl binary → hooks fail mỗi turn | README nhấn mạnh install binary trước plugin; SessionStart hook detect missing binary và in 1 lần warning rồi silent. Cân nhắc check `command -v memctl` trong SessionStart, in upgrade hint nếu thiếu. |
| `memctl capture` chậm (> 10s) trên session dài | Capture đã filter < 50 chars, async-able trong tương lai. Timeout 10s acceptable; nếu vượt → exit 0 silent (không block Claude Code). |
| Hook `UserPromptSubmit` block prompt nếu `memctl context-inject` hang | timeout: 5000 trong hooks.json; context-inject implementation đã có exit 0 fallback. |
| Vault auto-detect sai folder (user `cd` ra ngoài project mid-session) | Documented limitation — vault locator cwd-based; user dùng `MEMCTL_VAULT` env var để pin. README có note. |
| Marketplace repo bị spam / sai phiên bản | Marketplace.json là single source of truth, version pin per plugin entry; tag immutable. |
| Plugin name conflict với plugin khác tên `memctl` | Namespace bằng marketplace owner: install qua `memctl@vdg-solutions`. |
| Hook config conflict với user existing hooks | Claude Code merge plugin hooks với user hooks; document trong README cách disable individual hook bằng `MEMCTL_DISABLE_AUTOCAPTURE=1` env var (cần code change ở capture/context-inject). |

## Effort

~8-10h:
- 0.5h: read Claude Code plugin spec, validate format
- 1h: scaffold plugin.json + directory structure
- 1h: write hooks.json + test 3 hook events local
- 0.5h: skill sync script + verify
- 1.5h: 4 slash commands + test each
- 1h: README + SMOKE.md
- 1h: marketplace repo creation + marketplace.json
- 1.5h: end-to-end smoke test on clean Claude Code install
- 1h: troubleshoot cross-platform path quirks (Windows backslash, macOS Gatekeeper)
- 1h: docs cross-link với #25 release pipeline + memctl README

## User Actions Required

- [USER-ACTION-REQUIRED] Tạo public repo `vdg-solutions/claude-plugins` qua GitHub UI (https://github.com/organizations/vdg-solutions/repositories/new). Setting: Public, Empty (no README/license — bot init sau), Description: "VDG Solutions Claude Code plugin marketplace". Paste back: repo URL.
- [USER-ACTION-REQUIRED] Cấp permission `vdg-solutions/claude-plugins` cho `RELEASE_REPO_PAT` (đã có từ #25). Settings → Personal access tokens → edit existing PAT → add `vdg-solutions/claude-plugins` to Repository access. Paste back: confirmation.

## Notes

- LLM không cần "biết" plugin tồn tại — đây là điểm cốt lõi. Hook chạy ngầm, context xuất hiện như magic; bot chỉ thấy `## Memory Context` đầu prompt và tiếp tục công việc bình thường. Skill markdown vẫn có để bot **explicitly** invoke khi cần (lúc user nói "save this" thì bot có ngữ cảnh kiến thức về memctl).
- Slash commands là escape hatch cho power user — 90% workflow chạy auto qua hooks.
- Plugin `version` bump cần khớp `memctl.csproj` version để debug compatibility dễ hơn — NFR-3 enforce.
- Future: thêm `PreToolUse` hook để auto-boost weight cho file bot vừa edit (signal-of-importance).

## Publish & propagation

**Read `backlog/wiki/plugin-publish.md` BEFORE editing this plugin or building a new one.** Critical points discovered after first install attempt:

- This source repo is private → Claude Code cannot clone it for plugin install.
- Plugin source must mirror to **public** `vdg-solutions/memctl-releases/plugins/memctl-claude/` for `claude plugin install memctl@vdg-solutions` to work.
- `marketplace.json` `source` field must be object `{source: "git-subdir", url, path, ref}`, NOT legacy string `"github:owner/repo"` (Claude Code 2.1+ rejects string with "unsupported source type").
- Auto-sync on tag: `.github/workflows/release.yml` release job last step clones `memctl-releases`, copies `plugins/memctl-claude/`, pushes — handles tag-driven propagation. Mid-release iteration without a tag requires manual sync (see runbook).

## Comments

**2026-05-01 07:07 user:** Plugin shipped. Marketplace live at https://github.com/vdg-solutions/claude-plugins. Plugin source: plugins/memctl-claude/ in this repo (auto-synced to marketplace via release pipeline future). Install: claude plugin marketplace add vdg-solutions/claude-plugins && claude plugin install memctl@vdg-solutions. AC: FR-1..10 + NFR-1..3 verified mechanical. FR-11 (E2E smoke on clean Claude Code install) deferred to user manual run per SMOKE.md.
