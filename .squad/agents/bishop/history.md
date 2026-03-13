# Project Context

- **Owner:** Tim Heuer
- **Project:** Windows WorkIQ desktop chat client
- **Stack:** Windows app shell, Copilot SDK, WorkIQ MCP server, markdown-rich chat UI, local session history persistence
- **Created:** 2026-03-13T17:59:26.524Z

## Core Context

**Framework Decision:** WinUI 3 (Windows App SDK) for v1 — modern, fast, native rendering, direct Copilot SDK integration (100–300 MB footprint). SQLite + EF Core for persistence in ApplicationData.LocalFolder. MSIX + Windows App Installer for shipping. Markdig + WebView2 for markdown. In-process SDK with WorkIQ-only default tools.

**Architecture Established:** 
- Scope completed: WinUI 3 project under src/WorkIQC.App/, persistence layer with EF Core SQLite, full DI wiring in App.xaml.cs, XAML/ViewModel scaffolding ready, runtime abstraction contracts linked into app layer, local adapter implementations proving shell buildability pre-SDK.
- Three-zone UI layout (sidebar + chat pane + composer) locked. Fluent Design materials (Mica backdrop, Acrylic transient surfaces, rounded cards, warm tints) applied to both MainWindow and MainPage compositions.
- Chat history persisted to SQLite (Conversation/Message/Session tables). Bootstrap/readiness reporting structured for operator visibility. Fallback paths functional when runtime unsupported.
- Build verified clean (0 errors, 0 warnings). Tests baseline at 21/21 passing (persistence, runtime seams, bootstrap idempotency).

**Key Learnings:**
- For visible Fluent refresh, apply materials to hosted Page, not just MainWindow wrapper. Keep Mica on window, soft tints + rounded surfaces on page, Acrylic for transient UI only.
- If app extends content into title bar, provide page-defined drag region (TitleBarDragRegion) and let window bind after injection — preserves native drag behavior while keeping layout flexible.
- DPI scaling, code block syntax theming, message buffer virtualization, markdown table width constraints, and keyboard accessibility all critical for Windows markdown UI.
- Framework-dependent apps on Windows still need App Runtime prerequisite outside Store package path — separate from MSIX requirement notification.
- Fluent aesthetic acceptance requires both window and page inspection: Mica backdrop alone is not evidence if page still uses flat Grid/StackPanel + hard-coded colors.

## Team Updates (2026-03-13T18:13:10Z)

**Architecture finalized:** WinUI 3 confirmed. Copilot SDK integration spike needed (CLI discovery, config behavior). SQLite with app-owned schema (conversations + messages tables). Markdig + WebView2 markdown pipeline locked. MCP bootstrap with pinned WorkIQ version, EULA flow, app-owned workspace at %LocalAppData%\WorkIQC\.

## Team Updates (2026-03-14T09:59:19Z)

**Consent pre-bootstrap refactor complete:**
- Vasquez refactor: Moved WorkIQ EULA acceptance out of chat-session orchestration into native pre-session bootstrap path. Updated runtime/app coverage. Validated full solution (70/70 tests passing).
- Bishop UX alignment: Reframed consent messaging in Settings, first-run dialog, readiness summary, and consent/auth descriptions. Validated app tests (39/39 passing).
- Decision: Chat-session `workiq-accept_eula` proved unreliable. Bootstrap path now canonical. Local markers untrusted without verified live/bootstrap evidence payloads.
- For WinUI 3 inner-loop work, keep `WindowsPackageType=None`, disable MSIX tooling in Debug, and make the Visual Studio launch profile use `Project` so F5 behaves like a normal unpackaged desktop app.
- For a visibly Fluent WinUI refresh, move the treatment onto the hosted `Page`, not just `MainWindow`: keep Mica on the window backdrop, use a soft tinted rail and rounded content surfaces on the page, and reserve Acrylic for transient UI instead of persistent layout chrome.
- If the app extends content into the title bar, give the page a dedicated drag region (for example `TitleBarDragRegion`) and let the window bind to it after injecting the page so launch behavior stays native while the shell layout remains flexible.

### Framework Analysis (2026-03-13)

**Recommendation: WinUI 3 for v1**

- **Best choice:** WinUI 3 (Windows App SDK) — modern, fast, native markdown rendering, direct Copilot SDK integration.
  - Memory footprint: ~100–300 MB baseline (vs Electron's 400 MB–1 GB+).
  - Performance: Native DirectX rendering beats DOM-based markdown (Electron).
  - Shipping: MSIX + Windows App SDK handles updates automatically.

- **Rejected alternatives:**
  1. **Electron + React:** Memory bloat unacceptable for chat-heavy UI; feels like web app in a container.
  2. **.NET MAUI:** Overkill for v1 if Windows-only; desktop support immature.
  3. **WPF:** Legacy stack; no modern Windows 11 Fluent Design; no investment from Microsoft.

- **Storage:** SQLite + Entity Framework Core in `ApplicationData.Current.LocalFolder` (respects MSIX sandboxing).

- **Packaging:** MSIX + Windows App Installer. Never write to Program Files; use LocalFolder always.

- **Key Windows risks for markdown UIs:**
  1. DPI scaling on high-res displays.
  2. Code block syntax highlighting vs Windows theme (light/dark).
  3. Large message buffers leak memory without proper virtualization (ItemsRepeater + VirtualizingStackPanel).
  4. Complex markdown tables overflow chat pane; need width constraints.
  5. Keyboard accessibility must be tested (Tab, screen readers).

- **Next: Validate Copilot SDK embeds cleanly in WinUI 3; prototype markdown rendering library + session persistence.**

## Team Updates (2026-03-13T18:13:10Z)

**Squad decision merged to `.squad/decisions.md`:** WinUI 3 confirmed. SQLite app-owned schema + SDK session ID for resume. Markdig + WebView2 for markdown. In-process Copilot SDK with WorkIQ-only default tool list. MSIX packaging. Manual-first UX testing, automated early for persistence.

**Next:** Spike Copilot CLI discovery/config, WorkIQ bootstrap, markdown rendering, persistence fallback. Await Tim's clarification on unresolved questions.

## Team Updates (2026-03-14T14:40:56Z)

**Vasquez audit-copilot-workiq-flow:** Removed local answer synthesis from send flow. Clarified shell naming. Added deterministic runtime tracing to `%LocalAppData%\WorkIQC\logs\workiqc-runtime.log`. Build + tests passing.

**Bishop inspect-workiq-tool-binding:** Fixed session-layer binding bug. MCP server alias (`workiq`) ≠ callable tool ID (`workiq-ask_work_iq`). Session allowed-tools list must use concrete tool identifier. Tests 63/63 passing. Root cause identified: not app name, but session binding layer.

## Team Updates (2026-03-14T16:19:58Z)

**Vasquez completed audit-workiq-eula-flow:** Fixed real bug where local EULA marker was treated as success without requiring live WorkIQ consent. Consent flow now uses real WorkIQ acceptance handshake (`workiq-accept_eula`), rejects legacy marker-only state, and keeps tool available in runtime allow-list for recovery. Settings accept step drives live Copilot/WorkIQ session, persists verified marker payload. Runtime diagnostics trace live consent path clearly. `WorkIQC.Runtime.Tests` 21/21 passing.

**Bishop completed settings-consent-audit:** Confirmed current source routes Settings through live consent flow. Updated Settings UX copy/button to explicitly say "Accept with WorkIQ" and describe marker as verified live WorkIQ consent. Windows shell copy clarified: review terms, accept with WorkIQ, rely on recorded verified marker. `WorkIQC.App.Tests` 38/38 passing. Build clean after clearing stale process.

## Team Updates (2026-03-14T16:45:06Z)

**Bishop — Consent Config Inspection Complete:** Inspected consent session configuration; found no separate Windows/bootstrap path bug. Identified narrower tool exposure in consent session vs normal chat as root cause. Aligned consent-session tool exposure with normal WorkIQ chat exposure (workiq tools only) while keeping EULA prompting explicit via session directives. **Tests:** 68/68 build + suite passing. **Finding:** No mcp-config divergence between flows; risk was session binding consistency. **Decision merged:** Keep same tool allow-list in both consent and normal chat flows; prompt directives remain explicit for consent EULA invocation.

**Vasquez analysis: Live runtime vs principal resolution.** Current failure earlier than identity resolution — WorkIQ tool not surfacing to model/session at runtime, or model not seeing it as callable. Missing app-side principal payload not supported by evidence. Bootstrap auth marker does NOT verify live WorkIQ session can resolve signed-in principal. Next: Instrument first live session for actual bound tool visibility/execution, log callable tools for created session, capture tool-start/tool-complete/tool-failed events.

### App Shell Scaffold (2026-03-13)

**Delivered:**
- WinUI 3 project structure under `src/WorkIQC.App/`
- Persistence layer under `src/WorkIQC.Persistence/` with EF Core SQLite
- Working solution builds successfully (`WorkIQC.slnx`)

**Key patterns established:**
- `WorkIQDbContext` with Conversation/Message/Session tables matching team schema
- `IConversationService` provides clean API for chat history operations
- `StorageHelper` centralizes paths: `%LocalAppData%\WorkIQC\workiq.db` for database, `%LocalAppData%\WorkIQC\.copilot\` for MCP config
- DI wired in `App.xaml.cs` — database auto-initialized on first launch
- `Directory.Build.props` sets common MSBuild properties (net10.0-windows, win-x64/arm64, nullable enabled)

**File paths for other agents:**
- Database context: `src\WorkIQC.Persistence\WorkIQDbContext.cs`
- Service interface: `src\WorkIQC.Persistence\Services\IConversationService.cs`
- Storage helper: `src\WorkIQC.Persistence\StorageHelper.cs`
- App startup: `src\WorkIQC.App\App.xaml.cs`
- Solution: `src\WorkIQC.slnx`

**Build command:** `dotnet build src\WorkIQC.slnx`

**Architecture decision:** EF Core with migrations-ready setup. For production, run `dotnet ef migrations add InitialCreate --project WorkIQC.Persistence` before shipping. Development uses `EnsureCreatedAsync()` for convenience.

**Storage pattern:** `%LocalAppData%\WorkIQC\` for dev convenience. In MSIX package, replace with `Windows.Storage.ApplicationData.Current.LocalFolder.Path` for true container isolation.

**Ready for next steps:**
- UI team (Hicks) can inject `IConversationService` into ViewModels
- SDK orchestration (Vasquez) can use `StorageHelper.GetCopilotConfigPath()` for MCP setup
- Test team (Newt) can mock `IConversationService` for unit tests

**Unblocked work:** UI wiring, Copilot SDK integration, markdown rendering spike

## Follow-Up: Shell + Runtime Integration (2026-03-13T18:37:38Z)

**Deliverables completed:**
- ✅ App shell buildable with full DI/startup wiring
- ✅ Ready for Vasquez's `ICopilotBootstrap` injection
- ✅ Persistence layer tested and migration-ready
- ✅ Coordinating with Hicks on MainWindow XAML structure

**Coordination notes:**
- Vasquez has SDK contract layer ready; awaiting bootstrap spike before injection
- Hicks to drive UI layout design; Bishop will collaborate on XAML/ViewModel wiring
- DI container in App.xaml.cs ready to accept `ICopilotBootstrap`, `ISessionCoordinator`, `IMessageOrchestrator` once spikes complete
- MSIX manifest to be added once packaging requirements clarified by Tim

**Blocked on:** Vasquez SDK spike results, Hicks UI design, Tim's product decisions

### App Composition Hardening (2026-03-13)

- Moved the WinUI shell root onto DI so `App.xaml.cs` resolves `MainPage` and its `MainPageViewModel` instead of letting the page self-compose.
- Added an app-layer `IChatShellService` seam that owns persistence hydration, sample fallback, and runtime-facing send orchestration so the page no longer reaches into `IConversationService`.
- Linked the runtime abstraction contracts into the app project and registered local adapter implementations that keep the shell buildable on the app's current target framework while preserving the same interface shape Vasquez will wire to the real runtime later.

## Team Updates (2026-03-13T19:08:03Z)

**Bishop:** Linked full `WorkIQC.Runtime.Abstractions` tree into `WorkIQC.App`; resolved XAML compiler drift. Local adapters now implement structured readiness reports. Build clean, all tests passing.

**Ripley:** Triaged `build-rich.log` as stale SDK artifact (not code defect); assessed state-of-play (cuts 1–2 complete); locked Cut 3 scope as "Polished Chat Loop" (markdown + visual identity, no SDK changes). Owners assigned: Hicks, Vasquez, Newt.

**Vasquez:** Formalized readiness contracts; bootstrap returns structured reports, unsupported ops throw typed exceptions. Shell decodes readiness into visible status text (e.g., "Copilot CLI not found").

**Newt:** Verified clean baseline (21/21 tests passing); confirmed bootstrap idempotency regression coverage. Baseline locked as release gate.

**Decision Impact:** 4 inbox decisions merged to `decisions.md`: Build Repair, Runtime Readiness Seam, Verification Gate, Cut 3 Polished Chat Loop. Orchestration logs written for each agent. Session log summarizes recovery and next cut.

**Next Steps:** Hicks/Vasquez begin Cut 3 markdown + theming work. SDK spike continues in parallel. Tim's clarifications on data retention/consent flow unblock enterprise requirements.

## Team Updates (2026-03-14T01:00:17Z)

**Fluent Shell Refresh Revision Assigned**

**Incoming from Newt:** Hicks' Fluent refresh was rejected. Newt confirmed baseline (build, tests, smoke launch) is solid, but the user-facing MainPage does not realize the requested Fluent aesthetic. Only the MainWindow wrapper received material treatment.

**What to implement:**
1. Refresh actual MainPage composition (not just MainWindow)
2. Centered empty state with larger breathing room per reference
3. Rounded, elevated Fluent composer card (replace flat TextBox)
4. Sidebar/content contrast via theme resources (not hard-coded colors)
5. Suggestion cards with proper spacing

**What to preserve:**
- Recent-thread selection logic
- Draft/new-chat flow  
- Pinned composer position
- Send/stream state handling
- Launch-time hydration and history bootstrap

**Acceptance criteria:**
- Build succeeds
- Tests pass (21/21 baseline minimum)
- Unpackaged launch succeeds
- User-facing shell matches Fluent reference aesthetic
- No regression in existing shell wiring

**Status:** READY TO BEGIN — Bishop assigned for fluent shell revision pass.

## Team Updates (2026-03-14T00:42:05Z)

**Bishop:** Linked full `WorkIQC.Runtime.Abstractions` tree into `WorkIQC.App`; resolved XAML compiler drift. Local adapters now implement structured readiness reports. Build clean, all tests passing.

**Ripley:** Triaged `build-rich.log` as stale SDK artifact (not code defect); assessed state-of-play (cuts 1–2 complete); locked Cut 3 scope as "Polished Chat Loop" (markdown + visual identity, no SDK changes). Owners assigned: Hicks, Vasquez, Newt.

**Vasquez:** Formalized readiness contracts; bootstrap returns structured reports, unsupported ops throw typed exceptions. Shell decodes readiness into visible status text (e.g., "Copilot CLI not found").

**Newt:** Verified clean baseline (21/21 tests passing); confirmed bootstrap idempotency regression coverage. Baseline locked as release gate.

**Decision Impact:** 4 inbox decisions merged to `decisions.md`: Build Repair, Runtime Readiness Seam, Verification Gate, Cut 3 Polished Chat Loop. Orchestration logs written for each agent. Session log summarizes recovery and next cut.

**Next Steps:** Hicks/Vasquez begin Cut 3 markdown + theming work. SDK spike continues in parallel. Tim's clarifications on data retention/consent flow unblock enterprise requirements.

### Theme cleanup follow-through (2026-03-14)

- Moved remaining sidebar/transcript/send-button color choices into `App.xaml` theme dictionaries so light and dark mode now resolve from one shared resource set instead of per-view-model hardcoded brushes.
- Kept the existing chat shell + markdown WebView2 flow intact, but added a lightweight `RefreshTheme()` pass on `MainPage` theme changes so view-model-exposed brushes actually repaint when the user flips app theme at runtime.
- Also removed markdown fallback hex defaults in `MarkdownMessageView`; transcript HTML and fallback text now both source their palette from the same theme resources as the shell.

## Track B: Theme Resource Cleanup (2026-03-14T02:18:35Z)

**Status:** Complete

**Deliverables:**
- Deleted 9 static SolidColorBrush fields in ChatMessageViewModel (UserBubble, UserBorder, AssistantBubble, AssistantBorder, PrimaryText, SecondaryText, UserBadge, UserBadgeText, AssistantBadge)
- Removed all Brush-typed properties (BubbleBrush, BorderBrush, RoleBadgeBrush, RoleBadgeForeground, HeaderBrush, BodyBrush)
- Replaced XAML references with {ThemeResource} markup extensions pointing to App.xaml brushes
- Verified ChatMessageViewModel has zero WinUI brush/color imports
- Confirmed light/dark theme switch pure resource/CSS driven

**Fluent Shell Revision (2026-03-14):**
- Keep window on Mica for base layer
- Move Fluent expression onto MainPage.xaml with:
  - Soft tinted left rail instead of flat hard-coded sidebar
  - Rounded primary surfaces for header, transcript, composer
  - Centered, roomy empty state
  - Message/thread cards readable in live page
- Added themed shell brushes in App.xaml
- Hooked MainWindow.SetContent(...) to page-defined TitleBarDragRegion for native drag behavior

**Validation:**
- Build clean; all tests passing (21/21)
- Theme switching works without code changes (pure resource/CSS)
- Fluent shell revision accepted by Newt review
- All message styling controlled by CSS or XAML theme resources

**Key:** Preserved all existing shell wiring (recent-thread selection, draft/new-chat flow, pinned composer, send/stream states, launch-time hydration).

**Result:** Fluent aesthetic now realizes in user-facing MainPage, not just window wrapper.

## Team Update (2026-03-14T02:22:40Z)

**Incoming from Newt:** Final integrated review complete. Build passes, tests pass (35/35), unpackaged launch succeeds, product shell is feature-complete and demoable.

**Live-readiness verdict:** REJECTED — not ready for deployment

**Blockers assigned to Bishop:**
1. **App-owned WorkIQ version pinning** — Bootstrap writes floating `@microsoft/workiq` instead of pinned tested version (violates Decision #4)
2. **First-run EULA/auth flow** — App surfaces blockers as footer text only; no real consent/authentication handoff
3. **Transparent prerequisite reporting** — Replace silent fallback with actionable error messages

**What to preserve:**
- All existing shell wiring (recent-thread selection, draft/new-chat flow, pinned composer, send/stream states, launch-time hydration)
- Build success, test pass rate (35/35)
- Unpackaged launch behavior

**Acceptance criteria for revision:**
- Build succeeds (`dotnet build .\src\WorkIQC.slnx --nologo`)
- Tests pass (35/35 minimum)
- Unpackaged launch succeeds
- Bootstrap pins WorkIQ version in MCP config
- First-run provides real EULA accept/decline + auth flow
- Readiness blockers reported transparently in UI

**Status:** ASSIGNED TO BISHOP



- Live readiness on Windows needs two separate truths in the UI: app-owned setup state (pinned MCP package, consent marker, auth handoff marker) and machine prerequisites (Copilot CLI, Node.js, npx). Users should see both at once.
- A first-run desktop handoff can stay boring and reliable by opening the real external sign-in path (`copilot login`) while the app persists its own consent/auth markers locally and keeps fallback chat behavior intact.

## Team Updates (2026-03-14T02:37:09Z)

**Live-readiness revision accepted by Newt — product complete.**

- Pinned WorkIQ version in MCP config to @microsoft/workiq@1.0.0-preview.123 (Decision #4 compliance).
- Implemented first-run EULA/auth flow with two-marker persistence: ula-accepted.json + uth-handoff.json in %LocalAppData%\WorkIQC\.workiq\.
- Shell surfaces setup card + launch-time dialog with explicit accept/decline action and copilot login terminal handoff.
- Bootstrap readiness now reports concrete blockers and supports recheck path for operator recovery.
- Build/tests/smoke green (39/39). Unpackaged launch creates MCP config with pinned version. First-run markers created on corresponding actions.
- Shell, markdown, theme, persistence, runtime bridge unchanged outside readiness path.
- All existing wiring preserved (recent-thread selection, draft/new-chat flow, pinned composer, send/stream states, launch-time hydration).
- No revision lockout needed; no change of revision owner. Product live-ready.

**Decisions merged:** ishop-live-readiness-revision.md → decisions.md

## Learnings

- WebView2-backed transcript bubbles in a WinUI `ListView` can ignore responsive sizing if the width clamp lives only in a `DataTemplate` `ElementName` binding. For live bubble resizing, set the bubble width from the page on container realization and transcript resize so the running shell honors the actual pane width.
- For WinUI desktop shells that extend into the title bar, keep the caption area nearly silent: app/runtime state reads better in the in-pane conversation header, while the left rail should stay compact enough that Settings is just an affordance instead of a descriptive card.
- For Windows-owned MCP launch config, keep the pinned npm package reference in the generated args and wrap `npx` through `cmd.exe /d /s /c npx.cmd ...` instead of emitting raw `npx`; that preserves the user-visible shape closely enough while surviving normal Windows process launch rules.
- For Copilot SDK session gating, the MCP server name (`workiq`) is not a valid per-session tool binding. The session must allow the concrete MCP tool identifiers exposed by the runtime (for WorkIQ, `workiq-ask_work_iq`), or the assistant can claim the tool is "not bound in-session" even when the MCP server is installed and discovered.
- For WorkIQ desktop bootstrap, the app-owned markers are only readiness breadcrumbs. `auth-handoff.json` proves the app launched `copilot login`, not who authenticated, and the generated `mcp-config.json` plus `SessionConfiguration` currently forward no user principal or email into the runtime.

## Team Updates (2026-03-14T05:44:00Z)

**Shell Chrome Revision Assigned**

**Incoming from Newt:** Hicks' shell chrome cleanup was rejected. XAML inspection revealed title-bar status pills still present, sidebar still verbose, no Fluent NavigationView pattern.

**Reassignment:** Shell chrome revision assigned to Bishop for independent implementation.

**Requirements per User Directive (2026-03-14T05:39:13Z):**
1. Remove title-bar status UI completely — status pills should not appear in XAML
2. Relocate app status to less prominent location (consider composer metadata row)
3. Trim sidebar to Settings link only — no verbose explanatory text, no footer descriptions

**Fluent Pattern Guidance:**
- Consider standard NavigationView for sidebar rail
- Use soft tints and rounded surfaces instead of flat Grid/StackPanel
- Keep Mica on MainWindow; apply color/material treatment to MainPage content

**What to Preserve:**
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

**Status:** READY TO BEGIN

## Team Updates (2026-03-14T05:44:00Z)

**Shell Chrome Revision Assigned**

**Incoming from Newt:** Hicks' shell chrome cleanup was rejected. XAML inspection revealed title-bar status pills still present, sidebar still verbose, no Fluent NavigationView pattern.

**Reassignment:** Shell chrome revision assigned to Bishop for independent implementation.

**Requirements per User Directive (2026-03-14T05:39:13Z):**
1. Remove title-bar status UI completely — status pills should not appear in XAML
2. Relocate app status to less prominent location (consider composer metadata row)
3. Trim sidebar to Settings link only — no verbose explanatory text, no footer descriptions

**Fluent Pattern Guidance:**
- Consider standard NavigationView for sidebar rail
- Use soft tints and rounded surfaces instead of flat Grid/StackPanel
- Keep Mica on MainWindow; apply color/material treatment to MainPage content

**What to Preserve:**
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

**Status:** READY TO BEGIN

## Team Updates (2026-03-14T05:48:00Z)

**Shell Chrome Revision — Implementation Complete**

**Deliverables reported:**
- Removed title-bar status treatment entirely; the caption drag region now carries only app identity.
- Moved runtime/app status into the main conversation header so readiness remains visible without fighting window chrome.
- Kept the left rail intentionally compact: brand, new conversation, recent threads, and a simple Settings affordance only.
- Softened rail and surface resources plus slightly tighter corner radii so the shell reads closer to Fluent window chrome instead of a hand-built card stack.

**Build/Test Status:** Pass. Smoke launch succeeds.

**Next:** Pending Newt review of rendered shell artifact.

## Team Updates (2026-03-14T05:48:00Z)

**Shell Chrome Review — REJECTED**

**Verdict from Newt:** Rendered shell artifact still shows:
- `Bootstrap ready` pill in title-bar area
- Large verbose settings card in left rail

Code/test evidence acceptable but live artifact contradicts promised cleanup.

**Blocker:** For UX review, rendered shell wins over code inspection. Visual evidence of running app must match source promise.

## Team Updates (2026-03-14T06:00:37Z)

**MCP Launch Config Revision — REASSIGNED (from Vasquez rejection)**

Vasquez's proposed direct `npx` launch config rejected by Newt for version stability reasons. **REASSIGNED TO BISHOP** for independent revision.

**Context:**
- User directive (Tim via Copilot): Use direct `npx` command `{"command": "npx", "args": ["-y", "@microsoft/workiq", "mcp"]}`
- Rejection reason: Version pinning discarded; raw `npx` risks Windows execution inconsistency under preview CLI
- Task: Reconcile user clarity (literal form) with runtime stability (version tracking in args or bootstrap)

**Blockers from Vasquez attempt:**
- Version pinning was discarded in initial proposal
- No fallback handling if @latest diverges from tested version
- Violates app-owned dependency stability requirement

**Next Steps:**
- Implement version-aware launch config (pinned package reference or version check in bootstrap)
- Satisfy both user inspection clarity and team runtime contracts
- Route to Newt for review on config form and runtime stability evidence
- Reference existing bootstrap contracts and app-owned workspace patterns

**Previous revision locks:**
- Revision reassigned to Ripley for different reviewer-implementer pair
- Ripley will reconcile visual artifact with XAML source and prove final launch path
- Bishop locked out of shell chrome artifact


## Team Updates (2026-03-14T15:38:18Z)

**Principal Handoff Inspection — COMPLETE**

**Task:** Inspect auth handoff and principal identity flow for first-person WorkIQ requests.

**Findings:**
- App-side auth handoff markers (eula-accepted.json, auth-handoff.json) are metadata-only
- No user/principal payload forwarded by current MCP config or SessionConfiguration
- App proves handoff initiation; authenticated identity resolution is downstream (Copilot/WorkIQ responsibility)

**Evidence:**
- uth-handoff.json: launchedAt, loginCommand, workspacePath only
- MCP config: carries no env block or principal payload
- SessionConfiguration: workspace path, MCP config, version, guidance, streaming, tools only
- Runtime log: session creation successful, no surfaced principal identity

**Implication:**
Current gap is not an app-side bug. If WorkIQ resolves "me" from authenticated session, that's Copilot/M365 auth responsibility. App confirms handoff initiation only.

**Status:** Decision 23 merged to decisions.md. All tests passing (63/63). Next phase ready.
