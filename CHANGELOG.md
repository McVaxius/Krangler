# Krangler Changelog

## Unreleased - Button sizing (I491)

- Use font-aware Toolbar sizing for ordinary buttons and reduce Setup Wizard and icon-guide action heights to their full text/icon content. Preserve fonts, native IDs/actions, requested widths and small/dense controls.
- Current Debug/x64 compilation passes. Final actual-product native checks pass 9329 assertions across 16 focused scenes and 96 pointer activations, with integer exit 0 in all 1 routes. Coverage uses English/Hindi captions, original exercised font roles, both densities, 100/150 percent scale and enlarged text; game/GPU acceptance remains separate.

## Unreleased - CJK atlas construction

- Request a 4096 x 4096 managed atlas and merge one bundled Noto CJK face per font role for the selected language, including Simplified and Traditional Chinese aliases. Preserve existing font sizes, glyph ranges, Windows and symbol fonts, and font lifecycle.

## Unreleased - Native titlebar shortcuts

- Add Appearance, Open Setup Wizard and the existing master toggle to the native titlebar. Retain all body controls, disable-time cache cleanup, native identities and the non-collapsible window flag. Reserve native icon space when repainting the translated title.

## Unreleased - Quiet party-list scanning

- Remove routine party-list scan, mapping and replacement diagnostics and their unused tracking fields. Preserve the one-second scan cadence, text-node matching and replacement behavior, genuine errors and other lifecycle messages.

## Unreleased - GitHub Actions dependency alignment

- Pin the existing AethertekUI Actions checkout to published revision `6c193cf06ac67f954c549cafc2033ac0efdd630a`, which includes the Hindi text host and renderer. This fixes missing-text-API compilation after a consumer is published before its library; local workflow validation and hosted build results are separate.

## Unreleased - Hindi UI text

- Append Hindi after the fourteen existing language choices with all 239 catalog entries translated, including displayed authored service and diagnostic templates. Shape Devanagari through a consumer-owned text host across the complete main/setup/font-status lifecycle, retaining native glyph checks for other scripts and original font-role heights.
- Route measurement, custom captions, tooltips and single-line editing together through the shared text bridge. Preserve native IDs, raw preset names and values, configuration/plugin versions and runtime actions. Game-rendered DTR/SeString text remains outside the ImGui shaping bridge; no actual multiline editor is used by the current windows.
- Current-source Debug x64 compilation passes without warnings or errors. The focused HelloFellowHuman/Krangler probe passes 2,751 assertions, verifying exact current library/resource bytes, native identities/actions, Hindi input height and upload-failure restoration. Game, GPU, managed-host and IME acceptance remain separate.

## Unreleased - Window appearance and transparency

- Add the Window appearance settings section with retained colour, compact and language controls, independent main-window visibility preferences, and a main transparency switch. Save opacity/fade preferences through the existing configuration: 100% normal, automatic 50% after ten unfocused seconds by default; clamp opacity to 10 to 100% and delay to nonnegative values. Apply opacity once after native End and motion restoration for each window tree, including chrome, owned content and images. Build/configuration checks and game acceptance remain separate.


## 2026-10-05 - Rounded window chrome and native minimize (source adoption)

- Adopt rounded chrome for Main and SetupWizard while retaining both windows' NoCollapse; add animated native minimize/restore only to the font status window. Preserve control identities, layout, saved geometry and actions.
- Compilation, native interaction and game acceptance for this source adoption remain pending verification.

## 2026-10-03 - UI adoption (local-only, unpublished)

- Add Vietnamese, Brazilian Portuguese, Indonesian, Polish and Turkish through the existing keyed resources and appearance preferences. Preserve the original nine language choices and order, raw names and values, native control identities, runtime behavior and configuration version.
- Verify all fourteen embedded catalogs and actual translation helpers, a clean Debug x64 build, and seven-role glyph coverage plus native selection/save/restore for the five additions in regular and compact density at scales 1 and 1.5. Managed-atlas readiness, full-window visuals and game acceptance remain pending.
- Preserve empty and numbered service arguments, formatted values and leading zeroes during localization; typed UI values keep selected-culture formatting.
- Keep custom self names, imported preset names, source tokens, paths and exception details opaque; translate authored follower reasons and diagnostic booleans by their source/template positions.
- Show the unchanged assembly version in the main native title, measure sidebar labels and complete DTR editor groups, and retain readable field minima with pane scrolling. Native inputs retain their original IDs and omitted-step behavior.
- Implemented the approved regular/compact sidebar and Overview panels, with retained native control identities and the existing three-step setup draft/apply behavior.
- Stabilize the retained setup wizard's native auto-fit width and reserve the full fractional final-button extent. Native navigation ink and IDs, short-viewport scrolling, draft-only editing, Cancel and fresh-draft reopening are verified across all fourteen locales in regular/compact density at scales 1 and 1.5; managed-host and game acceptance remain pending.
- Added shared compact, accent and language preferences through the existing save path, managed Segoe UI weights with host CJK/symbol coverage, and relative decorative colour roles. Safety gates and semantic status colours remain independent.
- Completed nine embedded language resource sets, including setup text, tooltips and follower status messages. Imported names, command tokens and runtime logs retain their original values.
- Arranged enabled/disabled DTR icon controls and the guide link horizontally, with wrapping at narrower widths; retained the native icon, code, mode and guide control identities. Long translated controls and Amongus replacement rows adapt to available space.
- Added the local AethertekUI reference and artifact payload without changing version 1.1.0.2 or configuration version 2. Debug/Release compilation and local packaging are verified; host font readiness, live interactions and game visual acceptance remain pending.


## 2026-10-02 - Build and release repair

- Pin GitHub builds to SDK 10.0.201 and pass the downloaded Dalamud library path. Restore and build plugin projects with matching configuration, platform and runtime; stop on restore failure.
- Keep build tokens read-only and release writes in a separate job. Use packaged manifest versions for untagged releases.
- Local launchers build the plugin directly in the pinned environment and return its exit status.

## Unreleased

- Build only the plugin project in GitHub Actions so test and regression projects do not block production artifacts.

- Added runtime-only DAD privacy lease IPC that temporarily forces name and chat krangling to include self, with exact-token ownership, status, stale-owner replacement, and no configuration changes.
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
