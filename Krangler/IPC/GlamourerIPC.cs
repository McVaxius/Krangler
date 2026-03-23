using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Krangler.IPC;

/// <summary>
/// Glamourer IPC subscriber for appearance modification.
/// Discovers available IPC channels and uses them for customize changes.
/// </summary>
public class GlamourerIPC : IDisposable
{
    private readonly IPluginLog log;
    private readonly IDalamudPluginInterface pluginInterface;

    // API version check
    private ICallGateSubscriber<(int, int)>? apiVersions;

    // State-based IPC (base64 encoded appearance state)
    private ICallGateSubscriber<string, string>? getAllCustomization;
    private ICallGateSubscriber<string, string, object>? applyAll;
    private ICallGateSubscriber<string, object>? revertCustomization;

    // Index-based IPC (game object index)
    private ICallGateSubscriber<int, (int, string)>? getState;
    private ICallGateSubscriber<string, int, object>? applyState;
    private ICallGateSubscriber<int, object>? revertState;
    private ICallGateSubscriber<int, uint, object>? revertToAutomation;

    private bool isAvailable;
    private DateTime availabilityCacheExpiry = DateTime.MinValue;
    private bool hasLoggedDiscovery;
    private int apiBreaking;
    private int apiFeatures;

    // Track which players have been modified so we can revert
    private readonly HashSet<string> modifiedPlayers = new();
    private readonly HashSet<int> modifiedIndices = new();

    // IPC mode
    public enum IpcMode { None, NameBased, IndexBased }
    private IpcMode mode = IpcMode.None;

    public GlamourerIPC(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;

        DiscoverChannels();
    }

    private void DiscoverChannels()
    {
        // API Versions
        try
        {
            apiVersions = pluginInterface.GetIpcSubscriber<(int, int)>("Glamourer.ApiVersions");
            var versions = apiVersions.InvokeFunc();
            apiBreaking = versions.Item1;
            apiFeatures = versions.Item2;
            log.Information($"[Krangler] [GlamourerIPC] API versions: Breaking={apiBreaking}, Features={apiFeatures}");
        }
        catch (Exception ex)
        {
            log.Warning($"[Krangler] [GlamourerIPC] ApiVersions not available: {ex.Message}");
            apiVersions = null;
        }

        // Try name-based channels (older Glamourer API)
        TrySubscribeNameBased();

        // Try index-based channels (newer Glamourer API)
        TrySubscribeIndexBased();
    }

    private void TrySubscribeNameBased()
    {
        var channelNames = new[] {
            "Glamourer.GetAllCustomization",
            "Glamourer.GetAllCustomizationFromCharacter",
        };
        foreach (var name in channelNames)
        {
            try
            {
                getAllCustomization = pluginInterface.GetIpcSubscriber<string, string>(name);
                log.Information($"[Krangler] [GlamourerIPC] Subscribed to {name}");
                break;
            }
            catch { getAllCustomization = null; }
        }

        var applyNames = new[] {
            "Glamourer.ApplyAll",
            "Glamourer.ApplyAllToCharacter",
        };
        foreach (var name in applyNames)
        {
            try
            {
                applyAll = pluginInterface.GetIpcSubscriber<string, string, object>(name);
                log.Information($"[Krangler] [GlamourerIPC] Subscribed to {name}");
                break;
            }
            catch { applyAll = null; }
        }

        var revertNames = new[] {
            "Glamourer.Revert",
            "Glamourer.RevertCharacter",
            "Glamourer.RevertCustomization",
        };
        foreach (var name in revertNames)
        {
            try
            {
                revertCustomization = pluginInterface.GetIpcSubscriber<string, object>(name);
                log.Information($"[Krangler] [GlamourerIPC] Subscribed to {name}");
                break;
            }
            catch { revertCustomization = null; }
        }

        if (getAllCustomization != null && applyAll != null)
        {
            mode = IpcMode.NameBased;
            log.Information("[Krangler] [GlamourerIPC] Using NAME-BASED IPC mode");
        }
    }

    private void TrySubscribeIndexBased()
    {
        if (mode == IpcMode.NameBased) return; // Already have a working mode

        try
        {
            getState = pluginInterface.GetIpcSubscriber<int, (int, string)>("Glamourer.GetState");
            log.Information("[Krangler] [GlamourerIPC] Subscribed to Glamourer.GetState");
        }
        catch { getState = null; }

        try
        {
            applyState = pluginInterface.GetIpcSubscriber<string, int, object>("Glamourer.ApplyState");
            log.Information("[Krangler] [GlamourerIPC] Subscribed to Glamourer.ApplyState");
        }
        catch { applyState = null; }

        try
        {
            revertState = pluginInterface.GetIpcSubscriber<int, object>("Glamourer.RevertState");
            log.Information("[Krangler] [GlamourerIPC] Subscribed to Glamourer.RevertState");
        }
        catch { revertState = null; }

        try
        {
            revertToAutomation = pluginInterface.GetIpcSubscriber<int, uint, object>("Glamourer.RevertToAutomation");
            log.Information("[Krangler] [GlamourerIPC] Subscribed to Glamourer.RevertToAutomation");
        }
        catch { revertToAutomation = null; }

        if (getState != null && applyState != null)
        {
            mode = IpcMode.IndexBased;
            log.Information("[Krangler] [GlamourerIPC] Using INDEX-BASED IPC mode");
        }
    }

    /// <summary>
    /// Check if Glamourer is installed and available. Cached for 5 seconds.
    /// </summary>
    public bool IsAvailable()
    {
        if (DateTime.UtcNow < availabilityCacheExpiry)
            return isAvailable;

        availabilityCacheExpiry = DateTime.UtcNow.AddSeconds(5);

        try
        {
            if (apiVersions == null)
            {
                isAvailable = false;
                return false;
            }

            var versions = apiVersions.InvokeFunc();
            isAvailable = true;

            if (!hasLoggedDiscovery)
            {
                log.Information($"[Krangler] [GlamourerIPC] Available! Mode={mode}, API={versions.Item1}.{versions.Item2}");
                hasLoggedDiscovery = true;
            }

            return true;
        }
        catch
        {
            isAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Apply customized appearance to a character by name.
    /// Returns true if the call was attempted (even if it fails at runtime).
    /// </summary>
    public bool ApplyCustomize(string characterName, byte[] customizeBytes)
    {
        if (!IsAvailable() || mode == IpcMode.None)
        {
            log.Warning($"[Krangler] [GlamourerIPC] Cannot apply customize - Mode={mode}, Available={isAvailable}");
            return false;
        }

        try
        {
            if (mode == IpcMode.NameBased && getAllCustomization != null && applyAll != null)
            {
                // Get current state
                var currentState = getAllCustomization.InvokeFunc(characterName);
                if (string.IsNullOrEmpty(currentState))
                {
                    log.Warning($"[Krangler] [GlamourerIPC] Got empty state for '{characterName}'");
                    return false;
                }

                // Decode base64 state, modify customize bytes, re-encode
                var stateBytes = Convert.FromBase64String(currentState);
                log.Information($"[Krangler] [GlamourerIPC] Got state for '{characterName}': {stateBytes.Length} bytes");

                // Customize bytes start at offset 0 in the Glamourer state (first 26 bytes)
                var customizeLength = Math.Min(customizeBytes.Length, 26);
                for (var i = 0; i < customizeLength; i++)
                {
                    if (customizeBytes[i] != 0) // Only override non-zero values
                        stateBytes[i] = customizeBytes[i];
                }

                var modifiedState = Convert.ToBase64String(stateBytes);
                applyAll.InvokeAction(modifiedState, characterName);
                modifiedPlayers.Add(characterName);
                log.Information($"[Krangler] [GlamourerIPC] Applied customize to '{characterName}'");
                return true;
            }
        }
        catch (Exception ex)
        {
            log.Warning($"[Krangler] [GlamourerIPC] ApplyCustomize failed for '{characterName}': {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Apply customized appearance to a character by game object index.
    /// </summary>
    public bool ApplyCustomizeByIndex(int objectIndex, byte[] customizeBytes, string debugName = "")
    {
        if (!IsAvailable() || mode != IpcMode.IndexBased || getState == null || applyState == null)
            return false;

        try
        {
            var (errorCode, currentState) = getState.InvokeFunc(objectIndex);
            if (errorCode != 0 || string.IsNullOrEmpty(currentState))
            {
                log.Warning($"[Krangler] [GlamourerIPC] GetState failed for index {objectIndex} ({debugName}): error={errorCode}");
                return false;
            }

            var stateBytes = Convert.FromBase64String(currentState);
            log.Information($"[Krangler] [GlamourerIPC] Got state for index {objectIndex} ({debugName}): {stateBytes.Length} bytes");

            var customizeLength = Math.Min(customizeBytes.Length, 26);
            for (var i = 0; i < customizeLength; i++)
            {
                if (customizeBytes[i] != 0)
                    stateBytes[i] = customizeBytes[i];
            }

            var modifiedState = Convert.ToBase64String(stateBytes);
            applyState.InvokeAction(modifiedState, objectIndex);
            modifiedIndices.Add(objectIndex);
            log.Information($"[Krangler] [GlamourerIPC] Applied customize to index {objectIndex} ({debugName})");
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"[Krangler] [GlamourerIPC] ApplyCustomizeByIndex failed for {objectIndex} ({debugName}): {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Revert all appearance changes we've made.
    /// </summary>
    public void RevertAll()
    {
        if (revertCustomization != null)
        {
            foreach (var name in modifiedPlayers)
            {
                try
                {
                    revertCustomization.InvokeAction(name);
                    log.Information($"[Krangler] [GlamourerIPC] Reverted '{name}'");
                }
                catch (Exception ex)
                {
                    log.Warning($"[Krangler] [GlamourerIPC] Failed to revert '{name}': {ex.Message}");
                }
            }
        }

        if (revertState != null)
        {
            foreach (var idx in modifiedIndices)
            {
                try
                {
                    revertState.InvokeAction(idx);
                    log.Information($"[Krangler] [GlamourerIPC] Reverted index {idx}");
                }
                catch (Exception ex)
                {
                    log.Warning($"[Krangler] [GlamourerIPC] Failed to revert index {idx}: {ex.Message}");
                }
            }
        }

        modifiedPlayers.Clear();
        modifiedIndices.Clear();
    }

    public IpcMode GetMode() => mode;

    public void Dispose()
    {
        RevertAll();
    }
}
