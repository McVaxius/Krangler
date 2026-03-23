using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Krangler.Models;

namespace Krangler.Services;

public class GlamourerPresetService
{
    private readonly IPluginLog log;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly Dictionary<string, GlamourerPreset> presets = new();

    public string UserPresetsDir { get; }
    public int PresetCount => presets.Count;

    public GlamourerPresetService(IPluginLog log, IDalamudPluginInterface pluginInterface)
    {
        this.log = log;
        this.pluginInterface = pluginInterface;
        UserPresetsDir = Path.Combine(pluginInterface.ConfigDirectory.FullName, "data", "presets");
        LoadPresets();
    }

    private void LoadPresets()
    {
        // Create user presets directory if it doesn't exist
        if (!Directory.Exists(UserPresetsDir))
        {
            Directory.CreateDirectory(UserPresetsDir);
            log.Information($"[GlamourerPreset] Created user presets directory: {UserPresetsDir}");
        }

        // Install bundled presets from plugin install dir (won't overwrite user files)
        InstallBundledPresets();

        // Load all presets from user directory
        var jsonFiles = Directory.GetFiles(UserPresetsDir, "*.json");
        var loadedCount = 0;

        foreach (var file in jsonFiles)
        {
            try
            {
                var json = File.ReadAllText(file);
                var preset = JsonSerializer.Deserialize<GlamourerPreset>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (preset != null && !string.IsNullOrEmpty(preset.Name))
                {
                    presets[preset.Name] = preset;
                    loadedCount++;
                }
            }
            catch (Exception ex)
            {
                log.Warning($"[GlamourerPreset] Failed to load {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        log.Information($"[GlamourerPreset] Loaded {loadedCount} presets from {jsonFiles.Length} files. Dir: {UserPresetsDir}");
    }

    private void InstallBundledPresets()
    {
        try
        {
            // Bundled presets ship alongside the plugin DLL in data/presets/
            var pluginDir = pluginInterface.AssemblyLocation.DirectoryName;
            if (pluginDir == null) return;

            var bundledDir = Path.Combine(pluginDir, "data", "presets");
            if (!Directory.Exists(bundledDir))
            {
                log.Information($"[GlamourerPreset] No bundled presets directory found at: {bundledDir}");
                return;
            }

            var bundledFiles = Directory.GetFiles(bundledDir, "*.json");
            var installedCount = 0;

            foreach (var bundledFile in bundledFiles)
            {
                var fileName = Path.GetFileName(bundledFile);
                var targetFile = Path.Combine(UserPresetsDir, fileName);

                // Don't overwrite user files
                if (!File.Exists(targetFile))
                {
                    File.Copy(bundledFile, targetFile);
                    installedCount++;
                }
            }

            if (installedCount > 0)
                log.Information($"[GlamourerPreset] Installed {installedCount} bundled presets to user directory");
        }
        catch (Exception ex)
        {
            log.Warning($"[GlamourerPreset] Failed to install bundled presets: {ex.Message}");
        }
    }

    public GlamourerPreset? GetRandomPreset(string playerName)
    {
        if (presets.Count == 0) return null;

        var hash = GetStableHash(playerName);
        var rng = new Random(hash);
        var index = rng.Next(presets.Count);

        return presets.ElementAt(index).Value;
    }

    public List<string> GetPresetNames()
    {
        return presets.Keys.ToList();
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
