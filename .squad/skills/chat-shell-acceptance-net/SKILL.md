---
name: "chat-shell-acceptance-net"
description: "Service-level acceptance testing pattern for ChatShellService-style flows with real persistence and fake runtime seams"
domain: "testing"
confidence: "high"
source: "newt"
---

## Context

Use this when a WinUI chat shell needs meaningful regression coverage before UI automation or live runtime integration is ready.

## Patterns

### Link app-layer source into a non-WinUI test project
- Create a dedicated test project that references persistence/runtime contracts and links only the app-layer service/model files under test.
- Keep `UseWinUI=false`, blank runtime identifiers, and `SelfContained=false` so test discovery stays reliable.
- This preserves production code paths without pulling in XAML/UI compilation or overlapping active UI implementation files.

### Drive the service end-to-end with real SQLite
- Back `IConversationService` with an in-memory SQLite connection and DI-created scopes.
- Let each service call create fresh scopes against the same open connection to exercise persistence updates, recent-history ordering, preview generation, and session ID storage.
- Prefer real persistence for draft promotion, assistant-message writes, and recent-conversation loading; those are the regressions users actually feel.

### Fake runtime seams, not app behavior
- Stub `ICopilotBootstrap`, `ISessionCoordinator`, and `IMessageOrchestrator` with simple delegates plus call capture.
- Cover three runtime paths explicitly:
  1. bootstrap unavailable → safe fallback stream
  2. runtime send succeeds → delta streaming + persisted assistant message
  3. runtime send/stream fails → fallback footer/badge state with preserved local transcript

### Compare streamed placeholder text carefully
- Placeholder streaming may emit chunk-level trailing spaces for UX realism.
- Persisted assistant content is typically trimmed before saving, so compare `TrimEnd()` or normalize line endings before asserting equality between streamed and stored text.

### Add theme sentries without UI automation
- Parse `App.xaml` theme dictionaries as XML and assert both Light and Dark dictionaries contain the shell’s critical resource keys.
- Also assert key surface/text colors differ across themes; this catches “dark mode regressed to light values” mistakes without touching visual implementation files.

### Split product completion from runtime prerequisites
- At final acceptance, report shell/product readiness separately from machine/runtime readiness.
- Re-run build/tests, then verify what the shipped app actually writes under `%LocalAppData%` (database, `.copilot\mcp-config.json`, EULA markers) instead of trusting abstractions or tests alone.
- Treat floating preview dependency config as a product defect, not merely an environment issue, when the team has already decided to pin tested versions.

## Anti-Patterns

- Referencing the full WinUI app project when only service behavior is under test.
- Mocking persistence so heavily that recent-history ordering, preview flattening, and session storage never execute real code.
- Waiting for UI automation before adding any theme regression checks.
