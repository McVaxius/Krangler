using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace Krangler.Services;

public class AppearanceService
{
    private readonly IPluginLog log;
    private readonly IObjectTable objectTable;
    private readonly Configuration configuration;

    // Track which players we've already applied appearance changes to
    private readonly HashSet<ulong> appliedPlayers = new();

    // Valid race/tribe combinations: Race ID -> (Tribe1, Tribe2)
    private static readonly Dictionary<byte, (byte, byte)> RaceTribeMap = new()
    {
        { 1, (1, 2) },   // Hyur: Midlander, Highlander
        { 2, (3, 4) },   // Miqo'te: Seeker, Keeper
        { 3, (5, 6) },   // Lalafell: Plainsfolk, Dunesfolk
        { 4, (7, 8) },   // Roegadyn: Sea Wolves, Hellsguard
        { 5, (9, 10) },  // Elezen: Wildwood, Duskwight
        { 6, (11, 12) }, // Au Ra: Raen, Xaela
        { 7, (13, 14) }, // Hrothgar: Helions, The Lost
        { 8, (15, 16) }, // Viera: Rava, Veena
    };

    public AppearanceService(IPluginLog log, IObjectTable objectTable, Configuration configuration)
    {
        this.log = log;
        this.objectTable = objectTable;
        this.configuration = configuration;
    }

    /// <summary>
    /// Generate a randomized race and tribe for a given player name.
    /// Returns (race, tribe, gender) tuple.
    /// </summary>
    public static (byte race, byte tribe, byte gender) GetRandomRaceGender(string playerName)
    {
        var hash = GetStableHash(playerName);
        var rng = new Random(hash);

        var raceId = (byte)(rng.Next(1, 9)); // 1-8
        var tribes = RaceTribeMap[raceId];
        var tribe = rng.Next(2) == 0 ? tribes.Item1 : tribes.Item2;
        var gender = (byte)rng.Next(0, 2); // 0=Male, 1=Female

        return (raceId, tribe, gender);
    }

    /// <summary>
    /// Generate randomized appearance customize bytes for a given player name.
    /// Returns a partial set of customize indices and values.
    /// </summary>
    public static Dictionary<int, byte> GetRandomAppearance(string playerName, byte race, byte gender)
    {
        var hash = GetStableHash(playerName + "_appearance");
        var rng = new Random(hash);

        var appearance = new Dictionary<int, byte>
        {
            { 3, (byte)rng.Next(0, 101) },    // Height: 0-100
            { 5, (byte)rng.Next(1, 9) },       // Face: 1-8
            { 6, (byte)rng.Next(1, 50) },      // Hairstyle: 1-49 (FIXED: was index 7)
            { 7, (byte)rng.Next(0, 8) },      // Highlights: 0-7 (NEW)
            { 9, (byte)rng.Next(1, 192) },     // Skin Tone: varies
            { 10, (byte)rng.Next(1, 192) },    // Right Eye Color
            { 11, (byte)rng.Next(1, 192) },    // Hair Color
            { 15, (byte)rng.Next(1, 7) },      // Eyebrows
            { 17, (byte)rng.Next(1, 7) },      // Eyes
            { 18, (byte)rng.Next(1, 7) },      // Nose
            { 19, (byte)rng.Next(1, 7) },      // Jaw Shape
            { 20, (byte)rng.Next(1, 7) },      // Mouth
            { 25, (byte)rng.Next(0, 101) },    // Bust Size: 0-100
        };

        return appearance;
    }

    /// <summary>
    /// Check if we've already applied appearance changes to this player.
    /// </summary>
    public bool IsApplied(ulong entityId) => appliedPlayers.Contains(entityId);

    /// <summary>
    /// Mark a player as having had appearance changes applied.
    /// </summary>
    public void MarkApplied(ulong entityId) => appliedPlayers.Add(entityId);

    /// <summary>
    /// Clear tracking state when plugin is disabled.
    /// </summary>
    public void Reset()
    {
        appliedPlayers.Clear();
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
}
