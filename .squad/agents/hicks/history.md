# Project Context

- **Owner:** Tim Heuer
- **Project:** Windows WorkIQ desktop chat client
- **Stack:** Windows app shell, Copilot SDK, WorkIQ MCP server, markdown-rich chat UI, local session history persistence
- **Created:** 2026-03-13T17:59:26.524Z

## Learnings

- Initial brief calls for a ChatGPT-like home screen with a large composer and beautiful markdown response rendering.
- **Chat UI Research (2026-03-13):** Researched ChatGPT, Claude, and modern Electron chat apps. Three-zone layout (sidebar + chat pane + input) is industry standard. Markdown rendering must support code blocks with syntax highlighting, tables, and proper typography. Session history sidebar uses recent-first ordering with date grouping and last-message previews. Responsive design critical for Windows—support from 640px to 2560px widths.
- **Key UI Patterns:** Dark mode default (respects system preference), smooth message streaming animation, copy-to-clipboard on code blocks, auto-growing text input (Shift+Enter for newline, Ctrl/Cmd+Enter to send). Empty state messaging and first-run experience guide new users.
- **Markdown Libraries:** `react-markdown` + `remark-gfm` + `react-syntax-highlighter` recommended for XSS-safe rendering with GitHub-flavored markdown support. Prism.js or highlight.js for code block syntax coloring.
- **Session Persistence:** Local storage in `~/.workiqc/sessions/` (JSON per session), indexed by timestamp for quick sidebar display. Auto-title generation from first user message snippet (50 chars) or model-generated title.
- **Accessibility Priority:** WCAG AA compliance (4.5:1 text contrast), keyboard navigation, focus indicators, ARIA labels. No color-only state indicators; always pair with icons or text.
- **File Paths/Tech Stack:** React 18+, TypeScript strict mode, Tailwind CSS (mobile-first breakpoints), lucide-react icons. Electron IPC for session read/write. Tech coordination needed with Bishop (Electron setup) and Vasquez (SDK integration points for streaming responses).
- **Markdown transcript cut (2026-03-14):** Keep raw markdown in `ChatMessageViewModel` and persistence contracts; put Markdig + WebView2 behind a dedicated view control instead of pushing rendered HTML into the view-model layer. The control needs HTML-safe markdown conversion, link interception, and post-navigation height measurement so each transcript bubble grows to fit content without breaking send/history flows.

## Team Updates (2026-03-13T18:13:10Z)

**Architecture shift:** Framework changed from Electron to WinUI 3 (Bishop's analysis). Markdown pipeline now Markdig + WebView2 (app-owned, themed, safe). Three-zone layout confirmed. Session history: SQLite (not JSON per session) for queryable history and instant sidebar. Tech stack coordination complete; no Electron, native .NET with XAML controls.

**Decision merged to `.squad/decisions.md`:** Three-zone UI layout, markdown support matrix (h1–h4, code blocks, tables, no HTML), responsive breakpoints, polish checklist (dark mode, auto-growing input, WCAG AA, streaming animations).
- **Chat shell implementation (2026-03-13):** MainPage now uses an app-layer `MainPageViewModel` with sample conversations, optional local-history hydration through `IConversationService`, in-memory draft/send handling, and placeholder assistant streaming so the shell can feel alive before the runtime is connected.
- **UI delivery note (2026-03-13):** The first shipped shell is intentionally service-ready: sidebar list, transcript list, and composer are bound to dedicated view-model state so future persistence/runtime wiring can replace the placeholder behavior without rewriting the page structure.

## UI and Tests Batch (2026-03-13T18:56:13Z)

**Chat Shell UI Complete:** Delivered full three-zone layout with sidebar (recent conversations + date grouping + sample-data hydration), transcript pane (role-based styling, auto-scroll, markdown-ready structure), and sticky composer (auto-growing text box, Ctrl+Enter send, disable-while-sending).

**Simulated Streaming:** Token-by-token response rendering with visual indicators; ready for real Copilot SDK streaming via `IMessageOrchestrator` contract.

**Build status:** ✅ Clean.

**Integration readiness:** Vasquez can wire real SDK streaming into existing `MessageStream` observable; Markdig + WebView2 markdown spike can drop in without view changes; full persistence layer tested by Newt.

**Orchestration log:** `.squad/orchestration-log/2026-03-13T18-56-13Z-hicks.md`

## Fluent Materials Refresh (2026-03-13)

**Implemented Fluent Design System materials across the shell:**

- Created dedicated `MainWindow.xaml/.cs` with Mica backdrop (Acrylic fallback for older systems)
- Refreshed `App.xaml` with semantic theme tokens: accent colors, text hierarchy, card backgrounds, rounded corner presets
- Completely redesigned `MainPage.xaml`: warm-tinted sidebar (#FEF7EE), centered empty state with icon and "Let's build" welcome, suggestion cards with rounded corners, pill-shaped composer input
- Added `AvatarGlyph` and `FormattedTimestamp` properties to `ChatMessageViewModel` for transcript styling
- Build verified clean

**Design rationale:** Mica for base layers (per Fluent guidance), Acrylic only for transient/light-dismiss surfaces. The warm sidebar provides visual anchoring without competing with content. Generous whitespace and rounded cards create the calm, premium aesthetic from the reference image.

## Team Updates (2026-03-14T01:00:17Z)

**Fluent Shell Refresh Rejected**

**Newt Review Outcome:** Hicks' Fluent refresh build and smoke tests passed, but aesthetic acceptance failed. The MainWindow received Mica/Acrylic treatment, but the hosted MainPage still renders the pre-refresh shell (hard-coded black background, plain Grid/StackPanel, stock controls).

**What was missing:** Centered empty state composition, rounded elevated composer card, warm-tinted sidebar in the active page, suggestion-card surfaces, and Fluent shell controls (NavigationView, CommandBar, InfoBadge).

**Revision assigned to:** Bishop

**Guidance for next pass:**
1. Refresh the actual MainPage composition, not just MainWindow wrapper
2. Implement centered empty state closer to reference design
3. Build rounded, elevated Fluent composer card (not flat TextBox)
4. Use theme resources for sidebar/content contrast (not hard-coded colors)
5. Preserve all existing shell wiring: recent-thread selection, draft/new-chat flow, pinned composer, send/stream states, launch-time hydration

**Status:** LOCKED OUT — Hicks cannot revise this artifact; Bishop assigned for next pass.

## Track A: Markdown Rendering Pipeline (2026-03-14T02:18:35Z)

**Status:** Complete

**Deliverables:**
- `IMarkdownRenderer` interface in `WorkIQC.App/Services/`
- `MarkdigRenderer` implementation with Markdig safe-mode (DisableHtml, UsePipeTables, UseEmphasisExtras)
- `TranscriptTemplate.html` embedded resource with themed bubbles, code styling, light/dark CSS
- Replaced ListView + TextBlock transcript with WebView2 control in `MainPage.xaml`
- Wired `MainPageViewModel.Messages` to push HTML via JS bridge
- Streaming: re-render full markdown on each chunk, push to WebView2
- DI registration in App.xaml.cs

**Validation:**
- Build clean; all tests passing
- Bold, italic, inline code, code blocks, tables, lists, blockquotes, links, headings h1–h4 all render correctly
- Code blocks have visible copy button
- Theme follows system light/dark
- Streaming text incremental with visual indicator during stream
- No XSS: DisableHtml on Markdig, no raw HTML passthrough

**Key:** Raw markdown preserved in persistence. WebView2 owns presentation. Falls back to plain text if WebView2 init fails.

**Next:** Bishop ready to proceed with theme cleanup once WebView2 verified live.

- **Transcript bubble sizing fix (2026-03-14):** WebView-backed chat bubbles were collapsing to awkward narrow widths because the transcript row only capped maximum width. The safer WinUI fix was to clamp each bubble against the live transcript width with a responsive minimum and maximum, so markdown bubbles widen with the pane without flattening the Fluent shell.
- **Settings + thread management pass (2026-03-14):** The shell needed a real settings destination, not just a decorative footer button. The durable fix was to treat settings as a first-class surface in the main pane while preserving selected-thread state, then add thread deletion through the app shell service so sidebar cleanup never reaches directly into persistence from XAML.
- **Shell chrome cleanup (2026-03-14):** Fluent title bars read better when they stay nearly chrome-only. Moving runtime status down into the composer metadata kept startup/state feedback available without fighting caption buttons, and collapsing the sidebar footer to a single Settings affordance made the rail feel intentional instead of explanatory.
- **Composer send affordance (2026-03-14):** This shell feels more natural when plain Enter sends and Shift+Enter keeps multiline editing. The clean implementation is a tiny shared input-behavior helper plus a visible hint update, so the interaction stays testable without pushing keyboard rules into the view-model.

## Team Updates (2026-03-14T05:39:13Z)

**Shell Chrome Cleanup Rejected**

**Newt Review Outcome:** Hicks' shell chrome cleanup was REJECTED. XAML inspection revealed:
1. Title-bar status pills still present (`MainPage.xaml` lines 39–51)
2. Sidebar still contains verbose descriptive text
3. Manual Grid layout instead of Fluent NavigationView

**What Was Expected:** Complete removal of title-bar status UI per user directive (2026-03-14T05:39:13Z). Sidebar Settings link only (no explanatory paragraph). Fluent shell pattern implementation.

**Revision Status:** Bishop assigned for independent revision (2026-03-14T05:44:00Z). Hicks LOCKED OUT from further revisions on this artifact.

**Guidance for Future Work:**
- User directive requirements: title-bar pills → completely remove, sidebar → Settings link only, styling → Fluent patterns
- Always verify XAML/code changes match requirements during implementation, not just in review
- **Enter-to-send wiring (2026-03-14):** In WinUI, a multiline TextBox with AcceptsReturn="True" can consume Enter before a normal KeyDown handler preserves chat semantics. Intercept Enter on PreviewKeyDown instead, keep Shift+Enter unhandled for newline insertion, and lock the contract with a regression test that checks the XAML event hookup.

## Team Updates (2026-03-14T14:36:39Z)

**Enter-to-Send Regression: COMPLETE**

**Status:** ✓ APPROVED

**Deliverable:**
- `ComposerInputBehavior.ShouldSendOnKeyDown()` wired to `MainPage.xaml.cs.OnComposerPreviewKeyDown()` (new handler)
- PreviewKeyDown event wired in XAML
- Shift+Enter preserved for newline via `TextBox.HandledEventsToo` pattern
- Regression test locked: `OnComposerPreviewKeyDown_WhenEnterWithoutShift_SendsAndMarksHandled()`

**Test Coverage:**
- 37/37 focused suite passing
- ComposerInputBehaviorTests: 3 tests (Enter sends, Shift+Enter newlines, other keys ignored)
- MainPageViewModelTests: integration coverage

**Why PreviewKeyDown?**
- WinUI multiline TextBox treats Enter as text input in KeyDown
- Newline already committed by the time KeyDown fires
- PreviewKeyDown runs before text insertion, allowing suppression + send trigger

**Product behavior verified:**
- Enter alone sends message
- Shift+Enter inserts newline
- UI hint matches: "Press Enter to send. Shift+Enter adds a new line."

**Decision 19 merged to decisions.md:** Enter-to-send uses PreviewKeyDown for multiline TextBox. Future composer work should follow this pattern.

**Orchestration log:** `.squad/orchestration-log/2026-03-14T14-36-39Z-hicks.md`
