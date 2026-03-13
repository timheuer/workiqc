# WinUI chat composer input

## Intent
Keep chat composer keyboard behavior aligned with common chat expectations in WinUI: Enter sends, Shift+Enter inserts a newline.

## Pattern
- If the composer must stay multiline, keep AcceptsReturn="True" on the TextBox.
- Do **not** rely on the normal KeyDown event for Enter-to-send.
- Intercept Enter on PreviewKeyDown, mark it handled, and call the send path.
- Leave Shift+Enter unhandled so the TextBox inserts a newline naturally.
- Keep the keyboard rule in a tiny helper so behavior tests stay cheap.
- Add a regression test that verifies the XAML is wired to PreviewKeyDown while multiline editing remains enabled.

## Why
WinUI multiline TextBox controls can consume Enter as text input before regular KeyDown-based chat logic runs. Preview-stage interception preserves chat semantics without sacrificing multiline drafting.
