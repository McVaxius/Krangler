# How to Import and Use Krangler Plugin

This guide will walk you through installing and using the Krangler plugin for FFXIV.

---

## Prerequisites

### 1. XIVLauncher & Dalamud
- **XIVLauncher** must be installed and configured
- **Dalamud** must be enabled in XIVLauncher settings
- You must have launched FFXIV through XIVLauncher at least once

**Download XIVLauncher:** https://github.com/goatcorp/FFXIVQuickLauncher/releases

### 2. Optional: Glamourer
- **Glamourer** is required for gender, race, and appearance krangling
- Name krangling works without Glamourer
- Install Glamourer from its custom repository if you want full functionality

---

## Installation: Dev Plugin (During Development)

1. **Build the Plugin:**
   - Open `Krangler.sln` in Visual Studio 2022 or Rider
   - Build the solution (Build → Build Solution or Ctrl+Shift+B)
   - The plugin DLL will be in: `Krangler/bin/x64/Debug/Krangler.dll`

2. **Add to Dalamud Dev Plugins:**
   - Launch FFXIV with XIVLauncher
   - In-game, type `/xlsettings` in chat
   - Go to the **Experimental** tab
   - Under "Dev Plugin Locations", click the **+** button
   - Paste the full path to the `Krangler.dll` file
     - Example: `Z:\Krangler\Krangler\bin\x64\Debug\Krangler.dll`
   - Click **Save and Close**

3. **Enable the Plugin:**
   - Type `/xlplugins` in chat
   - Go to **Dev Tools → Installed Dev Plugins**
   - Find **Krangler** in the list
   - Click the checkbox to enable it

4. **Verify Installation:**
   - Type `/krangler` in chat
   - The Krangler window should open

---

## Installation: Custom Repository (Future)

> **Note:** This method will be available once the plugin is published.

1. Type `/xlsettings` in-game
2. Go to **Experimental** tab
3. Under "Custom Plugin Repositories", add the repository URL
4. Click **Save and Close**
5. Type `/xlplugins` → search for "Krangler" → **Install**

---

## Using Krangler

### Quick Start
1. Type `/krangler` to open the UI
2. Check **Enable Krangler** to activate
3. Check the features you want (Names, Genders, Races, Appearance)
4. Take your screenshots — all player info is randomized!
5. Uncheck **Enable Krangler** when done

### DTR Bar (Server Info Bar)
- A "KR: On/Off" entry appears in the server info bar at the top of the screen
- **Click it** to quickly toggle Krangler on/off
- Configure the display mode (text, icon+text, icon only) in the main window

### Slash Commands

| Command | Description |
|---------|-------------|
| `/krangler` | Open the Krangler window |
| `/kr` | Open the Krangler window |
| `/kr on` | Enable krangling |
| `/kr off` | Disable krangling |

### Feature Details

**Krangle Names** (works without Glamourer)
- Replaces all visible player nameplates with randomized exercise-themed words
- Same player always gets the same fake name (deterministic)
- Clears when you disable and re-enable

**Krangle Genders** (requires Glamourer)
- Randomizes the displayed gender of visible players

**Krangle Races** (requires Glamourer)
- Randomizes races including subraces (Midlander, Highlander, etc.)

**Krangle Appearance** (requires Glamourer)
- Randomizes hair, face, eyes, skin tone, etc.

**Super Krangle Master 4000** (requires Glamourer)
- Overrides all other options
- Transforms players into special NPC models (Gaius, Nero, Louisoix, etc.)
- Disabled by default — enable for maximum chaos

---

## Updating the Plugin

### Dev Plugin Updates
1. Pull latest code from repository
2. Rebuild solution
3. In-game: `/xlplugins` → Disable Krangler → Re-enable it

### Repository Plugin Updates
- Updates appear in `/xlplugins` automatically
- Click **Update** when available

---

## Uninstalling

### Remove Dev Plugin
1. `/xlsettings` → Experimental
2. Remove the DLL path from "Dev Plugin Locations"
3. `/xlplugins` → Disable Krangler

### Clean Configuration
- Configuration stored in: `%APPDATA%\XIVLauncher\pluginConfigs\Krangler\`
- Delete folder to remove all settings

---

## Troubleshooting

### Plugin Won't Load
- Ensure Dalamud is up to date
- Ensure the DLL path is correct in Dev Plugin Locations
- Check `/xllog` for error messages

### Names Not Changing
- Ensure **Enable Krangler** is checked
- Ensure **Krangle Names** is checked
- Names only change on visible player nameplates

### Glamourer Features Not Working
- Ensure Glamourer is installed and enabled
- Check the Glamourer status indicator in the Krangler window
- Name krangling works without Glamourer; other features require it

---

## FAQ

**Q: Can other players see the krangled names?**
A: No. All changes are local to your client only.

**Q: Will this get me banned?**
A: Krangler only modifies local display. Use at your own discretion.

**Q: Do names flicker or change between frames?**
A: No. The same player always gets the same fake name (deterministic hashing).

**Q: Does it work on my own character?**
A: Yes — your own nameplate will also be krangled.

---

*Last Updated: v0.0.0.1 — 2026-03-23*
