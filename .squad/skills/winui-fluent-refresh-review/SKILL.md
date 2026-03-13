# WinUI Fluent refresh review

## When to use

Use this when a WinUI 3 shell is said to have received a Fluent or "modern materials" refresh and you need to verify whether the user-facing composition actually changed.

## Steps

1. Verify the active launch path (`App.OnLaunched`) to see which `Window` and `Page` are really shown.
2. Inspect both layers:
   - window wrapper for backdrop/title-bar work (`Mica`, `DesktopAcrylic`, custom title bar)
   - hosted page for the visible shell structure, spacing, controls, and theme resources
3. Compare the visible page against the requested direction:
   - pane contrast and materials
   - empty-state hierarchy
   - composer prominence and rounded treatment
   - suggestion cards / affordances
4. Run baseline safety checks:
   - `dotnet build`
   - `dotnet test`
   - smoke-launch the unpackaged exe long enough to prove startup still works
5. Cross-check any screenshot or live artifact against the source/tests. If the visual evidence still shows title-bar status chrome or a verbose sidebar/settings treatment, treat that as authoritative evidence of a miss until the rendered shell and code agree.
6. Reject the artifact if materials exist only in the wrapper window while the active page remains flat or pre-refresh.

## Common false positive

- A Mica/Acrylic `MainWindow` can make the codebase look refreshed even when the launched `Page` still uses hard-coded colors and default controls. The user experiences the page, not the constructor comment.
- Static review gates can pass after markup changes while the captured shell still shows stale chrome from the real launch path. Do not approve on code evidence alone when the live artifact contradicts it.
