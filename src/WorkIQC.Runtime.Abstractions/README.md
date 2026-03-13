# WorkIQC Runtime Integration Contracts

This directory defines the integration seam between the WorkIQ desktop client and the GitHub Copilot SDK.

## Projects

### WorkIQC.Runtime.Abstractions

Defines interfaces, models, and exceptions for the runtime integration. This is the contract layer that both the app shell and the runtime implementation depend on.

**Core Interfaces:**

- `ICopilotBootstrap` — Probes runtime prerequisites, generates the WorkIQ MCP config, and reports EULA readiness
- `ISessionCoordinator` — Creates, resumes, and disposes Copilot sessions
- `IMessageOrchestrator` — Sends user messages, streams assistant responses, and exposes tool events

**Bootstrap Models:**

- `RuntimeReadinessReport` — Dependency and capability report for Copilot/WorkIQ prerequisites
- `WorkspaceInitializationResult` — Resolved workspace paths plus generated `mcp-config.json` details
- `EulaAcceptanceReport` — App-owned EULA marker status for first-run flow

**Session and Message Models:**

- `SessionConfiguration` — Workspace path, MCP config, allowed tools, system guidance
- `SessionState` — Session lifecycle plus structured error code/message
- `ChatMessage` — User/assistant/system messages with metadata
- `SendMessageRequest` / `SendMessageResponse` — Message send contracts
- `StreamingDelta` — Token-by-token streaming response
- `ToolEvent` — WorkIQ tool invocation lifecycle (Started, Progress, Completed, Failed)

**Exceptions:**

- `RuntimeException` — Base exception for runtime errors with optional error code
- `SessionNotFoundException` — Session ID not found or expired
- `BootstrapException` — Dependency or workspace initialization failures
- `UnsupportedRuntimeActionException` — Honest failure when SDK-backed behavior is still unavailable

### WorkIQC.Runtime

Readiness-first implementation with live session orchestration where the preview SDK can support it.

**Implemented now:**
1. Workspace path resolution under `%LocalAppData%\WorkIQC\`
2. App-managed `.copilot\mcp-config.json` generation for WorkIQ, including Windows-safe `cmd /c npx` launch shape
3. Dependency discovery for Copilot CLI, Node.js, npm, and npx
4. App-owned EULA marker reporting for first-run UX
5. GitHub Copilot SDK-backed session create/resume/dispose via a shared runtime bridge
6. Message dispatch and assistant streaming through buffered `IAsyncEnumerable<T>` channels
7. Tool event observation surfaced as explicit runtime activity updates for the shell

**Still blocked externally:**
1. First-run WorkIQ auth/EULA handshake still depends on the live preview SDK + CLI environment
2. Tool execution remains contingent on a working local Copilot CLI login and the preview SDK's MCP behavior on Windows
3. The current shell service exposes tool activity as human-readable status text; richer typed UI plumbing can land later without changing the runtime seam

## Design Principles

1. **Testable Boundaries** — Contracts are interface-based so the app shell can test against mocks without SDK dependency
2. **Streaming-First** — Streaming deltas and tool events are modeled as `IAsyncEnumerable<T>` for clean reactive pipeline
3. **App-Owned Persistence** — Session ID is returned to app for its own history tracking; SDK session resume is a bonus, not a requirement
4. **WorkIQ-Explicit** — `AllowedTools` defaults to `['workiq']` to make WorkIQ-first posture explicit
5. **Error Transparency** — Readiness reports, error codes, and typed exceptions keep failure modes debuggable

## Integration Flow

### Bootstrap (Cold Start)

```csharp
var bootstrap = serviceProvider.GetRequiredService<ICopilotBootstrap>();
var runtimeReadiness = await bootstrap.EnsureRuntimeDependenciesAsync();
var workIqReadiness = await bootstrap.EnsureWorkIQAvailableAsync();
var workspace = await bootstrap.InitializeWorkspaceAsync();
var eula = await bootstrap.VerifyEulaAcceptanceAsync();
```

### Session Creation (New Chat)

```csharp
var coordinator = serviceProvider.GetRequiredService<ISessionCoordinator>();
var config = new SessionConfiguration
{
    WorkspacePath = "%LocalAppData%\WorkIQC\",
    McpConfigPath = "%LocalAppData%\WorkIQC\.copilot\mcp-config.json",
    EnableStreaming = true,
    AllowedTools = new[] { "workiq" }
};

var sessionId = await coordinator.CreateSessionAsync(config);
```

### Message Send + Streaming (User Turn)

```csharp
var orchestrator = serviceProvider.GetRequiredService<IMessageOrchestrator>();

var response = await orchestrator.SendMessageAsync(new SendMessageRequest
{
    ConversationId = conversationId,
    UserMessage = userInput,
    SessionId = sessionId
});

await foreach (var delta in orchestrator.StreamResponseAsync(sessionId))
{
    Console.Write(delta.Content);
}

await foreach (var toolEvent in orchestrator.ObserveToolEventsAsync(sessionId))
{
    Console.WriteLine($"{toolEvent.ToolName}: {toolEvent.EventType}");
}
```

### Session State (Restore Chat)

```csharp
var resumed = await coordinator.ResumeSessionAsync(sessionId);
if (!resumed)
{
    // Fall back: display transcript from app-owned SQLite history
    // Create a fresh SDK session on the next user turn
}
```

## Next Steps

1. **WorkIQ First-Run Spike** — Persist explicit EULA acceptance and auth state against the live runtime, not just marker files
2. **Runtime Hardening** — Add retry/backoff around preview SDK startup and session resume failures
3. **Typed UI Activity Flow** — Promote tool activity from footer text into a richer shell event model when the UI is ready for it
