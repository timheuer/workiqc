# WorkICQ

A Windows desktop chat client for Microsoft 365 Copilot, powered by the GitHub Copilot SDK and the WorkIQ MCP server. Ask questions about your meetings, emails, documents, teammates, and org — all from a native WinUI 3 app.

## Features

- **Multi-session chat** — Run multiple conversations simultaneously; send in one while another streams
- **Persistent history** — Conversations, messages, and session resume hints saved locally in SQLite
- **Live WorkIQ integration** — Queries Microsoft 365 Copilot via the WorkIQ MCP tool path
- **Markdown rendering** — Rich response formatting with tables, code blocks, lists, and headings
- **Notification sound** — Configurable ICQ "uh oh" alert when responses finish
- **Copy to clipboard** — One-click copy of response content (strips follow-up suggestions)

## Prerequisites

- Windows 10 (1809+) or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (preview)
- [GitHub Copilot CLI](https://docs.github.com/en/copilot) (`github-copilot-cli` or `copilot`)
- Node.js + npm/npx (for WorkIQ MCP server)

## Building

```bash
cd src
dotnet build WorkIQC.slnx
```

## Running

```bash
cd src
dotnet run --project WorkIQC.App
```

In Visual Studio, use the **WorkIQC.App (Unpackaged)** launch profile for F5.

## Publishing

Self-contained x64 build:

```bash
dotnet publish src/WorkIQC.App/WorkIQC.App.csproj -c Release -r win-x64 --self-contained -o publish/WorkICQ
```

The output folder can be zipped and distributed — no installer required.

## Architecture

```
WorkIQC.App (WinUI 3 — UI, ViewModels, Services)
    ├── WorkIQC.Persistence (EF Core SQLite — conversations, messages, sessions)
    ├── WorkIQC.Runtime (bootstrap validation, session coordination)
    │     └── WorkIQC.Runtime.Abstractions (contracts and models)
    └── WorkIQC.Runtime.Shared (GitHub Copilot SDK bridge)
```

### Key layers

| Layer | Responsibility |
|-------|---------------|
| **App** | WinUI 3 shell, MVVM, chat UI, settings, sidebar |
| **Persistence** | SQLite database at `%LocalAppData%\WorkIQC\workiq.db` |
| **Runtime** | Copilot CLI/Node.js dependency validation, EULA/auth bootstrap |
| **Runtime.Shared** | `CopilotRuntimeBridge` — session lifecycle, message dispatch, event streaming via GitHub Copilot SDK |

### Database

Location: `%LocalAppData%\WorkIQC\workiq.db`

Tables:
- `conversations` — id, title, created_at, updated_at
- `messages` — id, conversation_id, role, content, timestamp, metadata
- `sessions` — id, conversation_id, copilot_session_id

## First-run setup

On first launch, complete the bootstrap from **Settings**:

1. **Accept WorkICQ consent** — Reviews and records EULA acceptance
2. **Launch Copilot sign-in** — Opens terminal for `copilot login`
3. **Recheck bootstrap** — Validates all prerequisites are met

Once both steps show green ✓, the app can create live Copilot SDK sessions.

## CI

GitHub Actions builds on every push. The workflow (`.github/workflows/ci.yml`) restores, builds, tests, and publishes a downloadable `WorkICQ-win-x64` artifact.

## License

[MIT](LICENSE)
