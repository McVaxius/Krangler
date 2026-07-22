using System;
using System.Collections.Generic;

namespace Krangler.Models;

public enum PlayerIdentityRuleAction
{
    Hide = 0,
    Replace = 1,
}

[Serializable]
public sealed class PlayerIdentityRule
{
    public bool Active { get; set; }
    public byte SourceRace { get; set; }
    public byte SourceClan { get; set; }
    public byte SourceGender { get; set; }
    public PlayerIdentityRuleAction Action { get; set; } = PlayerIdentityRuleAction.Hide;
    public byte ReplacementClan { get; set; }
    public byte ReplacementGender { get; set; }

    public PlayerIdentityRule Clone()
        => new()
        {
            Active = Active,
            SourceRace = SourceRace,
            SourceClan = SourceClan,
            SourceGender = SourceGender,
            Action = Action,
            ReplacementClan = ReplacementClan,
            ReplacementGender = ReplacementGender,
        };
}

public readonly record struct PlayerIdentityDescriptor(
    byte Race,
    string RaceName,
    byte Clan,
    string ClanName,
    byte Gender,
    string GenderName);

public static class PlayerIdentityCatalog
{
    private static readonly (byte Race, string RaceName, byte Clan, string ClanName)[] Clans =
    {
        (1, "Hyur", 1, "Midlander"),
        (1, "Hyur", 2, "Highlander"),
        (2, "Elezen", 3, "Wildwood"),
        (2, "Elezen", 4, "Duskwight"),
        (3, "Lalafell", 5, "Plainsfolk"),
        (3, "Lalafell", 6, "Dunesfolk"),
        (4, "Miqo'te", 7, "Seeker of the Sun"),
        (4, "Miqo'te", 8, "Keeper of the Moon"),
        (5, "Roegadyn", 9, "Sea Wolves"),
        (5, "Roegadyn", 10, "Hellsguard"),
        (6, "Au Ra", 11, "Raen"),
        (6, "Au Ra", 12, "Xaela"),
        (7, "Hrothgar", 13, "Helions"),
        (7, "Hrothgar", 14, "The Lost"),
        (8, "Viera", 15, "Rava"),
        (8, "Viera", 16, "Veena"),
    };

    public static IReadOnlyList<PlayerIdentityDescriptor> Entries { get; } = BuildEntries();

    public static IReadOnlyList<(byte Clan, string Name)> ClanOptions { get; } = BuildClanOptions();

    public static bool IsValidSource(byte race, byte clan, byte gender)
        => gender <= 1 && TryGetRaceForClan(clan, out var derivedRace) && derivedRace == race;

    public static bool IsValidClan(byte clan)
        => TryGetRaceForClan(clan, out _);

    public static bool TryGetRaceForClan(byte clan, out byte race)
    {
        race = clan switch
        {
            1 or 2 => 1,
            3 or 4 => 2,
            5 or 6 => 3,
            7 or 8 => 4,
            9 or 10 => 5,
            11 or 12 => 6,
            13 or 14 => 7,
            15 or 16 => 8,
            _ => 0,
        };

        return race != 0;
    }

    public static string GetClanName(byte clan)
    {
        foreach (var option in Clans)
        {
            if (option.Clan == clan)
                return option.ClanName;
        }

        return "Unknown";
    }

    public static string GetGenderName(byte gender)
        => gender == 0 ? "Male" : gender == 1 ? "Female" : "Unknown";

    public static int GetSourceKey(byte race, byte clan, byte gender)
        => race << 16 | clan << 8 | gender;

    public static bool TryNormalize(PlayerIdentityRule? source, out PlayerIdentityRule normalized)
    {
        normalized = null!;
        if (source == null || !IsValidSource(source.SourceRace, source.SourceClan, source.SourceGender))
            return false;

        var replacementClan = IsValidClan(source.ReplacementClan)
            ? source.ReplacementClan
            : source.SourceClan;
        var replacementGender = source.ReplacementGender <= 1
            ? source.ReplacementGender
            : source.SourceGender;
        var action = Enum.IsDefined(source.Action)
            ? source.Action
            : PlayerIdentityRuleAction.Hide;

        normalized = new PlayerIdentityRule
        {
            Active = source.Active,
            SourceRace = source.SourceRace,
            SourceClan = source.SourceClan,
            SourceGender = source.SourceGender,
            Action = action,
            ReplacementClan = replacementClan,
            ReplacementGender = replacementGender,
        };
        return true;
    }

    public static List<PlayerIdentityRule> CreateDraftRules(IEnumerable<PlayerIdentityRule>? savedRules)
    {
        var savedBySource = new Dictionary<int, PlayerIdentityRule>();
        if (savedRules != null)
        {
            foreach (var savedRule in savedRules)
            {
                if (!TryNormalize(savedRule, out var normalized))
                    continue;

                var key = GetSourceKey(normalized.SourceRace, normalized.SourceClan, normalized.SourceGender);
                savedBySource.TryAdd(key, normalized);
            }
        }

        var result = new List<PlayerIdentityRule>(Entries.Count);
        foreach (var entry in Entries)
        {
            var key = GetSourceKey(entry.Race, entry.Clan, entry.Gender);
            if (savedBySource.TryGetValue(key, out var saved))
            {
                result.Add(saved.Clone());
                continue;
            }

            result.Add(new PlayerIdentityRule
            {
                Active = false,
                SourceRace = entry.Race,
                SourceClan = entry.Clan,
                SourceGender = entry.Gender,
                Action = PlayerIdentityRuleAction.Hide,
                ReplacementClan = entry.Clan,
                ReplacementGender = entry.Gender,
            });
        }

        return result;
    }

    public static bool ContentEquals(PlayerIdentityRule left, PlayerIdentityRule right)
        => left.Active == right.Active &&
           left.SourceRace == right.SourceRace &&
           left.SourceClan == right.SourceClan &&
           left.SourceGender == right.SourceGender &&
           left.Action == right.Action &&
           left.ReplacementClan == right.ReplacementClan &&
           left.ReplacementGender == right.ReplacementGender;

    private static IReadOnlyList<PlayerIdentityDescriptor> BuildEntries()
    {
        var result = new List<PlayerIdentityDescriptor>(32);
        foreach (var clan in Clans)
        {
            result.Add(new PlayerIdentityDescriptor(clan.Race, clan.RaceName, clan.Clan, clan.ClanName, 0, "Male"));
            result.Add(new PlayerIdentityDescriptor(clan.Race, clan.RaceName, clan.Clan, clan.ClanName, 1, "Female"));
        }

        return result;
    }

    private static IReadOnlyList<(byte Clan, string Name)> BuildClanOptions()
    {
        var result = new List<(byte Clan, string Name)>(Clans.Length);
        foreach (var clan in Clans)
            result.Add((clan.Clan, clan.ClanName));
        return result;
    }
}
