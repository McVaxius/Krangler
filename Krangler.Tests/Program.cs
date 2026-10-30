using System.Text.Json;
using Krangler;
using Krangler.Services;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using System.Numerics;
using System.Reflection;
using System.Linq.Expressions;

var tests = new (string Name, Action Run)[]
{
    ("acquire and reacquire", AcquireAndReacquire),
    ("stale token replacement", StaleTokenReplacement),
    ("wrong-token protection", WrongTokenProtection),
    ("exact release", ExactRelease),
    ("disposal cleanup", DisposalCleanup),
    ("effective name/chat privacy includes self", EffectivePrivacyIncludesSelf),
    ("unrelated configured features remain unchanged", UnrelatedFeaturesRemainUnchanged),
    ("lease operations perform zero persistent saves", LeaseOperationsPerformZeroSaves),
    ("random identities are stable within an epoch", RandomIdentitiesStayStable),
    ("new epochs rotate all generated identities", RandomEpochsRotate),
    ("legacy mappings survive randomized mode roundtrip", LegacyMappingsSurviveRoundtrip),
    ("randomized setting defaults on and preserves explicit legacy choice", RandomizedSettingRoundtrip),
    ("actual appearance generators share the identity epoch", AppearanceUsesIdentityEpoch),
    ("fixed preset choices survive new epochs", FixedPresetChoiceSurvives),
    ("all shipped catalogs include randomized-mode controls", RandomizedCatalogs),
    ("LCARS appearance preserves legacy privacy and Picker values", LcarsAppearanceRoundtrip),
    ("native LCARS actions preserve IDs, styles, feedback and popup owner opacity", NativeLcarsActions),
};

foreach (var (name, run) in tests)
{
    run();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"PASS {tests.Length} Krangler tests");
return;

static string[] IdentitySnapshot() => Enumerable.Range(0, 24).SelectMany(i => new[]
{
    KrangleService.KrangleName($"Example{i} Adventurer@ExampleWorld{i}"),
    KrangleService.KrangleServer($"ExampleWorld{i}"),
    KrangleService.KrangleFCTag($"«Group{i}»"),
    KrangleService.KrangleTitle($"<Example title {i}>"),
}).ToArray();

static int SelectionSeed(string input) => (int)typeof(KrangleService).GetMethod("GetStableHash",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [input])!;

static void RandomizedSettingRoundtrip()
{
    True(new Configuration().RandomizedMode);
    var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>("{\"Enabled\":true,\"KrangleChat\":false}")!;
    True(legacy.RandomizedMode && legacy.Enabled && !legacy.KrangleChat);
    legacy.RandomizedMode = false;
    var saved = Newtonsoft.Json.JsonConvert.SerializeObject(legacy);
    var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(saved)!;
    True(!restored.RandomizedMode && restored.Enabled && !restored.KrangleChat);
}

static void LcarsAppearanceRoundtrip()
{
    const string legacy = """{"Version":2,"Enabled":true,"KrangleNames":false,"KrangleChat":true,"RandomizedMode":false,"UiLanguage":"ja","UiColors":{"Mode":1,"BackgroundRgb":1193046,"ControlsRgb":5666970,"TextRgb":14544639},"FutureAppearanceSetting":{"choice":"unchanged"}}""";
    var config = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(legacy)!;
    True(config.UiLcarsSoundsMuted);
    True(config.UiStylingEnabled && !config.UiWindowsLocked);
    True(config.UiColors.BackgroundBlurPercent == 75);
    foreach (var mode in Enum.GetValues<MaterialColorMode>())
        True(config.UiColors.HasBackgroundBlur(mode) == (mode == MaterialColorMode.Glassmorphism));
    var command = typeof(Plugin).GetMethod("ApplyAppearanceCommand", BindingFlags.NonPublic | BindingFlags.Static)!;
    True((bool)command.Invoke(null, [config, "style off"])! && !config.UiStylingEnabled);
    True((bool)command.Invoke(null, [config, "lock"])! && config.UiWindowsLocked);
    var picker = config.UiColors;
    True(picker.Mode == AethertekUI.MaterialColorMode.Picker);
    config.UiColors = picker with { Mode = AethertekUI.MaterialColorMode.Lcars };
    config.UiLcarsSoundsMuted = false;
    var saved = Newtonsoft.Json.JsonConvert.SerializeObject(config);
    var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(saved)!;
    True(restored.UiColors == picker with { Mode = AethertekUI.MaterialColorMode.Lcars });
    True(!restored.UiLcarsSoundsMuted && restored.Enabled && !restored.KrangleNames && restored.KrangleChat && !restored.RandomizedMode);
    True(restored.UiLanguage == "ja" && (string?)restored.AdditionalSettings!["FutureAppearanceSetting"]["choice"] == "unchanged");
    restored.UiColors = restored.UiColors with { Mode = AethertekUI.MaterialColorMode.Auto };
    var automatic = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(Newtonsoft.Json.JsonConvert.SerializeObject(restored))!;
    True(automatic.UiColors == picker with { Mode = AethertekUI.MaterialColorMode.Auto });
    True(!automatic.UiStylingEnabled && automatic.UiWindowsLocked);
    automatic.UiColors = automatic.UiColors with { Mode = MaterialColorMode.Glassmorphism,
        GlassBackground = MaterialGlassBackground.Image, GlassImagePath = "chosen.png",
        GlassLights = MaterialGlassLights.PointerGlow, GlassLight1Rgb = 0x123456,
        GlassWave2Rgb = 0xABCDEF, BackgroundBlurPercent = 82 };
    var glass = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(Newtonsoft.Json.JsonConvert.SerializeObject(automatic))!;
    True(glass.UiColors == automatic.UiColors);
    True((bool)command.Invoke(null, [glass, "style on"])! && glass.UiStylingEnabled);
    True((bool)command.Invoke(null, [glass, "unlock"])! && !glass.UiWindowsLocked);
    True(!(bool)command.Invoke(null, [glass, "on"])!);
}

static unsafe void NativeLcarsActions()
{
    const BindingFlags members = BindingFlags.Static | BindingFlags.NonPublic;
    var assembly = typeof(Configuration).Assembly;
    var gui = assembly.GetType("Krangler.Windows.UiGui", true)!;
    var presentation = assembly.GetType("Krangler.Windows.KranglerPresentation", true)!;
    var textType = assembly.GetType("Krangler.Windows.UiText", true)!;
    var constructor = textType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
    var callbackType = constructor.GetParameters()[1].ParameterType;
    var role = Expression.Parameter(callbackType.GenericTypeArguments[0]);
    var callback = Expression.Lambda(callbackType, Expression.Constant(new EmptyUiFont(), typeof(IDisposable)), role).Compile();
    using var text = (IDisposable)constructor.Invoke(["en", callback]);
    using var textScope = (IDisposable)textType.GetMethod("Enter", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(text, null)!;
    var themeFactory = presentation.GetMethod("Theme", members, null, [typeof(uint), typeof(MaterialColorSettings)], null)!;
    var button = gui.GetMethod("Button", members)!;
    var checkbox = gui.GetMethod("Checkbox", members, null, [typeof(string), typeof(bool).MakeByRefType(), typeof(string)], null)!;
    var surface = presentation.GetMethod("Surface", members)!;
    var previousContext = ImGui.GetCurrentContext();
    var context = ImGui.CreateContext();
    try
    {
        var io = ImGui.GetIO();
        io.IniFilename = null; io.LogFilename = null;
        io.DisplaySize = new(1000, 900); io.DeltaTime = 1f / 60;
        io.Fonts.AddFontDefault();
        byte* pixels = null; int width = 0, height = 0;
        io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
        io.Fonts.SetTexID(0, new ImTextureID((nint)1));
        PopupBackdropOwnerOpacity();
        foreach (var mode in new[] { MaterialColorMode.Auto, MaterialColorMode.Picker, MaterialColorMode.Lcars })
        foreach (var compact in new[] { false, true })
        foreach (var scale in new[] { 1f, 1.5f })
        {
            presentation.GetProperty("Compact", members)!.SetValue(null, compact);
            var theme = (MaterialTheme)themeFactory.Invoke(null, [0x6951E0u, new MaterialColorSettings { Mode = mode }])!;
            theme.Density = compact ? MaterialDensity.Compact : MaterialDensity.Standard;
            True(theme.IsLcars == (mode == MaterialColorMode.Lcars));
            var feedback = new List<MaterialUiSoundCue>();
            theme.Feedback = feedback.Add;
            var clicks = 0; var disabledClicks = 0;
            var toggle = new object?[] { "Show DTR Bar Entry##LcarsRegression", false, null };
            var buttonPoint = Vector2.Zero; var togglePoint = Vector2.Zero; var disabledPoint = Vector2.Zero;
            void Frame()
            {
                ImGui.NewFrame();
                using (MaterialTheme.Push(theme, scale, MaterialStyleMode.ColorsOnly))
                {
                    ImGui.SetNextWindowPos(Vector2.Zero);
                    ImGui.SetNextWindowSize(new Vector2(600, 500) * scale);
                    ImGui.Begin("Krangler appearance regression", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar);
                    var style = ImGui.GetStyle();
                    var snapshot = (style.FramePadding, style.FrameRounding, style.ItemSpacing, style.Colors[(int)ImGuiCol.Text], style.Colors[(int)ImGuiCol.Button]);
                    if ((bool)button.Invoke(null, ["Open Setup Wizard##LcarsRegression", Vector2.Zero, null])!) clicks++;
                    Equal(ImGui.GetID("Open Setup Wizard##LcarsRegression"), ImGuiP.GetItemID());
                    buttonPoint = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * .5f;
                    checkbox.Invoke(null, toggle);
                    Equal(ImGui.GetID((string)toggle[0]!), ImGuiP.GetItemID());
                    togglePoint = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * .5f;
                    ImGui.BeginDisabled();
                    if ((bool)button.Invoke(null, ["Disabled##LcarsRegression", Vector2.Zero, null])!) disabledClicks++;
                    disabledPoint = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * .5f;
                    ImGui.EndDisabled();
                    Equal(snapshot, (style.FramePadding, style.FrameRounding, style.ItemSpacing, style.Colors[(int)ImGuiCol.Text], style.Colors[(int)ImGuiCol.Button]));
                    var origin = ImGui.GetCursorScreenPos();
                    var vertices = ImGui.GetWindowDrawList().VtxBuffer.Size;
                    surface.Invoke(null, [origin, origin + new Vector2(240, 120) * scale, false]);
                    True(ImGui.GetWindowDrawList().VtxBuffer.Size > vertices);
                    ImGui.End();
                }
                ImGui.Render();
            }
            void Click(Vector2 point)
            {
                io.AddMousePosEvent(point.X, point.Y); Frame();
                io.AddMouseButtonEvent(0, true); Frame();
                io.AddMouseButtonEvent(0, false); Frame();
            }
            io.FontGlobalScale = scale;
            io.AddMousePosEvent(-1000, -1000);
            for (var frame = 0; frame < 4; frame++) Frame();
            Click(buttonPoint); Click(togglePoint); Click(disabledPoint);
            Equal(1, clicks); Equal(0, disabledClicks); True((bool)toggle[1]!);
            True(feedback.SequenceEqual(mode == MaterialColorMode.Lcars
                ? new[] { MaterialUiSoundCue.Click, MaterialUiSoundCue.Toggle } : Array.Empty<MaterialUiSoundCue>()));
        }
    }
    finally { ImGui.DestroyContext(context); ImGui.SetCurrentContext(previousContext); }
}

static void PopupBackdropOwnerOpacity()
{
    const BindingFlags instance = BindingFlags.NonPublic | BindingFlags.Instance;
    var plugin = (Plugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
    var config = new Configuration { UiTransparencyEnabled = true, UiWindowOpacityPercent = 80 };
    typeof(Plugin).GetProperty("Configuration")!.SetValue(plugin, config);
    var glass = (AethertekUI.Dalamud.MaterialGlassRenderer)System.Runtime.CompilerServices.RuntimeHelpers
        .GetUninitializedObject(typeof(AethertekUI.Dalamud.MaterialGlassRenderer));
    typeof(Plugin).GetField("backgroundGlass", instance)!.SetValue(plugin, glass);
    var first = new OpacityWindow("Opacity first##OwnerRegression");
    var second = new OpacityWindow("Opacity second##OwnerRegression");
    var unopened = new OpacityWindow("Opacity unopened##OwnerRegression");
    var firstOpacity = new MaterialWindowOpacity();
    var secondOpacity = new MaterialWindowOpacity();
    typeof(MaterialWindowOpacity).GetProperty("CurrentOpacity")!.SetValue(firstOpacity, .31f);
    typeof(MaterialWindowOpacity).GetProperty("CurrentOpacity")!.SetValue(secondOpacity, .67f);
    typeof(Plugin).GetField("windowOpacities", instance)!.SetValue(plugin,
        new Dictionary<Dalamud.Interface.Windowing.IWindow, MaterialWindowOpacity>
        { [first] = firstOpacity, [second] = secondOpacity });
    var setOpacity = typeof(Plugin).GetMethod("SetWindowBackgroundOpacity", instance)!;
    void DrawOwner(OpacityWindow owner)
    {
        ImGui.SetNextWindowSize(new Vector2(250, 150));
        ImGui.Begin(owner.WindowName, ImGuiWindowFlags.NoSavedSettings);
        ImGui.End();
    }
    for (var frame = 0; frame < 2; frame++)
    {
        ImGui.NewFrame();
        DrawOwner(first); DrawOwner(second);
        ImGui.Render();
    }
    True(!ImGuiP.FindWindowByName(first.WindowName).Appearing);
    setOpacity.Invoke(plugin, [first]); Equal(.31f, glass.Opacity);
    setOpacity.Invoke(plugin, [second]); Equal(.67f, glass.Opacity);
    setOpacity.Invoke(plugin, [first]); Equal(.31f, glass.Opacity);
    setOpacity.Invoke(plugin, [unopened]); Equal(.8f, glass.Opacity);
    config.UiTransparencyEnabled = false;
    setOpacity.Invoke(plugin, [first]); Equal(1f, glass.Opacity);
    config.UiTransparencyEnabled = true;
    ImGui.NewFrame(); DrawOwner(second); ImGui.Render();
    ImGui.NewFrame(); DrawOwner(first); DrawOwner(second);
    True(ImGuiP.FindWindowByName(first.WindowName).Appearing);
    // This runs outside the owner's Begin, as the centralized popup loop does.
    setOpacity.Invoke(plugin, [first]); Equal(.8f, glass.Opacity);
    setOpacity.Invoke(plugin, [second]); Equal(.67f, glass.Opacity);
    ImGui.Render();
}

static string[] AppearanceSnapshot() => Enumerable.Range(0, 24).Select(i =>
{
    var name = $"Example{i} Adventurer";
    var (race, tribe, gender) = AppearanceService.GetRandomRaceGender(name);
    return $"{race}/{tribe}/{gender}:" + string.Join(',', AppearanceService.GetRandomAppearance(name, race, gender)
        .OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
}).ToArray();

static void AppearanceUsesIdentityEpoch()
{
    KrangleService.BeginEpoch(false);
    var legacy = AppearanceSnapshot();
    KrangleService.BeginEpoch(true);
    var first = AppearanceSnapshot();
    True(first.SequenceEqual(AppearanceSnapshot()));
    KrangleService.BeginEpoch(true);
    True(!first.SequenceEqual(AppearanceSnapshot()));
    KrangleService.BeginEpoch(false);
    True(legacy.SequenceEqual(AppearanceSnapshot()));
}

static void FixedPresetChoiceSurvives()
{
    var service = (GlamourerPresetService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GlamourerPresetService));
    var fixedPreset = new Krangler.Models.GlamourerPreset { Name = "User's chosen look" };
    typeof(GlamourerPresetService).GetField("presets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .SetValue(service, new Dictionary<string, Krangler.Models.GlamourerPreset> { ["chosen"] = fixedPreset });
    typeof(GlamourerPresetService).GetField("presetAliases", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .SetValue(service, new Dictionary<string, Krangler.Models.GlamourerPreset> { ["chosen"] = fixedPreset });
    foreach (var randomized in new[] { false, true, true, false })
    {
        KrangleService.BeginEpoch(randomized);
        True(ReferenceEquals(fixedPreset, service.ResolvePresetSelection("Example Adventurer", "chosen")));
    }
}

static void RandomizedCatalogs()
{
    var assembly = typeof(KrangleService).Assembly;
    var catalogs = assembly.GetManifestResourceNames().Where(name => name.StartsWith("Krangler.Localization.Strings_")).ToArray();
    Equal(15, catalogs.Length);
    HashSet<string>? expectedKeys = null;
    foreach (var name in catalogs)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new System.Resources.ResourceReader(stream);
        var entries = reader.Cast<System.Collections.DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        True(!string.IsNullOrWhiteSpace(entries["RandomizedMode"]));
        True(!string.IsNullOrWhiteSpace(entries["RandomizedModeHelp"]));
        expectedKeys ??= entries.Keys.ToHashSet();
        True(expectedKeys.SetEquals(entries.Keys));
    }
}

static void RandomIdentitiesStayStable()
{
    KrangleService.BeginEpoch(true);
    var first = IdentitySnapshot();
    var appearanceSeed = SelectionSeed("Example Adventurer_appearance");
    KrangleService.ClearCache();
    True(first.SequenceEqual(IdentitySnapshot()));
    Equal(appearanceSeed, SelectionSeed("Example Adventurer_appearance"));
    True(first[2].StartsWith('«') && first[2].EndsWith('»'));
    True(first[3].StartsWith('<') && first[3].EndsWith('>'));
}

static void RandomEpochsRotate()
{
    KrangleService.BeginEpoch(true);
    var previous = IdentitySnapshot();
    var seed = SelectionSeed("Example Adventurer_appearance");
    KrangleService.BeginEpoch(true);
    var current = IdentitySnapshot();
    True(seed != SelectionSeed("Example Adventurer_appearance"));
    for (var field = 0; field < 4; field++)
        True(previous.Where((_, i) => i % 4 == field).Where((value, i) => value != current[i * 4 + field]).Any());
}

static void LegacyMappingsSurviveRoundtrip()
{
    KrangleService.BeginEpoch(false);
    // Values from the unchanged pre-epoch algorithm.
    Equal(17, SelectionSeed(""));
    Equal(18418, SelectionSeed("AB"));
    var before = IdentitySnapshot();
    KrangleService.BeginEpoch(true);
    IdentitySnapshot();
    KrangleService.BeginEpoch(false);
    True(before.SequenceEqual(IdentitySnapshot()));
    KrangleService.BeginEpoch(false);
    True(before.SequenceEqual(IdentitySnapshot()));
}

static void AcquireAndReacquire()
{
    var refreshes = 0;
    using var service = new DadPrivacyLeaseService(() => refreshes++);

    var acquired = Invoke(service.AcquireFromJson, "process-A");
    Equal(DadPrivacyLeaseService.AcquiredCode, acquired.Code);
    True(acquired.Ok && acquired.LeaseActive && acquired.OwnedByRequester);

    var reacquired = Invoke(service.AcquireFromJson, "process-A");
    Equal(DadPrivacyLeaseService.ReacquiredCode, reacquired.Code);
    True(reacquired.Ok && reacquired.LeaseActive && reacquired.OwnedByRequester);
    Equal(1, refreshes);
}

static void StaleTokenReplacement()
{
    var refreshes = 0;
    using var service = new DadPrivacyLeaseService(() => refreshes++);
    Invoke(service.AcquireFromJson, "stale-process");

    var replaced = Invoke(service.AcquireFromJson, "current-process");
    Equal(DadPrivacyLeaseService.ReplacedCode, replaced.Code);
    True(replaced.Ok && replaced.OwnedByRequester);

    var staleRelease = Invoke(service.ReleaseFromJson, "stale-process");
    Equal(DadPrivacyLeaseService.NotOwnerCode, staleRelease.Code);
    True(staleRelease.LeaseActive && !staleRelease.OwnedByRequester);

    var staleStatus = Invoke(service.GetStatusJson, "stale-process");
    Equal(DadPrivacyLeaseService.OwnedByOtherCode, staleStatus.Code);
    True(staleStatus.LeaseActive && !staleStatus.OwnedByRequester);

    var currentStatus = Invoke(service.GetStatusJson, "current-process");
    Equal(DadPrivacyLeaseService.OwnedCode, currentStatus.Code);
    True(currentStatus.LeaseActive && currentStatus.OwnedByRequester);
    Equal(1, refreshes);
}

static void WrongTokenProtection()
{
    using var service = new DadPrivacyLeaseService(() => { });
    Invoke(service.AcquireFromJson, "owner");

    var rejected = Invoke(service.ReleaseFromJson, "OWNER");
    Equal(DadPrivacyLeaseService.NotOwnerCode, rejected.Code);
    True(!rejected.Ok && rejected.LeaseActive && !rejected.OwnedByRequester);

    var status = Invoke(service.GetStatusJson, "owner");
    True(status.LeaseActive && status.OwnedByRequester);
}

static void ExactRelease()
{
    var refreshes = 0;
    using var service = new DadPrivacyLeaseService(() => refreshes++);
    Invoke(service.AcquireFromJson, "owner");

    var released = Invoke(service.ReleaseFromJson, "owner");
    Equal(DadPrivacyLeaseService.ReleasedCode, released.Code);
    True(released.Ok && !released.LeaseActive && !released.OwnedByRequester);

    var status = Invoke(service.GetStatusJson, "owner");
    Equal(DadPrivacyLeaseService.NotHeldCode, status.Code);
    True(status.Ok && !status.LeaseActive && !status.OwnedByRequester);
    Equal(2, refreshes);
}

static void DisposalCleanup()
{
    var service = new DadPrivacyLeaseService(() => { });
    Invoke(service.AcquireFromJson, "owner");
    service.Dispose();

    True(!service.IsActive);
    var disposed = Invoke(service.GetStatusJson, "owner");
    Equal(DadPrivacyLeaseService.DisposedCode, disposed.Code);
    True(!disposed.Ok && !disposed.LeaseActive && !disposed.OwnedByRequester);
}

static void EffectivePrivacyIncludesSelf()
{
    var effects = DadPrivacyLeasePolicy.Resolve(
        leaseActive: true,
        configuredMasterEnabled: false,
        configuredNameKrangling: false,
        configuredChatKrangling: false,
        configuredSkipSelf: true);

    True(effects.NameKranglingEnabled);
    True(effects.ChatKranglingEnabled);
    True(!effects.SkipSelfNameKrangling);

    using var service = new DadPrivacyLeaseService(() => { });
    var acquired = Invoke(service.AcquireFromJson, "owner");
    True(acquired.NamePrivacyActive && acquired.ChatPrivacyActive && acquired.IncludesSelf);
}

static void UnrelatedFeaturesRemainUnchanged()
{
    var settings = new PersistentSettingsProbe();
    var before = settings.FeatureSnapshot;

    using var service = new DadPrivacyLeaseService(() => { });
    Invoke(service.AcquireFromJson, "owner");
    _ = DadPrivacyLeasePolicy.Resolve(service.IsActive, false, false, false, true);

    Equal(before, settings.FeatureSnapshot);
}

static void LeaseOperationsPerformZeroSaves()
{
    var settings = new PersistentSettingsProbe();
    using var service = new DadPrivacyLeaseService(settings.RefreshRuntimeNameSurfaces);

    Invoke(service.AcquireFromJson, "owner-A");
    Invoke(service.AcquireFromJson, "owner-B");
    Invoke(service.ReleaseFromJson, "owner-A");
    Invoke(service.ReleaseFromJson, "owner-B");

    Equal(2, settings.RuntimeNameRefreshCount);
    Equal(0, settings.SaveCount);
}

static DadPrivacyLeaseContract.Response Invoke(Func<string, string> operation, string token)
{
    var json = JsonSerializer.Serialize(new DadPrivacyLeaseContract.Request { Token = token });
    return JsonSerializer.Deserialize<DadPrivacyLeaseContract.Response>(
               operation(json),
               new JsonSerializerOptions(JsonSerializerDefaults.Web))
           ?? throw new InvalidOperationException("Lease response was null.");
}

static void True(bool condition)
{
    if (!condition)
        throw new InvalidOperationException("Expected condition to be true.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}

sealed class PersistentSettingsProbe
{
    public bool KrangleRaces { get; } = false;
    public bool KrangleGenders { get; } = true;
    public bool KrangleAppearance { get; } = false;
    public bool KrangleNpcs { get; } = true;
    public int RuntimeNameRefreshCount { get; private set; }
    public int SaveCount { get; private set; }

    public (bool Race, bool Gender, bool Appearance, bool Npc) FeatureSnapshot
        => (KrangleRaces, KrangleGenders, KrangleAppearance, KrangleNpcs);

    public void RefreshRuntimeNameSurfaces()
        => RuntimeNameRefreshCount++;

    public void Save()
        => SaveCount++;
}

sealed class EmptyUiFont : IDisposable
{
    public void Dispose() { }
}

sealed class OpacityWindow(string name) : Dalamud.Interface.Windowing.Window(name)
{
    public override void Draw() { }
}
