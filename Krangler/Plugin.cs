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
using CharacterStruct = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

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
        if (Configuration.KrangleGenders || Configuration.KrangleRaces || Configuration.KrangleAppearance)
        {
            var now = DateTime.UtcNow;
            if ((now - lastAppearanceScan).TotalSeconds >= 5)
            {
                lastAppearanceScan = now;
                ScanAndKrangleAppearances();
            }
        }

        // Party list krangling (throttled to every 1 second)
        if (Configuration.KrangleNames)
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
                var (race, tribe, gender) = AppearanceService.GetRandomRaceGender(name);
                bool changed = false;

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

        // Walk all text nodes in the addon tree and replace matching names
        var rootNode = addon->RootNode;
        if (rootNode != null)
            WalkAndReplaceTextNodes(rootNode, nameMap);

        if (!hasLoggedPartyList)
        {
            Log.Information($"[Krangler] Party list: {nameMap.Count} members krangled");
            hasLoggedPartyList = true;
        }
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
}
