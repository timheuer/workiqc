# Project Context

- **Owner:** Tim Heuer
- **Project:** Windows WorkIQ desktop chat client
- **Stack:** Windows app shell, Copilot SDK, WorkIQ MCP server, markdown-rich chat UI, local session history persistence
- **Created:** 2026-03-13T17:59:26.524Z

## Core Context

**Test Strategy & Baseline Established:**
- Manual-first for UX/transcripts (visual regressions); automated early for persistence/encoding (silent failures).
- Persistence tests: 7 passing tests covering SQLite behavior (default conversations, Unicode/JSON metadata, foreign-key constraints, session ID uniqueness, recent-first ordering, cascade deletion).
- Runtime seam tests: 10 passing tests locking stub contract with explicit NotImplementedExceptions at boundaries.
- WorkIQC.App.Tests: 11 new ChatShellService tests (load empty/populated DB, send with fallback, draft creation, persistence round-trip, theme regression sentries).
- Full baseline: 21/21 tests passing (before final batch); Track C adds 11 more for service-level coverage.
- Bootstrap idempotency regression: Validates config file created once, not rewritten on launch — critical because ChatShellService calls bootstrap during shell load.

**Verification Approach:**
- Build: `dotnet build .\src\WorkIQC.slnx --nologo` verified clean (0 errors, 0 warnings).
- Tests: `dotnet test .\src\WorkIQC.slnx --nologo` locked as release gate (all passing, no skips).
- Unpackaged smoke: Real exe launch creates %LocalAppData%\WorkIQC\workiq.db + .copilot\mcp-config.json, stays alive long enough to prove shell starts cleanly.
- Fluent aesthetic: Both window and page inspected for materials treatment; flat Grid/StackPanel + hard-coded colors = rejection (e.g., Hicks' pass rejected; Bishop's revision accepted).
- Theme switching: Parse App.xaml Light/Dark dictionaries, assert distinct values for critical shell surfaces — catches regressions before UI automation.
- Markdown round-trip: Validate persistence stores raw markdown, handles streaming chunks with trimmed content, supports bold/italic/code/blocks/tables/lists/blockquotes/headings h1–h4.

**Known Limitations:**
- Full `dotnet test .\src\WorkIQC.slnx` fails in pre-existing SdkRuntime adapter compile errors (unrelated to test layer).
- Framework-dependent apps need Windows App Runtime outside Store — separate prerequisite from MSIX.
- Fluent aesthetic acceptance requires both layers: window-level Mica alone insufficient if page still flat.

## Learnings

- **Runtime Configuration testing requires explicit overrides:** When testing `ChatShellService`, mocking `ICopilotBootstrap` alone isn't enough; the `SessionCoordinator` callback must capture the configuration to verify `tool-posture` and `allowed-tools` guidance.
- **Fallback logic is distinct from runtime capability:** Tests must differentiate between "runtime is missing" (bootstrap check) and "runtime send failed" (orchestrator error), as they produce different UX states (setup card vs. fallback thread).
- **Screenshot evidence:** Visual verification of the sidebar footer text ("active reply is coming from the WorkIQ runtime") is a strong complement to unit tests asserting the `SidebarFooterText` property.
- **Project Context**

**v1 Acceptance checklist merged to `.squad/decisions.md`:** Chat UX (type, submit, immediate render, WorkIQ auto-invoke, continuous messaging), persistence (local sessions, list with previews, restart preservation), markdown (formatting, code blocks, tables), errors (timeouts, graceful fallback, crash safety), polish (dark mode, performance at scale).

**Test strategy finalized:** Manual-first for UX (visual regressions); automated early for persistence (session creation/load, ID uniqueness, encoding/Unicode, crash-safe operations). Seven highest-risk edge cases documented (file corruption, ID collision, bloat, data leak, markdown nesting, link hijacking, scroll position loss).

**Critical unknowns blocking verification:** Storage format (JSON/SQLite/binary?), markdown renderer choice, network error UX, session deletion behavior, auth scope (single/multi-user?), SDK constraints. Awaiting Tim's clarification.

## Team Updates (2026-03-14T05:34:42Z)

**Acceptance Criteria Locked:**
- Response content integrity: No internal runtime/planning chatter in visible assistant reply; only user-facing markdown surfaces.
- Readiness & auth messaging: Setup state reflects auth/EULA/bootstrap readiness; connection badge text changes appropriately ("Bootstrap ready" → "Runtime setup" → "WorkIQ runtime"); sidebar footer explains blockers vs. next steps.
- Diagnostic sufficiency: Bootstrap/auth/session failures surface actionable error messages with recovery commands and artifact paths.
- Existing test coverage baseline: 23 ChatShellServiceTests validate service-layer contracts; 3 minor gaps identified (explicit chatter validation, Node.js diagnostic, first-run transition flow).
- Status: Acceptance bar locked for SDK spike; 51/51 tests passing, ready for live MCP response streaming verification.

## Test Harness Implementation (2026-03-13T18:56:13Z)

**Persistence Tests Complete:** Added `WorkIQC.Persistence.Tests` with 7 passing tests covering SQLite behavior:
- Default conversation creation on first launch
- Unicode + JSON metadata storage
- Foreign-key constraints (orphan message rejection)
- Session ID uniqueness and upsert behavior
- Recent-first ordering
- Cascade deletion of messages/session rows

**Runtime Seam Tests Complete:** Added `WorkIQC.Runtime.Tests` with 10 passing tests:
- `CopilotBootstrap.InitializeAsync()` → throws explicit `NotImplementedException` with clear message
- `SessionCoordinator.CreateSessionAsync()` → throws explicit `NotImplementedException`
- `MessageOrchestrator.SendUserMessageAsync()` → throws explicit `NotImplementedException`
- Contract boundary testing locks current spike boundary; implementation throws with intent clarity

**Solution Wiring:** Both projects added to `WorkIQC.slnx` with test-specific configuration overrides (`RuntimeIdentifiers`, `UseWinUI`, `SelfContained`) so `dotnet test` runs them as test assemblies.

**Test Status:** ✅ 17/17 passing, no failures; `dotnet test .\src\WorkIQC.slnx --nologo` validates all seams.

**Integration readiness:** Vasquez can implement real SDK integration under existing contract stubs; tests will validate it. Bishop can add more persistence operations as needed. Hicks can rely on test suite to catch regressions in streaming or message handling.

**Orchestration log:** `.squad/orchestration-log/2026-03-13T18-56-13Z-newt.md`

## Learnings

- `build-rich.log` was stale enough to mislead verification; a clean `dotnet build .\src\WorkIQC.slnx --nologo` is the trustworthy check before calling the WinUI shell broken.
- `ChatShellService` invokes bootstrap workspace initialization during shell load, so runtime tests need an idempotency check to prove repeated startup does not rewrite `mcp-config.json` unnecessarily.

## Team Updates (2026-03-14T00:42:05Z)

**Newt (Verification Complete):** Triaged `build-rich.log` artifact (stale .NET SDK, not code defect). Confirmed clean baseline: `dotnet build` succeeds with 0 errors, `dotnet test` passes 21/21 (20 pre-existing + 1 bootstrap idempotency regression). Idempotency test validates config file created once, not rewritten on every launch — critical because `ChatShellService` calls bootstrap during shell load. **Impact:** Baseline locked as release gate; bootstrap regression covered; safe to proceed with Cut 3 visual work.

**Bishop (Build Repair):** Linked full `WorkIQC.Runtime.Abstractions` tree into app; resolved XAML compiler drift from partial file list. Updated local adapters to structured readiness models. Tests remain passing.

**Ripley (Assessment Complete):** Locked Cut 3 scope (markdown + visual identity, no SDK). Owners assigned: Hicks (WebView2 + theming), Vasquez (markdown pipeline), Newt (≥8 ChatShellService tests). Acceptance criteria documented; risk notes added (DPI scaling, HTML template simplicity, Markdig version validation).

**Vasquez (Readiness Seam):** Formalized app-visible readiness contracts. Explicit failure modes ready for operator troubleshooting.

**Decision Impact:** Decision #18 (Verification: Clean Solution as Release Gate) recorded and merged. Clean baseline lock enables Cut 3 to proceed with confidence.

**Unblocked:** Cut 3 markdown + theming work (Hicks/Vasquez). Newt ready to expand ChatShellService test coverage with ≥8 new tests for load/send/fallback scenarios. SDK spike proceeds in parallel.

**Next:** ChatShellService test expansion (load with empty/populated DB, send with fallback, CreateConversationAsync, title generation).

## Learnings

- Unpackaged WinUI F5 verification is strongest when it proves three layers together: project settings (`WindowsPackageType=None`, Debug MSIX disabled, unpackaged launch profile), clean `dotnet build`/`dotnet test`, and a real exe smoke run that stays alive long enough to create `%LocalAppData%\WorkIQC\workiq.db` plus `.copilot\mcp-config.json`.

## SDK Path & Logging Assessment (2026-03-14T21:10:50Z)

**Newt (Tester):** Completed independent inspection of SDK path verification requirements.

**Tim's Three Concerns Mapped:**

1. **"Prompts go through Copilot SDK only"** → Requires 4 independent signals:
   - Connection badge shows `"WorkIQ runtime"` (tested ✓)
   - `SessionCoordinator.CreateSessionAsync()` or `ResumeSessionAsync()` called (tested ✓, but not logged)
   - `IMessageOrchestrator.SendMessageAsync()` invoked with live session (tested ✓, but not logged)
   - Response contains actual WorkIQ data, not placeholder (visual test only, no regression coverage)

2. **"Local data stores only history/session metadata"** → Current contract is sound (✓)
   - SQLite stores conversation ID, messages, session ID, timestamps only
   - Fallback/placeholder responses persisted but clearly flagged in sidebar
   - No app-internal reasoning stored

3. **"Logs I can paste back for debugging"** → Partial coverage (8/11 needed signals present)
   - Missing: `runtime.session.created`, `runtime.session.resumed`, `runtime.orchestrator.send`, `runtime.orchestrator.stream`
   - Existing 15+ signals follow ISO 8601 + stage + artifact ID format (good for grep)

**Enter-to-Send Finding:** UI binding works (3 unit tests pass). Missing test: `ViewModel.SendAsync()` integration test verifying message added → shell service called → composer cleared. ComposerInputBehavior correct; gap is in ViewModel-level acceptance test.

**Decision:** Four small logging additions + one integration test will complete the acceptance criteria. No architecture changes needed. Decision document written to `.squad/decisions/inbox/newt-runtime-acceptance.md`.

**Learnings:**
- SDK path verification requires 4 independent signals; a single badge is insufficient
- Log lines must include artifact IDs + ISO timestamps to be individually inspectable
- ViewModel.SendAsync() is a multi-layer orchestration; integration test fills critical gap
- Placeholder responses are legitimate as long as sidebar clearly labels them
- A passing unpackaged smoke run on one machine does not prove cold-machine readiness; framework-dependent Windows App SDK apps still depend on the Windows App Runtime being available outside the Store package story, so that environment prerequisite should be called out separately from MSIX/package requirements.
- Fluent-refresh verification on WinUI has to inspect both the launched `Window` and the hosted `Page`: a Mica/Acrylic `MainWindow` is not evidence of a real refresh if the active page still uses flat `Grid`/`StackPanel` scaffolding, hard-coded colors, and default controls.

## Team Updates (2026-03-14T02:15:00Z)

**Newt (Acceptance Criteria Locked):** Defined acceptance bar for MCP server response integrity and readiness feedback. Three core requirements locked:
1. **No internal runtime/planning chatter** in visible assistant replies (only user-facing markdown streamed from MCP server).
2. **Clear readiness/auth messaging** in UI signals (connection badge, sidebar footer, setup state blockers/prerequisites).
3. **Diagnostic sufficiency** for bootstrap, auth, and session startup failures (actionable error messages, resolution text, marker paths).

Verified that 23 ChatShellServiceTests already provide strong coverage of these contracts:
- Response content purity validated implicitly (mock streaming matches output exactly).
- Readiness state transitions tested across empty, bootstrap-ready, and live-runtime paths.
- Diagnostics for missing Copilot CLI, Node.js, EULA, and auth all exposed in `SetupState.Blockers`.
- Error paths (`SessionCoordinator` exceptions, `MessageOrchestrator` failures) include resolution text.

**Gaps identified (non-blocking):**
- Explicit "no internal chatter" test (currently implicit in mock response matching).
- Single flow test for EULA → auth → bootstrap → runtime transition (currently separate).

**Documentation:** Acceptance bar written to `.squad/decisions/inbox/newt-runtime-acceptance.md` with verification approach, existing test mapping, and unresolved unknowns (live MCP response content, mid-stream error recovery).

**Impact:** Service-layer tests now enforce response purity as a release gate. SDK spike can validate live WorkIQ responses against this bar when real MCP integration lands.
- Track C coverage is cheapest and least invasive when it links app-layer service source into a dedicated non-WinUI test project, then drives `ChatShellService` end-to-end with real SQLite scopes plus fake bootstrap/runtime seams.
- Placeholder-stream assertions need to compare trimmed transcript content against persistence, because `StreamChunks` emits trailing spaces that the service later trims before saving the final assistant message.
- Theme-switching regressions are still catchable before UI automation exists by parsing `App.xaml` theme dictionaries and asserting both Light and Dark variants keep distinct values for the shell’s critical surface/text resources.
- Accepting a Fluent/material shell is strongest when three checks agree: the window/page composition clearly uses themed surfaces and centered empty states, the conversation wiring still passes build/test/smoke evidence, and any remaining hard-coded brushes are called out as dark-theme follow-up risk instead of silently ignored.

## Team Updates (2026-03-14T01:00:17Z)

**Fluent Shell Aesthetic Review Completed**

**Decision:** Reject Hicks' Fluent shell refresh artifact for revision.

**Evidence summary:**
- Build: Pass (`dotnet build .\src\WorkIQC.slnx --nologo`)
- Tests: Pass (21/21)
- Unpackaged smoke launch: Pass (exe stayed alive, created `%LocalAppData%\WorkIQC\workiq.db` + `.copilot\mcp-config.json`)
- Aesthetic acceptance: **FAIL**

**What failed:** The user-facing MainPage composition still renders the pre-refresh shell (hard-coded black background, plain Grid/StackPanel, stock ListView, TextBox, Button). No centered empty state, no rounded composer, no warm sidebar treatment, no suggestion cards, no Fluent controls (NavigationView, CommandBar, InfoBadge).

**What succeeded:** MainWindow.xaml.cs applies Mica/Acrylic, confirming the materials layer is reachable. Baseline shell behavior is intact (launch, persistence bootstrap, recent-history wiring).

**Key insight:** A window-level Mica backdrop is not sufficient evidence of a Fluent refresh when the hosted page still has flat scaffolding and hard-coded colors. The material treatment must reach the user-facing composition.

**Reviewer lockout:** Hicks is locked out from revising this artifact. Assignment transferred to Bishop.

**Guidance for Bishop's revision pass:**
1. Refresh the actual MainPage composition, not just MainWindow wrapper
2. Centered empty state with larger breathing room per reference image
3. Rounded, elevated Fluent composer card (not flat TextBox + Button)
4. Sidebar/content contrast via theme resources (not hard-coded black)
5. Keep existing shell wiring intact

**Status:** REJECTED; ASSIGNED TO BISHOP

## Learnings

- Unpackaged WinUI F5 verification is strongest when it proves three layers together: project settings (WindowsPackageType=None, Debug MSIX disabled, unpackaged launch profile), clean dotnet build/dotnet test, and a real exe smoke run that stays alive long enough to create %LocalAppData%\WorkIQC\workiq.db plus .copilot\mcp-config.json.
- A passing unpackaged smoke run on one machine does not prove cold-machine readiness; framework-dependent Windows App SDK apps still depend on the Windows App Runtime being available outside the Store package story, so that environment prerequisite should be called out separately from MSIX/package requirements.
- Fluent-refresh verification on WinUI has to inspect both the launched Window and the hosted Page: a Mica/Acrylic MainWindow is not evidence of a real refresh if the active page still uses flat Grid/StackPanel scaffolding, hard-coded colors, and default controls.
- Track C coverage is cheapest and least invasive when it links app-layer service source into a dedicated non-WinUI test project, then drives ChatShellService end-to-end with real SQLite scopes plus fake bootstrap/runtime seams.
- Placeholder-stream assertions need to compare trimmed transcript content against persistence, because StreamChunks emits trailing spaces that the service later trims before saving the final assistant message.
- Theme-switching regressions are still catchable before UI automation exists by parsing App.xaml theme dictionaries and asserting both Light and Dark variants keep distinct values for the shell’s critical surface/text resources.

## Track C: ChatShellService Test Coverage (2026-03-14T02:18:35Z)

**Status:** Complete

**Deliverables:**
- New WorkIQC.App.Tests project (net10.0, UseWinUI=false) with ChatShellService link-compilation
- Real ServiceCollection with AddPersistence() + mock runtime registrations
- All three runtime interfaces mocked to throw UnsupportedRuntimeActionException
- 11 ChatShellService-focused test cases covering load, send, fallback scenarios
- Real SQLite in-memory per test (no mocking persistence)

**Validation:**
- dotnet test .\src\WorkIQC.App.Tests\WorkIQC.App.Tests.csproj --nologo ✅ (11/11 passing)
- No filesystem access; in-memory SQLite only
- Tests run without WinUI runtime desktop window

**Key:** Track C focused on service/runtime/persistence; respects Hicks' UI implementation lockout.

**Result:** Service-layer safety net for parallel Hicks + Bishop work. All existing tests still passing.

## Final Integrated Review (2026-03-14T03:15:00Z)

**Decision:** Reject the integrated batch for one final live-readiness revision.

## Team Updates (2026-03-14T05:48:00Z)

**Shell Chrome Review — REJECTED**

**Assignment:** Review Bishop's shell chrome revision implementation.

**Evidence reviewed:**
- Shell capture showing title-bar area and left rail
- MainPage.xaml code inspection
- Test results for shell-review tests

**Verdict:** REJECTED

**Blocking evidence:**
The supplied shell capture still shows a `Bootstrap ready` pill in the title-bar area and a large verbose settings card in the left rail, so the live artifact cannot verify the promised chrome cleanup from the rendered display. The code/test evidence is directionally better: `MainPage.xaml` keeps `TitleBarDragRegion` to app identity only, moves runtime status into the conversation header, and the focused shell-review tests pass. However, the live artifact and the source do not align.

**Acceptance criterion failure:**
For this UX review, the rendered shell wins over code inspection. The visual evidence of the running app must match the source promise. Cannot approve until live shell shows: no title-bar pills, compact sidebar, clean Fluent surfaces.

**Recommendation:**
Revision reassigned to Ripley so a different reviewer-implementer pair can reconcile the visual artifact with the current markup and prove the final launch path. Bishop locked out of this artifact.

**Decision recorded:** Decision #14 — Shell Chrome Review, merged to decisions.md
Orchestration log written: 2026-03-14T05-48-00Z-newt.md
Session log written: 2026-03-14T05-48-00Z-second-shell-rejection.md

**What passed:**
- `dotnet build .\src\WorkIQC.slnx --nologo` succeeded.
- `dotnet test .\src\WorkIQC.slnx --nologo --verbosity minimal` succeeded (35/35).
- Unpackaged app launch still creates `%LocalAppData%\WorkIQC\workiq.db` and `%LocalAppData%\WorkIQC\.copilot\mcp-config.json`.
- Markdig + WebView2 rendering, theme-aware shell resources, persistence restore, and session/tool-activity bridge all appear product-complete enough for the desktop shell itself.

**What failed acceptance:**
- The active app bootstrap still writes floating WorkIQ config (`@microsoft/workiq`) instead of a pinned tested version, which violates the team's preview-dependency decision and keeps live behavior unstable.
- The app surfaces EULA/auth blockers as footer text only; there is still no real first-run consent/authentication handoff that proves an operator can get from "bootstrap ready" to a live authenticated WorkIQ turn.
- This machine confirms the environment gap honestly: `copilot`, `node`, `npm`, and `npx` resolve, but the app-owned EULA marker is missing, so live runtime success remains unverified.

**Reviewer lockout:** Vasquez is locked out from revising the final live-readiness artifact. Assignment transferred to Bishop for the app-owned bootstrap/consent revision.

## Learnings

- Final acceptance on a preview-backed desktop chat app has to separate product-complete surfaces (UI, persistence, rendering, local orchestration) from environment/runtime prerequisites, then state both explicitly instead of flattening them into one "done" claim.
- A live-readiness claim is still rejectable even when build, tests, and smoke launch pass if the shipped bootstrap writes a floating preview MCP package instead of the pinned version the team agreed to support.

## Team Update (2026-03-14T02:22:40Z)

**Scribe:** Merged Newt's final integrated review decision into `decisions.md` (Decision #21). Created orchestration log and session log. Newt's review complete; assignment transferred to Bishop for app-owned bootstrap revision (version pinning, first-run EULA/auth flow, transparent prerequisite reporting).

## Team Update (2026-03-13T19:36:03.1139095-07:00)

**Newt (Final acceptance):** Accepted Bishop's live-readiness revision. Verified `dotnet build .\src\WorkIQC.slnx --nologo` and `dotnet test .\src\WorkIQC.slnx --nologo --verbosity minimal` both pass (39/39). Smoke-launched the unpackaged app and confirmed `%LocalAppData%\WorkIQC\.copilot\mcp-config.json` is written with pinned package `@microsoft/workiq@1.0.0-preview.123`. Setup-card/dialog flow, app-owned EULA/auth handoff markers, and readiness refresh coverage are present. Product work is complete enough to call done; machine/user prerequisites remain explicit.

## Learnings

- Live-readiness acceptance should stay green when first-run consent/auth markers are still absent on the reviewer machine, provided the app proves three things together: launch writes the pinned bootstrap config, setup blockers stay visible in-product, and external auth remains clearly framed as an operator/environment prerequisite rather than hidden product state.



## Team Updates (2026-03-14T02:37:09Z)

**Live-readiness revision accepted — product complete.**

- Build: dotnet build .\src\WorkIQC.slnx --nologo ✅ (0 errors, 0 warnings)
- Tests: dotnet test .\src\WorkIQC.slnx --nologo --verbosity minimal ✅ (39/39 passing)
- Unpackaged smoke: App stays up, writes MCP config with pinned @microsoft/workiq@1.0.0-preview.123.
- EULA acceptance + auth handoff markers created and verified. Setup card + launch-time dialog functional.
- Service/runtime tests cover missing-marker state, post-acceptance/post-handoff refresh (11/11 ChatShellService tests green).
- Environment prerequisites explicit: This machine has copilot, 
ode, 
pm, 
px. EULA/auth markers pending operator completion (expected).
- Live WorkIQ turn requires: operator accepts terms, completes copilot login, Windows runtime/tooling on target machine.
- **Acceptance decision:** ACCEPTED. Remaining work is environment setup + operator first-run completion, not unfinished product implementation.

**Decisions merged:** 
ewt-live-readiness-acceptance.md → decisions.md

## Learnings

- Bubble-sizing acceptance on this shell needs live evidence, not just a larger XAML `MaxWidth`: the visible transcript is still rejectable when short line wraps make the user and assistant bubbles read like fixed narrow columns, especially with the markdown WebView hosted inside a left/right-aligned transcript item.

## Learnings
- Settings-flow acceptance is strongest when the test proves two exits: opening the settings surface from the rail and returning either through an explicit back action or by selecting a conversation, without losing the selected transcript.
- Thread-deletion review should verify both persistence cleanup and shell-state cleanup together: database/session rows disappear, the next recent thread becomes selected when available, and the transcript clears cleanly when the deleted thread was the last one.
- Bubble-width review should trust the live shell over the heuristic: if a screenshot still shows both user and assistant turns wrapping into tall narrow columns on a wide transcript pane, the revision is not accepted even when code now computes responsive min/max widths in page code.
- Readiness/auth reviews should trace three separate truths before blaming detection: the app-owned handoff marker, any external CLI proof of completed login, and the UI copy that maps internal state to labels. A system can internally mark auth as completed yet still show "Started" if the view model collapses completion into a handoff boolean.

## Learnings

- **App-owned markers for CLI auth:** Using file existence (\.workiq/eula-accepted.json\) is a robust way to bridge app-layer acceptance (UI) with runtime requirements (CLI), avoiding complex IPC for simple boolean states.
- **Transcript Purity:** Separating \ResponseStream\ (content) from \ActivityStream\ (tool status) in the view model prevents "thinking" noise from polluting the permanent chat history.
- **Placeholder Transparency:** When the runtime isn't ready, the shell should explicitly say "Placeholder response" but still behave like a real chat (streaming, bubbles) to validate the UI harness.
- Shell-refresh acceptance needs source, tests, and live artifact evidence to agree. If the screenshot still shows a title-bar readiness pill or a verbose sidebar settings card, reject even when static XAML gates now pass, because the user experiences the rendered shell rather than the intended markup.


## Team Updates (2026-03-14T05:39:13Z)

**Shell Chrome Review Completed**

**Verdict:** REJECTED

**Reviewed Artifact:** Hicks' shell chrome cleanup (2026-03-14T05:39:13Z)

**Issues Found:**
1. Title-bar status pills still present in XAML (MainPage.xaml lines 39–51) despite user directive to remove
2. Sidebar still verbose with descriptive text instead of Settings link only
3. Manual Grid layout instead of Fluent NavigationView pattern

**Test Status:** Build and previous baseline (39/39) remain passing; issue is user-facing implementation not matching requirements.

**Reassignment:** Shell chrome revision reassigned to Bishop for independent implementation.

**Lockout:** Hicks locked from further revisions on this artifact per Newt review protocol.

**Key Learning:** Visual review should include XAML inspection alongside behavior testing. Stated implementation ("removed status pills, reduced sidebar") did not match actual code changes.

**Runtime Live Readiness Decision:** APPROVED. Vasquez's implementation meets acceptance criteria. ChatShellService properly separates transcript from activity, auth/EULA flow correct, diagnostics user-friendly, test coverage sufficient (11 tests). Ready for live traffic.

## Team Update (2026-03-14T06:22:15Z)

**Shell Chrome Review Completed (Ripley)**

**Verdict:** APPROVED

**Evidence:**
- `MainPage.xaml` inspection: TitleBar contains only "WorkIQ". Sidebar contains only "Settings" button.
- `ShellReviewGateTests`: PASSED (31/31). Explicitly verifies absence of "StatusPill" and "SidebarFooterText".
- **Note on Artifacts**: The provided screenshot `copilot-image-1d320c.png` is stale (shows old UI). The verified code and tests take precedence.

**Learnings:**
- **Regex-based XAML Gates**: Used `ShellReviewGateTests.cs` to enforce UI constraints (e.g., "no status pills in title bar") without needing to launch the app.
- **Stale Artifacts**: Encountered a case where the provided screenshot contradicted the code. Trust the code (and tests) over the artifact when there is a mismatch, but explicitly call it out.


## Team Update (2026-03-14T06:50:00Z)

**WorkIQ MCP Launch Config Review (Newt)**

**Verdict:** REJECTED

**Evidence:**
- CopilotBootstrap.cs inspection: BuildMcpLaunchCommand hardcodes package name, ignoring version pinning from packageReference.
- McpConfigVerificationTests failure: Windows execution returns `npx` instead of cmd, which fails without shell wrapping.

**Reassignment:** MCP config revision reassigned to Bishop.

**Learnings:**
- **Configuration Regression**: Direct command execution bypasses version pinning logic intended for diagnostics and stability.
- **Platform Specificity**: Removing Windows cmd wrapper for `npx` breaks execution on Windows.

## Team Update (2026-03-14T06:00:37Z — Scribe Documentation Sync)

**Tasks completed:**

1. ✅ **Orchestration Logs Written:**
   - `.squad/orchestration-log/2026-03-14T06-00-37Z-vasquez.md` — MCP config rejection context
   - `.squad/orchestration-log/2026-03-14T06-07-00Z-bishop.md` — MCP config reassignment to Bishop

2. ✅ **Session Log Written:**
   - `.squad/log/2026-03-14T06-00-37Z-mcp-config-rejection.md` — MCP config rejection event

3. ✅ **Decisions Inbox Merged:**
   - 5 inbox files merged to `decisions.md` (Decisions 16–18)
   - Deleted inbox files (vasquez-mcp-launch-config.md, copilot-directive, hicks-composer-enter, newt-ripley-shell-review, ripley-shell-reconcile)
   - Deduplicated entries (shell chrome decisions consolidated)

4. ✅ **Agent Histories Updated:**
   - Vasquez: Added team updates for MCP config rejection and shell chrome review (2026-03-14T06:00:37Z)
   - Bishop: Added team updates for MCP config reassignment and shell chrome rejection (2026-03-14T06:00:37Z)
   - Newt: Added team updates summarizing rejection decisions (2026-03-14T06:00:37Z)

5. ✅ **Size Check (Task 5):**
   - `decisions.md` currently ~15KB — below 20KB threshold
   - `vasquez/history.md`, `bishop/history.md`, `newt/history.md` all within reasonable bounds (no archival needed)

6. ⏳ **Git Commit:** Ready for execution

7. ⏳ **History Summarization:** No file exceeds 12KB threshold requiring summarization

## Team Update (2026-03-14T14:36:39Z)

**Runtime Flow Verification Complete**

**Status:** ✓ FINALIZED

**Acceptance Criteria Completed:**

1. **Prompts go through Copilot SDK only**
   - Four evidence signals defined: badge, session creation/resume, orchestrator invocation, response content
   - ChatShellServiceTests lock the live-path contract
   - Sidebar footer confirms: "active reply is coming from the WorkIQ runtime"

2. **Local storage metadata-only**
   - ✓ Passing — conversation ID, title, messages, session ID, timestamps only
   - ✓ No fallback reasoning or internal state persisted
   - SQLite contract clean

3. **Diagnostics pasteable** (TODO)
   - Four new WriteDiagnostic calls identified:
     - `runtime.session.created`
     - `runtime.session.resumed`
     - `runtime.orchestrator.send`
     - `runtime.orchestrator.stream`
   - Log format: ISO 8601 + stage.noun + artifact IDs
   - Missing integration test: `SendAsync_WithComposerText_ClearsTextAndAddsUserMessageToTranscript()`

**Decision 20 merged to decisions.md:** Three-part acceptance criteria fully documented. No breaking changes; gaps are logging visibility + one integration test.

**Recommendations:**
1. Implement 4 new WriteDiagnostic calls in ChatShellService
2. Add SendAsync integration test to MainPageViewModelTests
3. Verify all four evidence signals present in a live session

**Orchestration log:** `.squad/orchestration-log/2026-03-14T14-36-39Z-newt.md`


