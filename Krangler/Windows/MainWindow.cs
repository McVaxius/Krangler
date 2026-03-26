using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Krangler.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

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
        var config = plugin.Configuration;
        var presetNames = plugin.GlamourerPresetService.GetPresetNames();
        if (EnsureSlotSelections(config))
        {
            config.Save();
        }

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
        {
            ImGui.SetTooltip("Support development on Ko-fi");
        }

        ImGui.Separator();

        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enable Krangler", ref enabled))
        {
            config.Enabled = enabled;
            if (!enabled)
                Services.KrangleService.ClearCache();
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Master toggle - enables or disables all krangling.");
        }

        ImGui.Spacing();
        ImGui.Text("DTR Bar");
        ImGui.Separator();

        var dtrEnabled = config.DtrBarEnabled;
        if (ImGui.Checkbox("Show DTR Bar Entry", ref dtrEnabled))
        {
            config.DtrBarEnabled = dtrEnabled;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Show Krangler status in the server info bar. Click the DTR entry to toggle enable or disable.");
        }

        if (config.DtrBarEnabled)
        {
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
            {
                ImGui.SetTooltip("Copies the Lodestone blog link with suggested glyphs.");
            }

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
        }

        ImGui.Spacing();
        ImGui.Text("Krangling Options");
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Options are processed in the order shown.");

        ImGui.Spacing();

        var krangleNames = config.KrangleNames;
        if (ImGui.Checkbox("Krangle Names", ref krangleNames))
        {
            config.KrangleNames = krangleNames;
            if (!krangleNames)
                Services.KrangleService.ClearCache();
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Randomize visible player names and party list names.");
        }

        var krangleChat = config.KrangleChat;
        if (ImGui.Checkbox("Krangle Chat", ref krangleChat))
        {
            config.KrangleChat = krangleChat;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Garble chat text for screenshot privacy.");
        }

        var krangleGenders = config.KrangleGenders;
        if (ImGui.Checkbox("Krangle Genders", ref krangleGenders))
        {
            config.KrangleGenders = krangleGenders;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Randomize genders for visible player characters.");
        }

        var krangleRaces = config.KrangleRaces;
        if (ImGui.Checkbox("Krangle Races", ref krangleRaces))
        {
            config.KrangleRaces = krangleRaces;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Randomize races and subraces for visible player characters.");
        }

        var krangleAppearance = config.KrangleAppearance;
        if (ImGui.Checkbox("Krangle Appearance", ref krangleAppearance))
        {
            config.KrangleAppearance = krangleAppearance;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Randomize hair, face, eyes, and other appearance fields.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

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

        if (superKrangle)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), $"({plugin.GlamourerPresetService.PresetCount} presets)");

            if (presetNames.Count == 0)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.75f, 0.3f, 1.0f), "No preset files are loaded. Built-in NPC looks will be used instead.");
            }

            var globalSelection = string.IsNullOrWhiteSpace(config.SuperKrangleSelection)
                ? "Random"
                : config.SuperKrangleSelection;
            if (DrawPresetSelectionCombo("Global Preset", ref globalSelection, presetNames, false))
            {
                config.SuperKrangleSelection = globalSelection;
                config.Save();
            }

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
            {
                ImGui.SetTooltip("Limit how many visible players are processed during one scan pass.");
            }

            var redrawDelay = config.SuperKrangleBaseRedrawDelayFrames;
            if (ImGui.SliderInt("Base Redraw Delay", ref redrawDelay, 1, 10))
            {
                config.SuperKrangleBaseRedrawDelayFrames = redrawDelay;
                config.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Base frame delay before the next queued redraw. Actual delay scales with crowd size.");
            }
        }

        ImGui.Spacing();

        if (config.Enabled)
        {
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "Status: KRANGLING ACTIVE");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Status: Disabled");
        }
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

    private static bool DrawPresetSelectionCombo(string label, ref string value, IReadOnlyList<string> presetNames, bool includeUseGlobal)
    {
        var preview = string.IsNullOrWhiteSpace(value)
            ? (includeUseGlobal ? "Use Global" : "Random")
            : value;
        var changed = false;

        if (ImGui.BeginCombo(label, preview))
        {
            if (includeUseGlobal)
            {
                changed |= DrawSelectionOption("Use Global", ref value);
            }

            changed |= DrawSelectionOption("Random", ref value);

            foreach (var presetName in presetNames)
            {
                changed |= DrawSelectionOption(presetName, ref value);
            }

            ImGui.EndCombo();
        }

        return changed;
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
