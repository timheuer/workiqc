---
name: "copilot-sdk-session-bridge"
description: "Bridge the GitHub Copilot .NET SDK into a UI/runtime seam without losing streamed deltas or tool events"
domain: "runtime-integration"
confidence: "high"
source: "vasquez"
---

## Context

Use this when a desktop or app-shell layer needs real Copilot SDK session orchestration, but the UI consumes response text and tool activity on separate timelines.

## Patterns

### Buffer one turn at a time
- Create a per-session turn object with separate channels for assistant deltas and tool events.
- Assign the turn before calling `session.SendAsync(...)` so early SDK events do not race ahead of the UI enumerators.
- Complete both channels on `SessionIdleEvent` or `SessionErrorEvent`.
- Buffer raw assistant deltas as a fallback, but prefer the SDK's completed assistant message as the user-facing transcript for tool-backed turns. That keeps planning/tool chatter out of the chat bubble while preserving a recovery path if the completed message never arrives.

### Keep one shared bridge for session + message APIs
- Let `ISessionCoordinator` and `IMessageOrchestrator` delegate into the same runtime bridge.
- The bridge should own Copilot client lifetime, session caches, and event subscriptions.
- If the app and runtime projects cannot reference each other directly, compile the bridge from shared source into both assemblies.

### Treat resume as part of the happy path
- Before creating a new session for a persisted conversation, attempt `ResumeSessionAsync`.
- Only fall back to `CreateSessionAsync` when resume fails cleanly.
- Keep the app-owned session ID in local persistence regardless of whether the SDK session is hot in memory.

### Make tool activity visible immediately
- Map `ToolExecutionStartEvent` / `ToolExecutionCompleteEvent` into human-readable shell activity even if the UI does not yet have a typed tool-event panel.
- This keeps debugging grounded in visible runtime behavior instead of hidden logs.

### Windows MCP launch shape matters
- For the WorkIQ MCP server, write the launch command directly as `npx` with args `["-y", "@microsoft/workiq", "mcp"]`.
- Keep the emitted `.copilot\mcp-config.json` literal and easy to diff; if the app tracks a requested version separately for diagnostics, do not smuggle it into the launch command shape.

## Anti-Patterns

- Calling `SendAsync` and only then allocating stream consumers.
- Keeping session and message orchestration in separate state stores.
- Treating persisted session IDs as enough without attempting resume.
- Hiding tool activity until a "perfect" UI exists.
