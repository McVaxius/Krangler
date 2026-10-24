# Krangler LCARS concepts

Approved LCARS design concepts. Implementation is selected for the repositories in Z:\myrepos.txt, with the rollout tracked in Z:\!todo\2026-10-22GOATFUCK0002.md. These images are design references, not runtime or test evidence.

- [Main window comparison](01-main-regular-compact.png)
- [Settings and supporting owners](02-settings-mini-and-auxiliary.png)
- [Exact source-checked prompts](PROMPTS.md)

The image links are the planned sheet filenames; root review publishes the final generated artwork. Regular/compact compare spacing at the same readable text size. A separate Mini, wizard, access loader or browser view stays a separate owner. Selected supporting views are shown at useful scale; other source windows and conditional states retain their existing layouts and actions, rather than each receiving an illegible thumbnail. Generated text and sample values are proposals, not test results.

## Source scope

Source commit: `aee2c896a8f374f2745a58c4550caf17f325a4b6`. Existing dirty paths were inspected by count (0); none are included or changed by this mockup task.

Racism is the existing in-game race/clan/gender page label; screenshot privacy and optional game-appearance changes remain distinct.

## Verified controls and owners

- Krangler/Windows/MainWindow.cs:27 — literal navigation; Names controls at 393.
- Krangler/Windows/MainWindow.cs:508 — editable Racism rules and Apply Rules/Discard Edits at 584.
- Krangler/Windows/MainWindow.cs:658 — Imaginary Fren enabled, display name, preset and Test Spawn/Despawn.
- Krangler/Windows/SetupWizardWindow.cs:144 — wizard choices; navigation at 198.

## Existing UI source inventory

- `Krangler/Windows/MainWindow.cs`
- `Krangler/Windows/SetupWizardWindow.cs`

## Shared appearance proposal

Auto / Picker / LCARS remain in that order, with LCARS third. Preserve native actions, IDs, saved scope and semantic status colours. Krangle obfuscates identifying text. Proposed shared UI tones default off; titlebar speaker and Settings use the same value, with no volume slider. Browser controls remain in the browser header; existing script-free owners do not gain unsupported timed fade. This appearance contract is approved for the allowlisted rollout; implementation and local verification remain separate from artwork approval.
