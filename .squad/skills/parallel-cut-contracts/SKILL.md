---
name: "parallel-cut-contracts"
description: "Pattern for defining file-ownership boundaries so multiple specialists can execute a cut in parallel without merge conflicts"
domain: "coordination"
confidence: "high"
source: "ripley"
---

## Context

Use this when a cut has multiple work streams (e.g., UI rendering + service tests) that must execute in parallel without blocking each other or creating merge conflicts.

## Patterns

### Explicit file ownership tables

- List every file each specialist creates or modifies
- List every file each specialist must NOT touch
- If two specialists touch the same file, prove they modify different sections (e.g., different DI registrations, different solution sections)

### Shared-contract freeze

- Identify interfaces and DTOs that both streams depend on
- Declare them frozen for the cut — no modifications allowed
- If a change is needed, it becomes a prerequisite task that runs before the parallel tracks start

### Content-model boundary

- Define where raw data lives vs. rendered output
- Example: view models hold raw markdown; view layer owns rendering to HTML
- This prevents tight coupling between service tests and UI implementation

### Test isolation from UI

- Service-layer tests use real persistence + mock runtime — never depend on UI framework assemblies
- Test projects override `UseWinUI`, `RuntimeIdentifiers`, and `SelfContained` to run as plain test assemblies
- Mocks mirror the exact behavior of local adapter stubs (same exceptions, same error codes)

## Anti-Patterns

- Two specialists modifying the same file in the same section without coordination
- Leaving ownership ambiguous ("whoever gets there first")
- Service tests that import WinUI controls or WebView2
- Changing a shared interface mid-cut without re-baselining both tracks
