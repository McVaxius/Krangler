using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace Krangler;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int MaxAmongusNpcReplacements = 100;
    public const string DefaultAmongusNpcName = "Alpha";
    public const string DefaultAmongusPresetKey = "e97d1e17-9247-46aa-a9ad-b942ab905d31";

    public int Version { get; set; } = 1;

    // Master toggle
    public bool Enabled { get; set; } = false;

    // Feature toggles (all enabled by default)
    public bool KrangleNames { get; set; } = true;
    public bool KrangleChat { get; set; } = true;
    public bool KrangleGenders { get; set; } = true;
    public bool KrangleRaces { get; set; } = true;
    public bool KrangleAppearance { get; set; } = true;
    public bool KrangleNpcs { get; set; } = false;
    public bool KrangleChocobos { get; set; } = false;
    public bool KrangleMinions { get; set; } = false;
    public bool SkipSelfKrangling { get; set; } = false;
    public string CustomSelfDisplayName { get; set; } = string.Empty;

    // Exact NPC preset replacements
    public bool AmongusEnabled { get; set; } = true;
    public bool AmongusDefaultSeeded { get; set; } = true;
    public List<AmongusNpcReplacement> AmongusNpcReplacements { get; set; } = CreateDefaultAmongusNpcReplacements();

    // Special mode (disabled by default)
    public bool SuperKrangleMaster4000 { get; set; } = false;
    public string SuperKrangleSelection { get; set; } = "Random";
    public bool SuperKrangleNpcs { get; set; } = false;
    public string SuperKrangleNpcSelection { get; set; } = "Random";
    public bool SuperKrangleChocobos { get; set; } = false;
    public string SuperKrangleChocoboSelection { get; set; } = "Random";
    public bool SuperKrangleMinions { get; set; } = false;
    public string SuperKrangleMinionSelection { get; set; } = "Random";
    public System.Collections.Generic.List<string> SuperKranglePartySlotSelections { get; set; } = new()
    {
        "Use Global",
        "Use Global",
        "Use Global",
        "Use Global",
        "Use Global",
        "Use Global",
        "Use Global",
        "Use Global",
    };
    public bool SuperKrangleApplyAppearance { get; set; } = true;
    public bool SuperKrangleApplyHead { get; set; } = true;
    public bool SuperKrangleApplyBody { get; set; } = true;
    public bool SuperKrangleApplyHands { get; set; } = true;
    public bool SuperKrangleApplyLegs { get; set; } = true;
    public bool SuperKrangleApplyFeet { get; set; } = true;
    public bool SuperKrangleApplyAccessories { get; set; } = false;
    public bool SuperKrangleApplyWeapons { get; set; } = false;
    public int SuperKrangleMaxPlayersPerCycle { get; set; } = 8;
    public int SuperKrangleBaseRedrawDelayFrames { get; set; } = 2;
    public bool ShowDebugOptions { get; set; } = false;
    public bool DisableDateBasedSuperKrangleEvent { get; set; } = false;

    // DTR bar settings
    public bool DtrBarEnabled { get; set; } = true;
    public int DtrBarMode { get; set; } = 0; // 0=text-only, 1=icon+text, 2=icon-only
    public string DtrIconEnabled { get; set; } = "\uE03C";
    public string DtrIconDisabled { get; set; } = "\uE03D";

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }

    public bool SanitizeAmongusNpcReplacements()
    {
        var changed = false;

        if (AmongusNpcReplacements == null)
        {
            AmongusNpcReplacements = CreateDefaultAmongusNpcReplacements();
            AmongusDefaultSeeded = true;
            return true;
        }

        if (!AmongusDefaultSeeded)
        {
            if (AmongusNpcReplacements.Count == 0)
            {
                AmongusNpcReplacements.Add(CreateDefaultAmongusNpcReplacement());
                changed = true;
            }

            AmongusDefaultSeeded = true;
            changed = true;
        }

        for (var i = AmongusNpcReplacements.Count - 1; i >= 0; i--)
        {
            if (AmongusNpcReplacements[i] != null)
                continue;

            AmongusNpcReplacements.RemoveAt(i);
            changed = true;
        }

        while (AmongusNpcReplacements.Count > MaxAmongusNpcReplacements)
        {
            AmongusNpcReplacements.RemoveAt(AmongusNpcReplacements.Count - 1);
            changed = true;
        }

        foreach (var replacement in AmongusNpcReplacements)
        {
            var npcName = replacement.NpcName?.Trim() ?? string.Empty;
            if (!string.Equals(replacement.NpcName, npcName, StringComparison.Ordinal))
            {
                replacement.NpcName = npcName;
                changed = true;
            }

            var presetKey = replacement.PresetKey?.Trim() ?? string.Empty;
            if (!string.Equals(replacement.PresetKey, presetKey, StringComparison.Ordinal))
            {
                replacement.PresetKey = presetKey;
                changed = true;
            }
        }

        return changed;
    }

    public static AmongusNpcReplacement CreateDefaultAmongusNpcReplacement()
        => new()
        {
            Enabled = true,
            NpcName = DefaultAmongusNpcName,
            PresetKey = DefaultAmongusPresetKey,
        };

    private static List<AmongusNpcReplacement> CreateDefaultAmongusNpcReplacements()
        => new() { CreateDefaultAmongusNpcReplacement() };
}

[Serializable]
public class AmongusNpcReplacement
{
    public bool Enabled { get; set; } = true;
    public string NpcName { get; set; } = string.Empty;
    public string PresetKey { get; set; } = string.Empty;
}
