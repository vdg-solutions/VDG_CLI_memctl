# memctl plugin — smoke test checklist

Manual checklist for end-to-end verification on a clean Claude Code install.

1. Backup `~/.claude/`, then install Claude Code from scratch.
2. Install `memctl` binary via `install.sh` / `install.ps1` and verify with `memctl --version`.
3. Add the marketplace and install the plugin:
   ```
   claude plugin marketplace add vdg-solutions/claude-plugins
   claude plugin install memctl@vdg-solutions
   ```
4. `memctl init ~/test-vault` to provision a fresh vault.
5. `cd ~/test-vault && claude` to start a session in the vault directory.
6. Inspect Claude Code logs for SessionStart hook output — `memctl status` must run without errors.
7. Type a prompt about a topic. Verify the prompt sent to the model contains a `## Memory Context` block (empty for an empty vault is fine).
8. Continue 3-4 conversation turns on the topic, then exit the session.
9. Inspect `~/test-vault/sessions/` — there must be a `<date>-<session_id>.md` file containing the transcript.
10. Restart Claude Code in the same directory and ask about the same topic — context from the previous session must appear in the prompt sent to the model.
