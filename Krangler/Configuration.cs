using Dalamud.Configuration;
using Krangler.Models;
using System;
using System.Collections.Generic;

namespace Krangler;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int MaxAmongusNpcReplacements = 100;
    public const string DefaultAmongusNpcName = "Alpha";
    public const string DefaultAmongusPresetKey = "e97d1e17-9247-46aa-a9ad-b942ab905d31";
    public const string DefaultImaginaryFrenName = "Golden Sven";
    public const string DefaultImaginaryFrenPresetKey = "e97d1e17-9247-46aa-a9ad-b942ab905d31";
    public const int MinSoulThiefCaptureIntervalSeconds = 5;
    public const int MaxSoulThiefCaptureIntervalSeconds = 300;

    public int Version { get; set; } = 2;

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

    // Exact player race/clan/gender rules (disabled by default)
    public bool RaceGenderRulesEnabled { get; set; } = false;
    public List<PlayerIdentityRule> PlayerIdentityRules { get; set; } = new();

    // Exact NPC preset replacements
    public bool AmongusEnabled { get; set; } = false;
    public bool AmongusDefaultSeeded { get; set; } = true;
    public List<AmongusNpcReplacement> AmongusNpcReplacements { get; set; } = new();

    // Soul Thief preset capture (disabled by default)
    public bool SoulThiefEnabled { get; set; } = false;
    public bool SoulThiefCapturePlayers { get; set; } = false;
    public bool SoulThiefCaptureNpcs { get; set; } = false;
    public bool SoulThiefCaptureChocobos { get; set; } = false;
    public int SoulThiefCaptureIntervalSeconds { get; set; } = MinSoulThiefCaptureIntervalSeconds;
    public int SoulThiefLastCapturedPlayers { get; set; } = 0;
    public int SoulThiefLastCapturedNpcs { get; set; } = 0;
    public int SoulThiefLastCapturedChocobos { get; set; } = 0;

    // One local-only spawned follower actor.
    public bool ImaginaryFrenEnabled { get; set; } = false;
    public string ImaginaryFrenName { get; set; } = DefaultImaginaryFrenName;
    public string ImaginaryFrenPresetKey { get; set; } = DefaultImaginaryFrenPresetKey;

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

    public static Configuration CreateFirstRun()
    {
        var configuration = new Configuration();
        configuration.AmongusNpcReplacements.Add(CreateDefaultAmongusNpcReplacement());
        configuration.AmongusDefaultSeeded = true;
        return configuration;
    }

    public bool Sanitize()
    {
        var changed = false;
        if (Version != 2)
        {
            Version = 2;
            changed = true;
        }

        changed |= SanitizePlayerIdentityRules();
        changed |= SanitizeAmongusNpcReplacements();
        changed |= SanitizeNonPlayerAppearanceSafety();
        changed |= SanitizeSoulThiefSettings();
        changed |= SanitizeImaginaryFrenSettings();
        return changed;
    }

    public bool SanitizePlayerIdentityRules()
    {
        if (PlayerIdentityRules == null)
        {
            PlayerIdentityRules = new List<PlayerIdentityRule>();
            return true;
        }

        var sanitized = new List<PlayerIdentityRule>(Math.Min(PlayerIdentityRules.Count, PlayerIdentityCatalog.Entries.Count));
        var seenSources = new HashSet<int>();

        foreach (var source in PlayerIdentityRules)
        {
            if (!PlayerIdentityCatalog.TryNormalize(source, out var normalized))
                continue;

            var sourceKey = PlayerIdentityCatalog.GetSourceKey(
                normalized.SourceRace,
                normalized.SourceClan,
                normalized.SourceGender);
            if (!seenSources.Add(sourceKey))
                continue;

            sanitized.Add(normalized);
        }

        var changed = sanitized.Count != PlayerIdentityRules.Count;
        if (!changed)
        {
            for (var index = 0; index < sanitized.Count; index++)
            {
                if (PlayerIdentityCatalog.ContentEquals(PlayerIdentityRules[index], sanitized[index]))
                    continue;

                changed = true;
                break;
            }
        }

        if (changed)
            PlayerIdentityRules = sanitized;

        return changed;
    }

    private bool SanitizeNonPlayerAppearanceSafety()
    {
        var changed = false;

        if (KrangleNpcs)
        {
            KrangleNpcs = false;
            changed = true;
        }

        if (KrangleChocobos)
        {
            KrangleChocobos = false;
            changed = true;
        }

        if (KrangleMinions)
        {
            KrangleMinions = false;
            changed = true;
        }

        if (SuperKrangleNpcs)
        {
            SuperKrangleNpcs = false;
            changed = true;
        }

        if (SuperKrangleChocobos)
        {
            SuperKrangleChocobos = false;
            changed = true;
        }

        if (SuperKrangleMinions)
        {
            SuperKrangleMinions = false;
            changed = true;
        }

        return changed;
    }

    public bool SanitizeAmongusNpcReplacements()
    {
        var changed = false;

        if (AmongusNpcReplacements == null)
        {
            AmongusNpcReplacements = new List<AmongusNpcReplacement>();
            AmongusDefaultSeeded = true;
            return true;
        }

        if (!AmongusDefaultSeeded)
        {
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

    private bool SanitizeSoulThiefSettings()
    {
        var changed = false;
        var interval = Math.Clamp(
            SoulThiefCaptureIntervalSeconds,
            MinSoulThiefCaptureIntervalSeconds,
            MaxSoulThiefCaptureIntervalSeconds);

        if (SoulThiefCaptureIntervalSeconds != interval)
        {
            SoulThiefCaptureIntervalSeconds = interval;
            changed = true;
        }

        if (SoulThiefLastCapturedPlayers < 0)
        {
            SoulThiefLastCapturedPlayers = 0;
            changed = true;
        }

        if (SoulThiefLastCapturedNpcs < 0)
        {
            SoulThiefLastCapturedNpcs = 0;
            changed = true;
        }

        if (SoulThiefLastCapturedChocobos < 0)
        {
            SoulThiefLastCapturedChocobos = 0;
            changed = true;
        }

        return changed;
    }

    private bool SanitizeImaginaryFrenSettings()
    {
        var changed = false;

        var name = ImaginaryFrenName?.Trim() ?? string.Empty;
        if (name.Length > 31)
            name = name[..31];
        if (string.IsNullOrWhiteSpace(name))
            name = DefaultImaginaryFrenName;
        if (!string.Equals(ImaginaryFrenName, name, StringComparison.Ordinal))
        {
            ImaginaryFrenName = name;
            changed = true;
        }

        var presetKey = ImaginaryFrenPresetKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(presetKey))
            presetKey = DefaultImaginaryFrenPresetKey;
        if (!string.Equals(ImaginaryFrenPresetKey, presetKey, StringComparison.Ordinal))
        {
            ImaginaryFrenPresetKey = presetKey;
            changed = true;
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
}

[Serializable]
public class AmongusNpcReplacement
{
    public bool Enabled { get; set; } = true;
    public string NpcName { get; set; } = string.Empty;
    public string PresetKey { get; set; } = string.Empty;
}
