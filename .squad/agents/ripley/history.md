# Project Context

- **Owner:** Tim Heuer
- **Project:** Windows WorkIQ desktop chat client
- **Stack:** Windows app shell, Copilot SDK, WorkIQ MCP server, markdown-rich chat UI, local session history persistence
- **Created:** 2026-03-13T17:59:26.524Z

## Learnings

- Initial brief: build a Windows app that feels like ChatGPT but defaults to WorkIQ-driven conversations.
- **Framework decision (proposed):** WPF for v1 due to mature markdown ecosystem (Markdig.Xaml) and rich styling; WinUI 3 considered but markdown support weaker.
- **Copilot SDK integration:** Use `GitHub.Copilot.SDK` NuGet with streaming enabled; configure WorkIQ as the sole MCP server at session creation.
- **Persistence:** SQLite recommended over JSON files for queryable session history.
- **Key UI patterns from ChatGPT research:** bottom-anchored composer, streaming text rendering, collapsible chat history sidebar, stop-generation button, light/dark theme.
- **Out of scope for v1:** voice, file uploads, conversation branching, cloud sync, multi-user.
- **Key file:** `.squad/decisions/inbox/ripley-workiq-desktop-scope.md` contains the full v1 scope proposal.
- When shell evidence and source disagree, trust the running page and inspect the compiled `obj\...\Views\MainPage.xaml` plus a live window capture before approving. For WinUI smoke evidence on this machine, `PrintWindow` produced a usable shell capture after screen-copy methods returned black frames.

## Team Updates (2026-03-13T18:13:10Z)

**Squad decision merged to `.squad/decisions.md`:** Framework decision changed to **WinUI 3** (Bishop's analysis found WPF legacy, WinUI 3 offers modern Fluent Design + native DirectX performance). Architecture locked on SQLite app-owned persistence + SDK session resume fallback, Markdig + WebView2 markdown pipeline, in-process Copilot SDK with WorkIQ-only tool list default.

**Spike priorities:** Copilot CLI discovery/config behavior, WorkIQ bootstrap, markdown rendering, persistence restore fallback.

**Blockers for Tim:** Self-contained app requirement? GitHub Copilot auth acceptable? WorkIQ-only or expand tools later? Data retention policy? First-run consent flow?

## Assessment: Cut 3 — Polished Chat Loop (2026-03-13)

**Build triage:** The `build-rich.log` failure was a stale SDK artifact (preview.26126 → preview.26156). Build now succeeds with 0 errors. All 20 tests pass. No code fix needed.

**State of play:** Cuts 1–2 delivered: app shell, three-zone UI, persistence, runtime abstractions, bootstrap implementation, ChatShellService bridge, and 20 tests. The app launches and completes a simulated chat loop with placeholder streaming. Everything works — it just looks like a prototype.

**Next cut proposed (Cut 3):** Markdown rendering via Markdig+WebView2, role-based message styling, Fluent Design theming, and ChatShellService test coverage. Rationale: SDK spike has external blockers; visual polish is fully app-side with no dependencies. Tim gets a demoable product before the AI backend lands.

**Decision written to:** `.squad/decisions/inbox/ripley-next-cut.md`

## Design Review: Cut 3 Parallel Contracts (2026-03-13)

**What I did:** Reviewed the full codebase to define minimum contracts for Cut 3 parallel execution. Read every service interface, view model, XAML page, and runtime adapter to identify collision points between Hicks (UI/rendering) and Newt (service tests).

**Key decisions:**
- `ChatMessageViewModel.Content` stays raw markdown; rendering is view-layer responsibility via `IMarkdownRenderer`
- Single `WebView2` per transcript (not per message) with JS-driven append for streaming performance
- Newt tests `ChatShellService` with real SQLite + mock runtime — no WinUI dependency in test project
- File ownership boundaries explicitly defined: Hicks owns `Views/` and renderer; Newt owns new `WorkIQC.App.Tests` project
- Shared files (`IChatShellService`, `ChatShellModels`, `MainPageViewModel`) are frozen — neither modifies them in this cut

**Parallel safety:** No file conflicts. Hicks adds `IMarkdownRenderer` to DI; Newt adds test project to solution. Different sections, clean merge.

**Decision written to:** `.squad/decisions/inbox/ripley-chat-loop-review.md`

## Team Updates (2026-03-14T00:42:05Z)

**Ripley (Assessment Complete):** Confirmed clean baseline (21/21 tests, 0 build errors). Locked Cut 3 scope and acceptance criteria:
- **Owners:** Hicks (WebView2 + Fluent theming), Vasquez (markdown pipeline integration), Newt (≥8 ChatShellService tests)
- **Acceptance:** Markdown with bold/code/lists/tables renders correctly; role-based styling distinguishes user/assistant; theme respects system light/dark; streaming indicator visible
- **Risk notes:** WebView2 DPI scaling on multi-monitor; keep HTML template simple; verify Markdig on net10.0
- **Sequence:** App-side work unblocked; SDK spike continues in parallel; demoable product ready before backend integration

**Bishop (Build Repair):** Linked full abstractions tree; XAML drift resolved; clean build confirmed.

**Vasquez (Readiness Seam):** Structured reports flowing through ChatShellService; explicit failure messages ready for UI error states.

**Newt (Verification):** Baseline locked; 21/21 passing; bootstrap idempotency regression covered.

**Decision recorded:** Cut 3 scope merged to decisions.md (Decision #19). Orchestration logs written. Session log summarizes recovery path and next cut. Inbox decisions deleted after merge.

**Unblocked:** Hicks/Vasquez can start Cut 3 immediately. Newt ready to add ChatShellService coverage. SDK spike continues independently.

**Awaiting:** Tim's clarifications on data retention, consent flow, self-contained requirement.

## Final Cut Definition (2026-03-14)

**What I did:** Assessed full codebase state, verified baseline (0 errors, 21/21 tests), and defined the final implementation cut to take the app from working prototype to demoable product.

**Key findings:**
- No markdown rendering exists — WebView2 NuGet referenced but unused; transcript is plain TextBlock
- ChatMessageViewModel hardcodes 9 brush constants that duplicate App.xaml theme resources
- Runtime session/streaming stubs all throw NotImplementedException (by design — SDK blocked)
- Fluent shell refresh was previously rejected; current MainPage has theme resources but basic composition
- No ChatShellService tests exist yet (was Cut 3 scope, never executed)

**Decision:** Four tracks defined with clear ownership, file boundaries, and acceptance criteria:
- **Track A (Hicks):** Markdig → WebView2 markdown pipeline — START NOW
- **Track B (Bishop):** Remove hardcoded brushes from ChatMessageViewModel — AFTER Track A
- **Track C (Newt):** ≥8 ChatShellService tests with real SQLite — START NOW
- **Track D (Vasquez):** Real Copilot SDK session/streaming — BLOCKED on SDK preview

**Reviewer bar locked:** Build pass, test pass, launch smoke, markdown functional check, theme check, no hardcoded colors in VM layer.

**Decision written to:** `.squad/decisions/inbox/ripley-final-cut.md`

**Pattern learned:** When a UI refresh gets rejected, the next pass must specify which file's composition needs to change (MainPage.xaml) vs. which is already acceptable (MainWindow.xaml Mica backdrop). Rejection guidance that says "refresh the actual page" without naming files leads to ambiguity.

## Team Updates (2026-03-14T05:49:00Z)

**Shell Chrome Reconciliation — ASSIGNED**

**Context:**
Bishop's shell chrome revision was rejected by Newt on rendered artifact evidence. The title-bar still shows a `Bootstrap ready` pill and the left rail still displays a large verbose settings card, contradicting the promised cleanup.

**Assignment:** Reconcile live shell UI with XAML source.

**Task:**
Verify the actual XAML changes in MainPage.xaml, reconcile the live shell with the source code, and either:
1. Confirm Bishop's changes are correct but the test artifact is stale (rebuild/relaunch shell)
2. Identify missing XAML changes and apply them
3. Verify Fluent aesthetic is realized in the running shell

**Acceptance criteria:**
- Rendered shell has no title-bar status pills
- Sidebar shows Settings link only (no verbose card)
- Fluent aesthetic visible in live launch
- Build succeeds (39/39+ tests pass)
- No regression in shell wiring

**Status:** In progress after second reviewer-mandated reassignment.

**Decision recorded:** Decision #15 — Shell UI Reconciliation, merged to decisions.md
Orchestration log written: 2026-03-14T05-49-00Z-ripley.md

## Readiness UX Analysis (2026-03-14)

**What I found:** Tim reported that after completing Copilot auth via `copilot login`, the check-readiness still says "Started" and feels ambiguous even though auth is actually done.

**Root cause:** Semantic mismatch between implementation state and UI label:
- Internal: `IsAuthenticationHandoffStarted = true` correctly tracks that the auth marker file exists (handoff succeeded)
- UI label: "Started" incorrectly suggests in-progress instead of complete
- Description after user acts: "Finish sign-in if terminal is waiting" contradicts what the user just completed

**Product contract verified:**
- `CopilotBootstrap.RecordAuthenticationHandoffAsync` writes a marker file, then calls `VerifyAuthenticationHandoffAsync`
- `AuthenticationHandoffReport.CanProceed` returns true when marker exists (status = Completed)
- `ChatShellService.GetBootstrapSummaryAsync` correctly only adds auth to blockers if `!auth.CanProceed`
- So the backend is correct—the UI labels are just confusing

**Three product-level fixes recommended (no implementation, decision only):**
1. **Change "Started" → "Completed"** in `AuthStepStatusText` when marker exists (MainPageViewModel.cs:185)
2. **Rewrite description** to acknowledge success: "Copilot authentication handoff is complete. Recheck readiness to confirm all setup is ready." (MainPageViewModel.cs:191–193)
3. **Verify setup card disappears** when auth + EULA are both done (already correct in service logic; UI labels were just masking it)

**Key principle:** State labels must reflect user intent ("Did my auth work?") not implementation details ("Does the marker file exist?").

**Decision written to:** `.squad/decisions/inbox/ripley-readiness-ux.md`
