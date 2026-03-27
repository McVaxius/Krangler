using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Krangler.Models;
using Newtonsoft.Json.Linq;

namespace Krangler.Services;

public sealed class GlamourerIpcService
{
    private const uint KranglerKey = 0x4B524E47;
    private const int ApplyFlags = 0x06; // Equipment | Customization

    private readonly IPluginLog log;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly Dictionary<string, Guid> designIdsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> appliedPlayerNames = new(StringComparer.OrdinalIgnoreCase);

    private DateTime lastAvailabilityCheck = DateTime.MinValue;
    private DateTime lastDesignRefresh = DateTime.MinValue;
    private bool isAvailable;
    private bool hasLoggedAvailabilityFailure;

    public GlamourerIpcService(IPluginLog log, IDalamudPluginInterface pluginInterface)
    {
        this.log = log;
        this.pluginInterface = pluginInterface;
    }

    public bool TryApplyDesign(int objectIndex, string playerName, GlamourerPreset preset)
    {
        if (string.IsNullOrWhiteSpace(playerName) || preset == null || !IsAvailable())
            return false;

        if (TryApplyPresetState(objectIndex, playerName, preset))
            return true;

        if (!TryResolveDesignId(preset, out var designId))
            return false;

        if (TryApplyResolvedDesign(objectIndex, playerName, preset, designId))
            return true;

        return false;
    }

    private bool TryApplyResolvedDesign(int objectIndex, string playerName, GlamourerPreset preset, Guid designId)
    {
        if (TryApplyResolvedDesignByIndex(objectIndex, playerName, preset, designId))
            return true;

        return TryApplyResolvedDesignByName(playerName, preset, designId);
    }

    private bool TryApplyResolvedDesignByIndex(int objectIndex, string playerName, GlamourerPreset preset, Guid designId)
    {
        try
        {
            var apply = pluginInterface.GetIpcSubscriber<Guid, int, uint, int, int>("Glamourer.ApplyDesign");
            var result = apply.InvokeFunc(designId, objectIndex, KranglerKey, ApplyFlags);
            if (!IsSuccess(result))
            {
                log.Warning($"[Krangler] Glamourer ApplyDesign failed for index {objectIndex} / '{playerName}' / '{preset.Name}' with code {result}.");
                return false;
            }

            appliedPlayerNames.Add(playerName);
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"[Krangler] Glamourer ApplyDesign failed for index {objectIndex} / '{playerName}' / '{preset.Name}': {ex.Message}");
            return false;
        }
    }

    private bool TryApplyResolvedDesignByName(string playerName, GlamourerPreset preset, Guid designId)
    {
        try
        {
            var apply = pluginInterface.GetIpcSubscriber<Guid, string, uint, int, int>("Glamourer.ApplyDesignName");
            var result = apply.InvokeFunc(designId, playerName, KranglerKey, ApplyFlags);
            if (!IsSuccess(result))
            {
                log.Warning($"[Krangler] Glamourer ApplyDesignName failed for '{playerName}' / '{preset.Name}' with code {result}.");
                return false;
            }

            appliedPlayerNames.Add(playerName);
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"[Krangler] Glamourer ApplyDesignName failed for '{playerName}' / '{preset.Name}': {ex.Message}");
            return false;
        }
    }

    private bool TryApplyPresetState(int objectIndex, string playerName, GlamourerPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.RawJson))
            return false;

        if (TryApplyPresetStateByIndex(objectIndex, playerName, preset))
            return true;

        return TryApplyPresetStateByName(playerName, preset);
    }

    private bool TryApplyPresetStateByIndex(int objectIndex, string playerName, GlamourerPreset preset)
    {
        try
        {
            var apply = pluginInterface.GetIpcSubscriber<object, int, uint, int, int>("Glamourer.ApplyState");
            var state = JObject.Parse(preset.RawJson);
            var result = apply.InvokeFunc(state, objectIndex, KranglerKey, ApplyFlags);
            if (!IsSuccess(result))
            {
                log.Warning($"[Krangler] Glamourer ApplyState failed for index {objectIndex} / '{playerName}' / '{preset.Name}' with code {result}.");
                return false;
            }

            appliedPlayerNames.Add(playerName);
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"[Krangler] Glamourer ApplyState failed for index {objectIndex} / '{playerName}' / '{preset.Name}': {ex.Message}");
            return false;
        }
    }

    private bool TryApplyPresetStateByName(string playerName, GlamourerPreset preset)
    {
        try
        {
            var apply = pluginInterface.GetIpcSubscriber<object, string, uint, int, int>("Glamourer.ApplyStateName");
            var state = JObject.Parse(preset.RawJson);
            var result = apply.InvokeFunc(state, playerName, KranglerKey, ApplyFlags);
            if (!IsSuccess(result))
            {
                log.Warning($"[Krangler] Glamourer ApplyStateName failed for '{playerName}' / '{preset.Name}' with code {result}.");
                return false;
            }

            appliedPlayerNames.Add(playerName);
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"[Krangler] Glamourer ApplyStateName failed for '{playerName}' / '{preset.Name}': {ex.Message}");
            return false;
        }
    }

    public void RevertAppliedDesigns()
    {
        if (appliedPlayerNames.Count == 0)
            return;

        if (!IsAvailable())
        {
            appliedPlayerNames.Clear();
            return;
        }

        try
        {
            var revert = pluginInterface.GetIpcSubscriber<string, uint, int, int>("Glamourer.RevertStateName");
            foreach (var playerName in appliedPlayerNames.ToArray())
            {
                try
                {
                    revert.InvokeFunc(playerName, KranglerKey, ApplyFlags);
                }
                catch (Exception ex)
                {
                    log.Warning($"[Krangler] Glamourer RevertStateName failed for '{playerName}': {ex.Message}");
                }
            }
        }
        finally
        {
            appliedPlayerNames.Clear();
        }
    }

    public void ClearTracking()
        => appliedPlayerNames.Clear();

    private bool IsAvailable()
    {
        if ((DateTime.UtcNow - lastAvailabilityCheck).TotalSeconds < 10)
            return isAvailable;

        lastAvailabilityCheck = DateTime.UtcNow;
        isAvailable = false;

        try
        {
            if (!pluginInterface.InstalledPlugins.Any(plugin =>
                    string.Equals(plugin.Name, "Glamourer", StringComparison.OrdinalIgnoreCase)))
            {
                if (!hasLoggedAvailabilityFailure)
                {
                    log.Information("[Krangler] Glamourer is not installed or not enabled; Super Krangle will use the local preset fallback path.");
                    hasLoggedAvailabilityFailure = true;
                }

                return false;
            }

            var apiVersion = pluginInterface.GetIpcSubscriber<int>("Glamourer.ApiVersion");
            _ = apiVersion.InvokeFunc();
            isAvailable = true;
            hasLoggedAvailabilityFailure = false;
        }
        catch (Exception ex)
        {
            if (!hasLoggedAvailabilityFailure)
            {
                log.Warning($"[Krangler] Glamourer IPC is unavailable; Super Krangle will use the local preset fallback path. {ex.Message}");
                hasLoggedAvailabilityFailure = true;
            }
        }

        return isAvailable;
    }

    private bool TryResolveDesignId(GlamourerPreset preset, out Guid designId)
    {
        designId = Guid.Empty;
        RefreshDesignCacheIfNeeded();

        if (designIdsByName.TryGetValue(preset.Name, out designId))
            return true;

        return Guid.TryParse(preset.Identifier, out designId);
    }

    private void RefreshDesignCacheIfNeeded()
    {
        if (!isAvailable || (DateTime.UtcNow - lastDesignRefresh).TotalSeconds < 30)
            return;

        lastDesignRefresh = DateTime.UtcNow;
        designIdsByName.Clear();

        try
        {
            var getDesignList = pluginInterface.GetIpcSubscriber<Dictionary<Guid, string>>("Glamourer.GetDesignList");
            foreach (var (id, name) in getDesignList.InvokeFunc())
            {
                if (!string.IsNullOrWhiteSpace(name))
                    designIdsByName[name.Trim()] = id;
            }

            return;
        }
        catch
        {
        }

        try
        {
            var legacyGetDesignList = pluginInterface.GetIpcSubscriber<(string Name, Guid DesignId)[]>("Glamourer.GetDesignList");
            foreach (var (name, id) in legacyGetDesignList.InvokeFunc())
            {
                if (!string.IsNullOrWhiteSpace(name))
                    designIdsByName[name.Trim()] = id;
            }
        }
        catch
        {
        }
    }

    private static bool IsSuccess(int result)
        => result is 0 or 1;
}
