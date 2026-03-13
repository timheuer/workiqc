---
name: "bootstrap-workspace-idempotency"
description: "Protect repeated workspace bootstrap calls from rewriting Copilot config on every shell load"
domain: "windows-app"
confidence: "high"
source: "newt"
---

## Context

Use this when the app checks runtime/bootstrap readiness during startup or conversation load and the bootstrap layer writes `.copilot\mcp-config.json`.

## Patterns

### Test the second call, not just the first
- First-run tests only prove the config can be created.
- Add a second bootstrap call with the same inputs and assert the result reports no rewrite work.

```csharp
var firstRun = await bootstrap.InitializeWorkspaceAsync(version: "1.2.3");
var secondRun = await bootstrap.InitializeWorkspaceAsync(version: "1.2.3");

Assert.IsTrue(firstRun.ConfigWasWritten);
Assert.IsFalse(secondRun.ConfigWasWritten);
```

### Tie the check to real shell behavior
- Add this coverage when the shell calls bootstrap during load or send flows.
- Repeated startup is the regression boundary; unnecessary file writes are easy to miss manually.

### Prefer result objects that surface write/no-write state
- Return structured initialization results with `ConfigWasWritten` or equivalent.
- Avoid `Task`/`void` bootstrap methods that hide idempotency regressions.

## Anti-Patterns

- Declaring bootstrap "tested" after verifying only initial file creation.
- Rewriting `mcp-config.json` on every launch because startup happens to be fast.
- Trusting a cached build log over a fresh clean build when validating WinUI changes.
