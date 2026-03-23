using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkUnitManager;
using Krangler.Services;
using Krangler.Windows;
using Krangler.Models;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using CharacterStruct = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using EquipSlot = FFXIVClientStructs.FFXIV.Client.Game.Character.DrawDataContainer.EquipmentSlot;

namespace Krangler;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static INamePlateGui NamePlateGui { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    
    private const string CommandName = "/krangler";
    private const string AliasCommandName = "/kr";

    public Configuration Configuration { get; init; }
    public AppearanceService AppearanceService { get; init; }
    public GlamourerPresetService GlamourerPresetService { get; init; }

    public readonly WindowSystem WindowSystem = new("Krangler");
    private MainWindow MainWindow { get; init; }

    private IDtrBarEntry? dtrEntry;
    private bool wasEnabled;
    private bool hasLoggedNameplateUpdate;
    private DateTime lastAppearanceScan = DateTime.MinValue;
    private DateTime lastPartyListScan = DateTime.MinValue;
    private bool hasLoggedAppearanceScan;
    private bool hasLoggedPartyList;

    // Track original customize data for revert, keyed by EntityId
    private readonly Dictionary<uint, byte[]> originalCustomizeData = new();
    // Staggered redraw queue — one character per N frames to avoid crashing the game
    private readonly Queue<nint> redrawQueue = new();
    private int redrawCooldownFrames = 0;
    private const int RedrawFrameDelay = 2; // frames between each redraw

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        AppearanceService = new AppearanceService(Log, ObjectTable, Configuration);
        GlamourerPresetService = new GlamourerPresetService(Log, PluginInterface);

        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Krangler window."
        });

        CommandManager.AddHandler(AliasCommandName, new CommandInfo(OnAliasCommand)
        {
            HelpMessage = "Krangler: /kr [on|off] to toggle, or /kr to open UI."
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // DTR bar
        SetupDtrBar();

        // Nameplate hook for name krangling
        NamePlateGui.OnNamePlateUpdate += OnNamePlateUpdate;

        // Framework update for DTR bar + appearance + party list
        Framework.Update += OnFrameworkUpdate;

        wasEnabled = Configuration.Enabled;

        Log.Information("===Krangler loaded===");
    }

    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
    }

    private void OnAliasCommand(string command, string args)
    {
        var arg = args.Trim().ToLowerInvariant();
        if (arg == "on")
        {
            Configuration.Enabled = true;
            Configuration.Save();
            Log.Information("[Krangler] Enabled via command");
        }
        else if (arg == "off")
        {
            Configuration.Enabled = false;
            KrangleService.ClearCache();
            Configuration.Save();
            Log.Information("[Krangler] Disabled via command");
        }
        else
        {
            MainWindow.Toggle();
        }
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
        // Process staggered redraws — one character per N frames
        ProcessRedrawQueue();

        // Track enable/disable transitions for logging
        if (Configuration.Enabled != wasEnabled)
        {
            if (Configuration.Enabled)
            {
                Log.Information("[Krangler] Krangling activated");
                hasLoggedNameplateUpdate = false;
                hasLoggedAppearanceScan = false;
                hasLoggedPartyList = false;
            }
            else
            {
                Log.Information("[Krangler] Krangling deactivated");
                KrangleService.ClearCache();
                AppearanceService.Reset();
                RevertAllAppearances();
            }
            wasEnabled = Configuration.Enabled;
        }

        if (!Configuration.Enabled)
        {
            UpdateDtrBar();
            return;
        }

        // Appearance krangling via direct memory (throttled to every 5 seconds)
        if (Configuration.KrangleGenders || Configuration.KrangleRaces || Configuration.KrangleAppearance || Configuration.SuperKrangleMaster4000)
        {
            var now = DateTime.UtcNow;
            if ((now - lastAppearanceScan).TotalSeconds >= 5)
            {
                lastAppearanceScan = now;
                ScanAndKrangleAppearances();
            }
        }

        // Party list krangling (throttled to every 1 second)
        if (Configuration.KrangleNames || Configuration.SuperKrangleMaster4000)
        {
            var now = DateTime.UtcNow;
            if ((now - lastPartyListScan).TotalSeconds >= 1)
            {
                lastPartyListScan = now;
                KranglePartyList();
            }
        }

        UpdateDtrBar();
    }

    // ─── Direct Appearance Modification ─────────────────────────────────────

    private unsafe void ScanAndKrangleAppearances()
    {
        var playerCount = 0;
        var appliedCount = 0;

        foreach (var obj in ObjectTable)
        {
            if (obj == null || obj.ObjectKind != ObjectKind.Player)
                continue;

            playerCount++;
            var name = obj.Name.ToString();
            if (string.IsNullOrEmpty(name))
                continue;

            // Skip if already applied
            if (AppearanceService.IsApplied(obj.EntityId))
                continue;

            try
            {
                var character = (CharacterStruct*)obj.Address;
                if (character == null) continue;

                // Save original customize data for revert
                var customizePtr = (byte*)&character->DrawData.CustomizeData;
                var originalBytes = new byte[26];
                for (int j = 0; j < 26; j++)
                    originalBytes[j] = customizePtr[j];
                originalCustomizeData[obj.EntityId] = originalBytes;

                // Generate krangled appearance
                var (race, tribe, gender) = Configuration.SuperKrangleMaster4000 
                    ? GetSuperKrangleAppearance(name) 
                    : AppearanceService.GetRandomRaceGender(name);
                bool changed = false;

                if (Configuration.SuperKrangleMaster4000)
                {
                    // Super Krangle: Use Glamourer presets for full appearance + equipment
                    var preset = GlamourerPresetService.GetRandomPreset(name);
                    if (preset != null)
                    {
                        ApplyGlamourerPreset(character, preset, customizePtr);
                        changed = true;
                        
                        if (!hasLoggedAppearanceScan)
                            Log.Information($"[Krangler] Applied Glamourer preset '{preset.Name}' to '{name}'");
                    }
                    else
                    {
                        // Fallback to hardcoded NPCs if no presets loaded
                        var superAppearance = GetSuperKrangleFullAppearance(name);
                        foreach (var (index, value) in superAppearance)
                        {
                            if (index < 26)
                                customizePtr[index] = value;
                        }
                        changed = true;
                    }
                }
                else
                {
                    // Normal krangling options
                    if (Configuration.KrangleRaces)
                    {
                        customizePtr[0] = race;   // Race
                        customizePtr[4] = tribe;  // Tribe
                        changed = true;
                    }

                    if (Configuration.KrangleGenders)
                    {
                        customizePtr[1] = gender; // Sex
                        changed = true;
                    }

                    if (Configuration.KrangleAppearance)
                    {
                        var appearance = AppearanceService.GetRandomAppearance(name, race, gender);
                        foreach (var (index, value) in appearance)
                        {
                            if (index < 26)
                                customizePtr[index] = value;
                        }
                        changed = true;
                    }
                }

                if (changed)
                {
                    // Queue a safe staggered redraw (one per N frames)
                    redrawQueue.Enqueue(obj.Address);

                    AppearanceService.MarkApplied(obj.EntityId);
                    appliedCount++;

                    if (!hasLoggedAppearanceScan)
                        Log.Information($"[Krangler] Applied appearance to '{name}': race={race}, tribe={tribe}, gender={gender}");
                }
            }
            catch (Exception ex)
            {
                if (!hasLoggedAppearanceScan)
                    Log.Warning($"[Krangler] Failed to modify appearance for '{name}': {ex.Message}");
            }
        }

        if (!hasLoggedAppearanceScan && playerCount > 0)
        {
            Log.Information($"[Krangler] Appearance scan: {playerCount} players, {appliedCount} modified (direct memory)");
            hasLoggedAppearanceScan = true;
        }
    }

    private unsafe void ProcessRedrawQueue()
    {
        if (redrawCooldownFrames > 0)
        {
            redrawCooldownFrames--;
            return;
        }

        if (redrawQueue.Count == 0) return;

        var address = redrawQueue.Dequeue();
        try
        {
            var gameObj = (GameObjectStruct*)address;
            // Safe redraw: DisableDraw/EnableDraw only affects rendering,
            // does NOT change ObjectKind so the game won't crash processing the object
            gameObj->DisableDraw();
            gameObj->EnableDraw();
        }
        catch (Exception ex)
        {
            Log.Warning($"[Krangler] Safe redraw failed: {ex.Message}");
        }

        redrawCooldownFrames = RedrawFrameDelay;
    }

    private unsafe void RevertAllAppearances()
    {
        var reverted = 0;
        foreach (var obj in ObjectTable)
        {
            if (obj == null) continue;
            if (!originalCustomizeData.TryGetValue(obj.EntityId, out var originalBytes)) continue;

            try
            {
                var character = (CharacterStruct*)obj.Address;
                var customizePtr = (byte*)&character->DrawData.CustomizeData;
                for (int j = 0; j < 26; j++)
                    customizePtr[j] = originalBytes[j];

                // Queue safe staggered redraw
                redrawQueue.Enqueue(obj.Address);
                reverted++;
            }
            catch { /* best effort revert */ }
        }

        if (reverted > 0)
            Log.Information($"[Krangler] Reverted {reverted} appearance changes");

        originalCustomizeData.Clear();
        AppearanceService.Reset();
    }

    // ─── Party List Krangling ───────────────────────────────────────────────

    private unsafe void KranglePartyList()
    {
        var addon = Instance()->GetAddonByName("_PartyList");
        if (addon == null || !addon->IsVisible) return;

        // Build lookup of original party member names -> krangled names
        var nameMap = new Dictionary<string, string>();
        for (int i = 0; i < PartyList.Length; i++)
        {
            var member = PartyList[i];
            if (member == null) continue;
            var orig = member.Name.ToString();
            if (!string.IsNullOrEmpty(orig))
                nameMap[orig] = KrangleService.KrangleName(orig);
        }

        if (nameMap.Count == 0) return;

        // Direct node targeting based on user investigation
        // Party members are at (15,17), (16,17), (17,17), (18,17), (19,17), (20,17), (21,17), (22,17)
        var rootNode = addon->RootNode;
        if (rootNode != null)
        {
            // Try direct node access first (more reliable)
            for (int row = 15; row <= 22; row++)
            {
                var node = GetNodeAt(rootNode, row, 17);
                if (node != null && node->Type == NodeType.Text)
                {
                    var textNode = (AtkTextNode*)node;
                    var text = textNode->NodeText.ToString();
                    
                    foreach (var (original, krangled) in nameMap)
                    {
                        if (text.Contains(original))
                        {
                            var newText = text.Replace(original, krangled);
                            textNode->SetText(newText);
                            break;
                        }
                    }
                }
            }
            
            // Fallback to recursive walk if direct targeting fails
            WalkAndReplaceTextNodes(rootNode, nameMap);
        }

        if (!hasLoggedPartyList)
        {
            Log.Information($"[Krangler] Party list: {nameMap.Count} members krangled (direct targeting)");
            hasLoggedPartyList = true;
        }
    }

    private unsafe AtkResNode* GetNodeAt(AtkResNode* root, int row, int col)
    {
        if (root == null) return null;
        
        // Navigate through the node tree to find the specific coordinates
        var current = root;
        
        // Try to find by node list index (party list uses specific node structure)
        var node = current->ChildNode;
        var currentIndex = 0;
        
        while (node != null && currentIndex < 100) // safety limit
        {
            if (currentIndex == row)
            {
                // Found the row, now look for column
                var colNode = node->ChildNode;
                var colIndex = 0;
                
                while (colNode != null && colIndex < 50) // safety limit
                {
                    if (colIndex == col)
                        return colNode;
                    
                    colNode = colNode->NextSiblingNode;
                    colIndex++;
                }
            }
            
            node = node->NextSiblingNode;
            currentIndex++;
        }
        
        return null;
    }

    private unsafe void WalkAndReplaceTextNodes(AtkResNode* node, Dictionary<string, string> nameMap)
    {
        if (node == null) return;

        // Check if this is a text node
        if (node->Type == NodeType.Text)
        {
            var textNode = (AtkTextNode*)node;
            var text = textNode->NodeText.ToString();

            foreach (var (original, krangled) in nameMap)
            {
                if (text.Contains(original))
                {
                    var newText = text.Replace(original, krangled);
                    textNode->SetText(newText);
                    break;
                }
            }
        }

        // Recurse into component nodes (Type >= 1000 = component)
        if ((int)node->Type >= 1000)
        {
            var comp = (AtkComponentNode*)node;
            if (comp->Component != null)
            {
                var child = comp->Component->UldManager.RootNode;
                while (child != null)
                {
                    WalkAndReplaceTextNodes(child, nameMap);
                    child = child->PrevSiblingNode;
                }
            }
        }

        // Walk siblings
        var sibling = node->PrevSiblingNode;
        if (sibling != null)
            WalkAndReplaceTextNodes(sibling, nameMap);
    }

    private void OnNamePlateUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        if (!Configuration.Enabled)
            return;

        var playerCount = 0;
        for (var i = 0; i < handlers.Count; i++)
        {
            var handler = handlers[i];

            // Only krangle player character nameplates
            if (handler.NamePlateKind != NamePlateKind.PlayerCharacter)
                continue;

            playerCount++;

            // Krangle name
            if (Configuration.KrangleNames)
            {
                var originalName = handler.Name.ToString();
                if (!string.IsNullOrEmpty(originalName))
                {
                    var krangled = KrangleService.KrangleName(originalName);
                    if (!hasLoggedNameplateUpdate)
                        Log.Information($"[Krangler] Name: '{originalName}' -> '{krangled}'");
                    handler.Name = krangled;
                }
            }

            // Krangle FC tag
            try
            {
                var originalFc = handler.FreeCompanyTag.ToString();
                if (!string.IsNullOrEmpty(originalFc))
                {
                    var krangledFc = KrangleService.KrangleFCTag(originalFc);
                    if (!hasLoggedNameplateUpdate)
                        Log.Information($"[Krangler] FC: '{originalFc}' -> '{krangledFc}'");
                    handler.FreeCompanyTag = krangledFc;
                }
            }
            catch (Exception ex)
            {
                if (!hasLoggedNameplateUpdate)
                    Log.Warning($"[Krangler] FreeCompanyTag not available: {ex.Message}");
            }

            // Krangle title
            try
            {
                var originalTitle = handler.Title.ToString();
                if (!string.IsNullOrEmpty(originalTitle))
                {
                    var krangledTitle = KrangleService.KrangleTitle(originalTitle);
                    if (!hasLoggedNameplateUpdate)
                        Log.Information($"[Krangler] Title: '{originalTitle}' -> '{krangledTitle}'");
                    handler.Title = krangledTitle;
                }
            }
            catch (Exception ex)
            {
                if (!hasLoggedNameplateUpdate)
                    Log.Warning($"[Krangler] Title not available: {ex.Message}");
            }
        }

        if (!hasLoggedNameplateUpdate && playerCount > 0)
        {
            Log.Information($"[Krangler] OnNamePlateUpdate: {handlers.Count} handlers, {playerCount} players");
            hasLoggedNameplateUpdate = true;
        }
    }

    public void SetupDtrBar()
    {
        try
        {
            dtrEntry = DtrBar.Get("Krangler");
            dtrEntry.Shown = Configuration.DtrBarEnabled;
            dtrEntry.Text = new SeString(new TextPayload("KR: Off"));
            dtrEntry.OnClick = (_) =>
            {
                Configuration.Enabled = !Configuration.Enabled;
                if (!Configuration.Enabled)
                {
                    KrangleService.ClearCache();
                    AppearanceService.Reset();
                }
                Configuration.Save();
            };
        }
        catch (Exception ex)
        {
            Log.Error($"[Krangler] Failed to setup DTR bar: {ex.Message}");
        }
    }

    public void UpdateDtrBar()
    {
        if (dtrEntry == null) return;

        dtrEntry.Shown = Configuration.DtrBarEnabled;
        if (!Configuration.DtrBarEnabled) return;

        var iconEnabled = string.IsNullOrEmpty(Configuration.DtrIconEnabled) ? "\uE044" : Configuration.DtrIconEnabled;
        var iconDisabled = string.IsNullOrEmpty(Configuration.DtrIconDisabled) ? "\uE04C" : Configuration.DtrIconDisabled;
        var glyph = Configuration.Enabled ? iconEnabled : iconDisabled;

        switch (Configuration.DtrBarMode)
        {
            case 1: // icon+text
                dtrEntry.Text = new SeString(new TextPayload($"{glyph} KR"));
                break;
            case 2: // icon-only
                dtrEntry.Text = new SeString(new TextPayload(glyph));
                break;
            default: // text-only
                var statusText = Configuration.Enabled ? "KR: On" : "KR: Off";
                dtrEntry.Text = new SeString(new TextPayload(statusText));
                break;
        }

        dtrEntry.Tooltip = new SeString(new TextPayload(
            Configuration.Enabled
                ? "Krangler active — Click to disable"
                : "Krangler disabled — Click to enable"));
    }

    public void ToggleMainUi() => MainWindow.Toggle();

    // ─── Glamourer Preset Application ─────────────────────────────────────

    /// <summary>
    /// Apply a Glamourer preset to a character, including appearance and equipment (except weapons).
    /// DrawDataContainer layout (from FFXIVClientStructs):
    ///   +0x010: WeaponData[3]
    ///   +0x1D0: EquipmentModelIds[10] (8 bytes each)
    ///   +0x220: CustomizeData (26 bytes)
    /// Equipment slots: Head=0, Body=1, Hands=2, Legs=3, Feet=4, Ears=5, Neck=6, Wrists=7, RFinger=8, LFinger=9
    /// </summary>
    private unsafe void ApplyGlamourerPreset(CharacterStruct* character, GlamourerPreset preset, byte* customizePtr)
    {
        // ── Apply customize data (26 bytes) ──
        var c = preset.Customize;

        if (c.Race.Apply) customizePtr[0] = c.Race.Value;
        if (c.Gender.Apply) customizePtr[1] = c.Gender.Value;
        if (c.BodyType.Apply) customizePtr[2] = c.BodyType.Value;
        if (c.Height.Apply) customizePtr[3] = c.Height.Value;
        if (c.Clan.Apply) customizePtr[4] = c.Clan.Value;
        if (c.Face.Apply) customizePtr[5] = c.Face.Value;
        if (c.Hairstyle.Apply) customizePtr[6] = c.Hairstyle.Value;
        if (c.Highlights.Apply) customizePtr[7] = c.Highlights.Value;
        if (c.SkinColor.Apply) customizePtr[8] = c.SkinColor.Value;
        if (c.EyeColorRight.Apply) customizePtr[9] = c.EyeColorRight.Value;
        if (c.HairColor.Apply) customizePtr[10] = c.HairColor.Value;
        if (c.HighlightsColor.Apply) customizePtr[11] = c.HighlightsColor.Value;

        // Byte 12: Facial features bitfield (bits 0-6 = features 1-7, bit 7 = legacy tattoo)
        byte facialBits = customizePtr[12];
        if (c.FacialFeature1.Apply) facialBits = (byte)((facialBits & ~0x01) | (c.FacialFeature1.Value != 0 ? 0x01 : 0));
        if (c.FacialFeature2.Apply) facialBits = (byte)((facialBits & ~0x02) | (c.FacialFeature2.Value != 0 ? 0x02 : 0));
        if (c.FacialFeature3.Apply) facialBits = (byte)((facialBits & ~0x04) | (c.FacialFeature3.Value != 0 ? 0x04 : 0));
        if (c.FacialFeature4.Apply) facialBits = (byte)((facialBits & ~0x08) | (c.FacialFeature4.Value != 0 ? 0x08 : 0));
        if (c.FacialFeature5.Apply) facialBits = (byte)((facialBits & ~0x10) | (c.FacialFeature5.Value != 0 ? 0x10 : 0));
        if (c.FacialFeature6.Apply) facialBits = (byte)((facialBits & ~0x20) | (c.FacialFeature6.Value != 0 ? 0x20 : 0));
        if (c.FacialFeature7.Apply) facialBits = (byte)((facialBits & ~0x40) | (c.FacialFeature7.Value != 0 ? 0x40 : 0));
        if (c.LegacyTattoo.Apply) facialBits = (byte)((facialBits & ~0x80) | (c.LegacyTattoo.Value != 0 ? 0x80 : 0));
        customizePtr[12] = facialBits;

        if (c.TattooColor.Apply) customizePtr[13] = c.TattooColor.Value;
        if (c.Eyebrows.Apply) customizePtr[14] = c.Eyebrows.Value;
        if (c.EyeColorLeft.Apply) customizePtr[15] = c.EyeColorLeft.Value;
        if (c.EyeShape.Apply) customizePtr[16] = c.EyeShape.Value;
        if (c.SmallIris.Apply) customizePtr[17] = c.SmallIris.Value;
        if (c.Nose.Apply) customizePtr[18] = c.Nose.Value;
        if (c.Jaw.Apply) customizePtr[19] = c.Jaw.Value;
        if (c.Mouth.Apply) customizePtr[20] = c.Mouth.Value;
        if (c.Lipstick.Apply) customizePtr[21] = c.Lipstick.Value;
        if (c.LipColor.Apply) customizePtr[22] = c.LipColor.Value;
        if (c.MuscleMass.Apply) customizePtr[23] = c.MuscleMass.Value;
        if (c.TailShape.Apply) customizePtr[24] = c.TailShape.Value;
        if (c.BustSize.Apply) customizePtr[25] = c.BustSize.Value;

        // ── Apply equipment (except weapons) ──
        // Equipment is at DrawData + 0x1D0, each slot is 8 bytes (EquipmentModelId)
        // We write the Glamourer ItemId as raw 8 bytes to each equipment slot
        var equipBase = (byte*)&character->DrawData + 0x1D0;

        // Map Glamourer slot names to game slot indices (skip MainHand/OffHand)
        var slotMap = new (string name, int index)[]
        {
            ("Head", 0), ("Body", 1), ("Hands", 2), ("Legs", 3), ("Feet", 4),
            ("Ears", 5), ("Neck", 6), ("Wrists", 7), ("RFinger", 8), ("LFinger", 9),
        };

        var equipChanged = 0;
        foreach (var (slotName, slotIndex) in slotMap)
        {
            if (preset.Equipment.TryGetValue(slotName, out var equipSlot) && equipSlot.Apply)
            {
                var slotPtr = (ulong*)(equipBase + slotIndex * 8);
                *slotPtr = equipSlot.ItemId;
                equipChanged++;
            }
        }

        if (!hasLoggedAppearanceScan && equipChanged > 0)
            Log.Information($"[Krangler] Applied {equipChanged} equipment slots from preset '{preset.Name}'");
    }

    // ─── Super Krangle Master 4000 Methods ─────────────────────────────────────

    /// <summary>
    /// Get special NPC appearance data for Super Krangle Master 4000 mode.
    /// Returns (race, tribe, gender) for iconic NPCs like Gaius, Nero, Louisoix, etc.
    /// </summary>
    private static (byte race, byte tribe, byte gender) GetSuperKrangleAppearance(string playerName)
    {
        var hash = GetStableHash(playerName + "_super");
        var rng = new Random(hash);

        // Special NPC appearances - iconic characters
        var npcAppearances = new[]
        {
            ((byte)1, (byte)1, (byte)0),   // Hyur Midlander Male (Gaius)
            ((byte)1, (byte)1, (byte)1),   // Hyur Midlander Female (Minfilia)
            ((byte)1, (byte)2, (byte)0),   // Hyur Highlander Male (Raubahn)
            ((byte)4, (byte)7, (byte)0),   // Roegadyn Sea Wolves Male (Nero)
            ((byte)5, (byte)9, (byte)0),   // Elezen Wildwood Male (Louisoix)
            ((byte)5, (byte)10, (byte)1),  // Elezen Duskwight Female (Urianger)
            ((byte)6, (byte)11, (byte)0),  // Au Ra Raen Male (Hien)
            ((byte)6, (byte)12, (byte)1),  // Au Ra Xaela Female (Lyse)
            ((byte)7, (byte)13, (byte)0),  // Hrothgar Helions Male (Varis)
            ((byte)8, (byte)15, (byte)1),  // Viera Rava Female (Y'shtola)
        };

        return npcAppearances[rng.Next(npcAppearances.Length)];
    }

    /// <summary>
    /// Get full customize data for Super Krangle Master 4000 mode.
    /// Returns complete 26-byte customize array for iconic NPCs.
    /// </summary>
    private static Dictionary<int, byte> GetSuperKrangleFullAppearance(string playerName)
    {
        var hash = GetStableHash(playerName + "_super_full");
        var rng = new Random(hash);

        // Select a base NPC template
        var templates = new[]
        {
            // Gaius van Baelsrar - Hyur Midlander Male
            new Dictionary<int, byte>
            {
                {0, 1}, {1, 0}, {2, 1}, {3, 50}, {4, 1}, {5, 4}, {6, 4}, {7, 0},
                {8, 8}, {9, 24}, {10, 24}, {11, 24}, {12, 0}, {13, 0}, {14, 0},
                {15, 1}, {16, 0}, {17, 1}, {18, 1}, {19, 1}, {20, 1}, {21, 0},
                {22, 0}, {23, 0}, {24, 0}, {25, 50}
            },
            // Nero tol Scaeva - Hyur Midlander Male
            new Dictionary<int, byte>
            {
                {0, 1}, {1, 0}, {2, 1}, {3, 75}, {4, 1}, {5, 2}, {6, 11}, {7, 0},
                {8, 12}, {9, 120}, {10, 120}, {11, 120}, {12, 0}, {13, 0}, {14, 0},
                {15, 2}, {16, 0}, {17, 2}, {18, 2}, {19, 2}, {20, 2}, {21, 0},
                {22, 0}, {23, 0}, {24, 0}, {25, 50}
            },
            // Louisoix Leveilleur - Elezen Wildwood Male  
            new Dictionary<int, byte>
            {
                {0, 5}, {1, 0}, {2, 1}, {3, 60}, {4, 9}, {5, 6}, {6, 1}, {7, 0},
                {8, 95}, {9, 180}, {10, 180}, {11, 180}, {12, 0}, {13, 0}, {14, 0},
                {15, 3}, {16, 0}, {17, 3}, {18, 3}, {19, 3}, {20, 3}, {21, 0},
                {22, 0}, {23, 0}, {24, 0}, {25, 50}
            },
            // Y'shtola Rhul - Viera Rava Female
            new Dictionary<int, byte>
            {
                {0, 8}, {1, 1}, {2, 1}, {3, 25}, {4, 15}, {5, 3}, {6, 1}, {7, 0},
                {8, 140}, {9, 160}, {10, 160}, {11, 160}, {12, 0}, {13, 0}, {14, 0},
                {15, 4}, {16, 0}, {17, 4}, {18, 4}, {19, 4}, {20, 4}, {21, 0},
                {22, 0}, {23, 0}, {24, 0}, {25, 25}
            },
            // Minfilia Warde - Hyur Midlander Female
            new Dictionary<int, byte>
            {
                {0, 1}, {1, 1}, {2, 1}, {3, 30}, {4, 1}, {5, 1}, {6, 2}, {7, 0},
                {8, 20}, {9, 90}, {10, 90}, {11, 90}, {12, 0}, {13, 0}, {14, 0},
                {15, 5}, {16, 0}, {17, 5}, {18, 5}, {19, 5}, {20, 5}, {21, 0},
                {22, 0}, {23, 0}, {24, 0}, {25, 30}
            }
        };

        var baseTemplate = templates[rng.Next(templates.Length)];
        
        // Add some randomization to make it interesting
        var result = new Dictionary<int, byte>(baseTemplate);
        result[3] = (byte)rng.Next(20, 80); // Height variation
        result[25] = (byte)rng.Next(20, 80); // Bust variation for females
        
        return result;
    }

    private static int GetStableHash(string input)
    {
        unchecked
        {
            int hash = 17;
            foreach (var c in input)
                hash = hash * 31 + c;
            return hash;
        }
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        NamePlateGui.OnNamePlateUpdate -= OnNamePlateUpdate;

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();

        // Revert any appearance changes
        RevertAllAppearances();

        dtrEntry?.Remove();

        CommandManager.RemoveHandler(AliasCommandName);
        CommandManager.RemoveHandler(CommandName);

        // Clear krangled name cache
        KrangleService.ClearCache();

        Log.Information("[Krangler] Plugin unloaded!");
    }
}
