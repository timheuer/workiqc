# WorkIQC Source Code

Windows desktop client for Microsoft 365 Copilot WorkIQ.

## Projects

### WorkIQC.App
WinUI 3 Windows App SDK application. Main desktop app shell.

**Key files:**
- `App.xaml.cs` — Application startup and DI configuration
- `Views/MainPage.xaml` — Main chat shell

### WorkIQC.Persistence
SQLite-based persistence layer using Entity Framework Core.

**Key APIs:**
- `IConversationService` — Manage conversations, messages, and Copilot session resume
- `WorkIQDbContext` — EF Core context with Conversation/Message/Session schema
- `StorageHelper` — Database and workspace paths (`%LocalAppData%\WorkIQC\`)

### WorkIQC.Runtime / WorkIQC.Runtime.Abstractions
Runtime seam for Copilot + WorkIQ integration.

**What is real now:**
- Workspace path resolution under `%LocalAppData%\WorkIQC\`
- App-managed `.copilot\mcp-config.json` generation for WorkIQ
- Dependency discovery/readiness reporting for Copilot CLI, Node.js, npm, and npx
- Explicit EULA marker reporting for first-run flow
- Live Copilot session creation, resume, disposal, and model-aware session reuse
- Live message dispatch, assistant streaming, and WorkIQ tool event observation
- Blocking responses when the live WorkIQ path is unavailable or not invoked

## Building

```bash
dotnet build WorkIQC.slnx
```

## Running

```bash
dotnet run --project WorkIQC.App
```

In Visual Studio, use the app project's default **WorkIQC.App (Unpackaged)** launch profile for F5. Debug builds intentionally avoid MSIX tooling so the desktop shell starts as a normal unpackaged WinUI app, while non-Debug configurations can still opt into packaging later.

## Database

Location: `%LocalAppData%\WorkIQC\workiq.db`

Schema:
- `conversations` — Chat sessions (id, title, created_at, updated_at)
- `messages` — Chat messages (id, conversation_id, role, content, timestamp, metadata)
- `sessions` — Copilot SDK session mapping (id, conversation_id, copilot_session_id)

## Architecture

```
WorkIQC.App (WinUI 3)
    ├─> WorkIQC.Persistence (EF Core SQLite)
    └─> WorkIQC.Runtime (bootstrap readiness + typed runtime seam)
          └─> WorkIQC.Runtime.Abstractions (contracts/models)
```

Dependency injection configured in `App.xaml.cs`:
```csharp
services.AddPersistence(); // Registers IConversationService, WorkIQDbContext
```

Access from ViewModels:
```csharp
var conversationService = (Application.Current as App)?.Services?.GetService<IConversationService>();
```

## Next Steps

- [ ] Simplify duplicated runtime/bootstrap code shared between the App and Runtime layers
- [ ] Reduce hot-path diagnostic noise while preserving lifecycle and failure evidence
- [ ] Batch shell-history loading to avoid per-conversation message/session fetches
- [ ] Revisit project boundaries and references once the first simplification pass is complete
