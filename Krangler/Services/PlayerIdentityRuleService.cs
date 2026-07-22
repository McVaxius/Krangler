using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Krangler.Models;
using DalamudObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using GameCustomizeData = FFXIVClientStructs.FFXIV.Client.Game.Character.CustomizeData;

namespace Krangler.Services;

public sealed class PlayerIdentityRuleService : IDisposable
{
    private const VisibilityFlags HiddenFlags = VisibilityFlags.Model | VisibilityFlags.Nameplate;

    private sealed class ActorIdentityState
    {
        public required ulong ObjectKey { get; init; }
        public required nint Address { get; init; }
        public required byte Race { get; init; }
        public required byte Clan { get; init; }
        public required byte Gender { get; init; }
    }

    private sealed class HiddenActorState
    {
        public required ulong ObjectKey { get; init; }
        public required nint Address { get; init; }
        public required VisibilityFlags OriginalHiddenFlags { get; init; }
    }

    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly Dictionary<ulong, ActorIdentityState> originalIdentities = new();
    private readonly Dictionary<ulong, HiddenActorState> hiddenActors = new();

    public PlayerIdentityRuleService(IObjectTable objectTable, IPluginLog log)
    {
        this.objectTable = objectTable;
        this.log = log;
    }

    public int HiddenActorCount => hiddenActors.Count;

    public static bool HasActiveReplacementRules(Configuration configuration)
    {
        if (!configuration.RaceGenderRulesEnabled || configuration.PlayerIdentityRules == null)
            return false;

        foreach (var rule in configuration.PlayerIdentityRules)
        {
            if (rule is { Active: true, Action: PlayerIdentityRuleAction.Replace })
                return true;
        }

        return false;
    }

    public static bool HasActiveRules(Configuration configuration)
    {
        if (!configuration.RaceGenderRulesEnabled || configuration.PlayerIdentityRules == null)
            return false;

        foreach (var rule in configuration.PlayerIdentityRules)
        {
            if (rule is { Active: true })
                return true;
        }

        return false;
    }

    public unsafe void Update(Configuration configuration)
    {
        var livePlayers = new Dictionary<ulong, IGameObject>();
        for (var index = 0; index < objectTable.Length; index++)
        {
            var obj = objectTable[index];
            if (obj == null || obj.ObjectKind != DalamudObjectKind.Pc || obj.Address == nint.Zero)
                continue;

            var objectKey = GetObjectKey(obj);
            if (objectKey == 0)
                continue;

            livePlayers[objectKey] = obj;
            CaptureOriginalIdentity(obj);
        }

        RemoveReplacedOrUnloadedActors(livePlayers);

        if (!configuration.Enabled || !HasActiveRules(configuration))
        {
            RestoreAll();
            PruneIdentityCache(livePlayers);
            return;
        }

        var stillHidden = new HashSet<ulong>();
        foreach (var (objectKey, obj) in livePlayers)
        {
            if (ShouldSkipSelf(configuration, obj) ||
                !TryGetMatchingRule(obj, configuration, out var rule) ||
                rule.Action != PlayerIdentityRuleAction.Hide)
            {
                RestoreActorIfHidden(objectKey, obj);
                continue;
            }

            HideActor(objectKey, obj);
            stillHidden.Add(objectKey);
        }

        foreach (var objectKey in new List<ulong>(hiddenActors.Keys))
        {
            if (stillHidden.Contains(objectKey))
                continue;

            livePlayers.TryGetValue(objectKey, out var obj);
            RestoreActorIfHidden(objectKey, obj);
        }

        PruneIdentityCache(livePlayers);
    }

    public unsafe bool CaptureOriginalIdentity(IGameObject? obj)
    {
        if (obj == null || obj.ObjectKind != DalamudObjectKind.Pc || obj.Address == nint.Zero)
            return false;

        var character = (Character*)obj.Address;
        if (character == null)
            return false;

        return CaptureOriginalIdentity(GetObjectKey(obj), obj.Address, &character->DrawData.CustomizeData);
    }

    public unsafe bool CaptureOriginalIdentity(ulong objectKey, nint address, GameCustomizeData* customize)
    {
        if (objectKey == 0 || address == nint.Zero || customize == null)
            return false;

        if (originalIdentities.TryGetValue(objectKey, out var existing))
        {
            if (existing.Address == address)
                return true;

            RestoreTrackedActorIfStillLive(objectKey, existing.Address);
            originalIdentities.Remove(objectKey);
        }

        originalIdentities[objectKey] = new ActorIdentityState
        {
            ObjectKey = objectKey,
            Address = address,
            Race = customize->Race,
            Clan = customize->Tribe,
            Gender = customize->Sex,
        };
        return true;
    }

    public bool TryGetMatchingRule(IGameObject? obj, Configuration configuration, out PlayerIdentityRule rule)
    {
        rule = null!;
        if (obj == null || obj.ObjectKind != DalamudObjectKind.Pc || obj.Address == nint.Zero ||
            !configuration.Enabled || !configuration.RaceGenderRulesEnabled ||
            ShouldSkipSelf(configuration, obj))
        {
            return false;
        }

        CaptureOriginalIdentity(obj);
        var objectKey = GetObjectKey(obj);
        if (!originalIdentities.TryGetValue(objectKey, out var identity) || identity.Address != obj.Address)
            return false;

        return TryFindRule(configuration, identity, out rule);
    }

    public bool TryGetMatchingRule(ulong gameObjectId, Configuration configuration, out PlayerIdentityRule rule)
    {
        rule = null!;
        if (gameObjectId == 0)
            return false;

        var obj = FindPlayerObject(gameObjectId);
        return TryGetMatchingRule(obj, configuration, out rule);
    }

    public bool ShouldReplace(IGameObject? obj, Configuration configuration)
        => TryGetMatchingRule(obj, configuration, out var rule) &&
           rule.Action == PlayerIdentityRuleAction.Replace;

    public bool ShouldReplace(ulong gameObjectId, Configuration configuration)
        => TryGetMatchingRule(gameObjectId, configuration, out var rule) &&
           rule.Action == PlayerIdentityRuleAction.Replace;

    public bool IsHidden(IGameObject? obj)
    {
        if (obj == null)
            return false;

        var objectKey = GetObjectKey(obj);
        return hiddenActors.TryGetValue(objectKey, out var hidden) && hidden.Address == obj.Address;
    }

    public unsafe void RestoreAll(bool clearIdentityCache = false)
    {
        if (hiddenActors.Count > 0)
        {
            var restored = 0;
            foreach (var obj in GetLivePlayerObjects())
            {
                var objectKey = GetObjectKey(obj);
                if (!hiddenActors.TryGetValue(objectKey, out var hidden) || hidden.Address != obj.Address)
                    continue;

                RestoreActor(obj, hidden);
                restored++;
            }

            if (restored > 0)
                log.Debug($"[Krangler] Restored {restored} identity-rule hidden actor(s).");
        }

        hiddenActors.Clear();
        if (clearIdentityCache)
            originalIdentities.Clear();
    }

    public void ResetIdentityCache()
    {
        RestoreAll();
        originalIdentities.Clear();
    }

    public void Dispose()
        => RestoreAll(clearIdentityCache: true);

    public static ulong GetObjectKey(IGameObject obj)
        => obj.GameObjectId != 0 ? obj.GameObjectId : obj.EntityId;

    private static bool TryFindRule(Configuration configuration, ActorIdentityState identity, out PlayerIdentityRule rule)
    {
        rule = null!;
        if (configuration.PlayerIdentityRules == null)
            return false;

        foreach (var candidate in configuration.PlayerIdentityRules)
        {
            if (candidate == null || !candidate.Active ||
                candidate.SourceRace != identity.Race ||
                candidate.SourceClan != identity.Clan ||
                candidate.SourceGender != identity.Gender)
            {
                continue;
            }

            rule = candidate;
            return true;
        }

        return false;
    }

    private bool ShouldSkipSelf(Configuration configuration, IGameObject obj)
    {
        if (!configuration.SkipSelfKrangling)
            return false;

        var localPlayer = objectTable.LocalPlayer;
        return localPlayer != null &&
               (localPlayer.Address == obj.Address || GetObjectKey(localPlayer) == GetObjectKey(obj));
    }

    private unsafe void HideActor(ulong objectKey, IGameObject obj)
    {
        var character = (Character*)obj.Address;
        if (character == null)
            return;

        if (!hiddenActors.TryGetValue(objectKey, out var hidden) || hidden.Address != obj.Address)
        {
            var originalHiddenFlags = character->GameObject.RenderFlags & HiddenFlags;
            if (originalHiddenFlags == HiddenFlags)
                return;

            hidden = new HiddenActorState
            {
                ObjectKey = objectKey,
                Address = obj.Address,
                OriginalHiddenFlags = originalHiddenFlags,
            };
            hiddenActors[objectKey] = hidden;
        }

        character->GameObject.DisableDraw();
        character->GameObject.RenderFlags |= HiddenFlags;
    }

    private unsafe void RestoreActorIfHidden(ulong objectKey, IGameObject? obj)
    {
        if (!hiddenActors.Remove(objectKey, out var hidden))
            return;

        if (obj == null || obj.Address != hidden.Address)
            return;

        RestoreActor(obj, hidden);
    }

    private static unsafe void RestoreActor(IGameObject obj, HiddenActorState hidden)
    {
        var character = (Character*)obj.Address;
        if (character == null)
            return;

        character->GameObject.EnableDraw();
        character->GameObject.RenderFlags =
            (character->GameObject.RenderFlags & ~HiddenFlags) |
            hidden.OriginalHiddenFlags;
    }

    private unsafe void RemoveReplacedOrUnloadedActors(IReadOnlyDictionary<ulong, IGameObject> livePlayers)
    {
        foreach (var (objectKey, hidden) in new List<KeyValuePair<ulong, HiddenActorState>>(hiddenActors))
        {
            if (livePlayers.TryGetValue(objectKey, out var obj) && obj.Address == hidden.Address)
                continue;

            RestoreTrackedActorIfStillLive(objectKey, hidden.Address);
        }
    }

    private unsafe void RestoreTrackedActorIfStillLive(ulong objectKey, nint address)
    {
        if (!hiddenActors.TryGetValue(objectKey, out var hidden) || hidden.Address != address)
            return;

        hiddenActors.Remove(objectKey);
        foreach (var obj in GetLivePlayerObjects())
        {
            if (obj.Address != address)
                continue;

            RestoreActor(obj, hidden);
            return;
        }
    }

    private void PruneIdentityCache(IReadOnlyDictionary<ulong, IGameObject> livePlayers)
    {
        foreach (var (objectKey, identity) in new List<KeyValuePair<ulong, ActorIdentityState>>(originalIdentities))
        {
            if (livePlayers.TryGetValue(objectKey, out var obj) && obj.Address == identity.Address)
                continue;

            originalIdentities.Remove(objectKey);
        }
    }

    private IGameObject? FindPlayerObject(ulong gameObjectId)
    {
        for (var index = 0; index < objectTable.Length; index++)
        {
            var obj = objectTable[index];
            if (obj == null || obj.ObjectKind != DalamudObjectKind.Pc)
                continue;

            if (obj.GameObjectId == gameObjectId || obj.EntityId == gameObjectId)
                return obj;
        }

        return null;
    }

    private IEnumerable<IGameObject> GetLivePlayerObjects()
    {
        for (var index = 0; index < objectTable.Length; index++)
        {
            var obj = objectTable[index];
            if (obj != null && obj.ObjectKind == DalamudObjectKind.Pc && obj.Address != nint.Zero)
                yield return obj;
        }
    }
}
