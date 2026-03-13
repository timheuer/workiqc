---
name: "runtime-flow-audit"
description: "Audit whether a desktop chat app is using local persistence correctly versus leaking tool/runtime behavior into the user-visible transcript"
domain: "runtime-integration"
confidence: "high"
source: "vasquez"
---

## Context

Use this when a chat shell mixes local history persistence with a live model/tool runtime and a user reports that the app is "using the database first" or is showing tool internals in the reply.

## Patterns

### Separate persistence flow from answer flow
- Trace sidebar/transcript restore separately from send/stream orchestration.
- Confirm whether local storage is only saving history/session IDs or is actually shaping the answer path.

### Verify the live workspace config, not repo examples
- Check the app-owned runtime workspace (for this project, `%LocalAppData%\WorkIQC\.copilot\mcp-config.json`).
- Repo sample configs can be misleading during an audit.

### Treat tool-trace leakage as a product bug
- Internal schema checks, cache lookups, or tool argument payloads should never appear in the assistant bubble.
- Even if the runtime uses those steps internally, the user-facing contract is still "answer the question directly."

### Audit the transcript boundary
- Find the code that forwards assistant deltas into the UI and persistence store.
- If it blindly streams every delta, that boundary is where raw tool/planning artifacts will leak.

### Regression-test the happy path
- Add tests that assert a people/org question yields a normal assistant answer stream and does not persist tool-call syntax or schema chatter.

### Block workplace prompts instead of fabricating fallbacks
- If a prompt is clearly asking for workplace/org data, do not reuse the generic placeholder/sample fallback path.
- Route it to the live WorkIQ runtime when ready; otherwise return an explicit blocking error that says no local/org fallback was used.
- Mirror that state in the UI status text so the shell never claims it is streaming a placeholder when it is really surfacing a runtime blocker.
