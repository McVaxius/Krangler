using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Krangler.IPC;

/// <summary>
/// Glamourer IPC subscriber for appearance modification.
/// Phase 2 implementation — currently a stub that checks availability.
/// </summary>
public class GlamourerIPC : IDisposable
{
    private readonly IPluginLog log;
    private readonly IDalamudPluginInterface pluginInterface;

    private ICallGateSubscriber<(int, int)>? apiVersions;
    private bool isAvailable;
    private DateTime availabilityCacheExpiry = DateTime.MinValue;

    public GlamourerIPC(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;

        try
        {
            apiVersions = pluginInterface.GetIpcSubscriber<(int, int)>("Glamourer.ApiVersions");
        }
        catch (Exception ex)
        {
            log.Warning($"[Krangler] Failed to subscribe to Glamourer IPC: {ex.Message}");
            apiVersions = null;
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
            return true;
        }
        catch
        {
            isAvailable = false;
            return false;
        }
    }

    public void Dispose()
    {
        // IPC subscriptions are cleaned up by Dalamud
    }
}
