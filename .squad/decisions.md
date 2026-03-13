# Squad Decisions

## WorkIQ Package Versioning: Use Latest (2026-03-14)

### Remove pinned @microsoft/workiq version, always resolve latest

**Decision:** Remove any hard-pinned `@microsoft/workiq@...` version constraints across bootstrap, runtime, and app paths. Always use `@microsoft/workiq` or `@microsoft/workiq@latest` for automatic resolution.

**Why:** The pinned preview package version (`@1.0.0-preview.123`) no longer exists in npm registry, causing native bootstrap to fail with `npm ERR! code ETARGET ... No matching version found for @microsoft/workiq@1.0.0-preview.123`.

**Implementation:**
- Removed hard-pinned version from all bootstrap configuration paths
- Removed pinned version from MCP startup sequence
- Removed pinned version from app-level package dependencies
- Normalized all paths to use latest package resolution via `@microsoft/workiq`
- Updated test fixtures and bootstrap flow expectations

**Owner:** Vasquez  
**Status:** Complete (70/70 tests passed; ETARGET error resolved)

---

## WorkIQ EULA Pre-Bootstrap Refactor (2026-03-14)

### WorkIQ EULA acceptance: Pre-session bootstrap path

**Decision:** Move WorkIQ EULA acceptance out of normal Copilot SDK chat turns and into an explicit native/bootstrap step that runs before live chat sessions.

**Why:** Repeated failures showed the in-session `workiq-accept_eula` path is not reliable enough; normal chat should not depend on a tool that may not be surfaced in-session.

**Implementation:**
- Use verified native WorkIQ bootstrap command (`accept-eula` via pinned WorkIQ CLI/package path)
- Establish consent evidence and persist marker only after successful bootstrap execution
- Keep regular chat sessions focused on `workiq-ask_work_iq`
- Local marker files remain untrusted unless they contain verified live/bootstrap evidence payloads; legacy marker-only shapes stay invalid

**Owners:** Vasquez, Bishop  
**Status:** Complete (Vasquez: refactor verified 70/70; Bishop: UX aligned 39/39)

**Note:** This supersedes the prior in-session EULA recovery direction due to unreliability of chat-session consent path.

---

## WorkIQ Desktop Client v1 Decisions (2026-03-13)

### 1. UI Framework: WinUI 3

**Decision:** Use WinUI 3 (Windows App SDK) for v1 Windows desktop client.

**Rationale:**
- Modern, fast, native DirectX rendering outperforms Electron for markdown-heavy UIs
- Baseline memory ~100–300 MB (vs Electron's 400 MB–1 GB+)
- Direct Copilot SDK integration without Node.js shim
- MSIX packaging with automatic updates via Windows runtime
- Fluent Design System gives polish out-of-box
- Team can use C# + XAML (proven patterns)

**Rejected Alternatives:**
- **Electron + React:** Memory bloat unacceptable for chat-heavy UI; feels like web app; Copilot SDK integration harder
- **.NET MAUI:** Desktop support immature; no advantage for Windows-only v1; markdown rendering weaker
- **WPF:** Legacy rendering stack; no modern Fluent Design; no Microsoft investment post-2025

**Owner:** Bishop  
**Status:** Recommended for team consensus

---

### 2. Local Persistence: SQLite + App-Owned Schema

**Decision:** Use SQLite as canonical history store with app-owned schema, plus SDK session ID for resume capability.

**Rationale:**
- Fast, queryable history for instant sidebar display
- Handles large histories (1000+ messages) efficiently
- Supports full-text search and session previews
- App-owned persistence gives deterministic recovery if SDK session resume fails
- Easy to export/backup (just copy .db file)

**Schema Sketch:**
- `conversations(id, title, created_at, updated_at)`
- `messages(id, conversation_id, role, content, timestamp, metadata)`
- `sessions(conversation_id, copilot_session_id, created_at)`

**Write Strategy:** Save user turn immediately; save assistant streaming as transient draft; finalize on complete or idle.

**Restore Strategy:** Load from SQLite first (instant UX); attempt SDK session resume in background; if resume fails, preserve transcript and re-home next user turn into fresh Copilot session.

**Storage Location:** `ApplicationData.Current.LocalFolder` (respects MSIX sandboxing and Windows user isolation)

**Owner:** Bishop, Vasquez  
**Status:** Recommended

---

### 3. Markdown Rendering: Markdig + WebView2

**Decision:** Use app-owned markdown pipeline: parse with Markdig, render into themed HTML, host in WebView2.

**Rationale:**
- Safe rendering (no XSS from arbitrary HTML)
- Strong control over theming, code block syntax highlighting, tables, links, citations
- Better code-block rendering and syntax highlighting than XAML-only
- Easier parity with modern chat UIs (ChatGPT-like polish)
- DPI scaling and Windows theme integration handled by WebView2

**Supported Markdown (v1):**
- Text: Headings (h1–h4), bold/italic, blockquotes, inline code, lists, links
- Code: Fenced blocks with language ID, syntax highlighting (Prism.js/highlight.js), copy button
- Tables: Pipe tables with aligned columns, scrollable on narrow screens
- Other: Horizontal rules, task lists
- NOT: HTML passthrough (security risk)

**Owner:** Vasquez, Hicks  
**Status:** Recommended

---

### 4. Copilot SDK Integration: WorkIQ-First In-Process Architecture

**Decision:** Host GitHub Copilot .NET SDK as in-process orchestration service with WorkIQ MCP as sole default tool.

**Rationale:**
- One Copilot session per chat thread, with streaming enabled
- WorkIQ-first system guidance restricts tool list to WorkIQ only by default
- App-owned workspace under `%LocalAppData%\WorkIQC\` with pinned WorkIQ version
- Tool execution visible in transcript (status "Checking meetings and mail…")
- Clear separation of concerns: model → tool → renderer

**Bootstrap Requirements:**
1. Ensure Copilot runtime prerequisites present
2. Ensure WorkIQ available at pinned, tested version
3. Write app-managed `.copilot\mcp-config.json`
4. Verify EULA acceptance and first-run authentication
5. Launch Copilot SDK with app-owned workspace

**MCP Configuration:**
```json
{
  "mcpServers": {
    "workiq": {
      "command": "npx",
      "args": ["-y", "@microsoft/workiq@<pinned-version>", "mcp"]
    }
  }
}
```

**Owner:** Vasquez  
**Status:** Recommended; needs technical spikes on CLI discovery and dependency bundling

---

### 5. UI Layout: Three-Zone ChatGPT-Like Pattern

**Decision:** Use proven three-zone desktop layout: sidebar (history) + chat pane + input area.

**Rationale:**
- Industry standard from ChatGPT, Claude, modern chat apps
- Sidebar (280px fixed, collapsible): New Chat button, session list with previews, settings
- Chat pane (center, flex-grow): Header, scrollable transcript, typing indicators
- Input area (bottom, sticky): Auto-growing text box (3–8 lines), send button, placeholder
- Responsive breakpoints: Wide (>1400px), Medium (800–1400px), Narrow (sidebar collapses)

**Session History Behavior:**
- Recent-first ordering with date grouping ("Today", "Yesterday", "1 week ago")
- Auto-generated titles from first message (50 chars) or user-set names
- Hover actions: Pin, Delete, Archive
- Smooth scroll between sessions

**Interaction Patterns:**
- Dark mode by default (respects system preference)
- Ctrl/Cmd+Enter to send; Shift+Enter for newline
- Disable send button while sending
- Streaming responses with visible token-by-token rendering
- Copy-to-clipboard on code blocks
- WCAG AA accessibility (4.5:1 contrast, keyboard navigation)

**Owner:** Hicks  
**Status:** Recommended; needs Figma wireframe and detailed interaction spec

---

### 6. Packaging: MSIX + Windows App Installer

**Decision:** Use MSIX for app packaging with Windows App Installer for distribution and automatic updates.

**Rationale:**
- Modern, works with Windows Package Manager or direct .msix file
- Automatic updates via Windows runtime (transparent to users)
- Clean uninstalls (no registry junk)
- Built-in sandboxing (app data isolated to `%LocalAppData%\Packages\...`)
- Code signing required for distribution but provides security assurance

**Important:** Never write SQLite DB to Program Files (read-only in MSIX). Always use `ApplicationData.Current.LocalFolder`.

**Owner:** Bishop  
**Status:** Recommended

---

### 7. Test Strategy: Manual-First UX, Automated Early Persistence

**Decision:** Manual-first testing for chat UX and visual regressions; automated early for persistence and encoding.

**Rationale:**
- Visual regressions (layout, markdown rendering, scrolling) caught manually; automated tests miss glitches
- Silent failures (data corruption, session loss, encoding bugs) compound over time; automation catches regressions
- Chat happy-path should be manually verified before automation

**Manual-First (UX & Rendering):**
- Single message cycle
- Chat history navigation
- Markdown rendering variety (code blocks, tables, links)
- Long conversation scroll (100+ messages)
- App restart with open session

**Automate Early (Persistence & Boundaries):**
- Session file creation and load
- Session ID uniqueness (rapid-fire creation >100)
- Markdown escape test suite (nested markdown, code block escape, link hijacking)
- Encoding/Unicode handling (emoji, RTL, CJK)
- App doesn't crash on empty/missing/corrupted data

**High-Risk Edge Cases:**
1. Session file corruption on crash (restart with corrupt/empty history)
2. Session ID collision (silent data loss)
3. Large session bloat (500+ messages → sluggish)
4. Cross-user session leak (privacy breach on shared machine)
5. Nested markdown chaos or code block escape
6. Link hijacking (javascript: URLs)
7. Scroll position lost when switching sessions

**Owner:** Newt  
**Status:** Recommended; needs clarification on storage format, markdown renderer choice, error state UX

---

## Unresolved Technical Questions (Tim Clarification Needed)

### Product & Packaging

1. **Must shipped app be self-contained?**  
   Copilot SDK depends on Copilot CLI runtime; WorkIQ depends on Node/npm. Can app require preinstalled dependencies, or must everything be bundled?

2. **Is GitHub Copilot dependency acceptable?**  
   SDK integration implies Copilot authentication or BYOK model. Is this acceptable for target audience?

3. **WorkIQ-only constraint in v1?**  
   Recommendation: Yes. If web/file/system tools wanted later, that's explicit product expansion.

### Enterprise & Security

4. **Local history storage policy for WorkIQ-derived content?**  
   App can save chats locally, but workplace summaries may rest on disk. Retention/encryption policy needed.

5. **Expected first-run consent flow?**  
   WorkIQ requires EULA acceptance and may require admin consent in tenant. Desired UX when tenant not ready?

6. **Roaming or shared-device protections?**  
   If for enterprise-managed devices: profile isolation, encryption at rest, explicit sign-out/session-clearing needed?

### UX & Data Behavior

7. **Source links/citations should open M365 artifacts?**  
   If yes, need clear contract for link representation and launch behavior.

8. **Chat export, deletion, or retention controls in v1?**  
   Persistence gets more complex if users must export or purge history on demand.

### Technical Spikes Required

9. **Copilot CLI discovery/config behavior spike** — App-owned `.copilot\mcp-config.json` + working directory expected to work, but needs spike before locking packaging.

10. **Preview dependencies may move** — Both Copilot SDK and WorkIQ are preview; pinning versions and upgrade paths essential.

---

### 8. Windows App Shell Scaffold

**Decision:** Created first-pass WinUI 3 app shell scaffold with persistence layer and DI registration.

**Implementation:**
- **WorkIQC.App** — WinUI 3 Windows App SDK application
- **WorkIQC.Persistence** — SQLite-based persistence layer with EF Core
  - Schema: `Conversation`, `Message`, `Session` tables (matches team decision #2)
  - `IConversationService` API for CRUD and session resume
  - `StorageHelper` for `%LocalAppData%\WorkIQC\` database paths
- **WorkIQC.slnx** — Solution file with Directory.Build.props configuration
- Database auto-initialized on first launch via `InitializeDatabaseAsync()`
- DI container wired at App startup

**Status:** Complete; build succeeds  
**Owner:** Bishop

---

### 9. Chat Shell UI: Three-Zone Implementation

**Decision:** Use `MainPageViewModel` as binding seam for first WinUI chat shell with local history hydration and simulated streaming.

**Implementation:**
- **Sidebar (280px):** Recent conversations with date grouping, "New Chat" button
- **Transcript Pane:** Message list with role-based styling, markdown-ready structure, auto-scroll
- **Sticky Composer:** Auto-growing text box, Ctrl+Enter send, disable-while-sending
- **Sample Data:** Hydrates sidebar from local SQLite on launch
- **Simulated Streaming:** Token-by-token response rendering with visual indicator
- Streaming behavior is local simulation; real Copilot SDK streams via `IMessageOrchestrator` contract later

**Why:**
- Keeps three-zone shell working before orchestration is wired
- Sidebar reflects real local history without coupling view to persistence internals
- Preserves stable transcript/composer state model that runtime deltas plug into later

**Status:** Complete; build succeeds  
**Owner:** Hicks

---

### 10. Runtime Integration Contracts

**Decision:** Create explicit, testable integration contracts between the WorkIQ desktop app shell and GitHub Copilot SDK runtime via code-first interfaces.

**Contracts:**
- `ICopilotBootstrap` — Ensures Copilot CLI + WorkIQ MCP present, workspace initialized
- `ISessionCoordinator` — Creates, resumes, disposes Copilot sessions
- `IMessageOrchestrator` — Sends user messages, streams assistant responses, exposes tool events

**Design Principles:**
- **Testable Boundaries** — Interfaces let app shell test against mocks without SDK dependency
- **Streaming-First** — Model deltas and tool events as `IAsyncEnumerable<T>`
- **App-Owned Persistence** — SDK session ID returned to app for history; session resume is bonus
- **WorkIQ-Explicit** — `AllowedTools` defaults to `["workiq"]`
- **Error Transparency** — Typed exceptions and nullable `ErrorMessage` properties

**Implementation:**
- `WorkIQC.Runtime.Abstractions` — Interface contracts (no SDK dependency)
- `WorkIQC.Runtime` — Implementation stubs (SDK integration pending spikes)

**Status:** Proposed; stubs in place  
**Owner:** Vasquez

---

### 11. Test Harness: Persistence and Runtime Seams

**Decision:** Add focused automated coverage for persistence and runtime seams instead of placeholder smoke tests.

**Implementation:**
- **WorkIQC.Persistence.Tests** (7 tests)
  - Default conversation creation on first launch
  - Unicode + JSON metadata storage
  - Foreign-key constraints (orphan message rejection)
  - Session ID uniqueness and upsert behavior
  - Recent-first ordering
  - Cascade deletion of messages/session rows
  - All tests are real SQLite + EF Core exercises with actual DB I/O

- **WorkIQC.Runtime.Tests** (10 tests)
  - Runtime contract boundaries: `CopilotBootstrap`, `SessionCoordinator`, `MessageOrchestrator`
  - Stubs throw explicit `NotImplementedException` messages until implementation lands
  - Tests lock current spike boundary; validates contract shape

- **Test Projects Configuration**
  - Both projects added to `WorkIQC.slnx`
  - Override repo-wide `RuntimeIdentifiers`, `UseWinUI`, `SelfContained` so `dotnet test` runs as test assemblies

**Test Status:** ✅ 17/17 passing, no failures; `dotnet test .\src\WorkIQC.slnx --nologo` validates all seams

**Status:** Complete  
**Owner:** Newt

---

## Next Implementation Spikes

1. **WinUI 3 + Copilot SDK spike** — Create/resume session, stream assistant output, capture tool events
2. **WorkIQ bootstrap spike** — App-owned MCP config, EULA handling, first-run auth/consent behavior
3. **Markdown rendering spike** — Markdig + WebView2 with code blocks, tables, links, dark mode, copy buttons
4. **Persistence spike** — Instant session restore from SQLite and fallback when Copilot session resume fails

---

### 12. App Composition via Dependency Injection

**Decision:** Resolve the WinUI 3 shell through DI at app startup with constructor-injected `MainPage` and `MainPageViewModel`.

**Rationale:**
- Removes page-level service location; keeps the shell production-shaped
- Eliminates coupling between XAML and service instantiation details
- Enables clear testability (can inject mocks at composition root)
- Provides stable binding seam for UI work while runtime integration happens underneath

**Implementation:**
- `App.xaml.cs` acts as composition root, resolving `MainPage` + dependencies from DI container
- All services (`IConversationService`, `IChatShellService`, `ICopilotBootstrap`, etc.) registered in one place
- Page constructor receives fully-wired dependencies; no service location

**Owner:** Bishop  
**Status:** Implemented and validated; build succeeds, DI wiring produces correct instantiation

---

### 13. Application-Layer Chat Shell Service Seam

**Decision:** Introduce `IChatShellService` as an application-owned bridge between persistence (`IConversationService`) and runtime abstractions (`ICopilotBootstrap`, `ISessionCoordinator`, `IMessageOrchestrator`).

**Rationale:**
- View models and XAML code-behind now depend on one testable boundary instead of juggling multiple service contracts
- Encapsulates load-from-persistence, fallback-to-sample, and send-via-runtime orchestration logic in one place
- Separates concerns: UI state updates flow through `IMessageOrchestrator`, but the app layer coordinates which orchestrator to use

**Implementation:**
- `IChatShellService` exposes methods like `LoadRecentConversationsAsync()`, `SendMessageAsync(text)`
- Implementation owns the logic: load from SQLite if data exists, fall back to sample data on first run, delegate actual send to `IMessageOrchestrator`
- View models bind to `IChatShellService`; runtime/persistence details hidden

**Owner:** Bishop  
**Status:** Implemented; provides stable interface for UI and test mocking

---

### 14. Framework Targeting Mismatch Resolution via Linked Abstractions

**Decision:** Link `WorkIQC.Runtime.Abstractions` source files directly into `WorkIQC.App` project instead of creating a project reference, to work around net10.0 vs net9.0 targeting mismatch.

**Rationale:**
- `WorkIQC.App` targets `net10.0-windows10.0.19041.0` (latest Windows SDK)
- `WorkIQC.Runtime` targets `net9.0-windows10.0.22621.0` (Vasquez's SDK integration lane)
- Mismatched targets prevent normal project reference
- Linking source preserves interface contract visibility in app while allowing independent targeting in runtime lane
- When SDK integration completes, either unify targeting or keep this approach if it proves cleaner

**Implementation:**
- `WorkIQC.App.csproj` includes `<Compile Include="..\\WorkIQC.Runtime.Abstractions\\**\\*.cs" />`
- App registers local adapter implementations of bootstrap/coordinator/orchestrator (placeholders until SDK spike)
- No binary coupling; app can target whatever Windows SDK version is stable

**Owner:** Bishop, Vasquez  
**Status:** Implemented; validated as workaround for framework mismatch

---

### 15. Runtime Readiness Bootstrap Over SDK Orchestration

**Decision:** Implement full `ICopilotBootstrap` behavior for workspace initialization, MCP config generation, and prerequisite discovery immediately; keep session orchestration and streaming as explicit `NotImplementedException` stubs until SDK spike results are available.

**Rationale:**
- Bootstrap work is safe and testable: workspace init, config generation, prerequisite checks, EULA tracking — no SDK dependency
- Session/streaming stubs with explicit failure messages prevent accidental coupling to incomplete SDK contract
- Allows app shell to launch and validate bootstrap readiness without waiting for SDK spike
- Bishop/Hicks/Newt get a real, usable readiness seam instead of placeholder stubs

**Implementation:**
- **`ICopilotBootstrap.VerifyAsync()`** fully implemented:
  - Creates/validates `%LocalAppData%\WorkIQC\.copilot\` workspace
  - Generates deterministic `mcp-config.json` with pinned WorkIQ version
  - Discovers Copilot CLI, Node.js, npm, npx
  - Tracks EULA acceptance via `.eula-accepted` marker file
  - Returns `BootstrapResult` with detailed status or `BootstrapException` with clear failure message
- **`ISessionCoordinator`** remains stub: `NotImplementedException("SessionCoordinator not implemented; SDK spike required")`
- **`IMessageOrchestrator`** remains stub: `NotImplementedException("MessageOrchestrator streaming not implemented; SDK spike required")`

**Test Coverage:**
- Workspace directory creation (idempotent)
- MCP config generation and JSON syntax validation
- EULA marker lifecycle (first run, already accepted, error cases)
- Copilot CLI detection (success and missing paths)
- Node/npm/npx prerequisite discovery
- Cascade failure reporting with detailed messages
- 10 tests, all passing; validates bootstrap contract boundaries

**Owner:** Vasquez  
**Status:** Implemented; 20/20 tests passing (including persistence and framework tests)

---

### 16. Build Stability: Linked Abstraction Source Tree

**Decision:** Link the entire `WorkIQC.Runtime.Abstractions` source tree into `WorkIQC.App` instead of maintaining a hand-picked file list.

**Rationale:**
- App and runtime lanes are split by target-framework constraints, preventing normal project reference
- Manual curation of compile lists drifted as new bootstrap models were added, breaking the WinUI build
- Linking whole abstractions tree preserves contract shape automatically; least fragile workaround at shipping time

**Implementation:**
- `WorkIQC.App.csproj` now includes `..\WorkIQC.Runtime.Abstractions\**\*.cs` with bin/obj excluded
- Local runtime adapters brought up to current bootstrap contract shape (`RuntimeReadinessReport`, `WorkspaceInitializationResult`, `EulaAcceptanceReport`)
- `ChatShellService` checks readiness via structured report properties instead of boolean returns

**Impact for Team:**
- Vasquez can evolve abstractions without re-breaking app on each model change
- Hicks and Bishop maintain buildable WinUI shell with safe fallbacks during runtime integration
- When frameworks unify, replace linked source with normal project reference

**Owner:** Bishop  
**Status:** Implemented; clean build, all tests passing  
**Date:** 2026-03-14

---

### 17. Runtime Readiness Seam: Explicit Status Over Silent Fallback

**Decision:** Keep WinUI app aligned with runtime contracts via full source linking, and translate bootstrap readiness reports into shell-visible status text before session/message handoff.

**Rationale:**
- Partial linking of abstraction files let app compile against stale contract assumptions
- Shell needs debuggable middle state: workspace/config readiness can be real before SDK orchestration exists
- Explicit fallback messaging beats silent placeholders; tells user whether blocker is Copilot CLI discovery, Node/npx, EULA, or unimplemented SDK

**Implementation:**
- `WorkIQC.App.csproj` links full abstraction surface including readiness models and typed exceptions
- Local adapters mirror contract shape; bootstrap returns structured reports, unsupported ops throw typed exceptions
- `ChatShellService` resolves startup/send status from bootstrap reports, carries into shell badge/footer on fallback

**Consequence:** App stays debuggable end-to-end while SDK orchestration is incomplete; operators get concrete setup signals.

**Owner:** Vasquez  
**Status:** Implemented; readiness reports flowing through ChatShellService  
**Date:** 2026-03-14

---

### 18. Verification: Clean Solution as Release Gate

**Decision:** Treat clean solution verification as release gate: current tree builds and tests pass after clean run, so build-rich.log failure should not block forward progress.

**Rationale:**
- `build-rich.log` was stale .NET SDK artifact (preview.26126 → preview.26156), not code defect
- Clean verification confirms build seam stability without regressing bootstrap idempotency

**Implementation:**
- Keep bootstrap initialization idempotent and covered by tests (shell calls it during load)
- Config rewrites on every launch would be regression even with placeholder runtime seam
- 21/21 tests passing (20 pre-existing + 1 bootstrap idempotency regression)

**Status:** ✅ Verified; baseline locked  
**Owner:** Newt  
**Date:** 2026-03-14

---

### 19. Cut 3: Polished Chat Loop (Markdown + Visual Identity)

**Decision:** Next cut focuses on visual quality and rendering fidelity (markdown, theming, styling) with no SDK integration; runtime stubs remain.

**Goal:** Make chat loop look and feel like product demo, not prototype.

**Scope:**
- **Markdown Rendering via WebView2** (Hicks + Vasquez): Parse via Markdig, render themed HTML with code blocks, tables, copy buttons
- **Role-Based Message Styling** (Hicks): User messages right-aligned, assistant left-aligned; timestamps, streaming indicator
- **Fluent Design Theming** (Hicks): Replace hardcoded colors with WinUI 3 theme resources; respect system dark/light mode
- **ChatShellService Tests** (Newt): ≥8 new tests covering load/send/fallback scenarios

**What NOT Included:**
- Copilot SDK integration (remains stubs)
- EULA/consent first-run dialog
- Conversation deletion/export UI

**Sequencing Rationale:**
- SDK spike has external dependencies (SDK preview, WorkIQ packaging)
- Markdown + polish are app-side only; unblocked
- Tim gets demoable product sooner; validates send→stream→render pipeline with real markdown

**Risk Notes:**
- WebView2 in WinUI 3: Generally stable, check DPI scaling on multi-monitor
- Markdig: Well-maintained, verify latest version on net10.0
- CSS theming: Keep HTML template simple; avoid JS frameworks in WebView2

**Owner:** Ripley (lead), Hicks (implementation), Vasquez (WebView2), Newt (tests)  
**Status:** Locked; owners assigned  
**Date:** 2026-03-14

---

### 20. Cut 3 Implementation Contracts: Markdown, Styling, and Testing Boundaries

**Decision:** Define execution contracts for Cut 3 work to enable Hicks and Newt to work in parallel without coordination overhead.

**Contracts:**

**1. Markdown Rendering Contract (Hicks)**
- Model layer: `ChatMessageViewModel.Content` holds raw markdown, never HTML

---

### 21. Session-Layer Tool Binding: Concrete MCP Tool IDs vs Server Aliases

**Decision:** Session-facing tool allow-lists must reference concrete MCP tool identifiers, not server aliases. The MCP server name (`workiq`) and callable tool ID (`workiq-ask_work_iq`) are separate layers.

**Rationale:**
- Local `mcp-config.json` defines MCP servers (e.g., `"workiq": {...}`), and those servers export tool IDs (e.g., `workiq-ask_work_iq`)
- When binding a Copilot session, the allowed-tools list must reference the concrete tool ID that the runtime can invoke
- Binding to server name instead of tool ID causes "tool not bound in-session" failures even when server is discoverable
- UI activity labels should normalize prefixed/unprefixed variants (both `workiq` and `workiq-ask_work_iq` display as "WorkIQ")

**Implementation:**
- Bootstrap generates `mcp-config.json` with MCP server entry for `workiq`
- Session binding layer uses `workiq-ask_work_iq` in allowed-tools list
- Runtime tracing at `%LocalAppData%\WorkIQC\logs\workiqc-runtime.log` tracks which tool ID was resolved

**Owner:** Bishop  
**Status:** Implemented; 63/63 tests passing; fixes "ask_work_iq is not bound in-session" user-facing symptom  
**Date:** 2026-03-14
- Interface: `IMarkdownRenderer.RenderToHtml(markdown)` — Markdig with `DisableHtml`, `UsePipeTables` extensions
- View layer: Single `WebView2` for transcript with embedded HTML/CSS/JS template; append via `ExecuteScriptAsync`
- Copy button: Post-process code blocks with `<button>` injection
- Streaming: Re-render full markdown on each chunk; push to WebView2 (acceptable for v1, optimize later if >200ms)
- Files: `IMarkdownRenderer.cs`, `MarkdigRenderer.cs`, `TranscriptTemplate.html`, `MainPage.xaml`, `App.xaml.cs`
- Off-limits: `ChatShellService.cs`, test projects, runtime contracts

**2. Message Styling Contract (Hicks)**
- Role-based CSS: User right-aligned (blue), Assistant left-aligned (dark slate), both 70% width, 12px radius
- Colors: Match existing `ChatMessageViewModel` brushes
- Streaming indicator: Pulsing dot below in-progress message
- Timestamp: Subtle secondary text below bubble, `h:mm tt` format
- Theme support: Current hardcoded colors → CSS class toggle later

**3. ChatShellService Testing Contract (Newt)**
- Boundary: Test `ChatShellService` as unit with real SQLite, mock runtime services (no WinUI)
- Mocks: All three runtime interfaces throw `UnsupportedRuntimeActionException`
- Minimum 8 test cases: LoadShell (empty/populated DB, bootstrap failure), SendAsync (fallback stream, persistence), CreateConversation (draft promotion, title generation)
- Test project: `WorkIQC.App.Tests` with `UseWinUI=false`, real `ServiceCollection` with `AddPersistence()` + mock registrations
- Off-limits: Views, ViewModels, runtime contracts

**Shared Contracts (Stable, No Changes):**
- `IChatShellService.cs` — 3-method interface frozen
- `ChatShellModels.cs` — all DTOs adequate
- Runtime abstractions (`ICopilotBootstrap`, `ISessionCoordinator`, `IMessageOrchestrator`) — frozen until SDK spike

**Parallel Execution Safety:**
- App.xaml.cs: Hicks registers `IMarkdownRenderer`, Newt doesn't touch it (no conflict)
- Solution file: Hicks adds no projects, Newt adds test project (clean merge)
- ViewModels: Hicks modifies how View reads them (WebView2 instead of TextBlock), Newt tests Service layer below (no overlap)

**Sequencing:** Both tracks can start immediately; no dependency between Hicks and Newt work items. Delivers visually polished, tested chat loop ready for SDK integration.

**Owner:** Ripley (design review lead), Hicks (markdown/styling implementation), Newt (testing implementation)  
**Status:** Approved for parallel execution  
**Date:** 2026-03-13

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
- Archive decisions older than 30 days to decisions-archive.md when main file exceeds ~20 KB


---
created_at: 2026-03-14T00:00:00Z
owner: Bishop
---

# Unpackaged Visual Studio F5 for WorkIQC.App

## Decision

Make `WorkIQC.App` default to an unpackaged **Project** launch path in Visual Studio and disable MSIX tooling for **Debug** builds only.

## Why

- Tim asked for the simplest reliable F5 path with no Store/MSIX requirement.
- The app already targets `WindowsPackageType=None`, so the remaining developer friction was Visual Studio surfacing packaged startup first.
- Keeping MSIX tooling available outside Debug preserves a future packaging lane without forcing every local run through it.

## Implementation Notes

- `Properties\launchSettings.json` now exposes only `WorkIQC.App (Unpackaged)` so new developer environments F5 the normal desktop executable by default.
- `WorkIQC.App.csproj` turns off `EnableMsixTooling` in Debug and leaves it available when a configuration explicitly needs packaging.

## Impact

- Visual Studio F5 works without a Store package requirement.
- Release/publish work can still re-enable packaging through non-Debug configurations instead of blocking everyday app startup.


### 2026-03-14T00:28:39Z: User directive
**By:** Tim Heuer (via Copilot)
**What:** The main app must be runnable with F5 from Visual Studio without any Store/MSIX packaging requirement.
**Why:** User request — captured for team memory


### 2026-03-14T00:30:00Z: User directive
**By:** Tim Heuer (via Copilot)
**What:** Prefer a Fluent design aesthetic that makes real use of WinUI 3 materials, aligned to the provided reference image and Fluent materials guidance.
**Why:** User request — captured for team memory


# Decision: Fluent Design Refresh

**Date:** 2026-03-13
**Author:** Hicks (UI Engineer)
**Status:** Implemented

## Summary

Refreshed the main app UI to align with Windows 11 Fluent Design System using real WinUI 3 materials. The prior shell had a black background with minimal styling; the new design uses Mica backdrop, warm-tinted sidebar, rounded cards, and generous whitespace for a calm, premium feel.

## Key Changes

### Materials
- **MainWindow**: Now applies `MicaBackdrop` (with Acrylic fallback on older systems) for the signature Windows 11 appearance
- **Sidebar**: Warm cream/beige tint (`#FEF7EE`) inspired by the reference design—feels soft and inviting
- **Main content**: Transparent background lets Mica show through

### Layout & Hierarchy
- Three-zone layout retained: sidebar (280px) + main content + composer
- Header bar with thread title and connection badge
- Centered empty state with icon + "Let's build" welcome message
- Suggestion cards (rounded, 12px corners) for quick prompts

### Composer
- Pill-shaped input (24px corner radius) with send button inside
- Supports auto-grow for multiline input
- Stays pinned at bottom

### Theme Resources
- New resource dictionary with semantic tokens: `AccentBrush`, `TextPrimaryBrush`, `TextSecondaryBrush`, `CardBackgroundBrush`
- Rounded corner presets: `CardCorners` (12px), `ComposerCorners` (24px), `SmallCorners` (8px)
- Consistent spacing constants for page padding

## Why This Matters

The reference image suggested a light, calm, roomy shell—this is now achievable with Mica as the base layer. Acrylic is reserved for transient surfaces (per Fluent guidance). The warm sidebar provides visual anchoring without competing with content.

## Files Changed

- `MainWindow.xaml` / `.cs` (new) — Mica backdrop host
- `App.xaml` — Theme resources and styles
- `App.xaml.cs` — Uses new MainWindow
- `MainPage.xaml` — Complete redesign
- `MainPage.xaml.cs` — Added suggestion click handler
- `ChatMessageViewModel.cs` — Added `AvatarGlyph` and `FormattedTimestamp`


# Newt Decision: Fluent shell refresh rejected

- **Date:** 2026-03-14
- **Scope:** `src/WorkIQC.App`
- **Decision:** Reject Hicks' Fluent shell refresh artifact for revision.
- **Reviewer lockout:** Hicks is locked out from revising or self-approving this artifact. Assign **Bishop** for the next revision pass.

## Evidence

- Verification passed for baseline shell safety:
  - `dotnet build .\src\WorkIQC.slnx --nologo`
  - `dotnet test .\src\WorkIQC.slnx --nologo`
  - unpackaged `WorkIQC.App.exe` launched successfully, stayed alive for 12 seconds, and preserved the existing local shell artifacts in `%LocalAppData%\WorkIQC\`
- The requested Fluent direction from Tim's reference is **not** realized in the active page shell:
  - `Views/MainPage.xaml` still renders a hard-coded black background with a plain two-column `Grid`, `StackPanel`, stock `ListView`, stock `TextBox`, and stock `Button`
  - there is no centered empty-state composition, no large rounded composer card, no warm split-pane treatment, and no suggestion-card treatment comparable to the reference
  - there is no active use of Fluent shell controls such as `NavigationView`, `CommandBar`, `InfoBadge`, or card/elevation patterns in the page that the user sees
- `MainWindow.xaml.cs` does apply Mica/Acrylic backdrops, but that is only the wrapper window. The hosted `MainPage` still presents the pre-refresh shell, so the material treatment does not reach the user-facing composition strongly enough to satisfy the request.

## Acceptance outcome

- **Build:** Pass
- **Tests:** Pass (21/21)
- **Launch smoke:** Pass
- **Aesthetic acceptance:** **Fail**
- **Core shell behavior:** Preserved at the current baseline (launch, persistence bootstrap, recent-history shell wiring), but the refresh is not acceptable because the requested Fluent visual direction is missing from the active shell content.

## Revision guidance for Bishop

1. Refresh the **actual** `MainPage` composition, not just the window wrapper.
2. Bring the empty state closer to the reference: centered hero copy, larger breathing room, and suggestion surfaces/cards.
3. Replace the flat composer with a rounded, elevated Fluent treatment sized like a primary interaction surface.
4. Introduce sidebar/content contrast using theme resources/material layers instead of hard-coded black and white.
5. Keep existing shell behaviors intact: recent-thread selection, draft/new-chat flow, pinned composer, send/stream states, and launch-time hydration.


# Newt Decision: Unpackaged Visual Studio F5 accepted

- **Date:** 2026-03-14
- **Scope:** `src/WorkIQC.App`
- **Decision:** Accept Bishop's unpackaged F5 path for the main app.

## Evidence

- `WorkIQC.App.csproj` sets `WindowsPackageType` to `None`.
- Debug builds disable MSIX tooling.
- `Properties/launchSettings.json` exposes the `WorkIQC.App (Unpackaged)` project launch profile for Visual Studio F5.
- Clean verification passed:
  - `dotnet build .\src\WorkIQC.slnx --nologo`
  - `dotnet test .\src\WorkIQC.slnx --nologo`
  - unpackaged `WorkIQC.App.exe` launched successfully, stayed alive, showed a WinUI window, and created `%LocalAppData%\WorkIQC\workiq.db` plus `%LocalAppData%\WorkIQC\.copilot\mcp-config.json`

## Remaining blocker / caveat

- This proves there is **no Store/MSIX package requirement** for the Visual Studio F5 path.
- It does **not** prove zero machine prerequisites on a clean box: the app is still framework-dependent on the Windows App Runtime being present. If the team wants truly zero-prereq developer launch, that is a separate self-contained/runtime-bootstrap decision.

---

## Final Implementation Batch — 2026-03-14

### Track A: Markdown Rendering (Hicks)

**Decision:** Implement Markdig + WebView2 pipeline for markdown rendering.

**Status:** Complete

Raw markdown preserved in view-models and persistence. WebView2 owns presentation with themed HTML shell and CSS-driven light/dark support. Streaming renders full markdown on each chunk append.

**Key:** IMarkdownRenderer → MarkdigRenderer in App.xaml.cs DI; TranscriptTemplate.html embedded resource.

---

### Track B: Theme Cleanup (Bishop)  

**Decision:** Remove hardcoded brush constants from ChatMessageViewModel; use App.xaml theme resources and CSS.

---

## WorkIQ EULA Consent Flow (2026-03-14)

### 1. Live WorkIQ EULA Validation — Vasquez

**Decision:** Treat WorkIQ EULA consent as valid only when app has evidence that the live `workiq-accept_eula` MCP tool completed successfully.

**Rationale:**
- Prior local marker-only flow could report "ready" even when real MCP runtime had never accepted EULA
- Hides actual first-run requirement and makes setup misleading
- Real consent requires handshake with live WorkIQ acceptance flow, not just local file marker

**Implementation:**
- Settings accept step now drives live Copilot/WorkIQ session scoped to `workiq-accept_eula`
- Persists verified marker payload from that run
- Rejects legacy/local-only markers as insufficient proof

**Impact:**
- Normal chat sessions keep WorkIQ consent recoverable (tool in allow-list)
- Runtime diagnostics trace live consent path clearly
- Setup flow no longer misleads operator about first-run requirement

**Owner:** Vasquez  
**Status:** Implemented & Tested (21/21 Runtime tests passing)

---

### 2. Settings UX Copy — Live WorkIQ Consent — Bishop

**Decision:** Settings consent copy must describe action as live WorkIQ consent step, not app-only toggle.

**Rationale:**
- Legacy/local-only `eula-accepted.json` markers are insufficient
- Accepted state must come from live `workiq-accept_eula` MCP flow and be recorded locally as evidence
- UX must clearly communicate handshake with real runtime

**Implementation:**
- Updated button text to "Accept with WorkIQ"
- Updated copy to describe marker as verified live WorkIQ consent
- Windows shell workflow: review terms → accept with WorkIQ → rely on recorded marker

**Owner:** Bishop  
**Status:** Implemented & Tested (38/38 App tests passing)

---

### 3. Live Runtime vs Principal Resolution — Vasquez (Analysis)

**Decision:** Treat current evidence as proof of app-owned MCP bootstrap + live Copilot SDK dispatch, but not as proof WorkIQ tool was callable in-session. Do not chase app-side principal/auth payload changes yet.

**Evidence:**
- App startup and send-path diagnostics point to `%LocalAppData%\WorkIQC\.copilot\mcp-config.json`
- Bootstrap succeeded: workspace init, EULA marker exists, auth-handoff marker exists, shell marked runtime ready
- App created Copilot SDK client, created/resumed sessions, dispatched prompts, received completed messages
- SessionConfiguration carries only workspace path, MCP config path, allowed tools, streaming, system guidance — no separate principal/auth payload on send path
- Bootstrap auth reporting shows local auth handoff marker does NOT verify live WorkIQ session can resolve signed-in principal
- Persisted assistant replies show WorkIQ tool access unavailable in-session; runtime log shows no tool start/complete entries

**Implication:**
- Current failure is earlier than identity resolution
- Either WorkIQ tool not surfaced to model/session at runtime, or model not seeing it as callable
- Missing app-side principal payload not supported by evidence
- WorkIQ-side principal resolution limitation not yet proven (tool does not appear to run)

**Next Debug Step:**
- Instrument and inspect first live session for actual bound tool visibility/execution
- Log SDK/CLI-reported callable tools for created session
- Capture tool-start/tool-complete/tool-failed events before investigating principal resolution
- If tool starts then fails on identity → pivot to WorkIQ principal resolution
- If no tool exposed → stay in binding/runtime layer

**Owner:** Vasquez  
**Status:** Analysis complete; action pending live session instrumentation

**Status:** Complete

Deleted 9 static SolidColorBrush fields and brush-typed properties. Replaced XAML references with {ThemeResource} markup extensions. ChatMessageViewModel now pure MVVM with zero WinUI brush imports.

**Key:** Fluent shell moves from MainWindow wrapper to MainPage composition with tinted left rail, rounded surfaces, centered empty state.

---

### Track C: ChatShellService Tests (Newt)

**Decision:** Add ≥8 service-layer tests covering load, send, fallback scenarios.

**Status:** Complete

New WorkIQC.App.Tests project (net10.0, UseWinUI=false) with 11 test cases: launch/bootstrap, history load, draft create, session reuse/create, markdown send, send fallback, empty stream, runtime recovery. Real SQLite in-memory, no WinUI runtime required.

**Key:** Theme regression sentries parse App.xaml Light/Dark dictionaries. Markdown round-trip tested.

---

### Track D: Runtime Session Bridge (Vasquez)

**Decision:** Replace bootstrap-only stubs with real Copilot SDK session/stream/tool-activity bridge.

**Status:** In progress (happy path blocked on Copilot CLI auth + preview SDK stability)

Shared bridge implementation for both WorkIQC.Runtime and WinUI app. Session creation, resume, message dispatch, assistant streaming, tool event observation. Fallback path functional.

**Key:** GitHub.Copilot.SDK 0.1.33-preview.1 now explicit dependency. Session resume persisted. Windows MCP bootstrap writes WorkIQ launch config.

---

### Copilot Directive — 2026-03-14T01:56:45Z

**Directive:** Proceed always — continue through remaining work items proactively without waiting between slices.

**Status:** Captured for team memory.

---

### 21. Final Integrated Review: Live Readiness Rejection

**Date:** 2026-03-14  
**Author:** Newt (Verification Lead)  
**Status:** Rejected for revision

## Decision

Reject the integrated batch for one final live-readiness revision by Bishop.

## What Passed

- ✅ Build clean: `dotnet build .\src\WorkIQC.slnx --nologo`
- ✅ Tests passing: 35/35 (20 persistence/runtime + 11 ChatShellService + 4 system)
- ✅ Unpackaged launch: App creates `%LocalAppData%\WorkIQC\workiq.db` and `.copilot\mcp-config.json`
- ✅ Product features complete:
  - Fluent shell composition (Mica backdrop, tinted sidebar, rounded surfaces, centered empty state)
  - Markdig + WebView2 markdown rendering (code blocks, tables, links, copy buttons, syntax highlighting)
  - Theme-aware resources (Light/Dark CSS, system theme sync)
  - Local SQLite history persistence (Conversation/Message/Session tables)
  - Session resume plumbing (SDK session ID stored, restore fallback functional)
  - Assistant streaming bridge (token-by-token rendering, in-progress indicator)
  - Tool-activity surfacing (WorkIQ tool events visible in transcript)

## What Failed Acceptance

1. **App-owned WorkIQ version pinning:** Bootstrap currently writes floating `@microsoft/workiq` instead of pinned tested version. This violates Decision #4 (Copilot SDK Integration — app-owned workspace with pinned WorkIQ version) and keeps live runtime behavior nondeterministic.

2. **First-run consent/auth handoff:** App surfaces EULA/auth blockers as footer text only. No real first-run consent flow or authentication path that proves operator can get from "bootstrap ready" to a live authenticated WorkIQ turn. This is a product gap, not just an environment issue.

3. **Environment prerequisite gap:** This machine confirms the honest environment limitation: `copilot`, `node`, `npm`, and `npx` all resolve, but `%LocalAppData%\WorkIQC\.workiq\eula-accepted.json` is missing, so authenticated WorkIQ execution remains unverified.

## Key Insight

Final acceptance on a preview-backed desktop chat app must separate:
- **Product-complete surfaces** (UI, persistence, rendering, local orchestration) — ALL PRESENT
- **Environment/runtime prerequisites** (Copilot CLI, Node.js, WorkIQ auth) — PARTIALLY VERIFIED

A live-readiness claim is still rejectable even when build, tests, and smoke pass if:
- Shipped bootstrap writes a floating preview package instead of the pinned version the team agreed to support
- First-run operator experience does not provide clear consent/auth path
- Live runtime success remains unverified on the actual machine

## Revision Guidance for Bishop

1. **Pin WorkIQ version in MCP config:** Replace `@microsoft/workiq` with explicit tested version (e.g., `@microsoft/workiq@1.0.0-preview.123`).
2. **Implement first-run EULA/auth flow:** Replace footer-only messaging with real dialog that:
   - Explains WorkIQ dependency and EULA scope
   - Provides explicit accept/decline choice
   - Authenticates user before first message send (if required)
   - Stores acceptance in `%LocalAppData%\WorkIQC\.workiq\eula-accepted.json`
3. **Surface bootstrap readiness transparently:** Shell should report concrete blockers (e.g., "Copilot CLI not found at %Path%", "WorkIQ not installed", "EULA not accepted") instead of silent fallback.

## Impact

- Product shell is **feature-complete and demoable** ✅
- Live deployment **blocked** on bootstrap/consent work ⚠️
- All existing shell wiring **preserved** (recent-thread selection, draft/new-chat flow, pinned composer, send/stream states, launch-time hydration)

**Owner:** Bishop  
**Reviewer lockout:** Vasquez (locked out from revising this artifact)



## WorkIQ Desktop Client v1 — Live-Readiness Finalization (2026-03-14)

### Bishop — Live Readiness Revision

# Bishop — Live readiness revision

## Decision

Pin the app-owned WorkIQ MCP bootstrap to `@microsoft/workiq@1.0.0-preview.123` and treat first-run readiness as an app-owned handoff with two persisted markers:
1. `%LocalAppData%\WorkIQC\.workiq\eula-accepted.json`
2. `%LocalAppData%\WorkIQC\.workiq\auth-handoff.json`

## Why

- The reviewer rejected floating MCP package resolution.
- Footer-only messaging was too easy to miss and did not guide an operator from setup into a first live turn.
- We still need to keep environment prerequisites explicit instead of pretending runtime success is guaranteed.

## Implemented shape

- Runtime bootstrap now defaults to the pinned WorkIQ package version and writes deterministic MCP config in the app-owned workspace.
- The shell exposes a first-run setup card plus launch-time dialog that calls out blockers, app-owned paths, and prerequisite status.
- Accepting WorkIQ terms writes the EULA marker in-app.
- Launching Copilot sign-in opens `copilot login` in a terminal window and records the handoff marker so the setup path is explicit and repeatable.
- Existing shell, markdown, theme, persistence, and runtime bridge behavior remain unchanged outside the readiness path.

## Validation

- `dotnet build src\WorkIQC.slnx --nologo`
- `dotnet test src\WorkIQC.slnx --no-build --nologo`


---

### Newt — Live Readiness Acceptance

# Newt — Live readiness acceptance

## Decision

Accepted Bishop's live-readiness revision. No reviewer lockout is needed and no new revision owner is required.

## Product readiness

- `dotnet build .\src\WorkIQC.slnx --nologo` passed.
- `dotnet test .\src\WorkIQC.slnx --nologo --verbosity minimal` passed: 39/39.
- Unpackaged smoke launch stayed up long enough to write `%LocalAppData%\WorkIQC\.copilot\mcp-config.json`.
- The written MCP config now pins `@microsoft/workiq@1.0.0-preview.123`.
- The app now exposes a first-run setup card plus a launch-time dialog, with app-owned EULA acceptance and authentication handoff actions and a readiness recheck path.
- Service/runtime tests cover missing-marker state plus post-acceptance/post-handoff refresh behavior.

## Environment and operator prerequisites

- This machine resolves `copilot`, `node`, `npm`, and `npx`.
- This machine does **not** yet have the app-owned EULA/auth markers, which is expected until an operator completes the first-run setup.
- A real live WorkIQ turn still depends on the operator accepting terms, completing `copilot login`, and having the required Windows runtime/tooling available on the target machine.

## Acceptance framing

The remaining product work items are complete enough to call done. What remains is environment setup and operator completion of the explicit first-run handoff, not unfinished product implementation.



---

## Batch: 2026-03-14 Runtime/Auth Implementation

### Vasquez: Runtime Fix — Final Assistant Message as Source of Truth
- **Decision:** Treat final assistant message from Copilot SDK as transcript source of truth for WorkIQ-backed turns. Raw assistant deltas stay buffered as fallback only.
- **Why:** SDK can surface planning/tool chatter in deltas before actual WorkIQ answer arrives; letting deltas stream straight into chat bubble makes product feel like it's answering from internal runtime steps instead of WorkIQ.
- **Implementation:** CopilotRuntimeBridge buffers deltas per turn, emits completed assistant content on turn completion, keeps tool activity on separate activity stream. ChatShellService persistence framed as history/session resume only.
- **Readiness Impact:** Auth-ready UI text now says "Completed" instead of "Started"; auth prerequisite feedback includes recorded command/timestamp path where available.
- **Status:** Implemented, 51/51 tests passing.

### Vasquez: Chat Flow & Answer Source Audit
- **Finding:** Local SQLite in WorkIQC is persistence only for chat history, timestamps, and stored Copilot session IDs; not source of truth for answering prompts.
- **Context:** User-visible "checking local WorkIQ data" wording is coming from live runtime streaming, not persistence layer.
- **Current Path:** Raw assistant deltas render directly in transcript; if SDK exposes planning/tool-call content as deltas, shell displays verbatim.
- **Solution:** Tool activity is separate path—runtime ToolEvents become UI status text via ChatShellService.StreamRuntimeActivityAsync() and MainPageViewModel.
- **Status:** Analyzed, implemented in runtime fix.

### Vasquez: Runtime Flow Audit — Local SQLite Integration
- **Decision:** Treat "local SQLite first" transcript as runtime integration bug, not intended product flow.
- **Why:** App send path persists user turn, routes prompt through Copilot session → SessionCoordinator/MessageOrchestrator → CopilotRuntimeBridge. SQLite used for history + resume only. Live config under .copilot/mcp-config.json is WorkIQ-only, so intended answer path is Copilot + WorkIQ.
- **Problem Identified:** Tool/schema choreography leaking into assistant-visible text—bad runtime/UI contract.
- **Verdict:** For org/people questions, flow should be: resume/create session → let Copilot use WorkIQ → return answer directly → keep plumbing invisible except optional activity text.
- **Next Fix:** Harden session/tool contract; stop passing raw deltas with tool-call syntax to transcript; add regression test.
- **Status:** Audit complete, fixes applied.

### Newt: Acceptance Criteria — WorkIQ Runtime Response & Readiness Feedback
- **Scope:** MCP server response integrity, readiness feedback clarity, bootstrap diagnostics.
- **Core Requirements:**
  1. No internal runtime/planning chatter in visible assistant reply (only user-facing markdown)
  2. Clear readiness/auth completion messaging in UI signals (badges, status text, setup state)
  3. Sufficient diagnostics to debug bootstrap, auth, and session startup failures
- **Response Integrity:** When SendAsync() succeeds, assistant message contains only markdown from MCP server; no internal state leaked.
- **Readiness Messaging:** Setup state reflects auth/EULA/bootstrap readiness; connection badge text changes (Sample data → Bootstrap ready → Runtime setup → WorkIQ runtime); sidebar footer explains blockers vs. ready.
- **Diagnostic Sufficiency:** Bootstrap failures surface actionable error messages; auth failures expose login command and marker path; session creation includes resolution text.
- **Test Coverage:** 23 ChatShellServiceTests validate contracts; 3 gaps identified (explicit chatter validation, Node.js diagnostic, first-run transition).
- **Status:** Acceptance bar locked, 51/51 tests passing, ready for SDK spike.

### Newt: Readiness & Logging Review (Auth State Semantics)
- **Decision:** Treat auth-state confusion as both copy problem and diagnostics gap.
- **Evidence:** AuthenticationHandoffReport.Status becomes "Completed" when auth-handoff.json exists. ChatShellService flattens to IsAuthenticationHandoffStarted. MainPageViewModel.AuthStepStatusText renders as "Started"—UI never shows completed auth even after recheck. External proof exists (copilot.log shows "Access token received successfully") but app doesn't surface it.
- **Problem:** UI makes completed handoff look perpetually in-progress.
- **Implication:** App cannot prove whether external auth finished; only proves handoff was recorded locally.
- **Follow-up:** Separate auth states into required, handoff recorded, verified. Add durable troubleshooting evidence (last handoff time, last verification attempt, verification result, artifact/path).
- **Status:** Identified, fixes applied in runtime implementation.

### Bishop: Bubble Sizing Revision
- **Context:** Fluent shell showed transcript bubbles wrapping like narrow fixed columns in running WinUI app, even after prior responsive width-cap pass.
- **Decision:** Drive transcript bubble widths from MainPage during ListView container realization and resize instead of relying on DataTemplate-scoped binding alone.
- **Why:** Markdown body hosted in WebView2; earlier binding path did not reliably affect live template instances. App-shell width pass keeps bubbles aligned, lets longer turns open wider, preserves markdown/rendering stack.
- **Implementation:** Name bubble border in item template; refresh widths on ContainerContentChanging, SizeChanged, message content changes; use conservative heuristic favoring wider assistant/markdown turns.
- **Outcome:** Rejected by Newt (visible bubbles still narrow); lockout applied, revision passed to Vasquez.

### Hicks: Bubble Sizing Decision
- **Context:** Transcript bubbles collapsing to narrow columns despite ample room in chat pane. Markdown body in WebView2, relying on single fixed MaxWidth produced uncomfortable wrapping.
- **Decision:** Clamp message bubbles to live transcript width with responsive min/max range, not just hard-coded max.
- **Why:** Keeps long markdown readable; lets bubbles widen when pane has space; preserves Fluent shell structure and markdown rendering control.
- **Implementation:** Use width-clamp converter so XAML derives bubble bounds from MessagesList.ActualWidth without pushing math into view-model.
- **Outcome:** Superseded by Bishop revision; both rejected by Newt. Vasquez's revision accepted.

### Vasquez: Bubble Sizing Revision (Accepted)
- **Changes:** Revised bubble sizing in MainPage.xaml.cs to use wider live clamp (~46% min / ~88% max of transcript width) so user and assistant bubbles reach comfortable reading width before wrapping.
- **Implementation:** Applied realized bubble content width directly to MarkdownMessageView; mirrored width into embedded WebView2. Keeps markdown-backed transcript aligned with live bubble width.
- **Preserved:** Settings navigation, thread deletion, markdown rendering, persistence, transcript refresh.
- **Validation:** Rebuilt, reran test suite, launched unpackaged app; confirmed visible shell shows meaningfully wider chat bubbles.
- **Status:** Accepted, 51/51 tests passing.

### Newt: Bishop Bubble Sizing Review
- **Decision:** Reject Bishop's bubble-sizing revision.
- **Visible Evidence:** Running shell screenshot shows bubbles still narrow on wide pane; user turn wraps after few words; assistant turn same despite ample space.
- **Code Evidence:** MainPage.xaml.cs applies responsive widths from RefreshMessageBubbleWidths() and ApplyBubbleWidth(), but live shell result doesn't show wider bounds in user-comfortable way. Acceptance bar is visible behavior, not heuristic.
- **Regression:** Full build + tests passed (47/47).
- **Lockout:** Bishop locked from further revisions; Vasquez assigned.
- **Acceptance Bar for Vasquez:** Live shell must show substantially wider restored/newly sent turns before wrapping while preserving alignment, markdown rendering, transcript restore.

### Newt: Bubble Sizing Review
- **Decision:** Reject current bubble-sizing pass.
- **Why:** Running shell screenshot shows visibly narrow bubbles not expanding to comfortable width. Both user and assistant bubbles wrap after few words, reading like fixed-width column not responsive chat surface.
- **Code Evidence:** MainPage.xaml still renders items as left/right Border with only MaxWidth="760"; markdown hosted through MarkdownMessageView/WebView2. Combination hasn't produced visibly growing bubble.
- **Regression Check:** dotnet test WorkIQC.App.Tests.csproj passed (15/15), service/transcript safety intact.
- **Build Note:** Full solution build blocked by running WorkIQC.App holding Persistence.dll; not bubble-specific.
- **Acceptance Bar:** Long messages should expand substantially wider before wrapping while preserving alignment, markdown, restore behavior.

### Hicks: Settings Surface & Thread Deletion
- **Decision:** Promote Settings from passive footer affordance to dedicated main-pane surface that reopens without disturbing selected conversation.
- **Implementation:** Keep selected thread in memory when settings open so "back to chat" feels immediate; conversation continuity intact. Route thread deletion through IChatShellService rather than direct persistence binding; sidebar can remove threads while preserving history/session seam.

### Newt: Settings & Thread Deletion Review (Accepted)
- **Decision:** Accepted.
- **Evidence:** Settings now opens dedicated surface, returns via in-surface back action. Conversation selection exits settings cleanly, restores chosen transcript. Thread deletion exposed from sidebar, confirms intent, deletes persisted conversation/session state, reselects next thread or clears when none remain.
- **Automated Coverage:** src/WorkIQC.App.Tests added review gates for settings/delete affordances plus MainPageViewModelTests covering settings return, reselection, deletion, empty-state.
- **Validation:** dotnet build .\src\WorkIQC.slnx --nologo and dotnet test (47/47) passed.
- **Status:** Accepted.

### Ripley: Readiness UX — Auth Handoff Feedback Gap
- **Problem:** After user completes copilot login and returns, AuthStepStatusText shows "Started" (confusing—sounds incomplete). Actual state: auth genuinely completed; marker file proves handoff recorded. Missing signal: no UI feedback indicating auth done.
- **Root Cause:** IsAuthenticationHandoffStarted tracks *whether marker exists*, not *whether auth complete*. Once marker written, flag true forever—UI never communicates auth ready for next phase.
- **Why It's Misleading:** Disconnect between internal state (marker exists = handoff succeeded) and UI label ("Started" = in-progress). Description then says "Finish sign-in if terminal waiting," contradicting app's own assertion that handoff recorded.
- **Product Intent:** After copilot login completion, app should recognize marker exists, UI confirm "Completed" not "Started", description acknowledge success, guide to next step. If auth AND EULA both done, setup card should disappear.
- **Fix Priority 1:** Change AuthStepStatusText from "Started" to "Completed" when IsAuthenticationHandoffStarted = true. Label correctly reflects marker state; "Completed" matches user intent.
- **Fix Priority 2:** When handoff recorded, description acknowledges success ("Copilot authentication handoff completed. Recheck readiness…") not "Finish sign-in if waiting…"
- **Fix Priority 3:** Hide setup card when auth + EULA both done, show only actual blockers (missing dependencies).
- **Design Principle:** State names should reflect user intent, not implementation details.
- **Status:** Recommendation (implemented in Vasquez's runtime fix).


---

### 2026-03-14T05:39:13Z: User Directive — Shell Chrome Refresh

**Issued by:** Tim Heuer (via Copilot)

**Request:**
1. Remove the title-bar status UI (does not read as Fluent; status pills compete with window chrome)
2. Relocate app status somewhere less visually intrusive
3. Trim sidebar Settings surface from verbose text down to Settings link only

**Why:** Visual polish — keep caption clean, move transient status to less prominent location, keep sidebar intentional.

**Status:** Captured for team memory; driving Bishop's shell chrome revision (2026-03-14T05:44:00Z)

---

### 2026-03-14: Runtime Live Readiness — Approved

**Decision:** Approve runtime bridge implementation for live traffic.

**Owner:** Newt (reviewer), Vasquez (implementer)

**Verification Evidence:**

1. **Transcript Purity:** \ChatShellService\ separates content deltas from tool activity. \MainPageViewModel\ consumes \ActivityStream\ into Sidebar Footer; no runtime chatter leaks into persistent chat transcript.

2. **Auth & EULA:** \CopilotBootstrap\ correctly checks for app-owned marker files (\ula-accepted.json\, \uth-handoff.json\). \ChatShellService\ blocks runtime attempts until present, returning \SetupState\ driving "Action Required" card.

3. **Diagnostics:** \Trace.WriteLine\ for internal logging. User-facing diagnostics (timeouts, missing dependencies) surfaced in Sidebar Footer via \ComposeStatusText\; chat bubble stays clean.

4. **Tests:** \ChatShellServiceTests\ (11 tests) cover empty history, runtime unavailable, session resumption, auth/EULA states, error propagation.

**Verdict:** APPROVE. Runtime bridge ready for live traffic with safe fallbacks for "not configured" state.

---

### 2026-03-14: Shell Chrome Review — Rejected (Reassigned)

**Decision:** Reject Hicks' shell chrome implementation; reassign to Bishop for independent revision.

**Reviewer:** Newt  
**Original Author:** Hicks  
**Reassigned To:** Bishop

**Blockers:**

1. **Title Bar Status Pills Not Removed:** XAML (\MainPage.xaml\ lines 39–51) still includes \Border Style="{StaticResource StatusPill}"\. User directive requires complete removal.

2. **Sidebar Still Verbose:** Contains static descriptive text ("A calm desktop workspace…") instead of compact Settings link only.

3. **Not Fluent Pattern:** Uses manual \Grid\ layout instead of standard \NavigationView\ for sidebar rail.

**Why Rejected:** Implementation claims to address requirements but XAML inspection reveals title-bar pills still present and sidebar text still verbose. Fluent pattern not realized in user-facing shell.

**What to Preserve:** Recent-thread selection, draft/new-chat flow, pinned composer, send/stream states, launch-time hydration, build/test baseline (39/39).

**Status:** Reassigned to Bishop; Hicks locked out. See orchestration log 2026-03-14T05-39-13Z-bishop.md for revision scope.

