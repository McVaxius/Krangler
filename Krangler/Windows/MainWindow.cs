using System;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace Krangler.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Krangler###KranglerMain")
    {
        this.plugin = plugin;

        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(400, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var config = plugin.Configuration;

        // Version header + Ko-fi button
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

        // Master enable/disable
        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enable Krangler", ref enabled))
        {
            config.Enabled = enabled;
            if (!enabled)
            {
                // Clear caches when disabling
                Services.KrangleService.ClearCache();
            }
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Master toggle — enables/disables all krangling");
        }

        ImGui.Spacing();

        // DTR Bar section
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
            ImGui.SetTooltip("Show Krangler status in the server info bar. Click the DTR entry to toggle enable/disable.");
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
            HelpMarker("Customize the glyphs used for enabled/disabled icon modes.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy Icon Guide Link"))
            {
                ImGui.SetClipboardText("https://na.finalfantasyxiv.com/lodestone/character/22423564/blog/4393835");
                Plugin.Log.Info("Copied icon guide link to clipboard");
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Copies the Lodestone blog link with suggested glyphs");

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

        // Feature toggles
        ImGui.Text("Krangling Options");
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Options are processed in the order shown.");

        ImGui.Spacing();

        // Krangle Names
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
            ImGui.SetTooltip("Randomize all visible player names and party list names.\nSame player always gets the same fake name per session.");
        }

        // Krangle Genders
        var krangleGenders = config.KrangleGenders;
        if (ImGui.Checkbox("Krangle Genders", ref krangleGenders))
        {
            config.KrangleGenders = krangleGenders;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Randomize genders for all visible player characters.\nDirect memory modification — no external plugins needed.");
        }

        // Krangle Races
        var krangleRaces = config.KrangleRaces;
        if (ImGui.Checkbox("Krangle Races", ref krangleRaces))
        {
            config.KrangleRaces = krangleRaces;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Randomize races (including subraces) for all visible player characters.\nDirect memory modification — no external plugins needed.");
        }

        // Krangle Appearance
        var krangleAppearance = config.KrangleAppearance;
        if (ImGui.Checkbox("Krangle Appearance", ref krangleAppearance))
        {
            config.KrangleAppearance = krangleAppearance;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Randomize hair, face, eyes, etc. for all visible player characters.\nDirect memory modification — no external plugins needed.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Super Krangle Master 4000
        /*
        var superKrangle = config.SuperKrangleMaster4000;
        if (ImGui.Checkbox("Super Krangle Master 4000", ref superKrangle))
        {
            config.SuperKrangleMaster4000 = superKrangle;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "OVERRIDE ALL OPTIONS — Apply full Glamourer presets to players!\n" +
                "Replaces appearance AND equipment (except weapons).\n\n" +
                $"Presets loaded: {plugin.GlamourerPresetService.PresetCount}\n" +
                "Import: Drop .json Glamourer preset files into your presets folder.\n" +
                "(Export presets from Glamourer, then copy the .json files)");
        }
        if (superKrangle)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), $"({plugin.GlamourerPresetService.PresetCount} presets)");
        }
        */

        ImGui.Spacing();

        // Status summary
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

    public void Dispose() { }
}
