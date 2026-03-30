using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Krangler.Models;

namespace Krangler.Services;

public class GlamourerPresetService
{
    private readonly IPluginLog log;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly Dictionary<string, GlamourerPreset> presets = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PropertyInfo> CustomizePropertyMap = typeof(CustomizeData)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.PropertyType == typeof(CustomValue) && property.CanWrite)
        .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);

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
        presets.Clear();

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
                var preset = ParsePreset(json);

                if (preset != null && !string.IsNullOrEmpty(preset.Name))
                {
                    preset.Name = preset.Name.Trim();
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

    public GlamourerPreset? GetPresetForPlayer(string playerName)
    {
        if (presets.Count == 0) return null;

        var hash = GetStableHash(playerName);
        var orderedPresets = presets.Values
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var index = Math.Abs(hash) % orderedPresets.Count;

        return orderedPresets[index];
    }

    public GlamourerPreset? GetPresetByName(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
            return null;

        var normalizedName = presetName.Trim();

        if (presets.TryGetValue(normalizedName, out var exactMatch))
            return exactMatch;

        return presets.Values.FirstOrDefault(p =>
            string.Equals(p.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    public GlamourerPreset? ResolvePresetSelection(string playerName, string selection)
    {
        if (string.IsNullOrWhiteSpace(selection) ||
            string.Equals(selection, "Random", StringComparison.OrdinalIgnoreCase))
        {
            return GetPresetForPlayer(playerName);
        }

        return GetPresetByName(selection);
    }

    public List<string> GetPresetNames()
    {
        return presets.Keys
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static GlamourerPreset? ParsePreset(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return null;

        var root = document.RootElement;
        return new GlamourerPreset
        {
            FileVersion = GetInt32(root, "FileVersion"),
            Identifier = GetString(root, "Identifier"),
            Name = GetString(root, "Name"),
            Description = GetString(root, "Description"),
            ForcedRedraw = GetBoolean(root, "ForcedRedraw"),
            Equipment = ParseEquipment(root),
            Bonus = ParseBonus(root),
            Customize = ParseCustomize(root),
        };
    }

    private static Dictionary<string, EquipmentSlotData> ParseEquipment(JsonElement root)
    {
        var equipment = new Dictionary<string, EquipmentSlotData>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetObjectProperty(root, "Equipment", out var equipmentElement))
            return equipment;

        foreach (var slot in equipmentElement.EnumerateObject())
        {
            if (slot.Value.ValueKind != JsonValueKind.Object)
                continue;

            equipment[slot.Name] = new EquipmentSlotData
            {
                ItemId = GetUInt64(slot.Value, "ItemId"),
                Stain = GetUInt32(slot.Value, "Stain"),
                Stain2 = GetUInt32(slot.Value, "Stain2"),
                Apply = GetBoolean(slot.Value, "Apply"),
                ApplyStain = GetBoolean(slot.Value, "ApplyStain"),
                Crest = GetBoolean(slot.Value, "Crest"),
                ApplyCrest = GetBoolean(slot.Value, "ApplyCrest"),
                Show = GetBoolean(slot.Value, "Show"),
                IsToggled = GetBoolean(slot.Value, "IsToggled"),
            };
        }

        return equipment;
    }

    private static Dictionary<string, BonusItemData> ParseBonus(JsonElement root)
    {
        var bonus = new Dictionary<string, BonusItemData>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetObjectProperty(root, "Bonus", out var bonusElement))
            return bonus;

        foreach (var slot in bonusElement.EnumerateObject())
        {
            if (slot.Value.ValueKind != JsonValueKind.Object)
                continue;

            bonus[slot.Name] = new BonusItemData
            {
                BonusId = GetUInt32(slot.Value, "BonusId"),
                Apply = GetBoolean(slot.Value, "Apply"),
            };
        }

        return bonus;
    }

    private static CustomizeData ParseCustomize(JsonElement root)
    {
        var customizeData = new CustomizeData();
        if (!TryGetObjectProperty(root, "Customize", out var customizeElement))
            return customizeData;

        customizeData.ModelId = GetInt32(customizeElement, "ModelId");

        foreach (var property in customizeElement.EnumerateObject())
        {
            if (property.NameEquals("ModelId"))
                continue;

            if (!CustomizePropertyMap.TryGetValue(property.Name, out var targetProperty))
                continue;

            targetProperty.SetValue(customizeData, ParseCustomValue(property.Value));
        }

        return customizeData;
    }

    private static CustomValue ParseCustomValue(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return new CustomValue();

        return new CustomValue
        {
            Value = (byte)Math.Clamp(GetInt32(element, "Value"), byte.MinValue, byte.MaxValue),
            Apply = GetBoolean(element, "Apply"),
        };
    }

    private static bool TryGetObjectProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out property) && property.ValueKind == JsonValueKind.Object)
            return true;

        property = default;
        return false;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return string.Empty;

        return property.GetString() ?? string.Empty;
    }

    private static int GetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return 0;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
            return intValue;

        return 0;
    }

    private static uint GetUInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return 0;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetUInt32(out var uintValue))
            return uintValue;

        return 0;
    }

    private static ulong GetUInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return 0;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetUInt64(out var ulongValue))
            return ulongValue;

        return 0;
    }

    private static bool GetBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False)
            return false;

        return property.GetBoolean();
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
