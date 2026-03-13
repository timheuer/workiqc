---
name: "windows-mcp-launch-config"
description: "Generate Windows-safe MCP launch config for npm-backed tools without dropping pinned package references"
domain: "windows-app"
confidence: "high"
source: "bishop"
---

## Context

Use this when a desktop app writes MCP server config for a Node/npm package and the desired launch form is `npx -y <package> <subcommand>`, especially on Windows.

## Patterns

### Keep the package reference authoritative
- Carry the full package reference, including the pinned version, all the way into the generated `mcp-config.json`.
- Do not downgrade a requested `@scope/pkg@version` to bare `@scope/pkg` just because diagnostics also track the version.

### Wrap `npx` on Windows
- On Windows, prefer:

```json
{
  "command": "cmd.exe",
  "args": ["/d", "/s", "/c", "npx.cmd -y @scope/pkg@1.2.3 subcommand"]
}
```

- This is safer than using raw `npx` as the direct spawned executable because `npx` commonly resolves to a batch wrapper.

### Keep the direct form elsewhere
- On non-Windows platforms, the direct form is usually fine:

```json
{
  "command": "npx",
  "args": ["-y", "@scope/pkg@1.2.3", "subcommand"]
}
```

### Test both stability and idempotency
- Assert the written config contains the pinned package reference.
- Assert the Windows command shape is `cmd.exe` + `/d /s /c ... npx.cmd ...`.
- Re-run bootstrap with the same inputs and verify the config is reused instead of rewritten.

## Anti-Patterns

- Treating version pinning as “diagnostics only” while the actual config floats to latest.
- Emitting raw `npx` on Windows and assuming every caller can launch batch files safely.
- Fixing the runtime bootstrap path but leaving the app-local bootstrap on a different command shape.
