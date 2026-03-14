# WorkIQC Copilot Instructions

## Build and test commands

Run commands from `src\`.

```powershell
dotnet restore WorkIQC.slnx
dotnet build WorkIQC.slnx
dotnet test WorkIQC.slnx
dotnet run --project WorkIQC.App
```

CI uses the release configuration on Windows:

```powershell
dotnet build WorkIQC.slnx --no-restore -c Release
dotnet test WorkIQC.slnx --no-build -c Release
dotnet publish WorkIQC.App\WorkIQC.App.csproj -c Release -r win-x64 --self-contained -o ..\publish\WorkICQ
```

Run a single test project:

```powershell
dotnet test WorkIQC.App.Tests\WorkIQC.App.Tests.csproj
dotnet test WorkIQC.Runtime.Tests\WorkIQC.Runtime.Tests.csproj
dotnet test WorkIQC.Persistence.Tests\WorkIQC.Persistence.Tests.csproj
```

Run a single test or test class with MSTest filters:

```powershell
dotnet test WorkIQC.App.Tests\WorkIQC.App.Tests.csproj --filter "FullyQualifiedName~ChatShellServiceTests"
dotnet test WorkIQC.App.Tests\WorkIQC.App.Tests.csproj --filter "FullyQualifiedName~ChatShellServiceTests.SendAsync_WhenSelectedModelChanges_CreatesFreshSessionInsteadOfResuming"
```

## High-level architecture

- `WorkIQC.App` is the WinUI 3 shell. `App.xaml.cs` wires DI for persistence, bootstrap, session coordination, message orchestration, and the main page/viewmodel.
- `WorkIQC.Persistence` is the local EF Core SQLite store at `%LocalAppData%\WorkIQC\workiq.db`. It persists conversations and messages plus a `sessions` row that maps each conversation to the current Copilot session id.
- `WorkIQC.Runtime` / `WorkIQC.Runtime.Shared` are the runtime seam over the GitHub Copilot SDK. `SessionCoordinator` and `MessageOrchestrator` are thin adapters; `CopilotRuntimeBridge` owns active runtime sessions and workspace-scoped Copilot clients; `GitHubCopilotSdkAdapters.cs` translates app/runtime configuration into SDK `SessionConfig` and `ResumeSessionConfig`.
- The app-owned Copilot workspace is also under `%LocalAppData%\WorkIQC`. `StorageHelper` resolves the local database path, diagnostics log path, and `.copilot\mcp-config.json` location.

### Prompt flow

1. `MainPageViewModel.StreamConversationResponseAsync()` sends a `ShellSendRequest` with the conversation id, persisted session id, and the thread's selected model.
2. `ChatShellService.SendAsync()` persists the user message, evaluates bootstrap readiness, creates a `SessionConfiguration`, then resumes or creates the runtime session and dispatches the prompt through `IMessageOrchestrator`.
3. `CopilotRuntimeBridge.SendMessageAsync()` sends the turn to the active SDK session and exposes both assistant deltas and tool activity as `IAsyncEnumerable`.
4. `MainPageViewModel` consumes both streams to update the transcript, activity badge, footer text, and conversation preview while the reply is in flight.

## Key conventions

- Keep the "live runtime only" contract intact. `ChatShellService` is intentionally opinionated: if bootstrap/runtime setup is not ready, it returns a blocked plan instead of fabricating a local answer from saved history or placeholders.
- Treat `%LocalAppData%\WorkIQC` as the real app workspace. Changes to persistence, MCP config generation, auth/EULA markers, or diagnostics should go through `StorageHelper` and the bootstrap services rather than assuming the repository root is the runtime working directory.
- Model choice is per conversation thread in `MainPageViewModel.ConversationSeed.SelectedModelId`, but it is applied to the underlying Copilot session used by that thread. When the selected model changes, the app should start a fresh runtime session rather than mutating an already active session in place.
- `GetAvailableModelsAsync()` should only surface models that the current user can actually use. The SDK adapter filters model results to enabled policy states before they reach the UI.
- The generated `.copilot\mcp-config.json` is app-owned and rewritten only when the serialized content changes. Runtime session creation reads this file and converts each `mcpServers` entry into an SDK `McpLocalServerConfig`.
- The system guidance passed in `ChatShellService` is part of product behavior, not incidental prompt text. It enforces the "WorkICQ-first" tool posture, first-person workplace resolution, and the rule against answering from local history.
- Conversation history and Copilot session state are separate concerns. Messages are persisted in SQLite, but only the Copilot session id is stored for resume. Any change that affects session lifecycle must keep `ConversationService.SetCopilotSessionIdAsync()` in sync with runtime behavior.
- Diagnostics are important in this repo. App/runtime components use `Trace.WriteLine`, and the desktop app writes a rotating diagnostics log under `%LocalAppData%\WorkIQC\logs\workiqc-runtime.log`.
- Tests rely heavily on in-memory SQLite and seam-friendly test doubles. `ChatShellServiceTests`, `MainPageViewModelTests`, `CopilotRuntimeBridgeTests`, and `ConversationServiceTests` are the best starting points for matching existing patterns.

## Commit messages

Follow these guidelines for commit messages for source control:

- Create a one-line summary at the top. 
- Then use conventional commit message format like feat, doc, chore, etc. for the details. 
- Use emojis.