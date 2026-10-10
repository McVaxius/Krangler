# Krangler

Krangler is a local-only Dalamud plugin for screenshot privacy and appearance changes in FINAL FANTASY XIV. It can garble chat, pseudonymize visible player names, randomize or preset appearances, hide or replace exact race/clan/gender combinations, capture loaded looks as presets, and spawn an optional local follower.

Krangler does not send appearance or identity changes to other players. It has no Mare integration, network synchronization, or privacy guarantee for information shown by systems it does not modify.

## Quick start

1. Open Krangler with `/kr`.
2. On a first install, use the three-step setup wizard to choose core privacy, self-display, and DTR options.
3. Reopen the wizard at any time from **Overview**, `/kr wizard`, or `/kr setup`.
4. Use the sidebar for advanced settings and exact identity rules.

Closing or cancelling the wizard discards its draft. Finishing applies only the settings shown in the wizard; presets, Soul Thief, Amongus, Imaginary Fren, advanced appearance settings, and Racism rules are preserved.

## Features and sections

- **Overview** — current status, loaded preset count, Soul Thief capture summary, setup-wizard shortcut, and DTR display choices.
- **Names** — deterministic exercise-themed pseudonyms, optional self exemption and self display name, plus independently controlled chat garbling.
- **Appearance** — local race, clan, gender, hair, face, eyes, and other supported customize-field randomization.
- **Racism** — a fixed 32-row race/clan/gender table. Each exact source combination can be inactive, hidden, or replaced with a selected clan and gender. Rows are edited as a draft and saved only with **Apply Rules**.
- **Presets** — Super Krangle preset selection and exact Amongus NPC replacement rules. Bundled presets are copied without overwriting user files, and Glamourer-style JSON presets can be imported into the plugin configuration's `data/presets` folder.
- **Imaginary Fren** — an optional local-only follower with a configurable name and preset.
- **Soul Thief** — captures supported looks from locally loaded players, NPCs, or chocobos as reusable preset JSON.
- **Debug** — event, redraw, placement, and diagnostic controls for troubleshooting.

The DTR entry can show text, icon plus text, or icon only. Clicking it toggles Krangler's master state.

The main header exposes compact mode (**C**) and language; **Appearance** also provides the theme colour swatch and these preferences. Compact mode reduces spacing across the main window and setup wizard while keeping the same controls. The interface supports English, German, French, Spanish, Italian, Russian, Japanese, Korean, Simplified Chinese, Vietnamese, Brazilian Portuguese, Indonesian, Polish, Turkish, and Hindi; imported preset names, player names, commands, and logs keep their original text.

Choose teal, blue, pink, or a custom RGB colour to recolour the decorative theme. Active and warning states keep their meaning. These preferences use Krangler's existing configuration save path and preserve existing gameplay settings. Narrower windows wrap the DTR icon groups and replacement controls as needed.

**Transparency** applies to the complete plugin window, including its titlebar and popups. Appearance provides normal opacity, automatic focus fade, faded opacity and delay; defaults are 100%, fading to 50% after 10 seconds without focus and restoring on focus. Compact and language controls can be hidden independently on Main while remaining available in Appearance.

The titlebar opens Appearance or Guided Setup and toggles Krangler's master state. The packaged icon appears beside Main branding and in its titlebar. Appearance changes and name privacy continue to act locally; the setup wizard applies its reviewed draft when finished.

Hindi is enabled only when the local font check passes. Otherwise the selector shows disabled **Hindi (unavailable)** while other languages remain usable. A saved Hindi choice that fails its required-font check shows an English status and **Use English**; that button explicitly saves English. Font failures never change the saved language automatically.

## Exact identity rules

Identity rules match a loaded player's original, pre-Krangler race, clan, and gender as one exact combination.

- **Hide** disables the matching 3D actor and in-world nameplate locally. Krangler restores actors it hid when a rule changes, the Racism tab is disabled, Krangler is disabled or unloaded, the territory changes, or the actor is replaced.
- **Replace** pseudonymizes the matching player's supported name surfaces and applies the selected clan-derived race and gender after other enabled appearance and preset work.
- **Do Not Krangle Self** also exempts the local player from Hide and Replace rules.
- Chat remains controlled only by **Krangle Chat**.

Rule-only name replacement affects classifiable, locally loaded players. An unloaded party member is left unchanged rather than guessed from a name alone.

## Supported name surfaces

Krangler currently updates:

- in-world player nameplates;
- the standard party list and `PartyMemberList` UI;
- target and target-of-target names;
- focus-target names;
- chat sender/message text when **Krangle Chat** is enabled.

Player Search and Friends List entries are not currently rewritten. Other addon windows or third-party overlays may also retain original names.

## Commands

| Command | Description |
| --- | --- |
| `/krangler` or `/kr` | Open the main Krangler window. |
| `/kr on` | Enable Krangler. |
| `/kr off` | Disable Krangler. |
| `/kr wizard` or `/kr setup` | Open a fresh setup-wizard draft. |
| `/kr debug` | Toggle debug controls. |
| `/kr fren` | Print Imaginary Fren status. |
| `/kr ws` | Reset the main window position. |
| `/kr j` | Move the main window to a random visible location. |

## Installation

Add the following custom repository URL in Dalamud, then install Krangler from the plugin installer:

```text
https://aethertek.io/x.json
```

See [how-to-import-plugins.md](how-to-import-plugins.md) for detailed import steps. Krangler requires XIVLauncher with Dalamud; Glamourer is not required.

## Support

[Support development on Ko-fi](https://ko-fi.com/mcvaxius)

More XA and Dhog plugins and guides are available at [aethertek.io](https://aethertek.io/).

## License

AGPL-3.0-or-later

## Support logs

Use **Copy / ZIP Dalamud log** in Debug to create a local ZIP and open its folder. At 100 MiB or above, the first click warns that logging may have stopped and recent activity may be missing; click **Export capped log anyway** only if you still want that snapshot. Share the ZIP manually and remove exports when no longer needed. **Open Export Folder** reopens the completed export’s folder.

When XA Slave is loaded, **Open XA Slave log tools** opens its **Utility > XA Mods** panel, which contains Dalamud Log Cleaner. The existing **Copy / ZIP Dalamud log** action remains separate. Opening the panel does not run cleanup or change XA Slave settings.
