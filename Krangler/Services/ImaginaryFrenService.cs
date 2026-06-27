using System;
using System.Text;
using System.Text.Json;
using Dalamud.Game.ClientState.Conditions;
using Krangler.Models;
using BattleChara = FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara;
using CharacterStruct = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using ClientObjectManager = FFXIVClientStructs.FFXIV.Client.Game.Object.ClientObjectManager;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using ObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind;
using BattleNpcSubKind = FFXIVClientStructs.FFXIV.Client.Game.Object.BattleNpcSubKind;
using ObjectTargetableFlags = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectTargetableFlags;
using Vector3 = FFXIVClientStructs.FFXIV.Common.Math.Vector3;

namespace Krangler.Services;

public sealed unsafe class ImaginaryFrenService : IDisposable
{
    private const int MaxNameBytes = 31;
    private const int ObjectTablePressureLimit = 180;
    private const float FollowBehindDistance = 2.2f;
    private const float FollowSideDistance = 1.05f;
    private const float SnapDistance = 18.0f;
    private const float LerpFactor = 0.22f;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
    };

    private readonly Plugin plugin;
    private BattleChara* actor;
    private ImaginaryFrenDesired? runtimeDesired;
    private string appliedPresetKey = string.Empty;
    private string appliedName = string.Empty;
    private int applyRetryCountdown;

    public ImaginaryFrenService(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.ClientState.Logout += OnLogout;
        Plugin.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public string LastStatus { get; private set; } = "Imaginary Fren idle.";
    public string LastError { get; private set; } = string.Empty;
    public bool IsSpawned => actor != null;

    public void Dispose()
    {
        Plugin.ClientState.Logout -= OnLogout;
        Plugin.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Despawn("plugin unload");
    }

    public void UseConfigDesired()
    {
        runtimeDesired = null;
        appliedPresetKey = string.Empty;
        appliedName = string.Empty;
    }

    public void RequestSpawnFromConfig()
    {
        UseConfigDesired();
        plugin.Configuration.ImaginaryFrenEnabled = true;
        plugin.Configuration.Save();
    }

    public void DisableFromConfig()
    {
        UseConfigDesired();
        plugin.Configuration.ImaginaryFrenEnabled = false;
        plugin.Configuration.Save();
        Despawn("manual disable");
    }

    public string SetFromJson(string json)
    {
        try
        {
            var request = string.IsNullOrWhiteSpace(json)
                ? new ImaginaryFrenSetRequest()
                : JsonSerializer.Deserialize<ImaginaryFrenSetRequest>(json, JsonOptions) ?? new ImaginaryFrenSetRequest();

            var desired = new ImaginaryFrenDesired(
                request.Enabled,
                SanitizeName(request.Name),
                SanitizePresetKey(request.PresetKey),
                string.IsNullOrWhiteSpace(request.Source) ? "ipc" : request.Source.Trim());

            runtimeDesired = desired;

            if (request.Persist)
            {
                plugin.Configuration.ImaginaryFrenEnabled = desired.Enabled;
                plugin.Configuration.ImaginaryFrenName = desired.Name;
                plugin.Configuration.ImaginaryFrenPresetKey = desired.PresetKey;
                plugin.Configuration.Save();
            }

            if (!desired.Enabled)
                Despawn($"disabled by {desired.Source}");

            Update();
            return GetStatusJson(ok: true);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastStatus = "Imaginary Fren IPC request failed.";
            return GetStatusJson(ok: false, error: ex.Message);
        }
    }

    public string GetStatusJson(bool ok = true, string? error = null)
    {
        var desired = GetDesired();
        var result = new ImaginaryFrenStatusResult
        {
            Ok = ok,
            Enabled = desired.Enabled,
            Name = desired.Name,
            PresetKey = desired.PresetKey,
            Source = desired.Source,
            Persist = runtimeDesired == null,
            Spawned = IsSpawned,
            Status = LastStatus,
            Error = error ?? LastError,
        };

        return JsonSerializer.Serialize(result, JsonOptions);
    }

    public ImaginaryFrenStatusResult GetStatus()
    {
        var desired = GetDesired();
        return new ImaginaryFrenStatusResult
        {
            Ok = true,
            Enabled = desired.Enabled,
            Name = desired.Name,
            PresetKey = desired.PresetKey,
            Source = desired.Source,
            Persist = runtimeDesired == null,
            Spawned = IsSpawned,
            Status = LastStatus,
            Error = LastError,
        };
    }

    public void Update()
    {
        var desired = GetDesired();
        if (!desired.Enabled)
        {
            if (actor != null)
                Despawn("disabled");
            LastStatus = "Imaginary Fren disabled.";
            LastError = string.Empty;
            return;
        }

        if (!plugin.Configuration.Enabled)
        {
            if (actor != null)
                Despawn("Krangler disabled");
            LastStatus = "Blocked: Krangler is disabled.";
            LastError = string.Empty;
            return;
        }

        if (!Plugin.ClientState.IsLoggedIn || Plugin.ObjectTable.LocalPlayer == null)
        {
            if (actor != null)
                Despawn("not logged in");
            LastStatus = "Waiting for local player.";
            LastError = string.Empty;
            return;
        }

        if (IsUnsafeGameState())
        {
            if (actor != null)
                Despawn("zone transition or cutscene");
            LastStatus = "Blocked: zone transition or cutscene.";
            LastError = string.Empty;
            return;
        }

        if (IsObjectTableUnderPressure())
        {
            if (actor != null)
                Despawn("object table pressure");
            LastStatus = "Blocked: object table pressure.";
            LastError = string.Empty;
            return;
        }

        if (actor == null)
        {
            TrySpawn(desired);
            return;
        }

        MaintainActor(desired);
    }

    public void Despawn(string reason)
    {
        if (actor == null)
            return;

        try
        {
            actor->DisableDraw();
            var objectManager = ClientObjectManager.Instance();
            var index = objectManager->GetIndexByObject((GameObjectStruct*)actor);
            if (index >= 0)
                objectManager->DeleteObjectByIndex((ushort)index, 0);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Plugin.Log.Warning($"[Krangler] Imaginary Fren despawn failed during {reason}: {ex.Message}");
        }
        finally
        {
            actor = null;
            appliedPresetKey = string.Empty;
            appliedName = string.Empty;
            LastStatus = $"Despawned Imaginary Fren: {reason}.";
        }
    }

    private void OnLogout(int type, int code)
        => Despawn("logout");

    private void OnTerritoryChanged(uint territory)
        => Despawn($"territory change {territory}");

    private ImaginaryFrenDesired GetDesired()
    {
        if (runtimeDesired != null)
            return runtimeDesired;

        return new ImaginaryFrenDesired(
            plugin.Configuration.ImaginaryFrenEnabled,
            SanitizeName(plugin.Configuration.ImaginaryFrenName),
            SanitizePresetKey(plugin.Configuration.ImaginaryFrenPresetKey),
            "config");
    }

    private bool TrySpawn(ImaginaryFrenDesired desired)
    {
        try
        {
            var objectManager = ClientObjectManager.Instance();
            var objectIndex = objectManager->CreateBattleCharacter();
            if (objectIndex == 0xffffffff)
            {
                LastStatus = "Failed to spawn Imaginary Fren.";
                LastError = "CreateBattleCharacter returned no free object slot.";
                return false;
            }

            var gameObject = objectManager->GetObjectByIndex((ushort)objectIndex);
            if (gameObject == null)
            {
                LastStatus = "Failed to spawn Imaginary Fren.";
                LastError = "Created object could not be resolved.";
                return false;
            }

            actor = (BattleChara*)gameObject;
            actor->CharacterSetup.SetupBNpc(0);
            actor->ObjectKind = ObjectKind.BattleNpc;
            actor->BattleNpcSubKind = (BattleNpcSubKind)4;
            actor->TargetableStatus &= ~ObjectTargetableFlags.IsTargetable;
            actor->Alpha = 1.0f;

            PlaceNearLocalPlayer(snap: true);
            ApplyName(desired.Name);
            var presetReady = TryApplyPreset(desired, force: true);
            actor->EnableDraw();

            if (presetReady)
            {
                LastStatus = $"Spawned Imaginary Fren '{desired.Name}'.";
                LastError = string.Empty;
            }
            else
            {
                LastStatus = $"Spawned Imaginary Fren '{desired.Name}' without an applied preset.";
            }
            return true;
        }
        catch (Exception ex)
        {
            actor = null;
            LastStatus = "Failed to spawn Imaginary Fren.";
            LastError = ex.Message;
            Plugin.Log.Warning($"[Krangler] Imaginary Fren spawn failed: {ex.Message}");
            return false;
        }
    }

    private void MaintainActor(ImaginaryFrenDesired desired)
    {
        if (actor == null)
            return;

        actor->TargetableStatus &= ~ObjectTargetableFlags.IsTargetable;

        if (!string.Equals(appliedName, desired.Name, StringComparison.Ordinal))
            ApplyName(desired.Name);

        var presetReady = true;
        if (!string.Equals(appliedPresetKey, desired.PresetKey, StringComparison.OrdinalIgnoreCase) ||
            applyRetryCountdown <= 0)
        {
            presetReady = TryApplyPreset(desired, force: false);
            applyRetryCountdown = 30;
        }
        else
        {
            applyRetryCountdown--;
        }

        PlaceNearLocalPlayer(snap: false);
        if (presetReady)
        {
            LastStatus = $"Following as '{desired.Name}' using preset '{desired.PresetKey}'.";
            LastError = string.Empty;
        }
        else
        {
            LastStatus = $"Following as '{desired.Name}' without an applied preset.";
        }
    }

    private bool TryApplyPreset(ImaginaryFrenDesired desired, bool force)
    {
        if (actor == null)
            return false;

        var preset = plugin.GlamourerPresetService.GetPresetByName(desired.PresetKey);
        if (preset == null)
        {
            LastError = $"Preset '{desired.PresetKey}' was not found.";
            LastStatus = "Imaginary Fren spawned without preset.";
            return false;
        }

        var applied = plugin.TryApplyPresetToImaginaryFren((CharacterStruct*)actor, preset, out var applyStatus);
        if (applied)
            appliedPresetKey = desired.PresetKey;

        if (!applied)
        {
            LastError = string.IsNullOrWhiteSpace(applyStatus) ? "Preset apply reported no changes." : applyStatus;
            return false;
        }

        LastError = string.Empty;
        return true;
    }

    private void ApplyName(string displayName)
    {
        if (actor == null)
            return;

        var safeName = SanitizeName(displayName);
        var bytes = Encoding.UTF8.GetBytes(safeName);
        var length = Math.Min(bytes.Length, MaxNameBytes);
        var character = (CharacterStruct*)actor;

        for (var i = 0; i <= MaxNameBytes; i++)
            character->Name[i] = 0;

        for (var i = 0; i < length; i++)
            character->Name[i] = bytes[i];

        character->Name[length] = 0;
        appliedName = safeName;
    }

    private void PlaceNearLocalPlayer(bool snap)
    {
        if (actor == null || Plugin.ObjectTable.LocalPlayer == null || Plugin.ObjectTable.LocalPlayer.Address == 0)
            return;

        var localPlayer = (BattleChara*)Plugin.ObjectTable.LocalPlayer.Address;
        var target = GetFollowPosition(localPlayer->Position, localPlayer->Rotation);
        var current = actor->Position;
        var distance = Distance(current, target);

        var next = snap || distance > SnapDistance
            ? target
            : Lerp(current, target, LerpFactor);

        actor->SetPosition(next.X, next.Y, next.Z);
        actor->DefaultPosition = next;

        var yawToPlayer = DirectionTo(next, localPlayer->Position);
        actor->SetRotation(yawToPlayer);
        actor->DefaultRotation = yawToPlayer;
    }

    private static Vector3 GetFollowPosition(Vector3 playerPosition, float playerRotation)
    {
        var forward = new Vector3(MathF.Sin(playerRotation), 0f, MathF.Cos(playerRotation));
        var right = new Vector3(MathF.Cos(playerRotation), 0f, -MathF.Sin(playerRotation));
        return new Vector3(
            playerPosition.X - (forward.X * FollowBehindDistance) + (right.X * FollowSideDistance),
            playerPosition.Y,
            playerPosition.Z - (forward.Z * FollowBehindDistance) + (right.Z * FollowSideDistance));
    }

    private bool IsUnsafeGameState()
        => Plugin.Condition[ConditionFlag.BetweenAreas] ||
           Plugin.Condition[ConditionFlag.BetweenAreas51] ||
           Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
           Plugin.Condition[ConditionFlag.WatchingCutscene] ||
           Plugin.Condition[ConditionFlag.WatchingCutscene78];

    private static float DirectionTo(Vector3 from, Vector3 to)
        => MathF.Atan2(to.X - from.X, to.Z - from.Z);

    private static float Distance(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static Vector3 Lerp(Vector3 current, Vector3 target, float amount)
        => new(
            current.X + ((target.X - current.X) * amount),
            current.Y + ((target.Y - current.Y) * amount),
            current.Z + ((target.Z - current.Z) * amount));

    private static string SanitizeName(string? name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? Configuration.DefaultImaginaryFrenName : name.Trim();
        return value.Length > MaxNameBytes ? value[..MaxNameBytes] : value;
    }

    private static string SanitizePresetKey(string? presetKey)
    {
        var value = presetKey?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(value) ? Configuration.DefaultImaginaryFrenPresetKey : value;
    }

    private static bool IsObjectTableUnderPressure()
    {
        var count = 0;
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj != null)
                count++;
        }

        return count >= ObjectTablePressureLimit;
    }
}

public sealed record ImaginaryFrenDesired(bool Enabled, string Name, string PresetKey, string Source);

public sealed class ImaginaryFrenSetRequest
{
    public bool Enabled { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PresetKey { get; set; } = string.Empty;
    public bool Persist { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class ImaginaryFrenStatusResult
{
    public bool Ok { get; set; }
    public bool Enabled { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PresetKey { get; set; } = string.Empty;
    public bool Persist { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool Spawned { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}
