---
name: "workiq-desktop-architecture"
description: "Pattern for a WorkIQ-first desktop chat app using Copilot SDK, explicit MCP wiring, and app-owned chat history"
domain: "architecture"
confidence: "high"
source: "vasquez"
---

## Context

Use this pattern when designing a Windows desktop app that behaves like a ChatGPT-style front-end for WorkIQ, with the Copilot SDK handling agent orchestration.

## Patterns

### WorkIQ-first tool posture

- Treat WorkIQ as the default and usually only tool path in v1.
- Do not rely on the Copilot SDK default tool set; explicitly restrict the allowed tools to the WorkIQ MCP tool.
- Encode that expectation in both session config and system guidance so debugging is straightforward.

### App-owned workspace for MCP wiring

- Create an app-owned working directory under `%LocalAppData%` and place `.copilot\mcp-config.json` there.
- Launch the Copilot SDK with its working directory set to that workspace so MCP discovery is deterministic.
- Prefer a pinned WorkIQ package/runtime over floating `latest` dependencies.

### Readiness-first runtime seam

- Make bootstrap do the safe work now: resolve workspace paths, generate `mcp-config.json`, and probe prerequisite executables.
- Report Copilot/WorkIQ readiness explicitly with dependency + capability objects instead of implying the runtime is ready.
- If live SDK session or streaming behavior is not wired yet, fail with typed runtime exceptions or structured failure state instead of `NotImplementedException` placeholders.
- Feed those readiness reports back into the shell UI as human-readable status text so startup and send fallback paths explain *why* the runtime is unavailable.
- Keep the app linked to the entire shared abstraction surface rather than cherry-picking files; otherwise UI code drifts from the runtime contract and build breaks hide the real integration seam.

### Two-layer persistence

- Let the Copilot SDK keep its own session state, but never depend on that alone for product UX.
- Persist app-owned conversation history separately with:
  - local conversation ID
  - Copilot session ID
  - title / preview / timestamps
  - final markdown transcript
- Restore from app storage first, then attempt SDK session resume.

### Streaming event pipeline

- Map SDK events directly into UI state:
  - user message saved immediately
  - assistant deltas shown live
  - tool-start/tool-complete mapped to status text
  - final assistant message committed on idle
- Keep this pipeline append-friendly so crashes do not erase the current turn.

### Markdown rendering contract

- The model returns markdown.
- The app owns markdown-to-safe-HTML conversion.
- The UI renders the sanitized result in a themed surface such as WebView2.
- Never trust raw model HTML for transcript rendering.

## Recommended Stack

- WinUI 3 / Windows App SDK
- GitHub Copilot .NET SDK
- WorkIQ MCP server via app-managed MCP config
- SQLite for chat history
- Markdig + themed WebView2 for markdown display

## Anti-Patterns

- **Relying on SDK defaults** — Makes WorkIQ usage implicit and hard to debug.
- **Using SDK session state as the only history source** — Sidebar/history UX becomes brittle.
- **Floating tool versions in production** — Preview dependencies will eventually break you.
- **Rendering unsanitized model HTML** — Security and consistency problems show up fast.
