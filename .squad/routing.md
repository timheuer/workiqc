# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Windows shell, packaging, local storage | Bishop | App bootstrap, installer strategy, native integration, persistence plumbing |
| Chat UX, markdown rendering, session navigation | Hicks | Chat screen, composer, transcript rendering, visual polish |
| Copilot SDK and WorkIQ integration | Vasquez | SDK wiring, tool invocation flow, WorkIQ-first orchestration |
| Testing and reviewer gates | Newt | Test plans, edge cases, regressions, verification |
| Scope, priorities, architecture, review | Ripley | Product trade-offs, technical shape, reviewer decisions |
| Session logging | Scribe | Automatic — never needs routing |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, evaluate @copilot fit, assign `squad:{member}` label | Lead |
| `squad:{name}` | Pick up issue and complete the work | Named member |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, Ripley triages it and assigns the right `squad:{member}` label.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the inbox for untriaged work.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. Ripley handles all `squad` triage.
8. **WorkIQ-first product work** — prefer Vasquez for SDK and WorkIQ tool-flow decisions, Hicks for end-user experience, and Bishop for Windows app shell choices.
