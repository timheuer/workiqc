# Squad Decisions — WorkIQ Desktop

## Decision 1: WinUI 3 Framework (2026-03-13)

**Verdict:** WinUI 3 (Windows App SDK) chosen for v1 Windows desktop app.

**Rationale:**
- Modern, fast, native rendering via DirectX
- Direct Copilot SDK integration
- Memory footprint: ~100–300 MB baseline (vs Electron 400 MB–1 GB+)
- Shipping via MSIX + Windows App SDK with automatic updates

**Rejected alternatives:**
1. Electron + React — memory bloat unacceptable for chat-heavy UI
2. .NET MAUI — overkill for v1 if Windows-only; desktop support immature
3. WPF — legacy stack, no modern Fluent Design

**Storage:** SQLite + Entity Framework Core in `ApplicationData.Current.LocalFolder`

**Packaging:** MSIX + Windows App Installer

**Key Windows risks for markdown UIs:**
1. DPI scaling on high-res displays
2. Code block syntax highlighting vs Windows theme
3. Large message buffers leak memory without proper virtualization
4. Complex markdown tables overflow chat pane
5. Keyboard accessibility must be tested

## Decision 2: Persistence Schema (2026-03-13)

**Verdict:** SQLite with EF Core migrations, app-owned database in LocalAppData.

**Tables:**
- Conversation (id, title, createdAt, updatedAt)
- Message (id, conversationId, role, content, createdAt)
- Session (id, sdkSessionId, conversationId, resumedAt)

**Path:** `%LocalAppData%\WorkIQC\workiq.db`

**Pattern:** Development uses `EnsureCreatedAsync()` for convenience. Production uses `dotnet ef migrations add InitialCreate` before shipping.

**DI injection:** `IConversationService` provides clean API for chat history operations across app layers.

## Decision 3: Markdown Rendering (2026-03-13)

**Verdict:** Markdig + WebView2 for chat transcript rendering.

**Pattern:**
- Markdig parses markdown to HTML AST
- WebView2 hosts compiled HTML
- CSS theming via App.xaml theme resources

**Responsibility boundary:**
- ViewModel: raw markdown content
- View: rendering via `IMarkdownRenderer`
- CSS: light/dark theme colors from shared resources

## Decision 4: WorkIQ Version Pinning (2026-03-14)

**Verdict:** App-owned pinned version in MCP config, not floating `@latest`.

**Pattern:**
- Bootstrap writes `@microsoft/workiq@1.0.0-preview.123` (or tested version)
- Versioning decision owned by app, not deferred to SDK
- First-run setup confirms version before allowing chat

**Rationale:** Ensures reproducible behavior, allows rollback strategy, complies with app-owned dependency management.

## Decision 5: First-Run EULA/Auth Flow (2026-03-14)

**Verdict:** Two-marker persistence for consent + authentication handoff.

**Files:**
- `eula-accepted.json` — user accepted terms
- `auth-handoff.json` — Copilot auth marker from `copilot login`

**Path:** `%LocalAppData%\WorkIQC\.workiq\`

**UI pattern:**
- Launch-time check for both markers
- Setup card visible until both complete
- Dialog offers explicit accept/decline action
- Terminal handoff to `copilot login` for sign-in

**Readiness reporting:** Blockers reported transparently (missing auth, missing EULA, missing CLI, etc.)

## Decision 6: App-Layer Service Seam (2026-03-14)

**Verdict:** `IChatShellService` bridges persistence + runtime + UI.

**Responsibilities:**
- Persistence hydration at launch
- Sample fallback when runtime unavailable
- Runtime-facing send orchestration
- Bootstrap readiness reporting

**Pattern:** Decouples view/viewmodel from persistence details; isolates runtime seams.

## Decision 7: Runtime Readiness Contracts (2026-03-14)

**Verdict:** Structured readiness reports with typed exceptions for unsupported operations.

**Contracts:**
- `IBootstrapReport` — detailed readiness status per component
- `UnsupportedOperationException` — for unavailable runtime features
- `CanProceed` — boolean gate for chat readiness

**Reporting:** Shell decodes readiness into visible status text (e.g., "Copilot CLI not found").

## Decision 8: Test Baseline Gate (2026-03-14)

**Verdict:** 39/39 tests locked as release gate.

**Coverage:**
- Persistence: CRUD operations, schema migrations
- Runtime: bootstrap idempotency, readiness seams
- Shell: recent-thread selection, draft/new-chat flow, launch-time hydration
- Readiness: component status reporting

**Acceptance criterion:** No regression below 39/39. Build must pass `dotnet build .\src\WorkIQC.slnx --nologo`.

## Decision 9: Cut 3 — Polished Chat Loop (2026-03-13)

**Verdict:** Markdown rendering, role-based styling, Fluent theming.

**Rationale:** SDK spike has external blockers. Visual polish is fully app-side. Tim gets demoable product before AI backend lands.

**Owners:**
- Hicks: WebView2 + Fluent theming
- Vasquez: markdown pipeline integration
- Newt: ChatShellService tests (≥8 test cases)

**Acceptance:**
- Markdown with bold/code/lists/tables renders correctly
- Role-based styling distinguishes user/assistant
- Theme respects system light/dark
- Streaming indicator visible

## Decision 10: Theme Resource Cleanup (2026-03-14)

**Verdict:** All shell colors sourced from App.xaml theme dictionaries, not hardcoded in ViewModels.

**Pattern:**
- Deleted 9 static SolidColorBrush fields from ChatMessageViewModel
- Removed per-ViewModel Brush properties
- Replaced with {ThemeResource} markup extensions
- Light/dark theme switch now pure resource-driven

**Files:**
- App.xaml — centralized theme resources
- MainPage.xaml — {ThemeResource} references
- ChatMessageViewModel — zero WinUI brush imports

## Decision 11: Live-Readiness Revision (2026-03-14)

**Verdict:** APPROVED. Bootstrap pins WorkIQ version. First-run provides real EULA/auth flow. Readiness blockers reported transparently.

**Deliverables:**
- Pinned WorkIQ version in MCP config to @microsoft/workiq@1.0.0-preview.123
- First-run EULA/auth flow with two-marker persistence
- Shell surfaces setup card + launch-time dialog
- Bootstrap readiness reports concrete blockers
- Build/tests/smoke green (39/39)

**Status:** Product complete and live-ready per Newt final review.

## Decision 12: Shell Chrome Revision (2026-03-14)

**Verdict:** ASSIGNED TO BISHOP

**Requirements:**
1. Remove title-bar status UI completely — no status pills in XAML
2. Relocate app status to less prominent location (consider composer metadata row)
3. Trim sidebar to Settings link only — no verbose explanatory text

**Fluent Pattern Guidance:**
- Consider standard NavigationView for sidebar rail
- Use soft tints and rounded surfaces instead of flat Grid/StackPanel
- Keep Mica on MainWindow; apply color/material treatment to MainPage content

**Preservation:**
- Recent-thread selection logic
- Draft/new-chat flow
- Pinned composer position
- Send/stream state handling
- Launch-time hydration and history bootstrap

**Acceptance Criteria:**
- Build succeeds (`dotnet build .\src\WorkIQC.slnx --nologo`)
- Tests pass (39/39 baseline minimum)
- Unpackaged launch succeeds
- Title-bar has no status pills
- Sidebar shows Settings link only
- User-facing shell realizes Fluent aesthetic
- No regression in shell wiring

## Decision 13: Shell Chrome Implementation — Bishop Revision (2026-03-14T05:44:00Z)

**Verdict:** REPORTED COMPLETE by Bishop. Build/test/smoke pass.

**Deliverables:**
- Removed title-bar status treatment entirely; caption drag region carries only app identity
- Moved runtime/app status into main conversation header so readiness remains visible without fighting window chrome
- Kept left rail intentionally compact: brand, new conversation, recent threads, simple Settings affordance only
- Softened rail and surface resources plus tighter corner radii for Fluent window chrome appearance

**Status:** Pending Newt review.

## Decision 14: Shell Chrome Review — Newt Verdict (2026-03-14T05:48:00Z)

**Verdict:** REJECTED

**Blocking evidence:**
- Rendered shell artifact shows title-bar `Bootstrap ready` pill still present
- Left rail shows large verbose settings card instead of Settings link only
- Code/test evidence acceptable but live artifact contradicts promised cleanup

**Acceptance criterion failure:**
For UX review, rendered shell wins over code inspection. Visual evidence of running app must match source promise.

**Recommendation:**
Revision reassigned to Ripley for different reviewer-implementer pair to reconcile visual artifact with current markup and prove final launch path.

## Decision 15: Shell UI Reconciliation (2026-03-14T05:49:00Z)

**Assigned to:** Ripley

**Task:** Reconcile live shell UI with XAML source.

**Context:**
Bishop's shell chrome revision rejected by Newt on rendered artifact evidence. Title-bar still shows `Bootstrap ready` pill. Left rail still displays large verbose settings card.

**Outcome determination:**
1. Verify actual XAML changes in MainPage.xaml
2. Confirm Bishop's changes correct but artifact stale (rebuild/relaunch)
3. OR identify missing XAML changes and apply
4. Verify Fluent aesthetic realized in running shell

**Acceptance criteria:**
- Rendered shell has no title-bar status pills
- Sidebar shows Settings link only
- Fluent aesthetic visible in live launch
- Build succeeds (39/39+ tests pass)
- No regression in shell wiring

**Status:** In progress after second reviewer-mandated reassignment.

## Decision 16: Composer Enter Behavior (2026-03-14)

**Verdict:** APPROVED. Pinned chat composer sends on plain **Enter**. **Shift+Enter** for newline.

**Rationale:**
- Matches user's requested chat behavior and ChatGPT convention
- Lowers friction on main happy path: type, press Enter, send
- Shift+Enter preserves multiline editing without slower Ctrl+Enter default

**Implementation:**
- Keyboard gating lives in small shared helper for testability
- No dependency on WinUI page instantiation in unit tests
- Reduces compose->send latency for user intent clarity

**Files affected:**
- `src/WorkIQC.App/Views/MainPage.xaml*`
- `src/WorkIQC.App/ViewModels/MainPageViewModel.cs`
- `src/WorkIQC.App.Tests`

**Status:** Implementation complete and locked.

## Decision 17: WorkIQ MCP Launch Config (2026-03-14T06:00:37Z)

**Context:**
User directive (Tim Heuer via Copilot): MCP config should use direct `npx` command with args `-y`, `@microsoft/workiq`, `mcp` without `cmd /d /s /c` wrapper.

**Proposed by:** Vasquez

**Config shape:**
```json
{
  "command": "npx",
  "args": ["-y", "@microsoft/workiq", "mcp"]
}
```

**Rationale:**
- Tim explicitly requested literal `npx` form for inspection clarity
- Generated config matches intended runtime contract exactly
- Failures easier to inspect without cmd wrapper

**Status:** REJECTED by Newt (2026-03-14T06:05:00Z)

**Blocking issue:**
- Version pinning discarded; raw `npx` risks Windows execution inconsistency under preview CLI
- No fallback handling if @latest diverges from tested version
- Violates app-owned dependency stability requirement

**Reassignment:**
Moved to Bishop for independent revision that reconciles user clarity (direct form) with runtime stability (version tracking in args or bootstrap).

## Decision 18: Shell Chrome Review — Second Rejection (2026-03-14T05:48:00Z)

**Event:** Newt review rejects Bishop's implementation on rendered artifact evidence.

**Evidence:**
- Live shell screenshot shows title-bar `Bootstrap ready` pill still present
- Left rail displays large verbose settings card
- Code/test assertions pass but rendered shell contradicts promised cleanup

**Verdict:** REASSIGNED TO RIPLEY

**Reasoning:**
For UX acceptance, visual artifact of running app supersedes source code inspection. Reviewer-implementer pair must be different to break stale assumptions.

**Reconciliation required:**
1. Verify XAML changes actually in MainPage.xaml
2. Rebuild/relaunch to confirm artifact not stale
3. If needed, apply missing XAML to realize visual promise
4. Prove Fluent aesthetic in live launch

**Status:** Ripley reconciliation in progress.

## Decision 19: Hicks — Enter-to-Send PreviewKeyDown (2026-03-14)

**Verdict:** APPROVED. Enter-to-send must use PreviewKeyDown, not KeyDown.

**Rationale:**
- WinUI's multiline TextBox treats Enter as text input first in regular KeyDown
- By KeyDown, newline already in buffer — too late to suppress
- PreviewKeyDown runs before text commitment, allowing send trigger + newline suppression

**Implementation:**
- `MainPage.xaml.cs.OnComposerPreviewKeyDown()` — new handler wired to PreviewKeyDown event
- `ComposerInputBehavior.ShouldSendOnKeyDown()` returns true for Enter without Shift
- `TextBox.HandledEventsToo` pattern preserves Shift+Enter newline behavior

**Testing:**
- 37/37 focused suite passing (ComposerInputBehaviorTests + MainPageViewModelTests)
- Regression test: `OnComposerPreviewKeyDown_WhenEnterWithoutShift_SendsAndMarksHandled()`

**Future guidance:**
Multiline text boxes wiring send behavior should use preview/input-preprocessing stage, not regular keydown. Shift+Enter contract preserved throughout.

## Decision 20: Newt — Runtime Flow Acceptance Criteria (2026-03-14)

**Verdict:** FINALIZED. Three user concerns map to verifiable signals + logging gaps.

### Concern 1: Prompts must go through Copilot SDK only

**Evidence signals required (all four must be present):**
1. Connection badge: `"WorkIQ runtime"` (not setup/placeholder/bootstrap text)
2. Session identity: `SessionCoordinator.CreateSessionAsync()` or `ResumeSessionAsync()` was called
3. Orchestrator invocation: `IMessageOrchestrator.SendMessageAsync()` called with live session ID
4. Response origin: Response contains actual WorkIQ markdown (org data, meetings, people), not placeholder templates

**Current status:** Tests locked (ChatShellServiceTests). Sidebar footer proves live path. Visual evidence ✓

**Missing:** Explicit log confirmations of session coordinator and orchestrator calls.

## Decision 21: Vasquez — Explicit MCP Binding for Consent Sessions (2026-03-14)

**Verdict:** Copilot SDK sessions must bind MCP servers explicitly from app-managed `.copilot\mcp-config.json`.

**Rationale:**
- Consent flow created the correct allow-list but SDK session lacked concrete MCP server definition, making `workiq-accept_eula` unavailable at runtime.
- Workspace discovery alone is insufficient; explicit binding maps app-managed config into SDK session MCP server config.
- Per-session allowed tool filter applied at both session and server levels.

**Rule:** Session creation treats app-written MCP config as source input, maps it into SDK session's MCP server config, and applies per-session allowed tool filter.

**Impact:** Future MCP integrations must separate "workspace has config" from "session is bound to concrete tool IDs." Consent/recovery flows can use narrower allow-lists without weakening verified-marker requirement.

**Status:** Fixed; runtime tests verify binding chain (23/23 Runtime, 38/38 App tests passing).

## Decision 22: Bishop — EULA Session Tool Binding Alignment (2026-03-14)

**Verdict:** Align Settings EULA consent session with normal chat session tool allow-list (workiq tools only).

**Rationale:**
- Generated MCP workspace/config path is shared between both consent and normal chat flows.
- Consent flow was the only place creating a session with a different (narrower) tool set.
- Keeping same concrete WorkIQ tool exposure in both flows removes avoidable session-binding difference.
- Prompt directives remain explicit: consent session invokes `workiq-accept_eula` via prompting.

**Finding:** No separate Windows packaging, workspace bootstrap path, or generated `mcp-config.json` divergence found. Risk sat in session setup consistency, not shell storage/layout.

**Status:** Fixed; full build + suite passing (68/68).

### Concern 2: Local storage is metadata only

**Allowed in SQLite:**
- Conversation ID, title, timestamp
- User messages (verbatim)
- Assistant messages (verbatim SDK transcript)
- Copilot session ID (resume hint)
- Metadata: created_at, updated_at, deleted_at

**Forbidden:**
- Local reasoning, fallback decisions, placeholder content
- Internal state (path taken, SDK unavailable reason)
- App-layer chatter/planning

**Current status:** Passing. `ChatShellService.SendAsync()` stores only user + transcript. Fallback clearly labeled in sidebar.

**Required:** No changes; contract already met.

### Concern 3: Diagnostics must be pasteable

**Existing WriteDiagnostic calls:** 14+ signals (bootstrap, history, send plan, runtime, auth, EULA)

**Missing signals (IMPLEMENT):**
```csharp
WriteDiagnostic("runtime.session.created", $"New session '{sessionId}' created with config tool-posture='{config["tool-posture"]}'; allowed-tools='{config["allowed-tools"]}'.");
WriteDiagnostic("runtime.session.resumed", $"Resumed persisted session '{sessionId}'.");
WriteDiagnostic("runtime.orchestrator.send", $"Sent to orchestrator: conversation='{convId}'; session='{sessionId}'; message-id='{msgId}'.");
WriteDiagnostic("runtime.orchestrator.stream", "First response delta received from orchestrator; streaming now.");
```

**Log format:** ISO 8601 timestamp + stage.noun + readable message with artifact IDs. Good for grep/tail.

### Missing Test Coverage

**Gap:** No `MainPageViewModel.SendAsync()` end-to-end test proving:
1. Initialize view model
2. Set `ComposerText = "prompt"`
3. Call `SendAsync()`
4. Assert: message in transcript + composer cleared + shell service called

**Recommended test:** `SendAsync_WithComposerText_ClearsTextAndAddsUserMessageToTranscript()`

**Why:** Catches refactoring breaks in ViewModel.SendAsync orchestration layer before shipping.

**Status:** Acceptance criteria finalized. No breaking changes required; logging visibility + test gaps identified.

## Decision 21: Vasquez — Live WorkIQ Routing Guardrail (2026-03-14)

**Verdict:** APPROVED. Workplace/org prompts route through live SDK or block explicitly.

**Rationale:**
- Users read placeholder/fallback copy as evidence of local org-data checking
- Breaks product contract: workplace answers must come from live SDK or fail transparently
- Local storage acceptable only for transcript/session-resume, not answer source

**Implementation:**
- `ChatShellService` classifies workplace/org prompts
- Routes to blocking-response path if runtime unavailable
- Blocking text explicitly states: no answer from local history or placeholder
- `MainPageViewModel` shows blocking-status string during stream

**Testing:**
- Workplace prompts return live answer when runtime ready ✓
- Workplace prompts return blocking error when runtime unavailable ✓
- Test assertions locked in AppTests

**Session guidance hardened:**
- `tool-posture: WorkIQ-first`
- `allowed-tools: workiq`
- Model told app name is not knowledge source; local data not valid answer source

**Local persistence:** Limited to thread messages + Copilot session IDs + setup markers only.

**Startup logging:** `%LocalAppData%\WorkIQC\logs\workiqc-runtime.log`

**Status:** Implementation complete and tested. Waiting for Newt's missing log signal implementation (Decision 20).

## Decision 22: Bishop — MCP Launch Config (2026-03-14)

**Verdict:** APPROVED. Use pinned WorkIQ package reference with Windows-safe cmd wrapper.

**Windows launch:**
```json
{
  "command": "cmd.exe",
  "args": ["/d", "/s", "/c", "npx.cmd -y @microsoft/workiq@<pinned-version> mcp"]
}
```

**Non-Windows launch:**
```json
{
  "command": "npx",
  "args": ["-y", "@microsoft/workiq@<pinned-version>", "mcp"]
}
```

**Rationale:**
- Raw `npx` is batch wrapper on Windows; not reliable direct child-process target
- Pinned version kept in args to preserve app-owned dependency tracking
- `cmd /d /s /c` form is boring but Windows-safe
- Visible launch intent stays close to requested shape: `npx -y ... mcp`

**Why over bare npx:**
- Deterministic Windows child-process launch
- Version pin prevents fallback to @latest divergence
- Runtime diagnostics now describe actual command + pin confirmation
- Generated config stays reproducible on Windows

**Impact:**
- Local app bootstrap mirrors shipped Windows behavior → runtime tests aligned
- Config deterministic; no silent version substitution
- All platforms (Windows, macOS, Linux) have explicit safe launch form

**Status:** APPROVED. Implementation complete.

## Decision 23: Bishop — Principal Handoff Finding (2026-03-14T15:38:18Z)

**Verdict:** APPROVED. Current app-side auth handoff is metadata-only; no principal payload forwarded.

**Evidence:**
- `auth-handoff.json` stores only: `launchedAt`, `loginCommand`, `workspacePath`
- `eula-accepted.json` stores consent metadata only
- MCP config carries no `env` block or user/principal payload
- `SessionConfiguration` carries workspace path, MCP config, version, guidance, streaming, tools only
- `ChatShellService` sends only conversation ID, prompt, session ID to runtime
- App proves session creation but cannot prove authenticated principal identity

**Implication:**
App can confirm handoff initiation only. Authenticated user identity is resolved by Copilot/WorkIQ downstream, not by app-side code.

**Follow-up requirement:**
If WorkIQ should map first-person requests to authenticated user, that contract must be explicit and product-approved, resolved by Copilot/M365 auth layer.

## Decision 24: Vasquez — WorkIQ Principal Flow (2026-03-14T15:38:18Z)

**Verdict:** APPROVED. Principal identity for first-person requests comes from authenticated Copilot/WorkIQ session.

**Why:**
- Copilot SDK session configuration exposes tool allow-listing and system-message shaping, but no first-class principal/auth payload channel
- WorkIQ expected to resolve `me`/`my` from delegated Microsoft 365 identity behind authenticated session
- Observed failure was not "missing app-supplied principal" but vague guidance allowing model to ask for name/email before invoking WorkIQ

**Implementation:**
- Principal resolution owned by Copilot/WorkIQ auth
- Strengthen runtime system guidance for first-person workplace requests to map to signed-in user by default
- Make UI/bootstrap wording explicit: `auth-handoff.json` is local evidence only, not proof of user resolution

**Status:** APPROVED. Runtime guidance hardened. All tests passing (63/63).
