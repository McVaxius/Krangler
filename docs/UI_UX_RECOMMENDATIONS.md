# Krangler UI/UX Recommendations

**Review date:** 2026-08-18  
**Scope:** UI code review only; no runtime behaviour or implementation changes are included in this document.

## Product goal

Protect screenshot privacy through local name/chat/appearance changes while making scope, reversibility, presets, and experimental features understandable.

## Reviewed surfaces

- `Krangler/Windows/MainWindow.cs`
- `Krangler/Windows/SetupWizardWindow.cs`

## What is already working

- The setup wizard covers broad privacy features, self handling, DTR, and a review step.
- Names, appearance, presets, follower, Soul Thief, and debug functions are separated into tabs.
- Tooltips consistently explain local transformations and DTR behaviour.

## Prioritized recommendations

| Priority | Recommendation | Rationale and completion signal |
| --- | --- | --- |
| P0 | Rename the `Racism` tab to `Identity Rules` or `Character Rules`. | The current label is ambiguous, distracts from the feature, and does not describe exact race/clan/gender matching and replacement. |
| P0 | Keep a persistent local-only and reversible banner. | State what surfaces are changed, confirm that other players do not see changes, and provide a one-click Restore original appearance/names action. |
| P0 | Add a live preview of effective scope. | Show Self, party, nearby players, chat, and non-player limitations with enabled/disabled badges as toggles and presets change. |
| P1 | Clarify toggle dependencies. | Indent or group self name, race, gender, appearance, chat, and DTR sub-options under their parents and explain why disabled controls are unavailable. |
| P1 | Make preset application explicit. | Distinguish selected preset, active/effective preset, unsaved edits, random choice, and `Use global`; preview affected surfaces before Apply. |
| P1 | Group niche features under Advanced/Experimental. | Imaginary Fren, Soul Thief, Amongus, detailed identity replacement, and Debug should not compete with core screenshot-privacy controls. |
| P2 | Improve wizard review with examples. | Show a sample before/after name and appearance scope, plus the exact features left untouched by Finish. |

## Suggested information hierarchy

1. Privacy state and Restore
2. Core names/chat/appearance
3. Effective-scope preview
4. Presets and rules
5. Advanced local effects

## Validation checklist

- A new user can identify the primary action and current blocker within five seconds.
- Every disabled control has a nearby plain-language reason and, when possible, a direct corrective action.
- Healthy, warning, error, running, and disabled states remain distinguishable without colour.
- The UI remains usable at narrow window widths and common Dalamud UI scales without clipped labels or unreachable controls.
- Destructive, global, or high-impact actions identify their scope and require confirmation or provide a safe undo.
- Empty, loading, stale-data, success, partial-success, and failure states each provide an appropriate next action.
- Settings clearly identify whether they apply globally, per account, per character, per preset, or only for the current session.
- Advanced diagnostics are still reachable but do not compete with the everyday workflow.

## Recommended implementation order

1. Implement P0 items and validate the primary workflow plus blocker recovery.
2. Implement P1 information-architecture and configuration improvements.
3. Apply P2 polish, then test at multiple UI scales with both fresh and mature configurations.
