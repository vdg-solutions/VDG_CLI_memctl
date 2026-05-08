# Design #48 — memctl add: --content alias + unknown-flag error

## Approach

Two targeted changes to `src/memctl/Bootstrap/Program.cs`, scoped to the `add` command.
No new files. No CommandLineBuilder migration. No operator changes.

## Change 1: --content option + optional positional arg

**Current:**
```csharp
var addTextArg = new Argument<string>("text", "Note content");
addCmd.AddArgument(addTextArg);
// handler: Text = pr.GetValueForArgument(addTextArg)
```

**After:**
```csharp
var addTextArg    = new Argument<string?>("text", () => null, "Note content (or use --content)");
var addContentOpt = new Option<string?>("--content", "Note content (alias for positional <text>)");
addCmd.AddArgument(addTextArg);
addCmd.AddOption(addContentOpt);

// handler: resolve text — --content wins over positional
var text = pr.GetValueForOption(addContentOpt) ?? pr.GetValueForArgument(addTextArg);
if (string.IsNullOrWhiteSpace(text))
{
    Console.Error.WriteLine(
        "Error: note content is required. Usage: memctl add <text> [--content <text>] [--title <title>] [--tags <tags>] [--file <file>]");
    ctx.ExitCode = 1;
    return;
}
```

Making `addTextArg` optional (`string?` with `() => null` default) allows System.CommandLine to not fail when positional is absent and content comes from `--content`.

## Change 2: Pre-parse unknown flags for add command

Before `root.InvokeAsync(args)`, add targeted pre-check for `add` command only:

```csharp
// Friendly unknown-flag error for 'add' command (LLM-first UX)
if (args.Length > 0 && args[0] == "add")
{
    var knownAddFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "--content", "--title", "--tags", "--file",
        "--vault", "--limit", "--llm-url", "--llm-model", "--llm-key", "--model-dir"
    };
    foreach (var arg in args.Skip(1))
    {
        if (!arg.StartsWith("--")) continue;
        var flag = arg.Contains('=') ? arg[..arg.IndexOf('=')] : arg;
        if (!knownAddFlags.Contains(flag))
        {
            Console.Error.WriteLine(
                $"Unknown option '{flag}'. Usage: memctl add <text> [--content <text>] [--title <title>] [--tags <tags>] [--file <file>]");
            return 1;
        }
    }
}
```

**Why pre-parse instead of CommandLineBuilder middleware:**
- CommandLineBuilder migration touches all commands — disproportionate scope
- Pre-parse is targeted: only runs when first arg is "add"
- Named constant `knownAddFlags` satisfies RULE #9 (no magic strings)
- No performance impact on other commands

## Change 3: SKILL.md

In the Commands/Encode table, update `memctl add` row:

Before: `` `memctl add "<text>"` ``
After:  `` `memctl add "<text>"` | Add note. `--content <text>` accepted as alias. `--llm-*` → auto-tags + wikilinks ``

## Test plan (manual, pre-commit)

```bash
# AC-1: --content alias works
memctl add --content "test via content flag" --title "test48-content" --vault .memctl-vault
# expect: success: true

# AC-2: unknown flag
memctl add --unknown "x" --vault .memctl-vault 2>&1
# expect: stderr "Unknown option '--unknown'..." exit 1

# AC-3: positional still works
memctl add "test positional" --title "test48-pos" --vault .memctl-vault
# expect: success: true

# AC-4: identical JSON (compare id fields differ but schema same)
# AC-5: SKILL.md contains "--content"
```
