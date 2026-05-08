# memctl plugin authoring guide

Hook plugins wire memctl into AI tools that don't support MCP. Each plugin is a thin adapter — hook config + optional version-check script. No business logic.

**Use MCP instead whenever possible.** Hook plugins exist for tools that predate MCP or don't expose an MCP host API. See `README.md` for the MCP setup.

---

## Overview

A plugin lives under `plugins/memctl-{tool}/` and contains:

```
plugins/memctl-{tool}/
├── .claude-plugin/        ← or equivalent manifest dir for the target tool
│   └── plugin.json        ← plugin metadata + minVersion
├── hooks/
│   ├── hooks.json         ← event → command wiring
│   └── version-check.js  ← (optional) warn if memctl binary is outdated
├── skills/
│   └── memctl/
│       └── SKILL.md       ← optional: copy of memctl skill for in-context recall
└── README.md
```

The canonical reference is `plugins/memctl-claude/` (Claude Code integration).

---

## Directory layout

| Path | Required | Purpose |
|------|----------|---------|
| `.claude-plugin/plugin.json` | Yes | Metadata + minVersion declaration |
| `hooks/hooks.json` | Yes | Event hook wiring |
| `hooks/version-check.js` | Optional | SessionStart guard: warn if memctl < minVersion |
| `skills/memctl/SKILL.md` | Optional | Skill doc for tools that support skill injection |
| `README.md` | Yes | Prerequisites, install steps |

---

## Hook wiring

Hook commands must only call `memctl <cmd>`. No logic in hooks — if logic is needed, it belongs in the `memctl` binary.

The three standard hooks:

```json
{
  "hooks": {
    "SessionStart": [
      {
        "matcher": "startup|resume",
        "hooks": [
          {
            "type": "command",
            "command": "node \"${PLUGIN_ROOT}/hooks/version-check.js\"",
            "timeout": 5000
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

Timeout values: Stop hook gets 10s (writes to disk), UserPromptSubmit gets 5s (reading). Tools must not block on non-zero exit — memctl fails silently if vault is absent.

Hook event names vary by tool. Claude Code uses `SessionStart`, `UserPromptSubmit`, `Stop`. Map to your tool's equivalents.

---

## Version pin

Declare `minVersion` in `plugin.json` to guard against binary drift:

```json
{
  "name": "memctl",
  "version": "1.0.0",
  "description": "memctl hook plugin for <tool>",
  "minVersion": "1.4.0"
}
```

`minVersion` is the minimum `memctl` binary version the plugin is compatible with. Check it in `version-check.js` at SessionStart and warn the user — don't block execution.

Reference implementation: `plugins/memctl-claude/hooks/version-check.js`.

---

## Version drift risk

**Keep plugins thin.** The more a hook does beyond calling `memctl <cmd>`, the more it can break when memctl's CLI interface changes. If the hook only calls `memctl capture` and `memctl context-inject`, the only breaking change is a command rename — which is a major version bump and will be called out in release notes.

If you find yourself parsing memctl output in hook scripts, that logic belongs in a new `memctl` subcommand. File a feature request.

---

## Minimal example

For a tool called "Toolname" with `before-prompt` and `after-response` events:

**`plugins/memctl-toolname/.toolname-plugin/plugin.json`:**
```json
{
  "name": "memctl",
  "version": "1.0.0",
  "description": "memctl memory integration for Toolname",
  "minVersion": "1.4.0"
}
```

**`plugins/memctl-toolname/hooks/hooks.json`:**
```json
{
  "hooks": {
    "before-prompt": [
      {
        "hooks": [{ "type": "command", "command": "memctl context-inject", "timeout": 5000 }]
      }
    ],
    "after-response": [
      {
        "hooks": [{ "type": "command", "command": "memctl capture", "timeout": 10000 }]
      }
    ]
  }
}
```

That's it. No JS, no logic. If Toolname has a version-check hook (like SessionStart), add `version-check.js` from the Claude plugin as a template.

---

## Checklist before shipping a plugin

- [ ] `plugin.json` has `minVersion` set to the earliest compatible release
- [ ] Hook commands are exactly `memctl capture` / `memctl context-inject` — no parsing, no logic
- [ ] `README.md` documents: how to install `memctl` binary, how to install the plugin, vault init command
- [ ] Tested: does the tool call the hook? Does `memctl capture` receive stdin correctly?
- [ ] No secrets or credentials in hook commands
- [ ] `version-check.js` warns but does not block on old binary
