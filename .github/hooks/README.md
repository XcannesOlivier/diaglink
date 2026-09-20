# Copilot Hooks

Hooks are scripts that run at lifecycle events during AI-assisted development. They intercept tool calls made by Copilot agents — letting you enforce policies, suggest best practices, or trigger reminders automatically.

## How It Works

This repository uses two Copilot lifecycle events:

- **`PreToolUse`** — Runs *before* a tool call executes. Can block the call (deny) or inject advisory messages.
- **`PostToolUse`** — Runs *after* a tool call completes. Can inject follow-up reminders.

Hooks are defined in `.github/hooks/hooks.json` and reference scripts in `.github/hooks/scripts/`.

## Hooks in This Repo

| Hook | Event | Purpose | Blocks? |
|------|-------|---------|---------|
| [Setup Check](scripts/setup-check.ps1) | `PreToolUse` | Checks local frontend/backend configuration before development commands | No (advisory) |
| [Doc Sync](scripts/doc-sync.ps1) | `PostToolUse` | Reminds to update ARCHITECTURE-FLOW.md after editing sensitive files | No (advisory) |

## JSON Contract

### Input (stdin)

Every hook receives a JSON object on stdin:

```json
{ "tool_name": "powershell", "tool_input": { "command": "npm run dev" }, "tool_use_id": "..." }
```

- `tool_name` — The tool being called (for example `powershell`, `edit`, or `create`)
- `tool_input` — A JSON object containing the tool's arguments; no second JSON parsing step is required

### Output (stdout)

**To block a tool call** (PreToolUse only):

```json
{
  "hookSpecificOutput": {
    "hookEventName": "PreToolUse",
    "permissionDecision": "deny",
    "permissionDecisionReason": "Explain why and what to do instead."
  }
}
```

**To show an advisory message** (PreToolUse or PostToolUse):

```json
{ "systemMessage": "Helpful reminder or suggestion." }
```

**To allow silently** — produce no output and exit 0.

## Creating Your Own Hook

### 1. Write the script

Create a new `.ps1` file in `.github/hooks/scripts/`:

```
.github/hooks/scripts/my-hook.ps1
```

Use the template below as a starting point.

### 2. Register it in the config

Add an entry to `hooks.json` under the appropriate event:

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "type": "command",
        "windows": "powershell -NoProfile -File ./scripts/my-hook.ps1",
        "cwd": ".github/hooks",
        "timeout": 10
      }
    ]
  }
}
```

### 3. Test it

Run the script manually by piping JSON to stdin:

```powershell
'{"tool_name":"powershell","tool_input":{"command":"npm run dev"},"tool_use_id":"test"}' | powershell -NoProfile -File .github/hooks/scripts/my-hook.ps1
```

## Template

Copy this as a starting point for new hooks:

```powershell
# Hook Name - PreToolUse/PostToolUse
# Brief description of what this hook does
#
# Input: { "tool_name": "...", "tool_input": { ... }, "tool_use_id": "..." }
# Output: { "permissionDecision": "deny", "permissionDecisionReason": "..." } to block
#         { "systemMessage": "..." } for advisory messages
#         (no output) to allow silently

$ErrorActionPreference = 'SilentlyContinue'
$rawInput = [Console]::In.ReadToEnd()

try {
    $hookData = $rawInput | ConvertFrom-Json
    $toolName = $hookData.tool_name
    $toolArgs = $hookData.tool_input

    # --- Your logic here ---

    # To block (PreToolUse only):
    # $response = @{
    #     hookSpecificOutput = @{
    #         hookEventName = "PreToolUse"
    #         permissionDecision = "deny"
    #         permissionDecisionReason = "Reason for blocking."
    #     }
    # }
    # $response | ConvertTo-Json -Compress
    # exit 0

    # To advise:
    # $response = @{ systemMessage = "Advisory message." }
    # $response | ConvertTo-Json -Compress
    # exit 0

    # To allow silently: do nothing (fall through)

} catch {
    # On error, allow silently (non-blocking)
}
```

## Tips

- **Keep hooks fast** — use a `timeout` of 10 seconds or less. Slow hooks degrade the agent experience.
- **Handle errors silently** — a failing hook should never block the agent. Wrap logic in `try/catch` and let errors fall through.
- **Prefer advisory over blocking** — use `systemMessage` to suggest rather than denying a tool call, unless the policy is critical.
- **Use `tool_input` directly** — it arrives as a JSON object, so no second `ConvertFrom-Json` call is required.
- **Check `tool_name` early** — exit immediately for irrelevant tools to avoid unnecessary work.
- **Test manually** — pipe sample JSON to your script before committing to verify the output format.
