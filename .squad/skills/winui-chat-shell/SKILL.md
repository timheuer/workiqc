---
name: "winui-chat-shell"
description: "Pattern for a WinUI chat shell that can ship with sample data before runtime orchestration is connected"
domain: "ui"
confidence: "high"
source: "hicks"
---

## Context

Use this when the WinUI app needs a credible chat shell before live runtime streaming is available.

## Patterns

### App-owned view model seam
- Keep the page bound to an app-layer `MainPageViewModel`.
- Let the view model hydrate recent conversations from persistence when available.
- Keep draft creation, send state, and placeholder streaming local until runtime wiring lands.

### DI-rooted shell composition
- Resolve the root page from `App.xaml.cs` through dependency injection instead of letting the page `new` its own view model.
- Hide persistence/runtime wiring behind an app-layer shell service so the page and XAML stay focused on UI state.
- If runtime contracts are ahead of the app target framework, keep the abstraction shape in the app and swap in local adapters until the real runtime can be referenced directly.

### Three-zone shell
- Sidebar: recent conversations and a clear new-chat action.
- Main pane: transcript list with role-aware rendering.
- Footer: sticky composer with disabled send while busy.
- Treat settings as a first-class destination in the main pane, not a dead-end footer control. Opening settings should preserve the selected thread so users can return to the same conversation without rebuilding context.
- Put destructive thread actions behind the shell service seam from the sidebar surface. Users need a visible delete affordance, but the page should still delegate deletion instead of talking to persistence directly.

### Fluent materials pass
- Keep `Mica` on the `Window` backdrop, but put the visible Fluent work on the hosted page.
- Use a softly tinted left rail plus rounded header/transcript/composer surfaces so the shell reads as materially Fluent at first glance.
- Prefer solid or semi-opaque surfaces for persistent layout chrome; keep Acrylic reserved for transient UI.
- Keep title-bar chrome nearly silent. If runtime or connection state matters, place it in shell content near the composer or transcript metadata rather than in caption-area pills.
- A good default is an in-pane conversation header: keep the drag region to identity only, then place runtime/setup state on the header's trailing edge where it stays visible without competing with window controls.
- Center the empty state inside the transcript surface with generous spacing so the app still feels intentional before any messages exist.
- Route conversation-item and message-bubble colors through theme-aware resources where possible; hard-coded light brushes are easy to miss during a bright-theme review and tend to become the first dark-theme regression.
- If view-models still expose `Brush` properties for transcript/sidebar state, resolve named app resources instead of literal colors and trigger a small refresh pass from `ActualThemeChanged` so runtime light/dark switches repaint without reworking the shell structure.
- Trim the sidebar ruthlessly: app identity, primary new-chat action, recent threads, and a plain Settings affordance are usually enough for a first desktop release.

### Markdown transcript seam
- Keep `Content` as raw markdown in the message view-model and persistence contracts.
- Host markdown rendering in a dedicated view control rather than storing rendered HTML on the view-model.
- Use Markdig for markdown-to-HTML conversion with HTML disabled, then render the result in WebView2 for tables, lists, headings, and fenced code.
- After each navigation, measure the document height in WebView2 and resize the host control so transcript bubbles grow naturally inside the `ListView`.
- Intercept external link navigation from WebView2 and hand it off to the OS so the transcript stays in-app.
- When a WebView-backed bubble starts measuring too narrowly, clamp the bubble against the live transcript width with a responsive min/max range instead of a single fixed max width; that keeps Fluent chat bubbles readable while still letting them shrink on narrower panes.
- If that clamp lives inside a `DataTemplate`, verify it affects the running list items. If the live shell still renders narrow columns, move the width application into page code (`ContainerContentChanging` + list `SizeChanged`) so each realized bubble gets an explicit width from the actual transcript pane.
- If the live shell still feels cramped after width propagation, widen the actual heuristic instead of polishing around it. A comfortable desktop transcript needed roughly half-pane minimums and high-80% maximums, plus explicit width propagation into the markdown host/WebView, before wrapping looked right in the running app.

### Custom title bar handoff
- If `ExtendsContentIntoTitleBar` is enabled, expose a dedicated page element such as `TitleBarDragRegion`.
- After injecting the page into the window, call `SetTitleBar(...)` against that element so custom shell layouts still drag and snap like a native desktop app.

### Safe placeholder behavior
- Seed sample conversations if persistence is empty.
- Simulate assistant streaming with lightweight placeholder text so the shell feels active.
- Keep placeholder logic easy to replace with runtime deltas later.

## Anti-Patterns
- Binding the page directly to persistence services.
- Leaving the composer dead when runtime is unavailable.
- Reworking the page structure once streaming arrives.
