using System.Text.Json;
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
};

foreach (var (name, run) in tests)
{
    run();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"PASS {tests.Length} Krangler DAD privacy lease tests");
return;

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
