using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Krangler.Models;

namespace Krangler.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string presetSearch = string.Empty;
    private Vector2? queuedPosition;
    private bool queuedRandomVisibleJump;
    private bool identityRuleDraftLoaded;
    private bool identityRuleDraftEnabled;
    private List<PlayerIdentityRule> identityRuleDraft = new();

    public MainWindow(Plugin plugin)
        : base("Krangler###KranglerMain")
    {
        this.plugin = plugin;

        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(520, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ApplyQueuedWindowPlacement();
        DrawTabbedInterface();
    }

    private void DrawTabbedInterface()
    {
        var config = plugin.Configuration;
        var presetNames = plugin.GlamourerPresetService.GetPresetNames();
        var configChanged = EnsureSlotSelections(config);
        configChanged |= config.Sanitize();
        if (configChanged)
            config.Save();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        ImGui.Text($"Krangler v{version}");
        ImGui.SameLine(ImGui.GetWindowWidth() - 120);
        if (ImGui.SmallButton("\u2661 Ko-fi \u2661"))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ko-fi.com/mcvaxius",
                UseShellExecute = true
            });
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Support development on Ko-fi");

        ImGui.Separator();

        DrawMasterToggle(config);

        if (!ImGui.BeginTabBar("KranglerTabs"))
            return;

        if (ImGui.BeginTabItem("Overview"))
        {
            DrawOverviewTab(config);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Names"))
        {
            DrawNamesTab(config);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Appearance"))
        {
            DrawAppearanceTab(config);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Racism"))
        {
            DrawRacismTab(config);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Presets"))
        {
            DrawPresetsTab(config, presetNames);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Imaginary Fren"))
        {
            DrawImaginaryFrenTab(config, presetNames);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Soul Thief"))
        {
            DrawSoulThiefTab(config);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Debug"))
        {
            DrawDebugTab(config);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawOverviewTab(Configuration config)
    {
        ImGui.Spacing();

        DrawStatus(config);

        ImGui.Spacing();
        ImGui.Text($"Presets loaded: {plugin.GlamourerPresetService.PresetCount}");
        ImGui.Text($"Soul Thief last capture: {config.SoulThiefLastCapturedPlayers} players, {config.SoulThiefLastCapturedNpcs} NPCs, {config.SoulThiefLastCapturedChocobos} chocobos");

        ImGui.Spacing();
        if (ImGui.Button("Open Setup Wizard"))
            plugin.OpenSetupWizard();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Reopen the three-step quick setup without changing advanced settings or Racism rules.");

        ImGui.Spacing();
        DrawDtrSection(config);
    }

    private void DrawMasterToggle(Configuration config)
    {
        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enable Krangler", ref enabled))
        {
            config.Enabled = enabled;
            if (!enabled)
                Services.KrangleService.ClearCache();
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Master toggle - enables or disables all krangling.");

        ImGui.Spacing();
    }

    private void DrawDtrSection(Configuration config)
    {
        ImGui.Text("DTR Bar");
        ImGui.Separator();

        var dtrEnabled = config.DtrBarEnabled;
        if (ImGui.Checkbox("Show DTR Bar Entry", ref dtrEnabled))
        {
            config.DtrBarEnabled = dtrEnabled;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show Krangler status in the server info bar. Click the DTR entry to toggle enable or disable.");

        ImGui.BeginDisabled(!config.DtrBarEnabled);

        var dtrMode = config.DtrBarMode;
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("DTR Mode", ref dtrMode, "Text Only\0Icon + Text\0Icon Only\0"))
        {
            config.DtrBarMode = dtrMode;
            config.Save();
        }

        ImGui.Text("DTR Icons (max 3 characters)");
        ImGui.SameLine();
        HelpMarker("Customize the glyphs used for enabled and disabled icon modes.");
        ImGui.SameLine();
        if (ImGui.SmallButton("Copy Icon Guide Link"))
        {
            ImGui.SetClipboardText("https://na.finalfantasyxiv.com/lodestone/character/22423564/blog/4393835");
            Plugin.Log.Information("Copied icon guide link to clipboard");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copies the Lodestone blog link with suggested glyphs.");

        var enabledIcon = config.DtrIconEnabled;
        if (DrawIconInputs("Enabled", ref enabledIcon, "\uE03C"))
        {
            config.DtrIconEnabled = enabledIcon;
            config.Save();
        }

        var disabledIcon = config.DtrIconDisabled;
        if (DrawIconInputs("Disabled", ref disabledIcon, "\uE03D"))
        {
            config.DtrIconDisabled = disabledIcon;
            config.Save();
        }

        ImGui.EndDisabled();
    }

    private void DrawNamesTab(Configuration config)
    {
        ImGui.Spacing();
        ImGui.Text("Names");
        ImGui.Separator();

        var krangleNames = config.KrangleNames;
        if (ImGui.Checkbox("Krangle Names", ref krangleNames))
        {
            config.KrangleNames = krangleNames;
            if (!krangleNames)
                Services.KrangleService.ClearCache();
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Randomize visible player names and party list names.");

        var skipSelfKrangling = config.SkipSelfKrangling;
        if (ImGui.Checkbox("Do Not Krangle Self", ref skipSelfKrangling))
            plugin.SetSkipSelfKrangling(skipSelfKrangling);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Keep your own character's appearance stable and optionally use a fixed self display name instead of a randomized one.");

        ImGui.BeginDisabled(!config.SkipSelfKrangling);
        var customSelfDisplayName = config.CustomSelfDisplayName ?? string.Empty;
        if (ImGui.InputText("Custom Self Display Name", ref customSelfDisplayName, 64))
            plugin.SetCustomSelfDisplayName(customSelfDisplayName);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Optional fixed name to use for your own character. Leave blank to keep your real name.");
        ImGui.EndDisabled();

        var krangleChat = config.KrangleChat;
        if (ImGui.Checkbox("Krangle Chat", ref krangleChat))
        {
            config.KrangleChat = krangleChat;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Garble chat text for screenshot privacy.");
    }

    private void DrawAppearanceTab(Configuration config)
    {
        ImGui.Spacing();
        ImGui.Text("Appearance");
        ImGui.Separator();

        var krangleGenders = config.KrangleGenders;
        if (ImGui.Checkbox("Krangle Genders", ref krangleGenders))
        {
            config.KrangleGenders = krangleGenders;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Randomize genders for visible player characters.");

        var krangleRaces = config.KrangleRaces;
        if (ImGui.Checkbox("Krangle Races", ref krangleRaces))
        {
            config.KrangleRaces = krangleRaces;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Randomize races and subraces for visible player characters.");

        var krangleAppearance = config.KrangleAppearance;
        if (ImGui.Checkbox("Krangle Appearance", ref krangleAppearance))
        {
            config.KrangleAppearance = krangleAppearance;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Randomize hair, face, eyes, and other appearance fields.");

        ImGui.Spacing();
        ImGui.Text("Non-Player Targets");
        ImGui.Separator();
        ImGui.TextWrapped("Broad non-player native mutation is currently blocked for crash safety.");

        ImGui.BeginDisabled();
        var krangleNpcs = false;
        ImGui.Checkbox("Krangle NPCs", ref krangleNpcs);

        var krangleChocobos = false;
        ImGui.Checkbox("Krangle Chocobos", ref krangleChocobos);

        var krangleMinions = false;
        ImGui.Checkbox("Krangle Minions", ref krangleMinions);
        ImGui.EndDisabled();
    }

    private void DrawRacismTab(Configuration config)
    {
        if (!identityRuleDraftLoaded)
            ReloadIdentityRuleDraft(config);

        ImGui.Spacing();
        ImGui.Text("Exact Race / Clan / Gender Rules");
        ImGui.TextWrapped("Rules match the actor's original local identity. Hide removes the matching 3D actor and in-world nameplate; Replace pseudonymizes supported names and applies the chosen clan and gender after other appearance work.");
        ImGui.Spacing();

        ImGui.Checkbox("Enable Racism Rules", ref identityRuleDraftEnabled);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("This is part of the draft. Use Apply Rules to save or disable the tab.");

        var tableFlags = ImGuiTableFlags.Borders |
                         ImGuiTableFlags.RowBg |
                         ImGuiTableFlags.ScrollY |
                         ImGuiTableFlags.SizingFixedFit;
        if (ImGui.BeginTable("##PlayerIdentityRules", 8, tableFlags, new Vector2(0, 475)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Active");
            ImGui.TableSetupColumn("Race");
            ImGui.TableSetupColumn("Clan/Subrace");
            ImGui.TableSetupColumn("Gender");
            ImGui.TableSetupColumn("Hide");
            ImGui.TableSetupColumn("Replace");
            ImGui.TableSetupColumn("Replacement Clan");
            ImGui.TableSetupColumn("Replacement Gender");
            ImGui.TableHeadersRow();

            for (var index = 0; index < identityRuleDraft.Count; index++)
            {
                var rule = identityRuleDraft[index];
                var descriptor = PlayerIdentityCatalog.Entries[index];

                ImGui.PushID(index);
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                var active = rule.Active;
                if (ImGui.Checkbox("##active", ref active))
                {
                    rule.Active = active;
                    if (active)
                        rule.Action = PlayerIdentityRuleAction.Hide;
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(descriptor.RaceName);

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(descriptor.ClanName);

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(descriptor.GenderName);

                ImGui.TableSetColumnIndex(4);
                if (ImGui.RadioButton("##hide", rule.Action == PlayerIdentityRuleAction.Hide))
                    rule.Action = PlayerIdentityRuleAction.Hide;

                ImGui.TableSetColumnIndex(5);
                if (ImGui.RadioButton("##replace", rule.Action == PlayerIdentityRuleAction.Replace))
                    rule.Action = PlayerIdentityRuleAction.Replace;

                var replacementEnabled = rule.Active && rule.Action == PlayerIdentityRuleAction.Replace;
                ImGui.BeginDisabled(!replacementEnabled);

                ImGui.TableSetColumnIndex(6);
                DrawReplacementClanCombo(rule);

                ImGui.TableSetColumnIndex(7);
                DrawReplacementGenderCombo(rule);

                ImGui.EndDisabled();
                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        var activeRules = identityRuleDraft.Count(rule => rule.Active);
        ImGui.Text($"Draft: {activeRules} active rule(s). Currently hidden by Krangler: {plugin.IdentityRuleService.HiddenActorCount} actor(s).");

        if (ImGui.Button("Apply Rules"))
        {
            plugin.ApplyPlayerIdentityRules(identityRuleDraftEnabled, identityRuleDraft);
            ReloadIdentityRuleDraft(config);
        }

        ImGui.SameLine();
        if (ImGui.Button("Discard Edits"))
            ReloadIdentityRuleDraft(config);
    }

    private static void DrawReplacementClanCombo(PlayerIdentityRule rule)
    {
        PlayerIdentityCatalog.TryGetRaceForClan(rule.ReplacementClan, out var replacementRace);
        var raceName = PlayerIdentityCatalog.Entries.First(entry => entry.Race == replacementRace).RaceName;
        var preview = $"{raceName} / {PlayerIdentityCatalog.GetClanName(rule.ReplacementClan)}";

        ImGui.SetNextItemWidth(180);
        if (!ImGui.BeginCombo("##replacementClan", preview))
            return;

        foreach (var (clan, clanName) in PlayerIdentityCatalog.ClanOptions)
        {
            PlayerIdentityCatalog.TryGetRaceForClan(clan, out var race);
            var optionRaceName = PlayerIdentityCatalog.Entries.First(entry => entry.Race == race).RaceName;
            var selected = rule.ReplacementClan == clan;
            if (ImGui.Selectable($"{optionRaceName} / {clanName}", selected))
                rule.ReplacementClan = clan;
            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private static void DrawReplacementGenderCombo(PlayerIdentityRule rule)
    {
        var replacementGender = (int)rule.ReplacementGender;
        ImGui.SetNextItemWidth(100);
        if (ImGui.Combo("##replacementGender", ref replacementGender, "Male\0Female\0"))
            rule.ReplacementGender = (byte)replacementGender;
    }

    private void ReloadIdentityRuleDraft(Configuration config)
    {
        identityRuleDraftEnabled = config.RaceGenderRulesEnabled;
        identityRuleDraft = PlayerIdentityCatalog.CreateDraftRules(config.PlayerIdentityRules);
        identityRuleDraftLoaded = true;
    }

    private void DrawPresetsTab(Configuration config, IReadOnlyList<string> presetNames)
    {
        ImGui.Spacing();
        ImGui.Text($"Presets loaded: {plugin.GlamourerPresetService.PresetCount}");

        DrawAmongusSection(config, presetNames);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawSuperKrangleSection(config, presetNames);
    }

    private void DrawImaginaryFrenTab(Configuration config, IReadOnlyList<string> presetNames)
    {
        ImGui.Spacing();
        ImGui.Text("Imaginary Fren");
        ImGui.Separator();
        ImGui.Text($"Presets loaded: {plugin.GlamourerPresetService.PresetCount}");
        ImGui.Spacing();

        var status = plugin.ImaginaryFrenService.GetStatus();
        var enabled = config.ImaginaryFrenEnabled;
        if (ImGui.Checkbox("Enabled##ImaginaryFrenEnabled", ref enabled))
        {
            config.ImaginaryFrenEnabled = enabled;
            config.Save();
            plugin.ImaginaryFrenService.UseConfigDesired();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Spawn one local-only, non-targetable fake NPC follower while Krangler is enabled.");

        ImGui.SameLine();
        ImGui.TextDisabled(status.Spawned ? "Spawned" : "Not spawned");

        var displayName = config.ImaginaryFrenName ?? string.Empty;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.InputText("Display Name##ImaginaryFrenName", ref displayName, 64))
        {
            config.ImaginaryFrenName = displayName;
            config.Sanitize();
            config.Save();
            plugin.ImaginaryFrenService.UseConfigDesired();
        }

        var presetKey = string.IsNullOrWhiteSpace(config.ImaginaryFrenPresetKey)
            ? Configuration.DefaultImaginaryFrenPresetKey
            : config.ImaginaryFrenPresetKey;
        ImGui.SetNextItemWidth(260f);
        if (DrawPresetSelectionCombo("Preset##ImaginaryFrenPreset", ref presetKey, presetNames, false, false))
        {
            config.ImaginaryFrenPresetKey = presetKey;
            config.Save();
            plugin.ImaginaryFrenService.UseConfigDesired();
        }

        if (ImGui.SmallButton("Test Spawn"))
        {
            plugin.ImaginaryFrenService.RequestSpawnFromConfig();
            plugin.ImaginaryFrenService.Update();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Enable and try to spawn the configured follower now. Krangler's master toggle still gates spawning.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Despawn"))
        {
            plugin.ImaginaryFrenService.DisableFromConfig();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Disable and remove the current local-only follower.");

        ImGui.TextWrapped($"Status: {status.Status}");
        if (!string.IsNullOrWhiteSpace(status.Error))
            ImGui.TextWrapped($"Warning: {status.Error}");
        if (!string.Equals(status.Source, "config", StringComparison.OrdinalIgnoreCase))
            ImGui.TextWrapped($"Runtime source: {status.Source}");
    }

    private void DrawSuperKrangleSection(Configuration config, IReadOnlyList<string> presetNames)
    {
        var superKrangle = config.SuperKrangleMaster4000;
        if (ImGui.Checkbox("Super Krangle Master 4000", ref superKrangle))
        {
            config.SuperKrangleMaster4000 = superKrangle;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Use imported Glamourer presets in place of normal appearance krangling.\n" +
                "Selection can be global, random, or overridden by party slot.\n\n" +
                $"Presets loaded: {plugin.GlamourerPresetService.PresetCount}");
        }

        ImGui.BeginDisabled(!config.SuperKrangleMaster4000);

        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), $"({plugin.GlamourerPresetService.PresetCount} presets)");

        if (presetNames.Count == 0)
            ImGui.TextColored(new Vector4(1.0f, 0.75f, 0.3f, 1.0f), "No preset files are loaded. Built-in NPC looks will be used instead.");

        var globalSelection = string.IsNullOrWhiteSpace(config.SuperKrangleSelection)
            ? "Random"
            : config.SuperKrangleSelection;
        if (DrawPresetSelectionCombo("Global Preset", ref globalSelection, presetNames, false))
        {
            config.SuperKrangleSelection = globalSelection;
            config.Save();
        }

        ImGui.Spacing();
        ImGui.Text("Non-Player Preset Targets");
        ImGui.Separator();
        ImGui.TextWrapped("Broad non-player native mutation is currently blocked for crash safety.");

        ImGui.BeginDisabled();
        var superKrangleNpcs = false;
        ImGui.Checkbox("NPCs", ref superKrangleNpcs);
        ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Text("Companion Preset Targets");
        ImGui.Separator();
        ImGui.TextWrapped("Broad non-player native mutation is currently blocked for crash safety.");

        ImGui.BeginDisabled();
        var superKrangleChocobos = false;
        ImGui.Checkbox("Chocobos", ref superKrangleChocobos);

        var superKrangleMinions = false;
        ImGui.Checkbox("Minions", ref superKrangleMinions);
        ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Text("Party Slot Overrides");
        ImGui.Separator();

        for (var i = 0; i < config.SuperKranglePartySlotSelections.Count; i++)
        {
            var slotSelection = string.IsNullOrWhiteSpace(config.SuperKranglePartySlotSelections[i])
                ? "Use Global"
                : config.SuperKranglePartySlotSelections[i];

            if (DrawPresetSelectionCombo(GetPartySlotLabel(i), ref slotSelection, presetNames, true))
            {
                config.SuperKranglePartySlotSelections[i] = slotSelection;
                config.Save();
            }
        }

        ImGui.Spacing();
        ImGui.Text("Apply From Preset");
        ImGui.Separator();

        DrawApplyFromPresetOptions(config);

        ImGui.Spacing();
        ImGui.Text("Propagation Control");
        ImGui.Separator();

        var maxPlayersPerCycle = config.SuperKrangleMaxPlayersPerCycle;
        if (ImGui.SliderInt("Max Players Per Cycle", ref maxPlayersPerCycle, 1, 24))
        {
            config.SuperKrangleMaxPlayersPerCycle = maxPlayersPerCycle;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Limit how many visible players are processed during one scan pass.");

        var redrawDelay = config.SuperKrangleBaseRedrawDelayFrames;
        if (ImGui.SliderInt("Base Redraw Delay", ref redrawDelay, 1, 10))
        {
            config.SuperKrangleBaseRedrawDelayFrames = redrawDelay;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Base frame delay before the next queued redraw. Actual delay scales with crowd size.");

        ImGui.EndDisabled();
    }

    private static void DrawApplyFromPresetOptions(Configuration config)
    {
        var applyAppearance = config.SuperKrangleApplyAppearance;
        if (ImGui.Checkbox("Appearance", ref applyAppearance))
        {
            config.SuperKrangleApplyAppearance = applyAppearance;
            config.Save();
        }
        ImGui.SameLine();
        var applyHead = config.SuperKrangleApplyHead;
        if (ImGui.Checkbox("Head", ref applyHead))
        {
            config.SuperKrangleApplyHead = applyHead;
            config.Save();
        }
        ImGui.SameLine();
        var applyBody = config.SuperKrangleApplyBody;
        if (ImGui.Checkbox("Body", ref applyBody))
        {
            config.SuperKrangleApplyBody = applyBody;
            config.Save();
        }

        var applyHands = config.SuperKrangleApplyHands;
        if (ImGui.Checkbox("Hands", ref applyHands))
        {
            config.SuperKrangleApplyHands = applyHands;
            config.Save();
        }
        ImGui.SameLine();
        var applyLegs = config.SuperKrangleApplyLegs;
        if (ImGui.Checkbox("Legs", ref applyLegs))
        {
            config.SuperKrangleApplyLegs = applyLegs;
            config.Save();
        }
        ImGui.SameLine();
        var applyFeet = config.SuperKrangleApplyFeet;
        if (ImGui.Checkbox("Feet", ref applyFeet))
        {
            config.SuperKrangleApplyFeet = applyFeet;
            config.Save();
        }

        var applyAccessories = config.SuperKrangleApplyAccessories;
        if (ImGui.Checkbox("Accessories", ref applyAccessories))
        {
            config.SuperKrangleApplyAccessories = applyAccessories;
            config.Save();
        }
        ImGui.SameLine();
        var applyWeapons = config.SuperKrangleApplyWeapons;
        if (ImGui.Checkbox("Weapons", ref applyWeapons))
        {
            config.SuperKrangleApplyWeapons = applyWeapons;
            config.Save();
        }
    }

    private void DrawSoulThiefTab(Configuration config)
    {
        ImGui.Spacing();
        ImGui.Text("Soul Thief");
        ImGui.Separator();

        var soulThiefEnabled = config.SoulThiefEnabled;
        if (ImGui.Checkbox("Enable Soul Thief", ref soulThiefEnabled))
        {
            config.SoulThiefEnabled = soulThiefEnabled;
            config.Save();
        }

        ImGui.BeginDisabled(!config.SoulThiefEnabled);

        var capturePlayers = config.SoulThiefCapturePlayers;
        if (ImGui.Checkbox("Capture Players", ref capturePlayers))
        {
            config.SoulThiefCapturePlayers = capturePlayers;
            config.Save();
        }

        var captureNpcs = config.SoulThiefCaptureNpcs;
        if (ImGui.Checkbox("Capture NPCs", ref captureNpcs))
        {
            config.SoulThiefCaptureNpcs = captureNpcs;
            config.Save();
        }

        var captureChocobos = config.SoulThiefCaptureChocobos;
        if (ImGui.Checkbox("Capture Chocobos", ref captureChocobos))
        {
            config.SoulThiefCaptureChocobos = captureChocobos;
            config.Save();
        }

        var intervalSeconds = config.SoulThiefCaptureIntervalSeconds;
        if (ImGui.SliderInt("Capture Interval", ref intervalSeconds, Configuration.MinSoulThiefCaptureIntervalSeconds, Configuration.MaxSoulThiefCaptureIntervalSeconds))
        {
            config.SoulThiefCaptureIntervalSeconds = intervalSeconds;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Seconds between Soul Thief capture passes. Appearance scanning still runs on Krangler's 5-second cadence.");

        ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Text($"Last capture: {config.SoulThiefLastCapturedPlayers} players, {config.SoulThiefLastCapturedNpcs} NPCs, {config.SoulThiefLastCapturedChocobos} chocobos");
        ImGui.TextWrapped($"Preset folders: {plugin.GlamourerPresetService.UserPresetsDir}\\players, \\npcs, \\chocobos");
    }

    private void DrawDebugTab(Configuration config)
    {
        ImGui.Spacing();
        ImGui.Text("Debug");
        ImGui.Separator();

        if (!plugin.ShowDebugOptions)
        {
            ImGui.TextDisabled("Debug controls hidden. Use /kr debug to toggle.");
            return;
        }

        var disableEventOverride = config.DisableDateBasedSuperKrangleEvent;
        if (ImGui.Checkbox("Disable date-based Wuk Lamat auto-event", ref disableEventOverride))
            plugin.SetDateBasedSuperKrangleEventSuppressed(disableEventOverride);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Suppress the March 31 through April 2 Wuk Lamat auto-event so normal Super Krangle testing is possible.");

        if (plugin.IsDateBasedSuperKrangleWindowActive)
        {
            var message = plugin.IsDateBasedSuperKrangleEventCurrentlyForced
                ? "The date-based Wuk Lamat override is currently active."
                : "The date-based Wuk Lamat override is currently suppressed by debug settings.";
            ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.35f, 1.0f), message);
        }
    }

    private static void DrawStatus(Configuration config)
    {
        if (config.Enabled)
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "Status: KRANGLING ACTIVE");
        else
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Status: Disabled");
    }

    public void QueueResetToOrigin()
    {
        queuedPosition = new Vector2(1f, 1f);
        queuedRandomVisibleJump = false;
    }

    public void QueueRandomVisibleJump()
    {
        queuedPosition = null;
        queuedRandomVisibleJump = true;
    }

    private void ApplyQueuedWindowPlacement()
    {
        if (!queuedPosition.HasValue && !queuedRandomVisibleJump)
            return;

        var targetPosition = queuedPosition ?? BuildRandomVisiblePosition();
        ImGui.SetWindowPos(targetPosition, ImGuiCond.Always);

        queuedPosition = null;
        queuedRandomVisibleJump = false;
    }

    private Vector2 BuildRandomVisiblePosition()
    {
        var viewport = ImGui.GetMainViewport();
        var workPos = viewport.WorkPos;
        var workSize = viewport.WorkSize;
        var windowSize = ImGui.GetWindowSize();

        var fallbackSize = Size ?? new Vector2(520f, 760f);
        var width = windowSize.X > 0f ? windowSize.X : fallbackSize.X;
        var height = windowSize.Y > 0f ? windowSize.Y : fallbackSize.Y;
        var margin = 24f;

        var minX = workPos.X + margin;
        var minY = workPos.Y + margin;
        var maxX = MathF.Max(minX, workPos.X + workSize.X - width - margin);
        var maxY = MathF.Max(minY, workPos.Y + workSize.Y - height - margin);

        if (maxX <= minX || maxY <= minY)
            return new Vector2(1f, 1f);

        var x = minX + (Random.Shared.NextSingle() * (maxX - minX));
        var y = minY + (Random.Shared.NextSingle() * (maxY - minY));
        return new Vector2(x, y);
    }

    private bool DrawIconInputs(string label, ref string value, string fallback)
    {
        var updated = false;
        var glyph = value;
        ImGui.SetNextItemWidth(80);
        if (ImGui.InputText($"{label} Icon", ref glyph, 8))
        {
            value = SanitizeIconInput(glyph, fallback);
            updated = true;
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"Shown when Krangler is {label.ToLowerInvariant()}");

        var code = FormatIconCode(value);
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputText($"{label} Icon Code", ref code, 64))
        {
            var parsed = ParseIconCode(code, value);
            value = SanitizeIconInput(parsed, fallback);
            updated = true;
        }

        return updated;
    }

    private static string SanitizeIconInput(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var trimmed = value.Trim();
        return trimmed.Length > 3 ? trimmed.Substring(0, 3) : trimmed;
    }

    private static string FormatIconCode(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append("\\u");
            sb.Append(rune.Value.ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static string ParseIconCode(string input, string fallback)
    {
        if (string.IsNullOrWhiteSpace(input))
            return fallback;

        var parts = input.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            if (sb.Length >= 3) break;

            var token = part.Trim();
            if (token.StartsWith("\\u", StringComparison.OrdinalIgnoreCase))
                token = token[2..];
            else if (token.StartsWith("u", StringComparison.OrdinalIgnoreCase))
                token = token[1..];
            else if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                token = token[2..];

            if (int.TryParse(token, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var codepoint))
            {
                sb.Append(char.ConvertFromUtf32(codepoint));
            }
        }

        return sb.Length == 0 ? fallback : sb.ToString();
    }

    private static void HelpMarker(string desc)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20.0f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    private bool DrawPresetSelectionCombo(string label, ref string value, IReadOnlyList<string> presetNames, bool includeUseGlobal)
        => DrawPresetSelectionCombo(label, ref value, presetNames, includeUseGlobal, true);

    private bool DrawPresetSelectionCombo(string label, ref string value, IReadOnlyList<string> presetNames, bool includeUseGlobal, bool includeRandom)
    {
        var fallbackPreview = includeUseGlobal ? "Use Global" : includeRandom ? "Random" : "Select preset";
        var preview = string.IsNullOrWhiteSpace(value)
            ? fallbackPreview
            : value;
        var changed = false;

        if (ImGui.BeginCombo(label, preview))
        {
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint($"##PresetSearch_{label}", "Search presets...", ref presetSearch, 128);
            ImGui.Separator();

            if (includeUseGlobal)
            {
                changed |= DrawSelectionOption("Use Global", ref value);
            }

            if (includeRandom)
            {
                changed |= DrawSelectionOption("Random", ref value);
            }

            var filteredPresetNames = string.IsNullOrWhiteSpace(presetSearch)
                ? presetNames
                : presetNames.Where(presetName =>
                    presetName.Contains(presetSearch, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var presetName in filteredPresetNames)
            {
                changed |= DrawSelectionOption(presetName, ref value);
            }

            if (!filteredPresetNames.Any())
                ImGui.TextDisabled("No presets match the current search.");

            ImGui.EndCombo();
        }

        return changed;
    }

    private void DrawAmongusSection(Configuration config, IReadOnlyList<string> presetNames)
    {
        ImGui.Spacing();
        ImGui.Text("Amongus");
        ImGui.Separator();

        var amongusEnabled = config.AmongusEnabled;
        if (ImGui.Checkbox("Amongus", ref amongusEnabled))
        {
            config.AmongusEnabled = amongusEnabled;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Replace exact battle and event NPC names with imported local presets.");
        }

        ImGui.BeginDisabled(!config.AmongusEnabled);

        ImGui.TextDisabled($"{config.AmongusNpcReplacements.Count}/{Configuration.MaxAmongusNpcReplacements}");
        ImGui.SameLine();

        if (config.AmongusNpcReplacements.Count < Configuration.MaxAmongusNpcReplacements)
        {
            if (ImGui.SmallButton("+"))
            {
                config.AmongusNpcReplacements.Add(new AmongusNpcReplacement());
                config.Save();
            }
        }
        else
        {
            ImGui.TextDisabled("+");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Maximum 100 NPC replacements.");
        }

        var removeIndex = -1;
        for (var i = 0; i < config.AmongusNpcReplacements.Count; i++)
        {
            var replacement = config.AmongusNpcReplacements[i];
            ImGui.PushID(i);

            var rowEnabled = replacement.Enabled;
            if (ImGui.Checkbox("##AmongusRowEnabled", ref rowEnabled))
            {
                replacement.Enabled = rowEnabled;
                config.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Enable this exact NPC replacement.");
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(150f);
            var npcName = replacement.NpcName ?? string.Empty;
            if (ImGui.InputTextWithHint("##AmongusNpcName", "NPC name", ref npcName, 64))
            {
                replacement.NpcName = npcName;
                config.Save();
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(220f);
            var presetKey = replacement.PresetKey ?? string.Empty;
            if (DrawPresetSelectionCombo("Preset##AmongusPreset", ref presetKey, presetNames, false, false))
            {
                replacement.PresetKey = presetKey;
                config.Save();
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("-"))
                removeIndex = i;

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            config.AmongusNpcReplacements.RemoveAt(removeIndex);
            config.Save();
        }

        ImGui.EndDisabled();
    }

    private static bool DrawSelectionOption(string option, ref string value)
    {
        var isSelected = string.Equals(value, option, StringComparison.OrdinalIgnoreCase);
        if (!ImGui.Selectable(option, isSelected))
            return false;

        value = option;
        return true;
    }

    private static bool EnsureSlotSelections(Configuration config)
    {
        var changed = false;

        while (config.SuperKranglePartySlotSelections.Count > 8)
        {
            config.SuperKranglePartySlotSelections.RemoveAt(config.SuperKranglePartySlotSelections.Count - 1);
            changed = true;
        }

        while (config.SuperKranglePartySlotSelections.Count < 8)
        {
            config.SuperKranglePartySlotSelections.Add("Use Global");
            changed = true;
        }

        return changed;
    }

    private static string GetPartySlotLabel(int index)
    {
        if (index == 0)
        {
            var localName = Plugin.ObjectTable.LocalPlayer?.Name.ToString();
            return string.IsNullOrWhiteSpace(localName) ? "You" : $"You ({localName})";
        }

        if (index < Plugin.PartyList.Length)
        {
            var memberName = Plugin.PartyList[index]?.Name.ToString();
            if (!string.IsNullOrWhiteSpace(memberName))
                return $"Party {index + 1} ({memberName})";
        }

        return $"Party {index + 1}";
    }

    public void Dispose() { }
}
