using System;
using System.Collections.Generic;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace Krangler.Services;

public sealed class KranglerIpcService : IDisposable
{
    private const string ImaginaryFrenSetName = "Krangler.ImaginaryFren.SetFromJson";
    private const string ImaginaryFrenStatusName = "Krangler.ImaginaryFren.GetStatusJson";
    private const string ImaginaryFrenPresetNamesName = "Krangler.ImaginaryFren.GetPresetNamesJson";
    private const string PresetExportName = "Krangler.Presets.ExportPresetJson";
    private const string PresetImportName = "Krangler.Presets.ImportPresetJson";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
    };

    private readonly ImaginaryFrenService imaginaryFrenService;
    private readonly GlamourerPresetService presetService;
    private readonly DadPrivacyLeaseService dadPrivacyLeaseService;
    private readonly List<Action> unregister = new();

    public KranglerIpcService(
        IDalamudPluginInterface pluginInterface,
        ImaginaryFrenService imaginaryFrenService,
        GlamourerPresetService presetService,
        DadPrivacyLeaseService dadPrivacyLeaseService)
    {
        this.imaginaryFrenService = imaginaryFrenService;
        this.presetService = presetService;
        this.dadPrivacyLeaseService = dadPrivacyLeaseService;

        Register(pluginInterface.GetIpcProvider<string, string>(ImaginaryFrenSetName), imaginaryFrenService.SetFromJson);
        Register(pluginInterface.GetIpcProvider<string>(ImaginaryFrenStatusName), () => imaginaryFrenService.GetStatusJson());
        Register(pluginInterface.GetIpcProvider<string>(ImaginaryFrenPresetNamesName), GetPresetNamesJson);
        Register(pluginInterface.GetIpcProvider<string, string>(PresetExportName), ExportPresetJson);
        Register(pluginInterface.GetIpcProvider<string, string>(PresetImportName), ImportPresetJson);
        Register(pluginInterface.GetIpcProvider<string, string>(DadPrivacyLeaseContract.AcquireIpcName), dadPrivacyLeaseService.AcquireFromJson);
        Register(pluginInterface.GetIpcProvider<string, string>(DadPrivacyLeaseContract.ReleaseIpcName), dadPrivacyLeaseService.ReleaseFromJson);
        Register(pluginInterface.GetIpcProvider<string, string>(DadPrivacyLeaseContract.StatusIpcName), dadPrivacyLeaseService.GetStatusJson);
    }

    public void Dispose()
    {
        try
        {
            foreach (var action in unregister)
                action();
        }
        finally
        {
            unregister.Clear();
            dadPrivacyLeaseService.Dispose();
        }
    }

    private string GetPresetNamesJson()
        => JsonSerializer.Serialize(new
        {
            ok = true,
            presets = presetService.GetPresetSummaries(),
            status = $"Loaded {presetService.PresetCount} preset(s).",
            error = string.Empty,
        }, JsonOptions);

    private string ExportPresetJson(string presetKey)
    {
        try
        {
            var preset = presetService.GetPresetByName(presetKey);
            if (preset == null)
            {
                return SerializePresetResult(false, presetKey, string.Empty, string.Empty, $"Preset '{presetKey}' was not found.");
            }

            return presetService.TryExportPresetJson(presetKey, out var exportJson, out var error)
                ? SerializePresetResult(true, string.IsNullOrWhiteSpace(preset.Identifier) ? preset.Name : preset.Identifier, preset.Name, exportJson, string.Empty)
                : SerializePresetResult(false, presetKey, string.Empty, string.Empty, error);
        }
        catch (Exception ex)
        {
            return SerializePresetResult(false, presetKey, string.Empty, string.Empty, ex.Message);
        }
    }

    private string ImportPresetJson(string exportJson)
    {
        try
        {
            var rawPresetJson = ExtractPresetJson(exportJson);
            if (presetService.TryImportPresetJson(rawPresetJson, out var preset, out var error) && preset != null)
            {
                var key = string.IsNullOrWhiteSpace(preset.Identifier) ? preset.Name : preset.Identifier;
                return SerializePresetResult(true, key, preset.Name, string.Empty, string.Empty, "Imported preset.");
            }

            return SerializePresetResult(false, string.Empty, string.Empty, string.Empty, error);
        }
        catch (Exception ex)
        {
            return SerializePresetResult(false, string.Empty, string.Empty, string.Empty, ex.Message);
        }
    }

    private static string ExtractPresetJson(string exportJson)
    {
        if (string.IsNullOrWhiteSpace(exportJson))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(exportJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("exportJson", out var exportProperty) &&
                    exportProperty.ValueKind == JsonValueKind.String)
                {
                    return exportProperty.GetString() ?? string.Empty;
                }

                if (document.RootElement.TryGetProperty("presetJson", out var presetProperty) &&
                    presetProperty.ValueKind == JsonValueKind.String)
                {
                    return presetProperty.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            // Input may already be a raw Glamourer preset JSON string.
        }

        return exportJson;
    }

    private static string SerializePresetResult(
        bool ok,
        string presetKey,
        string name,
        string exportJson,
        string error,
        string status = "")
        => JsonSerializer.Serialize(new
        {
            ok,
            presetKey,
            name,
            exportJson,
            spawned = false,
            enabled = false,
            persist = false,
            source = "krangler",
            status = string.IsNullOrWhiteSpace(status) ? (ok ? "OK" : "Failed") : status,
            error,
        }, JsonOptions);

    private void Register<TReturn>(ICallGateProvider<TReturn> provider, Func<TReturn> func)
    {
        provider.RegisterFunc(func);
        unregister.Add(provider.UnregisterFunc);
    }

    private void Register<TArg, TReturn>(ICallGateProvider<TArg, TReturn> provider, Func<TArg, TReturn> func)
    {
        provider.RegisterFunc(func);
        unregister.Add(provider.UnregisterFunc);
    }
}
