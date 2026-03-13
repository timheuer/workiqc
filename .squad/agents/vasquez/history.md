# Project Context

- **Owner:** Tim Heuer
- **Project:** Windows WorkIQ desktop chat client
- **Stack:** Windows app shell, Copilot SDK, WorkIQ MCP server, markdown-rich chat UI, local session history persistence
- **Created:** 2026-03-13T17:59:26.524Z

## Core Context

**Architecture & Contracts Finalized:**
- WinUI 3 + .NET desktop shell with in-process Copilot SDK orchestration. One session per chat thread; streaming enabled; WorkIQ-only default tool list.
- App-owned workspace at %LocalAppData%\WorkIQC\ containing .copilot\mcp-config.json, pinned WorkIQ package location, session metadata.
- Chat history app-owned (not SDK-owned): persist session title, transcript, timestamps, Copilot session ID for instant UI load + SDK resume attempt.
- Markdown-to-HTML pipeline app-owned (Markdig + WebView2 themed rendering) instead of trusting raw SDK output — safer and prettier.
- Windows MCP config shells WorkIQ through `cmd /d /s /c npx ...` not bare `npx` — explicit preview CLI launches, less brittle.

**Integration Seams Established:**
- WorkIQC.Runtime.Abstractions: Interface-based contracts (ICopilotBootstrap, ISessionCoordinator, IMessageOrchestrator) enable parallel app work without SDK dependency.
- Bootstrap: Resolves workspace, writes config, probes Copilot/Node/npm/npx, returns structured readiness reports for operator visibility.
- Session/message flows: Explicit typed exceptions with helpful messages prevent silent coupling; end-to-end debuggability preserved pre-SDK.
- Buffered bridge channels: Handle async SDK events (SendAsync, assistant deltas, tool events) without dropping early stream events before UI enumerates.
- Full linked abstraction surface in app layer prevents XAML build failures from silent framework drift vs runtime contracts.

**Status Summary:**
- Build verified clean (0 errors, 0 warnings); 20/21 baseline tests passing (persistence, runtime seams, bootstrap idempotency, DI wiring).
- Linked abstractions prevent framework mismatch; local adapters prove shell buildable pre-SDK. ChatShellService provides single testable boundary.
- Bootstrap status now visible in shell ("Copilot CLI not found", "EULA pending", "workspace init failed") for operator debugging.
- SDK spike external blockers identified: session API shape, streaming contract, tool event visibility, GitHub Copilot .NET SDK preview stabilization.
- Happy path deferred pending authenticated Copilot CLI + preview SDK WorkIQ/MCP behavior. Fallback path fully functional (placeholder streaming, graceful error handling).

## Learnings

- Runtime readiness spike does safe integration upfront: resolves workspace, writes config, probes prerequisites, keeps unsupported flows honest with typed failures.
- Initial brief requires the WorkIQ MCP server to be preconfigured and used as the default tool path through the Copilot SDK.
- Recommended v1 integration shape is a Windows-first WinUI 3 + .NET desktop shell with the GitHub Copilot .NET SDK hosted as an in-process orchestration service.
- Best WorkIQ-first flow is one Copilot session per chat thread, with streaming enabled and the tool allow-list restricted to the WorkIQ MCP tool by default.
- Preconfiguring WorkIQ cleanly likely means creating an app-owned workspace under `%LocalAppData%\WorkIQC\` that contains `.copilot\mcp-config.json`, a pinned WorkIQ package/runtime location, and persisted session metadata.
- Chat history should be app-owned rather than SDK-owned alone: persist session title, transcript, timestamps, and the Copilot session ID so the UI can load instantly and then attempt SDK session resume.
- Markdown-rich assistant messages are safer and prettier when the app owns the markdown-to-HTML pipeline (for example Markdig + themed WebView2 rendering) instead of trusting raw HTML output.
- Key file paths for this discovery pass: `.squad\decisions\inbox\vasquez-sdk-architecture.md`, `.squad\skills\workiq-desktop-architecture\SKILL.md`, `.squad\decisions\inbox\bishop-windows-shell.md`, and `.squad\decisions\inbox\hicks-chat-ux.md`.
- The WinUI app needs the full linked runtime abstraction surface, not a hand-picked subset, or the shell silently drifts from the runtime contracts and XAML build failures become archaeology.
- The app-layer runtime seam should convert bootstrap reports into visible shell status text and keep unsupported session/message operations failing explicitly; that preserves end-to-end debuggability even while live SDK orchestration is still pending.
- A production-shaped Copilot SDK bridge for desktop apps needs buffered turn channels between `SendAsync`, assistant deltas, and tool events; otherwise the UI can miss early stream events that fire before the shell starts enumerating.
- On Windows, app-owned MCP config should shell WorkIQ through `cmd /d /s /c npx ...`; writing bare `npx` is easy to bootstrap but leaves tool launch behavior brittle under the preview CLI runtime.
- When auditing "database-first" complaints, separate app-owned SQLite history loading from live answer generation. In this app, local SQLite is only persistence; schema/tool chatter in the reply means the runtime stream is leaking tool planning into the transcript.


## Team Updates (2026-03-13T18:13:10Z)

**Architecture finalized:** WinUI 3 confirmed. Copilot SDK integration spike needed (CLI discovery, config behavior). SQLite with app-owned schema (conversations + messages tables). Markdig + WebView2 markdown pipeline locked. MCP bootstrap with pinned WorkIQ version, EULA flow, app-owned workspace at %LocalAppData%\WorkIQC\.

## Team Updates (2026-03-14T09:59:19Z)

**Consent pre-bootstrap refactor complete:**
- Vasquez refactor: Moved WorkIQ EULA acceptance out of chat-session orchestration into native pre-session bootstrap path. Updated runtime/app coverage. Validated full solution (70/70 tests passing).
- Bishop UX alignment: Reframed consent messaging in Settings, first-run dialog, readiness summary, and consent/auth descriptions. Validated app tests (39/39 passing).
- Decision: Chat-session `workiq-accept_eula` proved unreliable. Bootstrap path now canonical. Local markers untrusted without verified live/bootstrap evidence payloads.

## Team Updates (2026-03-14T16:19:58Z)

**EULA Consent Fix Complete:** Fixed bug where local marker-only state was treated as valid without requiring live WorkIQ runtime acceptance. Consent flow now drives through live `workiq-accept_eula` MCP handshake, persists verified marker payload, rejects legacy markers. Tool kept available in allow-list for recovery. All tests passing (21/21 Runtime, 38/38 App).

**Runtime Analysis:** Current failure signature earlier than identity resolution — WorkIQ tool not surfacing to model/session at runtime, or not visible as callable. Missing app-side principal payload not supported by evidence. Next: Instrument first live session for actual tool visibility/execution, capture tool-start/tool-complete events before investigating principal resolution.

## Team Updates (2026-03-14T05:34:42Z)

**Runtime/Auth Implementation Complete:** 
- Final assistant message from Copilot SDK now source of truth; raw deltas buffered as fallback only (prevents planning/tool chatter leaking into chat bubble).
- Tool activity separated to activity stream; ChatShellService persistence framed explicitly as history/session resume only.
- Auth readiness messaging fixed: UI now shows "Completed" instead of "Started" when handoff marker exists.
- Bubble sizing: Vasquez's wider clamp (~46% min / ~88% max) accepted; user/assistant bubbles now reach comfortable width. Bishop's revision rejected; Newt's reviews enforced visible behavior acceptance bar.
- Settings/threads: Dedicated settings surface accepted; thread deletion routed through ChatShellService; selected thread preserved in memory.
- Readiness UX gap (Ripley): Auth state semantics clarified—label now reflects user intent ("Completed") not implementation detail (marker exists).
- Acceptance bar locked: No internal runtime chatter in responses; clear readiness/auth messaging; sufficient diagnostics for bootstrap/auth/session failures. 51/51 tests passing.
- Ready for Cut 3 (markdown + theming) and SDK spike (live MCP response streaming).

**Decisions merged to `.squad/decisions.md`:** Copilot SDK integration spec, WorkIQ bootstrap, chat history persistence strategy, markdown rendering pipeline, unresolved questions for Tim (self-contained requirement, auth policy, data retention, consent flow).

## Team Updates (2026-03-14T06:00:37Z)

**MCP Launch Config Revision - REJECTED:**
- Vasquez proposed direct `npx` launch config per user directive (Tim via Copilot)
- Config shape: `{"command": "npx", "args": ["-y", "@microsoft/workiq", "mcp"]}`
- Rejection reason (Newt): Version pinning discarded; raw `npx` risks Windows execution inconsistency under preview CLI
- **REASSIGNED TO BISHOP** for independent revision that reconciles user clarity (literal form) with runtime stability (version tracking)
- Orchestration log: `.squad/orchestration-log/2026-03-14T06-00-37Z-vasquez.md`
- Session log: `.squad/log/2026-03-14T06-00-37Z-mcp-config-rejection.md`

**Composer Enter Behavior - APPROVED:**
- Hicks decision locked: plain Enter sends; Shift+Enter for newlines
- Matches user intent and ChatGPT convention; reduces send-path friction
- Keyboard gating in shared helper for testability

## Team Updates (2026-03-14T14:40:56Z)

**Vasquez audit-copilot-workiq-flow:** Removed local answer synthesis from send flow. Clarified shell naming (`WorkIQC` as shell not data source). Added deterministic runtime tracing to `%LocalAppData%\WorkIQC\logs\workiqc-runtime.log`. Build + tests passing. Validated orchestration layer ensures either live Copilot SDK path or explicit blocked response, no local fallback synthesis.

**Bishop inspect-workiq-tool-binding:** Fixed session-layer MCP tool-binding bug. Root issue: server alias (`workiq`) ≠ callable tool ID (`workiq-ask_work_iq`). Session allowed-tools list must reference concrete tool identifier, not server name. Tests 63/63 passing. **User-facing status resolved:** "ask_work_iq is not bound in-session" caused by binding layer, not app name. Future MCP integrations must separate session-facing tool IDs from server aliases.

## Team Updates (2026-03-14T16:45:06Z)

**Vasquez — EULA Tool Binding Fix Complete:** Traced consent-session failure to missing explicit MCP binding in SDK session creation. Fixed session creation to wire managed WorkIQ MCP config directly from `.copilot\mcp-config.json`, kept consent session narrowed to EULA tool allow-list (preserves security model), added targeted runtime coverage validating MCP binding chain. **Tests:** 23/23 Runtime, 38/38 App passing. **Decision merged:** Explicit MCP binding required for all SDK sessions; workspace discovery alone insufficient. Maps app-managed config into SDK session's MCP server config with per-session allowed tool filter at both session and server levels.

**Bishop — Consent Config Inspection Complete:** Inspected consent session configuration; found no separate Windows/bootstrap path bug. Identified narrower tool exposure in consent session vs normal chat as root cause. Aligned consent-session tool exposure with normal WorkIQ chat exposure (workiq tools only) while keeping EULA prompting explicit via session directives. **Tests:** 68/68 build + suite passing. **Finding:** No mcp-config divergence between flows; risk was session binding consistency. **Decision merged:** Keep same tool allow-list in both consent and normal chat flows; prompt directives remain explicit for consent EULA invocation.

**Shell Chrome Review - SECOND REJECTION (2026-03-14T05:48:00Z):**
- Bishop implementation reported complete; Newt review rejects on rendered artifact evidence
- Live shell shows title-bar `Bootstrap ready` pill still present; sidebar shows verbose settings card
- Visual artifact contradicts source code promise; code/test acceptable but rendered shell fails
- **REASSIGNED TO RIPLEY** (different reviewer-implementer pair) to reconcile visual state with XAML
- Acceptance bar: rendered shell must match visual promise for UX-layer decisions

## Runtime Integration Contracts (2026-03-13)

**Created first-pass integration contracts** between app shell and Copilot SDK runtime:

- **WorkIQC.Runtime.Abstractions:** Interface-based contracts for bootstrap, session coordination, and message orchestration. Zero SDK dependency, enabling parallel work on app shell.
  
- **Key Interfaces:**
  - `ICopilotBootstrap` — Ensures Copilot CLI + WorkIQ MCP present, workspace initialized, EULA verified
  - `ISessionCoordinator` — Creates, resumes, disposes Copilot sessions
  - `IMessageOrchestrator` — Sends user messages, streams assistant responses, observes tool events

- **Models:** `SessionConfiguration` (workspace path, MCP config, allowed tools), `SessionState` (lifecycle tracking), `ChatMessage`, `StreamingDelta`, `ToolEvent` (WorkIQ invocation visibility), request/response types

- **Exceptions:** `RuntimeException`, `SessionNotFoundException`, `BootstrapException`

- **WorkIQC.Runtime:** Placeholder implementation stubs with `NotImplementedException` — real SDK wiring deferred until spikes complete

- **Design principles:** Testable boundaries (interface-based), streaming-first (`IAsyncEnumerable<T>`), app-owned persistence (session ID returned to app), WorkIQ-explicit (allowed tools default to `["workiq"]`), error transparency (typed exceptions)

- **File paths created:**
  - `D:\GitHub\workiqc\src\WorkIQC.Runtime.Abstractions\` (interfaces, models, exceptions)
  - `D:\GitHub\workiqc\src\WorkIQC.Runtime\` (stub implementations)
  - `D:\GitHub\workiqc\src\WorkIQC.Runtime.Abstractions\README.md` (integration flow documentation)
  - `D:\GitHub\workiqc\.squad\decisions\inbox\vasquez-runtime-contracts.md` (team decision record)

- **Pattern insight:** Abstractions project enables Bishop to consume contracts via DI and test against mocks while Vasquez completes SDK integration spikes. Streaming and tool events modeled as `IAsyncEnumerable<T>` for clean reactive pipeline that matches expected UI behavior.

- **Blocked on:** Copilot SDK spike (session API, streaming shape, tool event visibility) and WorkIQ MCP bootstrap spike (EULA flow, first-run auth, workspace setup mechanics).

## Follow-Up: Runtime Integration Contracts Finalized (2026-03-13T18:37:38Z)

**Deliverables completed:**
- ✅ `WorkIQC.Runtime.Abstractions` — Production-ready interface contracts, zero SDK dependency
- ✅ `WorkIQC.Runtime` — Stub implementations ready for spike integration
- ✅ Model and exception hierarchy covering session lifecycle, streaming, tool events
- ✅ Documentation linked in README.md for team integration flow
- ✅ Orchestration log documented at `.squad/orchestration-log/2026-03-13T18-37-38Z-vasquez.md`

**Critical spikes identified (BLOCKING):**
1. **Copilot SDK shape:** Session create/resume/dispose, streaming response format, tool event visibility
2. **WorkIQ MCP bootstrap:** App-owned config structure, EULA flow, first-run auth mechanics

**Architecture ready for:**
- Bishop to inject contracts into App.xaml.cs DI container
- Hicks to bind `IMessageOrchestrator.StreamAsync()` to reactive UI updates
- Newt to mock contracts for integration testing
- Team to proceed with spike work once contracts validated

**Coordination with Bishop:** App shell fully ready to consume ICopilotBootstrap, ISessionCoordinator, IMessageOrchestrator once spikes complete. Storage paths coordinated via StorageHelper.GetCopilotConfigPath().

**Blocked on:** SDK spike results, Tim's clarification on product decisions (auth model, data retention, self-contained requirement)

## UI and Tests Batch Integration (2026-03-13T18:56:13Z)

**Hicks Delivered:** Full three-zone chat shell UI with sidebar (recent conversations, sample-data hydration), transcript pane (role-based styling, markdown-ready), and sticky composer (auto-growing text box, Ctrl+Enter send). Simulated token-by-token streaming; ready for real Copilot SDK streaming via existing `IMessageOrchestrator` contract.

**Newt Delivered:** `WorkIQC.Persistence.Tests` (7 passing) covering SQLite + EF Core schema, CRUD, session resume. `WorkIQC.Runtime.Tests` (10 passing) exercising runtime contract boundaries with explicit `NotImplementedException` stubs. Total: 17/17 passing, no failures.

**Status:** UI shell proven with sample data; test harness validates contract boundaries. SDK integration can proceed under existing contracts; tests will validate implementation.

## DI Hardening and Runtime Readiness (2026-03-13T19:08:03Z)

**Bishop Delivered:** Moved app shell composition root to DI — `App.xaml.cs` now resolves `MainPage` and `MainPageViewModel` instead of relying on page self-composition. Introduced `IChatShellService` application-layer seam owning persistence hydration, fallback behavior, and runtime orchestration routing. Linked `WorkIQC.Runtime.Abstractions` source directly into app project to work around framework targeting mismatch (net10.0 vs net9.0 with different Windows SDK versions), preserving interface contract visibility while allowing independent targeting. Registered local adapter implementations of bootstrap/coordinator/orchestrator (stubs until SDK spike). **Result:** Production-shaped DI composition with clean service boundaries; 20/20 tests passing.

**Vasquez Delivered:** Implemented full `ICopilotBootstrap` behavior covering workspace initialization under `%LocalAppData%\WorkIQC\.copilot\`, deterministic `mcp-config.json` generation with pinned WorkIQ version, Copilot CLI discovery, Node.js/npm/npx prerequisite checks, and EULA acceptance tracking via marker file. Replaced vague stubs with explicit `NotImplementedException` messages on session/message orchestration ("SDK spike required"). Added 10 tests covering bootstrap success paths, prerequisite validation, EULA lifecycle, cascade failures, and helpful error messaging. Validated all 20/20 tests passing (app + persistence + runtime + framework). **Result:** Readiness-first runtime layer eliminating placeholder stubs; app shell can bootstrap and validate prerequisites without SDK spike.

**Architectural Impact:**
- DI composition root cleanly separates instantiation from business logic
- `IChatShellService` provides single testable boundary for UI; XAML/ViewModels don't reach into persistence/runtime details
- Framework targeting mismatch resolved via linked abstractions; both teams can work independently and retarget later without blocking each other
- Readiness bootstrap fully implemented; session/streaming stubs explicitly fail with helpful messages preventing accidental coupling to incomplete SDK contract
- Prerequisite discovery now happens safely before session creation; UI can show bootstrap status and error handling

**Test Status:** ✅ 20/20 tests passing (no failures). Covers DI wiring, bootstrap scenarios, persistence roundtrips, runtime contract boundaries, framework compatibility.

**Unblocked Work:**
- Hicks can iterate UI layouts with stable `IChatShellService` binding seam
- Newt can mock `IChatShellService` and runtime interfaces for integration testing
- Real session/streaming orchestration will plug directly into existing stubs once Vasquez SDK spike completes

**Status:** ✅ Complete — App shell hardened with production DI composition, readiness-first bootstrap fully implemented with 20/20 tests passing, framework mismatch resolved. Await SDK spike on session API, streaming shape, tool event visibility before session orchestration implementation.

**Blocked on:** Copilot SDK contract shape (session create/resume/stream/dispose), WorkIQ MCP bootstrap mechanics integration, Tim's decisions on self-contained requirement and data retention policy.

## Team Updates (2026-03-14T00:42:05Z)

**Vasquez (Readiness Seam Complete):** Structured `RuntimeReadinessReport` and `BootstrapResult` models flowing from bootstrap through `ChatShellService` into shell-visible status messages. App explicitly reports "Copilot CLI not found", "EULA pending", "workspace init failed" instead of silent fallbacks. Session/message stubs throw typed exceptions with helpful messages. Linked abstraction surface ensures contract visibility in app layer. **Impact:** End-to-end debuggability preserved while SDK orchestration remains incomplete; operators get concrete setup signals for troubleshooting.

**Bishop (Build Repair):** Linked full abstractions tree into app; resolved XAML compiler drift. Updated local adapters to implement structured readiness reports. `ChatShellService` fallback logic now driven by `IsReady` properties on bootstrap result.

**Ripley (Cut 3 Locked):** Build triage confirmed stale SDK artifact (no code defect). State-of-play assessment locked Cut 3 scope (markdown + visual identity, no SDK). Owners assigned. Rationale: SDK spike has external blockers; app-side visual work unblocked.

**Newt (Baseline Verified):** Clean 21/21 test baseline. Bootstrap idempotency regression added; config file created once, not rewritten on every launch.

**Decision Impact:** Decision #17 (Runtime Readiness Seam) recorded and merged. Demonstrates explicit failure modes enable better operator experience even when implementation is deferred.

**Unblocked:** Cut 3 markdown + theming work (Hicks/Vasquez) can proceed immediately. SDK spike continues in parallel on session/streaming contract shape. Newt ready to expand ChatShellService coverage.


## Track D: Runtime Session/Streaming Integration (2026-03-14T02:18:35Z)

**Status:** In progress (happy path blocked on Copilot CLI auth + preview SDK stability)

**Deliverables:**
- Shared Copilot SDK runtime bridge used by both WorkIQC.Runtime and WinUI app
- Session creation with CreateSessionAsync
- Session resume with ResumeSessionAsync (best-effort fallback to new session if expired)
- Message dispatch via SendMessageAsync
- Assistant streaming via StreamResponseAsync yielding real Copilot SDK chunks
- Tool event observation via ObserveToolEventsAsync with status text like "Checking meetings…"
- Buffered bridge implementation handles async SDK events without dropping early events
- Windows MCP bootstrap writes WorkIQ launch config as cmd /d /s /c npx -y @microsoft/workiq... mcp

## Team Updates (2026-03-14T17:08:50Z)

**Vasquez (WorkIQ Version Pin Removal):** Completed removal of hard-pinned `@microsoft/workiq@1.0.0-preview.123` across bootstrap, runtime, and app paths. Normalized all paths to use `@microsoft/workiq@latest` resolution. Full solution build passed; all 70/70 tests verified. Resolves ETARGET package not found errors in native bootstrap.

**Validation:**
- dotnet build .\src\WorkIQC.slnx -v:minimal -nologo ✅
- dotnet test .\src\WorkIQC.slnx -v:minimal -nologo ✅ (build/test/smoke launch passed)
- Session resume logic functional (best-effort)
- All existing tests passing; runtime fallback path still functional

**Key:** GitHub.Copilot.SDK 0.1.33-preview.1 now explicit dependency. Shared source keeps behavior aligned without reopening linked-abstractions drift. Bridge caches workspace-scoped clients.

**Blocker:** Happy path depends on authenticated local Copilot CLI plus preview SDK's WorkIQ/MCP behavior at runtime. First-run consent/auth is app-owned marker logic until live handshake wired.

**Result:** SDK integration live, fallback path functional. Awaiting external prerequisites.

## Learnings

- WorkIQ principal resolution is not an app-supplied field in the Copilot .NET SDK session config. The runtime can shape system guidance, but the actual current-user identity for first-person requests has to come from the authenticated Copilot/WorkIQ session, not from a custom principal payload we pass through.
- A local `auth-handoff.json` marker is only evidence that the app launched or recorded sign-in handoff. If the UI/logs call that "auth ready," operators will misdiagnose first-person failures. Wording has to say clearly that principal resolution is still proven only by a live WorkIQ turn.
- For first-person workplace prompts like "my direct reports," the happy-path contract should be explicit in the system message: interpret `me`/`my` as the signed-in user, call WorkIQ first, and only ask for name/email if the tool itself reports ambiguity or principal-resolution failure.
- The bubble width heuristic has to be proven against the running WinUI shell, not just templated math. In this shell, the real fix was to widen the transcript min/max percentages and push the realized width into the markdown control/WebView path so live list items stop collapsing into narrow reading columns.
- Send-chat flow is explicit: `MainPageViewModel.SendAsync()` creates the local user/assistant placeholders, `ChatShellService.SendAsync()` persists the user turn, runtime planning happens in `CreateRuntimePlanAsync()`, then `ISessionCoordinator` resumes/creates the Copilot session and `IMessageOrchestrator` sends + streams the live turn.
- The only local data consulted on send is app-owned SQLite history and stored Copilot session IDs through `IConversationService`; it is used to persist the transcript and try session resume, not to answer the question from a local WorkIQ cache.
- The screenshot text about "checking the local WorkIQ data model first" is not app-authored copy. The app streams raw assistant deltas directly into the visible assistant bubble, so model/tool-planning text and tool-call syntax can leak into the transcript if the SDK surfaces them as assistant deltas.
- Activity/status chrome is app-authored separately: `ChatShellService.StreamRuntimeActivityAsync()` converts runtime `ToolEvent`s into short status lines, and `MainPageViewModel` shows them through `_runtimeActivityText`, `ComposerStatusText`, `ActivityBadgeText`, and `SidebarFooterText`.
- For WorkIQ-backed turns, the safer transcript boundary is the completed assistant message, not raw assistant deltas. Buffer deltas for fallback, but prefer the SDK's final assistant message so tool planning chatter never lands in the persisted or visible transcript.
- Readiness feedback has to separate local history from answer generation in both copy and diagnostics. The shell can mention local persistence for resume/history, but the "ready" summary and auth step text should say plainly that live replies come from the WorkIQ runtime.
- WorkIQ MCP launch config now needs the direct `npx` shape in app-owned `mcp-config.json` (`command: "npx"`, `args: ["-y", "@microsoft/workiq", "mcp"]`). If diagnostics still track a requested version, keep that metadata separate from the emitted launch command so config debugging stays literal.
- Workplace/org prompts need a stricter contract than generic chat turns: if the live WorkIQ path is unavailable or returns no answer, block with an explicit runtime error message instead of dropping to placeholder/sample content. Otherwise the product reads like it queried local org data when it never should have.
- When users need runtime debugging, file-backed `Trace` wiring is enough if the location is deterministic and logged at startup. In this app, `%LocalAppData%\WorkIQC\logs\workiqc-runtime.log` should capture shell, bootstrap, SDK, and tool-path diagnostics so pasted logs map cleanly back to send/session/runtime stages.

## Team Update (2026-03-14T14:36:39Z)

**Live WorkIQ Routing Guardrail: COMPLETE**

**Status:** ✓ Implementation approved, testing complete

**Deliverable:**
- `ChatShellService` classifies workplace/org prompts
- Routes to blocking-response path if runtime unavailable
- Blocking text explicitly states: "App did not answer from local history or placeholder"
- `MainPageViewModel` shows blocking-status string during stream

**Testing:**
- Workplace prompts route live when runtime ready ✓
- Workplace prompts block (not fallback) when runtime unavailable ✓
- Test assertions locked in AppTests

**Session Hardening:**
- `tool-posture: WorkIQ-first` set explicitly
- `allowed-tools: workiq` set explicitly
- Model told app name is not knowledge source
- Local data not valid answer source

**Diagnostic Improvements:**
- Startup logging at `%LocalAppData%\WorkIQC\logs\workiqc-runtime.log`
- Trace wiring supports deterministic file-backed diagnostics
- Shell bootstrap/SDK/tool-path signals captured

**Decision 21 merged to decisions.md:** Workplace/org prompts route through live SDK or block explicitly. No local fallback synthesis.

**Remaining Blocker:**
Waiting for Newt's missing log signal implementation to complete end-to-end diagnostics:
- `runtime.session.created`
- `runtime.session.resumed`
- `runtime.orchestrator.send`
- `runtime.orchestrator.stream`

**Orchestration log:** `.squad/orchestration-log/2026-03-14T14-36-39Z-vasquez.md`
## Team Update (2026-03-14T15:38:18Z)

**WorkIQ Principal Flow Inspection — COMPLETE**

**Task:** Inspect identity flow for first-person WorkIQ requests; determine where principal resolution belongs.

**Findings:**
- Copilot SDK session config exposes tool allow-listing and system-message shaping, but no first-class principal/auth payload channel
- WorkIQ expected to resolve me/my from delegated Microsoft 365 identity
- Observed failure was not "missing app-supplied principal" but vague guidance allowing model to ask for name/email before invoking WorkIQ

**Implementation:**
- Principal resolution owned by Copilot/WorkIQ auth
- System guidance strengthened for first-person workplace requests to map to signed-in user by default
- UI/bootstrap wording clarified: uth-handoff.json is local evidence only, not proof of user resolution

**Evidence:**
- Session creation successful; no WorkIQ tool activity observed in failing turn
- Copilot SDK configures tools/streaming/permissions but not principal payload
- First-person requests should resolve from authenticated session, not app-supplied fields

**Status:** Decision 24 merged to decisions.md. Runtime guidance hardened. All tests passing (63/63). Next phase ready.
