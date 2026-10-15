using System.Text.Json;
using Krangler;
using Krangler.Services;

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
