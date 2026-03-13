# Windows live-readiness handoff

## When to use

Use this pattern when a Windows desktop app depends on external tooling or authentication before the first real task can succeed.

## Pattern

1. Pin every app-owned external package reference in the bootstrap path.
2. Store app-owned first-run markers under `%LocalAppData%\<AppName>\.<feature>\`.
3. Separate UI state into:
   - app-owned setup markers (consent, auth handoff)
   - machine prerequisite checks (tool discovery, runtime dependencies)
4. Show both a launch-time prompt and a persistent setup surface until the handoff is complete.
5. Launch the real external auth path from the app (`copilot login`, browser, etc.) instead of faking success.
6. Keep fallback/product surfaces working while setup is incomplete, but report blockers explicitly.
7. Keep "handoff recorded" separate from "auth verified"; a marker file only proves the app launched the flow, not that the external login finished.
8. Make UI labels match the underlying readiness state exactly; do not map a completed auth report back to a `Started` badge just because the stored boolean is named around handoff.

## Good outputs

- Deterministic bootstrap config
- Visible blocker list
- App-owned marker files for consent/handoff
- Recheck action after external auth completes
- A debuggable proof path for auth verification (for example: marker timestamp plus external CLI log or credential evidence)
