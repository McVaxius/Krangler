# Krangler Changelog

## Unreleased

- Added a first-run, reopenable three-step setup wizard for core privacy, self-display, and DTR choices.
- Added configuration v2 and the 32-row `Racism` tab for exact race/clan/gender Hide or Replace rules, including deterministic sanitation and self opt-out.
- Added local actor/nameplate suppression with owned restoration, plus rule-aware nameplate, party-list, target, focus-target, and identity-last appearance replacement hooks.
- Rewrote the README, manifests, and Aethertek copy around Krangler's current local-only features and limitations; Glamourer is no longer listed as a requirement.

## v0.0.0.1 — 2026-03-23

### Initial Release
- **Plugin structure**: Full Dalamud plugin with csproj, solution, manifest, icon
- **Krangle Names**: Nameplate text replacement for all visible player characters using exercise-word randomization
- **DTR Bar**: Click-to-toggle enable/disable with text/icon/icon+text modes
- **Main Window**: Full UI with master toggle, per-feature checkboxes, Glamourer status display
- **Ko-fi integration**: Donation button in upper right of main window
- **KrangleService**: Stable hash-based name randomization with caching (from VERMAXION pattern)
- **AppearanceService**: Race/gender/appearance randomization data generation (stub for Phase 2)
- **GlamourerIPC**: Glamourer availability detection with 5-second cache (stub for Phase 2)
- **Commands**: `/krangler` to open UI, `/kr [on|off]` to toggle
- **Configuration**: Persistent settings for all toggles and DTR bar options
- **Documentation**: README, how-to-import-plugins.md, project plan, knowledge base
