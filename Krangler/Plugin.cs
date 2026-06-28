using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Game.Chat;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkUnitManager;
using Krangler.Services;
using Krangler.Windows;
using Krangler.Models;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using CharacterStruct = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using CharacterBaseStruct = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CharacterBase;
using DrawDataContainerStruct = FFXIVClientStructs.FFXIV.Client.Game.Character.DrawDataContainer;
using GameCustomizeData = FFXIVClientStructs.FFXIV.Client.Game.Character.CustomizeData;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using ClientVisibilityFlags = FFXIVClientStructs.FFXIV.Client.Game.Object.VisibilityFlags;
using HumanDrawData = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Human.DrawData;
using HumanStruct = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Human;
using BattleNpcSubKind = FFXIVClientStructs.FFXIV.Client.Game.Object.BattleNpcSubKind;

namespace Krangler;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static INamePlateGui NamePlateGui { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IToastGui ToastGui { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    
    private const string CommandName = "/krangler";
    private const string AliasCommandName = "/kr";

    public Configuration Configuration { get; init; }
    public AppearanceService AppearanceService { get; init; }
    public GlamourerPresetService GlamourerPresetService { get; init; }
    public ImaginaryFrenService ImaginaryFrenService { get; init; }
    private KranglerIpcService IpcService { get; init; }

    public readonly WindowSystem WindowSystem = new("Krangler");
    private MainWindow MainWindow { get; init; }

    private IDtrBarEntry? dtrEntry;
    private bool wasEnabled;
    private bool hasLoggedNameplateUpdate;
    private DateTime lastAppearanceScan = DateTime.MinValue;
    private DateTime lastPartyListScan = DateTime.MinValue;
    private bool hasLoggedAppearanceScan;
    private bool hasLoggedPartyList;
    private bool hasLoggedEventActivation;
    private readonly HashSet<string> loggedSoulThiefSkipReasons = new(StringComparer.Ordinal);
    private DateTime lastEventFlagReset = DateTime.MinValue;
    private DateTime lastSoulThiefCapture = DateTime.MinValue;
    private const int CustomizeByteCount = 26;
    private const int EquipmentSlotByteCount = 8;
    private const int EquipmentSlotCount = 10;
    private const int EquipmentByteCount = EquipmentSlotByteCount * EquipmentSlotCount;
    private const uint InvisibilityDrawStateFlag = 0x00000002;
    private const uint ObservedExactNpcHiddenRenderFlag = 0x00000100;
    private static readonly uint HiddenExactNpcRenderFlagsMask = (uint)(ClientVisibilityFlags.Model | ClientVisibilityFlags.Nameplate) | ObservedExactNpcHiddenRenderFlag;
    private const int ExactNpcHumanizeAttempts = 300;
    private const ushort SmallClothesNpcModelId = 9903;
    private static readonly uint[][] PartyMemberListTextNodePaths =
    {
        new uint[] { 26, 2, 41 },
        new uint[] { 26, 2, 42 },
        new uint[] { 26, 3, 42 },
        new uint[] { 26, 4, 42 },
        new uint[] { 26, 5, 42 },
        new uint[] { 26, 6, 42 },
        new uint[] { 26, 7, 42 },
        new uint[] { 26, 8, 42 },
        new uint[] { 26, 9, 42 },
    };

    private sealed class OriginalAppearanceData
    {
        public byte[] CustomizeData { get; } = new byte[CustomizeByteCount];
        public byte[] EquipmentData { get; } = new byte[EquipmentByteCount];
        public WeaponModelId MainHandWeapon { get; set; }
        public WeaponModelId OffHandWeapon { get; set; }
        public ushort Glasses0 { get; set; }
        public ushort Glasses1 { get; set; }
        public bool IsHatHidden { get; set; }
        public bool IsWeaponHidden { get; set; }
        public bool IsVisorToggled { get; set; }
        public bool VieraEarsHidden { get; set; }
        public uint RenderFlags { get; set; }
        public bool HasRenderFlags { get; set; }
        public int ModelCharaId { get; set; }
        public int ModelCharaId2 { get; set; }
        public int ModelSkeletonId { get; set; }
        public int ModelSkeletonId2 { get; set; }
        public bool HasModelContainerIds { get; set; }
    }

    private readonly struct PresetApplyResult
    {
        public PresetApplyResult(
            bool customizeApplied,
            bool customizeRefreshed,
            int equipmentApplied,
            int weaponsApplied,
            bool bonusApplied,
            bool metaApplied,
            bool preAppliedDuringCreate)
        {
            CustomizeApplied = customizeApplied;
            CustomizeRefreshed = customizeRefreshed;
            EquipmentApplied = equipmentApplied;
            WeaponsApplied = weaponsApplied;
            BonusApplied = bonusApplied;
            MetaApplied = metaApplied;
            PreAppliedDuringCreate = preAppliedDuringCreate;
        }

        public bool CustomizeApplied { get; }
        public bool CustomizeRefreshed { get; }
        public int EquipmentApplied { get; }
        public int WeaponsApplied { get; }
        public bool BonusApplied { get; }
        public bool MetaApplied { get; }
        public bool PreAppliedDuringCreate { get; }
        public bool AnyApplied => CustomizeApplied || EquipmentApplied > 0 || WeaponsApplied > 0 || BonusApplied || MetaApplied;
        public bool GeneralSuccess => CustomizeRefreshed || EquipmentApplied > 0 || WeaponsApplied > 0 || BonusApplied || MetaApplied;
        public bool ExactCustomizeSatisfied => !CustomizeApplied || CustomizeRefreshed || PreAppliedDuringCreate;
        public bool ExactSuccess => AnyApplied && ExactCustomizeSatisfied;
    }

    private enum PendingRedrawKind
    {
        InvisibleThenVisible,
        VisibleOnly,
        RestoreRenderFlags,
        ExactDisableDraw,
        ExactEnableDraw,
    }

    private readonly struct PendingRedrawEntry
    {
        public PendingRedrawEntry(nint address, PendingRedrawKind kind, uint? renderFlags = null)
        {
            Address = address;
            Kind = kind;
            RenderFlags = renderFlags;
        }

        public nint Address { get; }
        public PendingRedrawKind Kind { get; }
        public uint? RenderFlags { get; }
    }

    private readonly struct PendingCreatedCharacterBaseEntry
    {
        public PendingCreatedCharacterBaseEntry(nint address, int remainingAttempts)
        {
            Address = address;
            RemainingAttempts = remainingAttempts;
        }

        public nint Address { get; }
        public int RemainingAttempts { get; }
    }

    private readonly struct PendingAmongusNpcEntry
    {
        public PendingAmongusNpcEntry(
            string npcName,
            ObjectKind objectKind,
            ulong gameObjectId,
            nint queuedAddress,
            int remainingAttempts,
            bool preAppliedDuringCreate = false,
            int lastModelCharaId = 0,
            int lastModelCharaId2 = 0,
            int lastModelSkeletonId = 0,
            int lastModelSkeletonId2 = 0,
            string lastDrawObjectType = "unknown")
        {
            NpcName = npcName;
            ObjectKind = objectKind;
            GameObjectId = gameObjectId;
            QueuedAddress = queuedAddress;
            RemainingAttempts = remainingAttempts;
            PreAppliedDuringCreate = preAppliedDuringCreate;
            LastModelCharaId = lastModelCharaId;
            LastModelCharaId2 = lastModelCharaId2;
            LastModelSkeletonId = lastModelSkeletonId;
            LastModelSkeletonId2 = lastModelSkeletonId2;
            LastDrawObjectType = lastDrawObjectType;
        }

        public string NpcName { get; }
        public ObjectKind ObjectKind { get; }
        public ulong GameObjectId { get; }
        public nint QueuedAddress { get; }
        public int RemainingAttempts { get; }
        public bool PreAppliedDuringCreate { get; }
        public int LastModelCharaId { get; }
        public int LastModelCharaId2 { get; }
        public int LastModelSkeletonId { get; }
        public int LastModelSkeletonId2 { get; }
        public string LastDrawObjectType { get; }

        public PendingAmongusNpcEntry WithRemainingAttempts(int remainingAttempts)
            => new(NpcName, ObjectKind, GameObjectId, QueuedAddress, remainingAttempts, PreAppliedDuringCreate, LastModelCharaId, LastModelCharaId2, LastModelSkeletonId, LastModelSkeletonId2, LastDrawObjectType);

        public PendingAmongusNpcEntry WithQueuedAddress(nint queuedAddress)
            => new(NpcName, ObjectKind, GameObjectId, queuedAddress, RemainingAttempts, PreAppliedDuringCreate, LastModelCharaId, LastModelCharaId2, LastModelSkeletonId, LastModelSkeletonId2, LastDrawObjectType);

        public PendingAmongusNpcEntry WithPreAppliedDuringCreate()
            => new(NpcName, ObjectKind, GameObjectId, QueuedAddress, RemainingAttempts, true, LastModelCharaId, LastModelCharaId2, LastModelSkeletonId, LastModelSkeletonId2, LastDrawObjectType);

        public PendingAmongusNpcEntry WithLastObserved(
            nint queuedAddress,
            int lastModelCharaId,
            int lastModelCharaId2,
            int lastModelSkeletonId,
            int lastModelSkeletonId2,
            string lastDrawObjectType)
            => new(NpcName, ObjectKind, GameObjectId, queuedAddress, RemainingAttempts, PreAppliedDuringCreate, lastModelCharaId, lastModelCharaId2, lastModelSkeletonId, lastModelSkeletonId2, lastDrawObjectType);
    }

    // Track original customize data for revert, keyed by GameObjectId
    private readonly Dictionary<ulong, OriginalAppearanceData> originalAppearanceData = new();

    // Runtime override for event-based activation
    private bool IsSuperKrangleEventWindowActive => !string.IsNullOrEmpty(GetDateBasedForcedPreset());
    private bool IsSuperKrangleEventActive => !Configuration.DisableDateBasedSuperKrangleEvent && IsSuperKrangleEventWindowActive;
    private bool SuperKrangleMaster4000_Active => Configuration.SuperKrangleMaster4000 || IsSuperKrangleEventActive;

    private void ResetEventFlags()
    {
        var today = DateTime.Today;
        if (today != lastEventFlagReset)
        {
            hasLoggedEventActivation = false;
            lastEventFlagReset = today;
        }
    }
    // Staged local redraw queue modeled after Penumbra's local redraw sequencing.
    private readonly Queue<PendingRedrawEntry> redrawQueue = new();
    private readonly HashSet<nint> pendingRedrawAddresses = new();
    private readonly Queue<PendingCreatedCharacterBaseEntry> pendingCreatedCharacterBaseQueue = new();
    private readonly HashSet<nint> pendingCreatedCharacterBaseAddresses = new();
    private readonly HashSet<ulong> pendingAmongusObjectKeys = new();
    private readonly Dictionary<ulong, PendingAmongusNpcEntry> pendingAmongusNpcEntries = new();
    private readonly HashSet<ulong> amongusKeepVisibleObjectKeys = new();
    private readonly HashSet<ulong> loggedAmongusMatchObjectKeys = new();
    private readonly HashSet<ulong> loggedAmongusVisibilityObjectKeys = new();
    private readonly HashSet<ulong> loggedPendingAmongusObjectKeys = new();
    private readonly HashSet<ulong> loggedAppliedPendingAmongusObjectKeys = new();
    private readonly HashSet<ulong> loggedAmongusCustomizeRefreshFalseObjectKeys = new();
    private readonly HashSet<ulong> loggedExhaustedPendingAmongusObjectKeys = new();
    private readonly HashSet<string> loggedMissingAmongusObjectNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> loggedMissingAmongusPresetKeys = new(StringComparer.OrdinalIgnoreCase);
    private Hook<CreateCharacterBaseDelegate>? createCharacterBaseHook;
    private int redrawCooldownFrames = 0;
    private int currentVisiblePlayerCount;
    private bool isRevertingAppearances;
    private bool hasShownPartyMemberListFallbackWarning;
    private const int CharacterBaseReapplyAttempts = 5;

    private unsafe delegate CharacterBaseStruct* CreateCharacterBaseDelegate(uint modelId, GameCustomizeData* customize, EquipmentModelId* equipment, byte unk);

    public Plugin()
    {
        var savedConfiguration = PluginInterface.GetPluginConfig() as Configuration;
        var isFirstRun = savedConfiguration == null;
        Configuration = savedConfiguration ?? Configuration.CreateFirstRun();
        if (isFirstRun || Configuration.Sanitize())
            Configuration.Save();

        AppearanceService = new AppearanceService(Log, ObjectTable, Configuration);
        GlamourerPresetService = new GlamourerPresetService(Log, PluginInterface);
        ImaginaryFrenService = new ImaginaryFrenService(this);
        IpcService = new KranglerIpcService(PluginInterface, ImaginaryFrenService, GlamourerPresetService);

        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Krangler window."
        });

        CommandManager.AddHandler(AliasCommandName, new CommandInfo(OnAliasCommand)
        {
            HelpMessage = "Krangler: /kr [on|off|debug|fren|ws|j] to control the plugin, or /kr to open UI."
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // DTR bar
        SetupDtrBar();

        // Nameplate hook for name krangling
        NamePlateGui.OnNamePlateUpdate += OnNamePlateUpdate;

        try 
        {
            ChatGui.ChatMessage += OnChatMessage;
            Log.Information("[Krangler] ChatMessage event subscription successful");
        }
        catch (Exception ex)
        {
            Log.Error($"[Krangler] Failed to subscribe to ChatMessage event: {ex.Message}");
        }

        // Framework update for DTR bar + appearance + party list
        Framework.Update += Framework_OnUpdate;
        AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "PartyMemberList", OnPartyMemberListAddon);
        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "PartyMemberList", OnPartyMemberListAddon);

        // Territory change handler for re-applying krangling
        ClientState.TerritoryChanged += OnTerritoryChanged;

        try
        {
            unsafe
            {
                createCharacterBaseHook = GameInterop.HookFromAddress<CreateCharacterBaseDelegate>((nint)CharacterBaseStruct.MemberFunctionPointers.Create, CreateCharacterBaseDetour);
            }
            createCharacterBaseHook.Enable();
        }
        catch (Exception ex)
        {
            Log.Error($"[Krangler] Failed to initialize CharacterBase.Create hook: {ex.Message}");
        }

        wasEnabled = Configuration.Enabled;

        Log.Information("===Krangler loaded===");
    }

    private void OnCommand(string command, string args) => HandleCommand(args);

    private void OnAliasCommand(string command, string args) => HandleCommand(args);

    private void HandleCommand(string args)
    {
        var arg = args.Trim().ToLowerInvariant();
        if (arg == "on")
        {
            Configuration.Enabled = true;
            Configuration.Save();
            Log.Information("[Krangler] Enabled via command");
            PrintStatus("Enabled.");
        }
        else if (arg == "off")
        {
            Configuration.Enabled = false;
            KrangleService.ClearCache();
            Configuration.Save();
            Log.Information("[Krangler] Disabled via command");
            PrintStatus("Disabled.");
        }
        else if (arg == "debug")
        {
            ToggleDebugOptions();
        }
        else if (arg == "fren")
        {
            PrintImaginaryFrenStatus();
        }
        else if (arg == "ws")
        {
            ResetMainWindowPosition();
        }
        else if (arg == "j")
        {
            JumpMainWindowToRandomVisibleLocation();
        }
        else
        {
            MainWindow.Toggle();
        }
    }

    private void Framework_OnUpdate(IFramework framework)
    {
        ResetEventFlags();
        
        // Process staggered redraws — one character per N frames
        ProcessRedrawQueue();
        ProcessCreatedCharacterBaseQueue();
        ProcessPendingAmongusNpcEntries();

        // Track enable/disable transitions for logging
        if (Configuration.Enabled != wasEnabled)
        {
            if (Configuration.Enabled)
            {
                Log.Information("[Krangler] Krangling activated");
                hasLoggedNameplateUpdate = false;
                hasLoggedAppearanceScan = false;
                hasLoggedPartyList = false;
            }
            else
            {
                Log.Information("[Krangler] Krangling deactivated");
                RevertAllAppearances();
                RestoreTargetInfoSurfaces();
            }
            wasEnabled = Configuration.Enabled;
        }

        ImaginaryFrenService.Update();

        if (!Configuration.Enabled)
        {
            UpdateDtrBar();
            return;
        }

        MaintainAmongusNpcVisibility();

        // Appearance krangling via direct memory (throttled to every 5 seconds)
        if (Configuration.KrangleGenders ||
            Configuration.KrangleRaces ||
            Configuration.KrangleAppearance ||
            SuperKrangleMaster4000_Active ||
            HasActiveAmongusReplacements() ||
            HasActiveSoulThiefTargets())
        {
            var now = DateTime.UtcNow;
            if ((now - lastAppearanceScan).TotalSeconds >= 5)
            {
                lastAppearanceScan = now;
                ScanAndKrangleAppearances();
            }
        }

        // Party list krangling (throttled to every 1 second)
        if (Configuration.KrangleNames || SuperKrangleMaster4000_Active)
        {
            UpdateTargetInfoSurfaces();

            var now = DateTime.UtcNow;
            if ((now - lastPartyListScan).TotalSeconds >= 1)
            {
                lastPartyListScan = now;
                KranglePartyList();
                KranglePartyMemberList();
            }
        }

        UpdateDtrBar();
    }

    // ─── Territory Change Handler ───────────────────────────────────────

    private void OnTerritoryChanged(uint territory)
    {
        ImaginaryFrenService.Despawn($"territory change {territory}");

        if (!Configuration.Enabled) return;
        
        Log.Information($"[Krangler] Territory changed to {territory}, re-applying krangle mode");

        RevertAllAppearances();
        
        // Force immediate scan to re-apply krangling
        lastAppearanceScan = DateTime.MinValue;
        lastPartyListScan = DateTime.MinValue;
        hasLoggedAppearanceScan = false;
        hasLoggedPartyList = false;
    }

    private void OnTerritoryChanged(ushort territory)
        => OnTerritoryChanged((uint)territory);

    private unsafe CharacterBaseStruct* CreateCharacterBaseDetour(uint modelId, GameCustomizeData* customize, EquipmentModelId* equipment, byte unk)
    {
        var createModelId = modelId;
        if (Configuration.Enabled &&
            (SuperKrangleMaster4000_Active || HasActiveAmongusReplacements()) &&
            !isRevertingAppearances &&
            !ImaginaryFrenService.IsSpawningActor &&
            customize != null &&
            equipment != null)
        {
            try
            {
                TryApplyPresetToCreateBuffers(ref createModelId, customize, equipment);
            }
            catch (Exception ex)
            {
                if (!hasLoggedAppearanceScan)
                    Log.Warning($"[Krangler] CharacterBase.Create pre-apply failed: {ex.Message}");
            }
        }

        var createdCharacterBase = createCharacterBaseHook!.Original(createModelId, customize, equipment, unk);
        if (!Configuration.Enabled ||
            (!SuperKrangleMaster4000_Active && !HasActiveAmongusReplacements()) ||
            isRevertingAppearances ||
            ImaginaryFrenService.IsSpawningActor ||
            createdCharacterBase == null ||
            IsManagedImaginaryFrenDrawObject((nint)createdCharacterBase) ||
            createdCharacterBase->GetModelType() != CharacterBaseStruct.ModelType.Human)
        {
            return createdCharacterBase;
        }

        try
        {
            if (!TryReapplyPresetToCreatedCharacterBase((nint)createdCharacterBase))
                QueueCreatedCharacterBaseReapply((nint)createdCharacterBase);
        }
        catch (Exception ex)
        {
            if (!hasLoggedAppearanceScan)
                Log.Warning($"[Krangler] CharacterBase.Create reapply failed: {ex.Message}");
        }

        return createdCharacterBase;
    }

    private void QueueCreatedCharacterBaseReapply(nint address)
    {
        if (address == 0 || !pendingCreatedCharacterBaseAddresses.Add(address))
            return;

        pendingCreatedCharacterBaseQueue.Enqueue(new PendingCreatedCharacterBaseEntry(address, CharacterBaseReapplyAttempts));
    }

    private unsafe void ProcessCreatedCharacterBaseQueue()
    {
        if (pendingCreatedCharacterBaseQueue.Count == 0)
            return;

        var pendingCount = pendingCreatedCharacterBaseQueue.Count;
        for (var i = 0; i < pendingCount; i++)
        {
            var pending = pendingCreatedCharacterBaseQueue.Dequeue();
            if (TryReapplyPresetToCreatedCharacterBase(pending.Address))
            {
                pendingCreatedCharacterBaseAddresses.Remove(pending.Address);
                continue;
            }

            if (pending.RemainingAttempts > 1)
            {
                pendingCreatedCharacterBaseQueue.Enqueue(new PendingCreatedCharacterBaseEntry(pending.Address, pending.RemainingAttempts - 1));
            }
            else
            {
                pendingCreatedCharacterBaseAddresses.Remove(pending.Address);
            }
        }
    }

    private unsafe bool TryReapplyPresetToCreatedCharacterBase(nint characterBaseAddress)
    {
        if (IsManagedImaginaryFrenDrawObject(characterBaseAddress))
            return true;

        if (TryFindAmongusNpcByDrawObject(
                characterBaseAddress,
                out var amongusObjectKey,
                out var amongusName,
                out var amongusCharacter,
                out var amongusReplacement,
                out var amongusObjectAddress,
                out var amongusObjectKind,
                out var amongusGameObjectId))
        {
            SaveOriginalAppearanceIfNeeded(amongusObjectKey, amongusCharacter, amongusObjectAddress);
            ForceAmongusNpcVisible(amongusObjectKey, amongusName, amongusObjectKind, amongusGameObjectId, amongusObjectAddress);

            var amongusPreset = ResolveAmongusPreset(amongusReplacement, amongusName);
            if (amongusPreset == null)
            {
                RemovePendingAmongusNpcEntry(amongusObjectKey);
                return true;
            }

            if (!TryGetPendingAmongusNpcEntry(
                    amongusObjectKey,
                    amongusGameObjectId,
                    amongusObjectAddress,
                    amongusName,
                    out var pendingObjectKey,
                    out var pendingEntry))
            {
                pendingObjectKey = amongusObjectKey;
                pendingEntry = CreatePendingAmongusNpcEntry(
                    amongusName,
                    amongusObjectKind,
                    amongusGameObjectId,
                    amongusObjectAddress,
                    amongusCharacter);
                pendingAmongusObjectKeys.Add(pendingObjectKey);
                pendingAmongusNpcEntries[pendingObjectKey] = pendingEntry;
            }

            ApplyExactNpcPresetDrawData(amongusCharacter, amongusPreset);
            ForceExactNpcModelContainer(amongusCharacter, amongusPreset);

            var result = ApplySuperKranglePresetDetailed(
                amongusCharacter,
                amongusPreset,
                logRefreshResult: false,
                exactNpcReplacement: true,
                logDetails: false,
                preAppliedDuringCreate: pendingEntry.PreAppliedDuringCreate);

            if (result.ExactSuccess)
            {
                AppearanceService.MarkApplied(amongusObjectKey);
                RemovePendingAmongusNpcEntry(pendingObjectKey, amongusObjectKey);
                LogAmongusPresetAppliedIfNeeded(amongusObjectKey, amongusName, amongusPreset.Name, pendingEntry.PreAppliedDuringCreate ? "CharacterBase.Create" : "post-create refresh");
            }
            else if (result.CustomizeApplied && !result.CustomizeRefreshed && !pendingEntry.PreAppliedDuringCreate)
            {
                QueueExactNpcRedraw(amongusObjectAddress, ReadExactNpcVisibleRenderFlags(amongusObjectAddress));
                LogAmongusCustomizeRefreshFalseIfNeeded(amongusObjectKey, amongusName, amongusPreset.Name);
            }

            return true;
        }

        if (!SuperKrangleMaster4000_Active)
            return pendingAmongusObjectKeys.Count > 0 ? false : true;

        if (!TryFindPlayerCharacterByDrawObject(characterBaseAddress, out var objectKey, out var playerName, out var character))
            return false;

        if (!AppearanceService.IsApplied(objectKey) && !originalAppearanceData.ContainsKey(objectKey))
            return true;

        var selection = ResolveSuperKrangleSelection(playerName);
        var preset = GlamourerPresetService.ResolvePresetSelection(playerName, selection);
        if (preset == null)
            return true;

        SaveOriginalAppearanceIfNeeded(objectKey, character);
        if (ApplySuperKranglePreset(character, preset, false) && !hasLoggedAppearanceScan)
            Log.Information($"[Krangler] Re-applied preset '{preset.Name}' during CharacterBase.Create for '{playerName}'");

        return true;
    }

    private unsafe bool TryApplyPresetToCreateBuffers(ref uint modelId, GameCustomizeData* customize, EquipmentModelId* equipment)
    {
        if (TryApplyAmongusPresetToCreateBuffers(ref modelId, customize, equipment))
            return true;

        if (!SuperKrangleMaster4000_Active)
            return false;

        if (!TryFindPlayerCharacterByCreateBuffers(customize, equipment, out var objectKey, out var playerName, out var character))
            return false;

        var selection = ResolveSuperKrangleSelection(playerName);
        var preset = GlamourerPresetService.ResolvePresetSelection(playerName, selection);
        if (preset == null)
            return true;

        SaveOriginalAppearanceIfNeeded(objectKey, character);

        var appliedAppearance = ApplyCustomizeData(customize, preset);
        var appliedEquipment = ApplyEquipmentData(equipment, preset, null);
        var appliedModelId = false;
        if (Configuration.SuperKrangleApplyAppearance && preset.Customize.ModelId > 0)
        {
            modelId = (uint)preset.Customize.ModelId;
            appliedModelId = true;
        }

        if ((appliedAppearance || appliedEquipment > 0 || appliedModelId) && !hasLoggedAppearanceScan)
            Log.Information($"[Krangler] Pre-applied preset '{preset.Name}' during CharacterBase.Create for '{playerName}': modelId={modelId}, equipmentSlots={appliedEquipment}");

        return true;
    }

    private unsafe bool TryApplyAmongusPresetToCreateBuffers(ref uint modelId, GameCustomizeData* customize, EquipmentModelId* equipment)
    {
        if (!TryFindAmongusNpcByCreateBuffers(
                customize,
                equipment,
                out var objectKey,
                out var npcName,
                out var character,
                out var replacement,
                out var objectAddress,
                out var objectKind,
                out var gameObjectId))
            return false;

        SaveOriginalAppearanceIfNeeded(objectKey, character, objectAddress);
        ForceAmongusNpcVisible(objectKey, npcName, objectKind, gameObjectId, objectAddress);

        var preset = ResolveAmongusPreset(replacement, npcName);
        if (preset == null)
            return true;

        var appliedAppearance = ApplyCustomizeData(customize, preset);
        var appliedEquipment = ApplyEquipmentData(equipment, preset, null, true);
        var originalModelId = modelId;
        modelId = GetExactNpcCreateModelId(preset);
        ApplyExactNpcPresetDrawData(character, preset);
        ForceExactNpcModelContainer(character, preset);

        UpsertPendingAmongusNpcEntry(
            objectKey,
            npcName,
            objectKind,
            gameObjectId,
            objectAddress,
            character,
            preAppliedDuringCreate: true);

        if ((appliedAppearance || appliedEquipment > 0 || originalModelId != modelId) && !hasLoggedAppearanceScan)
            Log.Information($"[Krangler] Pre-applied Amongus preset '{preset.Name}' during CharacterBase.Create for '{npcName}': modelId={modelId}, equipmentSlots={appliedEquipment}");

        return true;
    }

    private unsafe bool TryFindAmongusNpcByCreateBuffers(
        GameCustomizeData* customize,
        EquipmentModelId* equipment,
        out ulong objectKey,
        out string npcName,
        out CharacterStruct* character,
        out AmongusNpcReplacement replacement,
        out nint objectAddress,
        out ObjectKind objectKind,
        out ulong gameObjectId)
    {
        objectKey = 0;
        npcName = string.Empty;
        character = null;
        replacement = null!;
        objectAddress = 0;
        objectKind = default;
        gameObjectId = 0;

        if (!HasActiveAmongusReplacements() || customize == null && equipment == null)
            return false;

        for (var objectIndex = 0; objectIndex < ObjectTable.Length; objectIndex++)
        {
            var obj = ObjectTable[objectIndex];
            if (obj == null || obj.Address == 0 || ImaginaryFrenService.IsManagedActor(obj.Address) || !IsAmongusObjectKind(obj.ObjectKind))
                continue;

            var name = obj.Name.ToString();
            if (!TryGetAmongusReplacement(name, out replacement))
                continue;

            var candidate = (CharacterStruct*)obj.Address;
            if (candidate == null)
                continue;

            var customizeMatches = customize != null && (GameCustomizeData*)&candidate->DrawData.CustomizeData == customize;
            var equipmentMatches = false;
            fixed (EquipmentModelId* candidateEquipment = &candidate->DrawData.EquipmentModelIds[0])
            {
                equipmentMatches = equipment != null && candidateEquipment == equipment;
            }

            if (!customizeMatches && !equipmentMatches)
                continue;

            objectKey = GetAppearanceObjectKey(obj);
            npcName = name;
            character = candidate;
            objectAddress = obj.Address;
            objectKind = obj.ObjectKind;
            gameObjectId = obj.GameObjectId;
            return !string.IsNullOrWhiteSpace(npcName);
        }

        return false;
    }

    private unsafe bool TryFindAmongusNpcByDrawObject(
        nint drawObjectAddress,
        out ulong objectKey,
        out string npcName,
        out CharacterStruct* character,
        out AmongusNpcReplacement replacement,
        out nint objectAddress,
        out ObjectKind objectKind,
        out ulong gameObjectId)
    {
        objectKey = 0;
        npcName = string.Empty;
        character = null;
        replacement = null!;
        objectAddress = 0;
        objectKind = default;
        gameObjectId = 0;

        if (!HasActiveAmongusReplacements() || drawObjectAddress == 0)
            return false;

        for (var objectIndex = 0; objectIndex < ObjectTable.Length; objectIndex++)
        {
            var obj = ObjectTable[objectIndex];
            if (obj == null || obj.Address == 0 || ImaginaryFrenService.IsManagedActor(obj.Address) || !IsAmongusObjectKind(obj.ObjectKind))
                continue;

            var name = obj.Name.ToString();
            if (!TryGetAmongusReplacement(name, out replacement))
                continue;

            var candidate = (CharacterStruct*)obj.Address;
            if (candidate == null || (nint)candidate->DrawObject != drawObjectAddress)
                continue;

            objectKey = GetAppearanceObjectKey(obj);
            npcName = name;
            character = candidate;
            objectAddress = obj.Address;
            objectKind = obj.ObjectKind;
            gameObjectId = obj.GameObjectId;
            return !string.IsNullOrWhiteSpace(npcName);
        }

        return false;
    }

    private unsafe bool TryFindPlayerCharacterByCreateBuffers(GameCustomizeData* customize, EquipmentModelId* equipment, out ulong objectKey, out string playerName, out CharacterStruct* character)
    {
        objectKey = 0;
        playerName = string.Empty;
        character = null;

        if (customize == null && equipment == null)
            return false;

        for (var objectIndex = 0; objectIndex < ObjectTable.Length; objectIndex++)
        {
            var obj = ObjectTable[objectIndex];
            if (obj == null || obj.Address == 0 || ImaginaryFrenService.IsManagedActor(obj.Address) || obj.ObjectKind != ObjectKind.Pc)
                continue;

            var candidate = (CharacterStruct*)obj.Address;
            if (candidate == null)
                continue;

            var customizeMatches = customize != null && (GameCustomizeData*)&candidate->DrawData.CustomizeData == customize;
            var equipmentMatches = false;
            fixed (EquipmentModelId* candidateEquipment = &candidate->DrawData.EquipmentModelIds[0])
            {
                equipmentMatches = equipment != null && candidateEquipment == equipment;
            }

            if (!customizeMatches && !equipmentMatches)
                continue;

            objectKey = obj.GameObjectId;
            playerName = obj.Name.ToString();
            character = candidate;
            if (IsLocalPlayerObject(objectKey, obj.Address))
                return false;
            return !string.IsNullOrWhiteSpace(playerName);
        }

        return false;
    }

    private unsafe bool TryFindPlayerCharacterByDrawObject(nint drawObjectAddress, out ulong objectKey, out string playerName, out CharacterStruct* character)
    {
        objectKey = 0;
        playerName = string.Empty;
        character = null;

        if (drawObjectAddress == 0)
            return false;

        for (var objectIndex = 0; objectIndex < ObjectTable.Length; objectIndex++)
        {
            var obj = ObjectTable[objectIndex];
            if (obj == null || obj.Address == 0 || ImaginaryFrenService.IsManagedActor(obj.Address) || obj.ObjectKind != ObjectKind.Pc)
                continue;

            var candidate = (CharacterStruct*)obj.Address;
            if (candidate == null || (nint)candidate->DrawObject != drawObjectAddress)
                continue;

            objectKey = obj.GameObjectId;
            playerName = obj.Name.ToString();
            character = candidate;
            if (IsLocalPlayerObject(objectKey, obj.Address))
                return false;
            return !string.IsNullOrWhiteSpace(playerName);
        }

        return false;
    }

    // ─── Chat Message Garbling ───────────────────────────────────────

    private bool HasActiveAmongusReplacements()
    {
        if (!Configuration.AmongusEnabled || Configuration.AmongusNpcReplacements == null)
            return false;

        foreach (var replacement in Configuration.AmongusNpcReplacements)
        {
            if (replacement == null ||
                !replacement.Enabled ||
                string.IsNullOrWhiteSpace(replacement.NpcName) ||
                string.IsNullOrWhiteSpace(replacement.PresetKey))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool ShouldMaintainAmongusNpcVisibility()
        => HasActiveAmongusReplacements() ||
           pendingAmongusObjectKeys.Count > 0 ||
           amongusKeepVisibleObjectKeys.Count > 0;

    private unsafe void ProcessPendingAmongusNpcEntries()
    {
        if (!Configuration.Enabled || pendingAmongusNpcEntries.Count == 0)
            return;

        var pendingEntries = new List<KeyValuePair<ulong, PendingAmongusNpcEntry>>(pendingAmongusNpcEntries);
        foreach (var (pendingObjectKey, pendingEntry) in pendingEntries)
        {
            var currentPendingEntry = pendingEntry;
            if (!TryFindAmongusNpcForPendingEntry(
                    pendingObjectKey,
                    currentPendingEntry,
                    out var actualObjectKey,
                    out var npcName,
                    out var character,
                    out var replacement,
                    out var objectAddress,
                    out var objectKind,
                    out var gameObjectId))
            {
                DecrementPendingAmongusAttempt(pendingObjectKey, pendingEntry);
                continue;
            }

            SaveOriginalAppearanceIfNeeded(actualObjectKey, character, objectAddress);
            ForceAmongusNpcVisible(actualObjectKey, npcName, objectKind, gameObjectId, objectAddress);

            var preset = ResolveAmongusPreset(replacement, npcName);
            if (preset == null)
            {
                RemovePendingAmongusNpcEntry(pendingObjectKey, actualObjectKey);
                continue;
            }

            var previousQueuedAddress = currentPendingEntry.QueuedAddress;
            currentPendingEntry = currentPendingEntry.WithLastObserved(
                objectAddress,
                character->ModelContainer.ModelCharaId,
                character->ModelContainer.ModelCharaId_2,
                character->ModelContainer.ModelSkeletonId,
                character->ModelContainer.ModelSkeletonId_2,
                GetDrawObjectTypeName(character));
            pendingAmongusNpcEntries[pendingObjectKey] = currentPendingEntry;

            ApplyExactNpcPresetDrawData(character, preset);
            ForceExactNpcModelContainer(character, preset);
            if (objectAddress != previousQueuedAddress)
            {
                QueueExactNpcRedraw(objectAddress, ReadExactNpcVisibleRenderFlags(objectAddress));
                currentPendingEntry = currentPendingEntry.WithQueuedAddress(objectAddress);
                pendingAmongusNpcEntries[pendingObjectKey] = currentPendingEntry;
            }

            if (!SupportsHumanCustomize(character))
            {
                QueueExactNpcRedraw(objectAddress, ReadExactNpcVisibleRenderFlags(objectAddress));
                DecrementPendingAmongusAttempt(pendingObjectKey, currentPendingEntry);
                continue;
            }

            var result = ApplySuperKranglePresetDetailed(
                character,
                preset,
                logRefreshResult: false,
                exactNpcReplacement: true,
                logDetails: false,
                preAppliedDuringCreate: currentPendingEntry.PreAppliedDuringCreate);

            if (result.ExactSuccess)
            {
                AppearanceService.MarkApplied(actualObjectKey);
                RemovePendingAmongusNpcEntry(pendingObjectKey, actualObjectKey);
                LogAmongusPresetAppliedIfNeeded(actualObjectKey, npcName, preset.Name, currentPendingEntry.PreAppliedDuringCreate ? "CharacterBase.Create" : "after humanize redraw");
                continue;
            }

            if (result.CustomizeApplied && !result.CustomizeRefreshed && !currentPendingEntry.PreAppliedDuringCreate)
            {
                QueueExactNpcRedraw(objectAddress, ReadExactNpcVisibleRenderFlags(objectAddress));
                LogAmongusCustomizeRefreshFalseIfNeeded(actualObjectKey, npcName, preset.Name);
            }

            DecrementPendingAmongusAttempt(pendingObjectKey, currentPendingEntry);
        }
    }

    private unsafe bool TryFindAmongusNpcForPendingEntry(
        ulong pendingObjectKey,
        PendingAmongusNpcEntry pendingEntry,
        out ulong objectKey,
        out string npcName,
        out CharacterStruct* character,
        out AmongusNpcReplacement replacement,
        out nint objectAddress,
        out ObjectKind objectKind,
        out ulong gameObjectId)
    {
        objectKey = 0;
        npcName = string.Empty;
        character = null;
        replacement = null!;
        objectAddress = 0;
        objectKind = default;
        gameObjectId = 0;

        for (var objectIndex = 0; objectIndex < ObjectTable.Length; objectIndex++)
        {
            var obj = ObjectTable[objectIndex];
            if (obj == null || obj.Address == 0 || ImaginaryFrenService.IsManagedActor(obj.Address) || !IsAmongusObjectKind(obj.ObjectKind))
                continue;

            var candidateObjectKey = GetAppearanceObjectKey(obj);
            var candidateName = obj.Name.ToString();
            var gameObjectIdMatches = pendingEntry.GameObjectId != 0 && obj.GameObjectId == pendingEntry.GameObjectId;
            var objectKeyMatches = candidateObjectKey == pendingObjectKey;
            var queuedAddressMatches = pendingEntry.QueuedAddress != 0 && obj.Address == pendingEntry.QueuedAddress;
            if (!gameObjectIdMatches && !objectKeyMatches && !queuedAddressMatches)
                continue;

            if (!TryGetAmongusReplacement(candidateName, out replacement))
                continue;

            objectKey = candidateObjectKey;
            npcName = candidateName;
            character = (CharacterStruct*)obj.Address;
            objectAddress = obj.Address;
            objectKind = obj.ObjectKind;
            gameObjectId = obj.GameObjectId;
            return !string.IsNullOrWhiteSpace(npcName);
        }

        if (pendingEntry.GameObjectId != 0 || CountPendingAmongusEntriesByName(pendingEntry.NpcName) != 1)
            return false;

        IGameObject? fallbackObject = null;
        AmongusNpcReplacement fallbackReplacement = null!;
        var matchingNameCount = 0;
        for (var objectIndex = 0; objectIndex < ObjectTable.Length; objectIndex++)
        {
            var obj = ObjectTable[objectIndex];
            if (obj == null || obj.Address == 0 || ImaginaryFrenService.IsManagedActor(obj.Address) || !IsAmongusObjectKind(obj.ObjectKind))
                continue;

            var candidateName = obj.Name.ToString();
            if (!string.Equals(candidateName, pendingEntry.NpcName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryGetAmongusReplacement(candidateName, out var candidateReplacement))
                continue;

            matchingNameCount++;
            if (matchingNameCount == 1)
            {
                fallbackObject = obj;
                fallbackReplacement = candidateReplacement;
            }
        }

        if (matchingNameCount != 1 || fallbackObject == null)
            return false;

        objectKey = GetAppearanceObjectKey(fallbackObject);
        npcName = fallbackObject.Name.ToString();
        character = (CharacterStruct*)fallbackObject.Address;
        replacement = fallbackReplacement;
        objectAddress = fallbackObject.Address;
        objectKind = fallbackObject.ObjectKind;
        gameObjectId = fallbackObject.GameObjectId;
        return !string.IsNullOrWhiteSpace(npcName);
    }

    private int CountPendingAmongusEntriesByName(string npcName)
    {
        var count = 0;
        foreach (var pendingEntry in pendingAmongusNpcEntries.Values)
        {
            if (string.Equals(pendingEntry.NpcName, npcName, StringComparison.OrdinalIgnoreCase))
                count++;
        }

        return count;
    }

    private static unsafe string GetDrawObjectTypeName(CharacterStruct* character)
    {
        var characterBase = character == null ? null : character->DrawObject;
        return characterBase == null
            ? "none"
            : characterBase->GetObjectType().ToString();
    }

    private static unsafe PendingAmongusNpcEntry CreatePendingAmongusNpcEntry(
        string npcName,
        ObjectKind objectKind,
        ulong gameObjectId,
        nint queuedAddress,
        CharacterStruct* character,
        bool preAppliedDuringCreate = false)
    {
        return character == null
            ? new PendingAmongusNpcEntry(npcName, objectKind, gameObjectId, queuedAddress, ExactNpcHumanizeAttempts, preAppliedDuringCreate)
            : new PendingAmongusNpcEntry(
                npcName,
                objectKind,
                gameObjectId,
                queuedAddress,
                ExactNpcHumanizeAttempts,
                preAppliedDuringCreate,
                character->ModelContainer.ModelCharaId,
                character->ModelContainer.ModelCharaId_2,
                character->ModelContainer.ModelSkeletonId,
                character->ModelContainer.ModelSkeletonId_2,
                GetDrawObjectTypeName(character));
    }

    private unsafe void UpsertPendingAmongusNpcEntry(
        ulong objectKey,
        string npcName,
        ObjectKind objectKind,
        ulong gameObjectId,
        nint queuedAddress,
        CharacterStruct* character,
        bool preAppliedDuringCreate)
    {
        pendingAmongusObjectKeys.Add(objectKey);
        var entry = CreatePendingAmongusNpcEntry(npcName, objectKind, gameObjectId, queuedAddress, character, preAppliedDuringCreate);
        if (!preAppliedDuringCreate || !pendingAmongusNpcEntries.TryGetValue(objectKey, out var existingEntry))
        {
            pendingAmongusNpcEntries[objectKey] = entry;
            return;
        }

        pendingAmongusNpcEntries[objectKey] = entry.WithPreAppliedDuringCreate().WithRemainingAttempts(existingEntry.RemainingAttempts);
    }

    private void LogAmongusCustomizeRefreshFalseIfNeeded(ulong objectKey, string npcName, string presetName)
    {
        if (loggedAmongusCustomizeRefreshFalseObjectKeys.Add(objectKey))
            Log.Information($"[Krangler] Amongus exact NPC '{npcName}' customize refresh false; recreate queued; preset='{presetName}'.");
    }

    private static string FormatPendingModelContainerIds(PendingAmongusNpcEntry pendingEntry)
        => $"modelCharaId={pendingEntry.LastModelCharaId}, modelCharaId2={pendingEntry.LastModelCharaId2}, modelSkeletonId={pendingEntry.LastModelSkeletonId}, modelSkeletonId2={pendingEntry.LastModelSkeletonId2}";

    private bool TryGetPendingAmongusNpcEntry(
        ulong objectKey,
        ulong gameObjectId,
        nint objectAddress,
        string npcName,
        out ulong pendingObjectKey,
        out PendingAmongusNpcEntry pendingEntry)
    {
        if (pendingAmongusNpcEntries.TryGetValue(objectKey, out pendingEntry))
        {
            pendingObjectKey = objectKey;
            return true;
        }

        foreach (var (candidateObjectKey, candidateEntry) in pendingAmongusNpcEntries)
        {
            if (gameObjectId != 0 && candidateEntry.GameObjectId == gameObjectId ||
                objectAddress != 0 && candidateEntry.QueuedAddress == objectAddress)
            {
                pendingObjectKey = candidateObjectKey;
                pendingEntry = candidateEntry;
                return true;
            }
        }

        if (gameObjectId == 0 && CountPendingAmongusEntriesByName(npcName) == 1)
        {
            foreach (var (candidateObjectKey, candidateEntry) in pendingAmongusNpcEntries)
            {
                if (!string.Equals(candidateEntry.NpcName, npcName, StringComparison.OrdinalIgnoreCase))
                    continue;

                pendingObjectKey = candidateObjectKey;
                pendingEntry = candidateEntry;
                return true;
            }
        }

        pendingObjectKey = 0;
        pendingEntry = default;
        return false;
    }

    private void DecrementPendingAmongusAttempt(ulong objectKey, PendingAmongusNpcEntry pendingEntry)
    {
        var remainingAttempts = pendingEntry.RemainingAttempts - 1;
        if (remainingAttempts > 0)
        {
            pendingAmongusNpcEntries[objectKey] = pendingEntry.WithRemainingAttempts(remainingAttempts);
            return;
        }

        RemovePendingAmongusNpcEntry(objectKey);
        if (loggedExhaustedPendingAmongusObjectKeys.Add(objectKey))
            Log.Warning($"[Krangler] Amongus exact NPC '{pendingEntry.NpcName}' humanize exhausted after {ExactNpcHumanizeAttempts} framework attempts: objectId={pendingEntry.GameObjectId}, lastAddress={FormatAddress(pendingEntry.QueuedAddress)}, {FormatPendingModelContainerIds(pendingEntry)}, drawType={pendingEntry.LastDrawObjectType}.");
    }

    private void RemovePendingAmongusNpcEntry(ulong objectKey, ulong? actualObjectKey = null)
    {
        pendingAmongusObjectKeys.Remove(objectKey);
        pendingAmongusNpcEntries.Remove(objectKey);

        if (actualObjectKey.HasValue && actualObjectKey.Value != objectKey)
        {
            pendingAmongusObjectKeys.Remove(actualObjectKey.Value);
            pendingAmongusNpcEntries.Remove(actualObjectKey.Value);
        }
    }

    private unsafe void MaintainAmongusNpcVisibility()
    {
        if (!ShouldMaintainAmongusNpcVisibility())
            return;

        for (var objectIndex = 0; objectIndex < ObjectTable.Length; objectIndex++)
        {
            var obj = ObjectTable[objectIndex];
            if (obj == null || obj.Address == 0 || ImaginaryFrenService.IsManagedActor(obj.Address) || !IsAmongusObjectKind(obj.ObjectKind))
                continue;

            var name = obj.Name.ToString();
            if (string.IsNullOrWhiteSpace(name) || !TryGetAmongusReplacement(name, out _))
                continue;

            var objectKey = GetAppearanceObjectKey(obj);
            SaveOriginalAppearanceIfNeeded(objectKey, (CharacterStruct*)obj.Address, obj.Address);
            ForceAmongusNpcVisible(objectKey, name, obj.ObjectKind, obj.GameObjectId, obj.Address);
        }
    }

    private bool TryGetAmongusReplacement(string npcName, out AmongusNpcReplacement replacement)
    {
        replacement = null!;
        if (!Configuration.AmongusEnabled ||
            Configuration.AmongusNpcReplacements == null ||
            string.IsNullOrWhiteSpace(npcName))
        {
            return false;
        }

        foreach (var candidate in Configuration.AmongusNpcReplacements)
        {
            if (candidate == null ||
                !candidate.Enabled ||
                string.IsNullOrWhiteSpace(candidate.NpcName) ||
                string.IsNullOrWhiteSpace(candidate.PresetKey))
            {
                continue;
            }

            if (!string.Equals(candidate.NpcName.Trim(), npcName.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            replacement = candidate;
            return true;
        }

        return false;
    }

    private GlamourerPreset? ResolveAmongusPreset(AmongusNpcReplacement replacement, string npcName)
    {
        var presetKey = replacement.PresetKey?.Trim() ?? string.Empty;
        var preset = GlamourerPresetService.GetPresetByName(presetKey);
        if (preset != null)
            return preset;

        var logKey = $"{replacement.NpcName}\u001F{presetKey}";
        if (loggedMissingAmongusPresetKeys.Add(logKey))
            Log.Warning($"[Krangler] Amongus preset '{presetKey}' for exact NPC '{npcName}' was not found in local presets.");

        return null;
    }

    private unsafe bool QueueAmongusNpcRecreation(ulong objectKey, string npcName, ObjectKind objectKind, ulong gameObjectId, nint address, CharacterStruct* character, AmongusNpcReplacement replacement)
    {
        SaveOriginalAppearanceIfNeeded(objectKey, character, address);
        ForceAmongusNpcVisible(objectKey, npcName, objectKind, gameObjectId, address);

        var preset = ResolveAmongusPreset(replacement, npcName);
        if (preset == null)
            return false;

        ApplyExactNpcPresetDrawData(character, preset);
        ForceExactNpcModelContainer(character, preset);

        var visibleRenderFlags = ReadExactNpcVisibleRenderFlags(address);

        var hadPendingEntry = pendingAmongusNpcEntries.TryGetValue(objectKey, out var pendingEntry);
        var queued = pendingAmongusObjectKeys.Add(objectKey);
        var nextPendingEntry = CreatePendingAmongusNpcEntry(
            npcName,
            objectKind,
            gameObjectId,
            address,
            character,
            pendingEntry.PreAppliedDuringCreate);
        if (hadPendingEntry)
            nextPendingEntry = nextPendingEntry.WithRemainingAttempts(pendingEntry.RemainingAttempts);

        pendingAmongusNpcEntries[objectKey] = nextPendingEntry;

        var redrawQueued = false;
        if (queued || !hadPendingEntry || pendingEntry.QueuedAddress != address)
            redrawQueued = QueueExactNpcRedraw(address, visibleRenderFlags);

        if (loggedPendingAmongusObjectKeys.Add(objectKey))
            Log.Information($"[Krangler] Amongus exact NPC '{npcName}' humanize queued; preset='{preset.Name}', redrawQueued={redrawQueued}, forcedModelIds={FormatModelContainerIds(character)}.");

        return queued || redrawQueued;
    }

    private unsafe void ApplyExactNpcPresetDrawData(CharacterStruct* character, GlamourerPreset preset)
    {
        if (character == null)
            return;

        ApplyCustomizeData(&character->DrawData.CustomizeData, preset);
        fixed (EquipmentModelId* equipmentModelPtr = &character->DrawData.EquipmentModelIds[0])
        {
            ApplyEquipmentData(equipmentModelPtr, preset, null, true, false);
        }

        ApplyGlamourerWeapons(character, preset, false);
        ApplyGlamourerBonusItems(character, preset, false);
        ApplyGlamourerMetaState(character, preset, false);
    }

    private static unsafe bool ForceExactNpcModelContainer(CharacterStruct* character, GlamourerPreset preset)
    {
        if (character == null)
            return false;

        var targetModelCharaId = preset.Customize.ModelId > 0 ? preset.Customize.ModelId : 0;
        var changed =
            character->ModelContainer.ModelCharaId != targetModelCharaId ||
            character->ModelContainer.ModelCharaId_2 != -1 ||
            character->ModelContainer.ModelSkeletonId != 0 ||
            character->ModelContainer.ModelSkeletonId_2 != 0;

        character->ModelContainer.ModelCharaId = targetModelCharaId;
        character->ModelContainer.ModelCharaId_2 = -1;
        character->ModelContainer.ModelSkeletonId = 0;
        character->ModelContainer.ModelSkeletonId_2 = 0;

        return changed;
    }

    private static uint GetExactNpcCreateModelId(GlamourerPreset preset)
        => preset.Customize.ModelId > 0 ? (uint)preset.Customize.ModelId : 0u;

    private static bool IsAmongusObjectKind(ObjectKind objectKind)
        => objectKind == ObjectKind.BattleNpc || objectKind == ObjectKind.EventNpc;

    private static ulong GetAppearanceObjectKey(IGameObject obj)
        => obj.GameObjectId != 0 ? obj.GameObjectId : unchecked((ulong)obj.Address.ToInt64());

    private void LogMissingAmongusObjects(HashSet<string> seenAmongusNpcNames)
    {
        if (!Configuration.AmongusEnabled || Configuration.AmongusNpcReplacements == null)
            return;

        foreach (var replacement in Configuration.AmongusNpcReplacements)
        {
            if (replacement == null ||
                !replacement.Enabled ||
                string.IsNullOrWhiteSpace(replacement.NpcName) ||
                string.IsNullOrWhiteSpace(replacement.PresetKey))
            {
                continue;
            }

            var npcName = replacement.NpcName.Trim();
            if (seenAmongusNpcNames.Contains(npcName))
                continue;

            if (loggedMissingAmongusObjectNames.Add(npcName))
                Log.Warning($"[Krangler] Amongus exact NPC '{npcName}' is not present in ObjectTable; visibility cannot be fixed without client-side spawning.");
        }
    }

    private unsafe void LogAmongusMatchIfNeeded(ulong objectKey, string npcName, ObjectKind objectKind, ulong gameObjectId, nint address)
    {
        if (!loggedAmongusMatchObjectKeys.Add(objectKey))
            return;

        var character = address == 0 ? null : (CharacterStruct*)address;
        var renderFlags = address == 0 ? 0 : ReadRenderFlags((GameObjectStruct*)address);
        var supportsHumanCustomize = character != null && SupportsHumanCustomize(character);
        Log.Information($"[Krangler] Amongus exact NPC match: name='{npcName}', kind={objectKind}, objectId={gameObjectId}, address={FormatAddress(address)}, renderFlags=0x{renderFlags:X8}, {FormatModelContainerIds(character)}, supportsHumanCustomize={supportsHumanCustomize}.");
    }

    private void LogAmongusPresetAppliedIfNeeded(ulong objectKey, string npcName, string presetName, string source)
    {
        if (loggedAppliedPendingAmongusObjectKeys.Add(objectKey))
            Log.Information($"[Krangler] Amongus exact NPC '{npcName}' preset applied; preset='{presetName}', source={source}.");
    }

    private unsafe uint? ForceAmongusNpcVisible(ulong objectKey, string npcName, ObjectKind objectKind, ulong gameObjectId, nint address)
    {
        amongusKeepVisibleObjectKeys.Add(objectKey);
        LogAmongusMatchIfNeeded(objectKey, npcName, objectKind, gameObjectId, address);

        if (address == 0)
            return null;

        var gameObj = (GameObjectStruct*)address;
        var beforeFlags = ReadRenderFlags(gameObj);
        var afterFlags = ClearHiddenExactNpcRenderFlags(beforeFlags);
        if (afterFlags != beforeFlags)
        {
            WriteRenderFlags(gameObj, afterFlags);

            if (loggedAmongusVisibilityObjectKeys.Add(objectKey))
                Log.Information($"[Krangler] Amongus exact NPC '{npcName}' visibility clear: renderFlags=0x{beforeFlags:X8}->0x{afterFlags:X8}.");
        }

        return afterFlags;
    }

    private static uint ClearHiddenExactNpcRenderFlags(uint renderFlags)
        => renderFlags & ~HiddenExactNpcRenderFlagsMask;

    private static string FormatAddress(nint address)
        => $"0x{address.ToInt64():X}";

    private static unsafe string FormatModelContainerIds(CharacterStruct* character)
        => character == null
            ? "modelCharaId=<null>, modelCharaId2=<null>, modelSkeletonId=<null>, modelSkeletonId2=<null>"
            : $"modelCharaId={character->ModelContainer.ModelCharaId}, modelCharaId2={character->ModelContainer.ModelCharaId_2}, modelSkeletonId={character->ModelContainer.ModelSkeletonId}, modelSkeletonId2={character->ModelContainer.ModelSkeletonId_2}";

    private void OnChatMessage(IHandleableChatMessage chatMessage)
    {
        if (!Configuration.Enabled || !Configuration.KrangleChat)
            return;

        try
        {
            var messageText = chatMessage.Message.TextValue;
            var senderText = chatMessage.Sender.TextValue;
            var garbledMessage = GenerateGarbledText(messageText.Length);
            var garbledSender = ShouldSkipSelfKrangling(senderText)
                ? GetResolvedSelfDisplayName(senderText)
                : GenerateGarbledText(senderText.Length);

            chatMessage.Message = new SeString(new List<Payload> { new TextPayload(garbledMessage) });
            chatMessage.Sender = new SeString(new List<Payload> { new TextPayload(garbledSender) });
        }
        catch (Exception ex)
        {
            Log.Error($"[Krangler] Error in chat message processing: {ex.Message}");
        }
    }

    private static string GenerateGarbledText(int length)
    {
        if (length <= 0) return string.Empty;

        var random = new Random();
        var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_+-=[]{}|;:,.<>?";
        var result = new char[length];

        for (int i = 0; i < length; i++)
        {
            result[i] = chars[random.Next(chars.Length)];
        }

        return new string(result);
    }

    private unsafe void ScanAndKrangleAppearances()
    {
        var playerCount = 0;
        var appliedCount = 0;
        var maxPlayersPerCycle = Math.Max(1, Configuration.SuperKrangleMaxPlayersPerCycle);
        var maxAuxiliaryTargetsPerCycle = Math.Max(16, maxPlayersPerCycle * 4);
        var processedPlayersThisCycle = 0;
        var processedAuxiliaryTargetsThisCycle = 0;
        var seenAmongusNpcNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var captureSoulThiefThisScan = ShouldRunSoulThiefCaptureThisScan();
        var soulThiefCapturedPlayers = 0;
        var soulThiefCapturedNpcs = 0;
        var soulThiefCapturedChocobos = 0;

        // Event activation notification
        if (IsSuperKrangleEventActive && !Configuration.SuperKrangleMaster4000 && !hasLoggedEventActivation)
        {
            Log.Information("[Krangler] EVENT ACTIVATED: Super Krangle Master 4000 auto-enabled for Wuk Lamat event (March 31 - April 2)");
            hasLoggedEventActivation = true;
        }

        for (var objectIndex = 0; objectIndex < ObjectTable.Length; objectIndex++)
        {
            var obj = ObjectTable[objectIndex];
            if (obj == null)
                continue;

            if (obj.Address != 0 && ImaginaryFrenService.IsManagedActor(obj.Address))
                continue;

            var name = obj.Name.ToString();
            if (string.IsNullOrEmpty(name))
                continue;

            var objectKey = GetAppearanceObjectKey(obj);
            var isPlayer = obj.ObjectKind == ObjectKind.Pc;
            var isChocobo = obj.ObjectKind == ObjectKind.BattleNpc && obj.Name.ToString().Contains("Companion", StringComparison.OrdinalIgnoreCase);
            var isMinion = obj.ObjectKind == ObjectKind.Companion;
            var isNpc = IsAppearanceNpc(obj.ObjectKind, isChocobo, isMinion);
            AmongusNpcReplacement amongusReplacement = null!;
            var isAmongusNpc = isNpc && TryGetAmongusReplacement(name, out amongusReplacement);
            var targetLabel = GetAppearanceTargetLabel(isNpc, isChocobo, isMinion);

            if (captureSoulThiefThisScan && !IsSoulThiefSourceAlreadyKrangled(objectKey))
            {
                if (TryCaptureSoulThiefPreset(obj, name, isPlayer, isNpc, isChocobo, out var soulThiefTargetKind, out var soulThiefError))
                {
                    if (soulThiefTargetKind == SoulThiefTargetKind.Player)
                        soulThiefCapturedPlayers++;
                    else if (soulThiefTargetKind == SoulThiefTargetKind.Npc)
                        soulThiefCapturedNpcs++;
                    else if (soulThiefTargetKind == SoulThiefTargetKind.Chocobo)
                        soulThiefCapturedChocobos++;
                }
                else if (!string.IsNullOrWhiteSpace(soulThiefError))
                {
                    LogSoulThiefSkipIfNeeded(obj, name, soulThiefError);
                }
            }

            if (isAmongusNpc)
            {
                seenAmongusNpcNames.Add(amongusReplacement.NpcName.Trim());
                if (obj.Address != 0)
                    SaveOriginalAppearanceIfNeeded(objectKey, (CharacterStruct*)obj.Address, obj.Address);

                ForceAmongusNpcVisible(objectKey, name, obj.ObjectKind, obj.GameObjectId, obj.Address);
            }

            if (AppearanceService.IsApplied(objectKey))
                continue;

            if (!isPlayer && !isNpc && !isChocobo && !isMinion)
                continue;

            if (isPlayer)
            {
                playerCount++;
                if (IsLocalPlayerObject(objectKey, obj.Address))
                    continue;

                if (processedPlayersThisCycle >= maxPlayersPerCycle)
                    continue;
            }
            else if (isNpc || isChocobo || isMinion)
            {
                if (processedAuxiliaryTargetsThisCycle >= maxAuxiliaryTargetsPerCycle)
                    continue;
            }

            if (!ShouldProcessAppearanceTarget(isPlayer, isNpc, isChocobo, isMinion) && !isAmongusNpc)
                continue;

            try
            {
                var character = (CharacterStruct*)obj.Address;
                if (character == null)
                    continue;

                if (isAmongusNpc)
                {
                    if (QueueAmongusNpcRecreation(objectKey, name, obj.ObjectKind, obj.GameObjectId, obj.Address, character, amongusReplacement))
                        processedAuxiliaryTargetsThisCycle++;

                    continue;
                }

                if (!SupportsHumanCustomize(character))
                {
                    if (!hasLoggedAppearanceScan)
                        Log.Warning($"[Krangler] Skipping unsupported {targetLabel} appearance target '{name}' - local appearance krangling currently requires a human CharacterBase draw object.");
                    continue;
                }

                var customizePtr = (byte*)&character->DrawData.CustomizeData;

                var race = customizePtr[0];
                var tribe = customizePtr[4];
                var gender = customizePtr[1];
                if (!isAmongusNpc)
                {
                    (race, tribe, gender) = SuperKrangleMaster4000_Active
                        ? GetSuperKrangleAppearance(name)
                        : AppearanceService.GetRandomRaceGender(name);
                }
                bool changed = false;

                if (SuperKrangleMaster4000_Active)
                {
                    var selection = ResolveSuperKrangleSelection(name, isNpc, isChocobo, isMinion);
                    var preset = GlamourerPresetService.ResolvePresetSelection(name, selection);
                    if (preset != null)
                    {
                        SaveOriginalAppearanceIfNeeded(objectKey, character);
                        changed = ApplySuperKranglePreset(character, preset, true);

                        if (changed)
                        {
                            race = customizePtr[0];
                            tribe = customizePtr[4];
                            gender = customizePtr[1];
                        }

                        if (!hasLoggedAppearanceScan && changed)
                            Log.Information($"[Krangler] Applied Super Krangle preset '{preset.Name}' to '{name}' ({targetLabel}) via local path");
                    }
                    else if (Configuration.SuperKrangleApplyAppearance)
                    {
                        SaveOriginalAppearanceIfNeeded(objectKey, character);
                        var superAppearance = GetSuperKrangleFullAppearance(name);
                        foreach (var (index, value) in superAppearance)
                        {
                            if (index < CustomizeByteCount)
                                customizePtr[index] = value;
                        }

                        var refreshedAppearance = RefreshCharacterCustomize(character);

                        race = customizePtr[0];
                        tribe = customizePtr[4];
                        gender = customizePtr[1];
                        changed = refreshedAppearance;

                        if (!hasLoggedAppearanceScan)
                            Log.Information($"[Krangler] Native customize refresh for fallback Super Krangle appearance returned {refreshedAppearance}");
                    }
                }
                else
                {
                    var shouldApplyRace = isPlayer ? Configuration.KrangleRaces : true;
                    var shouldApplyGender = isPlayer ? Configuration.KrangleGenders : true;
                    var shouldApplyAppearance = isPlayer ? Configuration.KrangleAppearance : true;

                    SaveOriginalAppearanceIfNeeded(objectKey, character);

                    if (shouldApplyRace)
                    {
                        customizePtr[0] = race;
                        customizePtr[4] = tribe;
                        changed = true;
                    }

                    if (shouldApplyGender)
                    {
                        customizePtr[1] = gender;
                        changed = true;
                    }

                    if (shouldApplyAppearance)
                    {
                        var appearance = AppearanceService.GetRandomAppearance(name, race, gender);
                        foreach (var (index, value) in appearance)
                        {
                            if (index < CustomizeByteCount)
                                customizePtr[index] = value;
                        }

                        changed = true;
                    }

                    if (changed)
                    {
                        var refreshedAppearance = RefreshCharacterCustomize(character);
                        if (!refreshedAppearance && !hasLoggedAppearanceScan)
                            Log.Warning($"[Krangler] Native customize refresh reported false for regular krangle target '{name}', continuing with redraw.");
                    }
                }

                if (changed)
                {
                    if (isAmongusNpc)
                        QueuePenumbraStyleRedraw(obj.Address, ReadExactNpcVisibleRenderFlags(obj.Address));
                    else
                        QueuePenumbraStyleRedraw(obj.Address);

                    AppearanceService.MarkApplied(objectKey);
                    appliedCount++;
                    if (isPlayer)
                        processedPlayersThisCycle++;
                    else
                        processedAuxiliaryTargetsThisCycle++;

                    if (!hasLoggedAppearanceScan)
                        Log.Information($"[Krangler] Applied appearance to '{name}' ({targetLabel}): race={race}, tribe={tribe}, gender={gender}");
                }
            }
            catch (Exception ex)
            {
                if (!hasLoggedAppearanceScan)
                    Log.Warning($"[Krangler] Failed to modify appearance for '{name}': {ex.Message}");
            }
        }

        LogMissingAmongusObjects(seenAmongusNpcNames);
        UpdateSoulThiefCaptureCounts(captureSoulThiefThisScan, soulThiefCapturedPlayers, soulThiefCapturedNpcs, soulThiefCapturedChocobos);

        if (!hasLoggedAppearanceScan && playerCount > 0)
        {
            currentVisiblePlayerCount = playerCount;
            Log.Information($"[Krangler] Appearance scan: {playerCount} visible players, {appliedCount} modified, players {processedPlayersThisCycle}/{maxPlayersPerCycle}, auxiliary {processedAuxiliaryTargetsThisCycle}/{maxAuxiliaryTargetsPerCycle} processed this cycle");
            hasLoggedAppearanceScan = true;
        }
        else
        {
            currentVisiblePlayerCount = playerCount;
        }
    }

    private enum SoulThiefTargetKind
    {
        None,
        Player,
        Npc,
        Chocobo,
    }

    private bool HasActiveSoulThiefTargets()
        => Configuration.SoulThiefEnabled &&
           (Configuration.SoulThiefCapturePlayers ||
            Configuration.SoulThiefCaptureNpcs ||
            Configuration.SoulThiefCaptureChocobos);

    private bool ShouldRunSoulThiefCaptureThisScan()
    {
        if (!HasActiveSoulThiefTargets())
            return false;

        var now = DateTime.UtcNow;
        if ((now - lastSoulThiefCapture).TotalSeconds < GetSoulThiefCaptureIntervalSeconds())
            return false;

        lastSoulThiefCapture = now;
        return true;
    }

    private int GetSoulThiefCaptureIntervalSeconds()
        => Math.Clamp(
            Configuration.SoulThiefCaptureIntervalSeconds,
            Configuration.MinSoulThiefCaptureIntervalSeconds,
            Configuration.MaxSoulThiefCaptureIntervalSeconds);

    private bool IsSoulThiefSourceAlreadyKrangled(ulong objectKey)
        => AppearanceService.IsApplied(objectKey) || originalAppearanceData.ContainsKey(objectKey);

    private unsafe bool TryCaptureSoulThiefPreset(
        IGameObject obj,
        string name,
        bool isPlayer,
        bool isNpc,
        bool isChocobo,
        out SoulThiefTargetKind targetKind,
        out string error)
    {
        targetKind = SoulThiefTargetKind.None;
        error = string.Empty;

        try
        {
            if (!TryResolveSoulThiefTargetKind(isPlayer, isNpc, isChocobo, out targetKind))
                return false;

            if (obj.Address == 0)
            {
                error = "object address was zero";
                return false;
            }

            var character = (CharacterStruct*)obj.Address;
            if (!TryValidateSoulThiefExportCandidate(obj, character, out error))
                return false;

            var presetDisplayName = GetSoulThiefPresetDisplayName(obj, name, targetKind);
            var preset = CreateSoulThiefPreset(presetDisplayName, targetKind, character);
            var category = GetSoulThiefCategory(targetKind);
            var fileName = GetSoulThiefFileName(obj, name, targetKind);

            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(fileName))
            {
                error = $"category or file name was empty for target kind {targetKind}";
                return false;
            }

            var exported = GlamourerPresetService.TryExportSoulThiefPreset(
                category,
                fileName,
                preset,
                out var skippedExisting,
                out _,
                out error);

            if (skippedExisting)
            {
                error = string.Empty;
                return false;
            }

            if (!exported && string.IsNullOrWhiteSpace(error))
                error = "preset export returned false";

            return exported;
        }
        catch (Exception ex)
        {
            error = $"{name}: {ex.Message}";
            return false;
        }
    }

    private void LogSoulThiefSkipIfNeeded(IGameObject obj, string name, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return;

        var displayName = string.IsNullOrWhiteSpace(name) ? "<unnamed>" : name;
        var addressText = obj.Address == 0 ? "0x0" : $"0x{obj.Address.ToInt64():X}";
        var logKey = $"{obj.GameObjectId:X16}:{addressText}:{reason}";
        if (!loggedSoulThiefSkipReasons.Add(logKey))
            return;

        Log.Warning(
            $"[Krangler] Soul Thief skipped '{displayName}' " +
            $"({obj.ObjectKind}, objectId=0x{obj.GameObjectId:X}, address={addressText}): {reason}");
    }

    private unsafe bool TryValidateSoulThiefExportCandidate(
        IGameObject obj,
        CharacterStruct* character,
        out string reason)
    {
        reason = string.Empty;

        if (character == null)
        {
            reason = "character pointer was null";
            return false;
        }

        if (obj.ObjectKind is not ObjectKind.Pc and not ObjectKind.BattleNpc and not ObjectKind.EventNpc)
        {
            reason = $"object kind {obj.ObjectKind} is not a character actor kind";
            return false;
        }

        if (!HasUsableSoulThiefAppearanceData(character, out reason))
            return false;

        return true;
    }

    private unsafe bool HasUsableSoulThiefAppearanceData(CharacterStruct* character, out string reason)
    {
        reason = string.Empty;

        if (character == null)
        {
            reason = "character pointer was null";
            return false;
        }

        var modelCharaId = character->ModelContainer.ModelCharaId;
        var customize = character->DrawData.CustomizeData;
        var hasModelIdentity = modelCharaId > 0;
        var hasCustomizeIdentity =
            customize.Race > 0 ||
            customize.Tribe > 0 ||
            customize.Height > 0 ||
            customize.Face > 0 ||
            customize.Hairstyle > 0;

        var hasEquipment = false;
        fixed (EquipmentModelId* equipmentModelPtr = &character->DrawData.EquipmentModelIds[0])
        {
            for (var i = 0; i < EquipmentSlotCount; i++)
            {
                var slot = equipmentModelPtr + i;
                if (slot->Id != 0 || slot->Variant != 0 || slot->Stain0 != 0 || slot->Stain1 != 0)
                {
                    hasEquipment = true;
                    break;
                }
            }
        }

        var mainHand = character->DrawData.Weapon(DrawDataContainerStruct.WeaponSlot.MainHand).ModelId;
        var offHand = character->DrawData.Weapon(DrawDataContainerStruct.WeaponSlot.OffHand).ModelId;
        var hasWeapon =
            mainHand.Id != 0 ||
            mainHand.Type != 0 ||
            mainHand.Variant != 0 ||
            offHand.Id != 0 ||
            offHand.Type != 0 ||
            offHand.Variant != 0;
        var hasBonus = character->DrawData.GlassesIds[0] != 0 || character->DrawData.GlassesIds[1] != 0;

        if (hasModelIdentity || hasCustomizeIdentity || hasEquipment || hasWeapon || hasBonus)
            return true;

        reason = "no usable model, customize, equipment, weapon, or bonus appearance data was available";
        return false;
    }

    private bool TryResolveSoulThiefTargetKind(bool isPlayer, bool isNpc, bool isChocobo, out SoulThiefTargetKind targetKind)
    {
        targetKind = SoulThiefTargetKind.None;

        if (isPlayer && Configuration.SoulThiefCapturePlayers)
            targetKind = SoulThiefTargetKind.Player;
        else if (isChocobo && Configuration.SoulThiefCaptureChocobos)
            targetKind = SoulThiefTargetKind.Chocobo;
        else if (isNpc && Configuration.SoulThiefCaptureNpcs)
            targetKind = SoulThiefTargetKind.Npc;

        return targetKind != SoulThiefTargetKind.None;
    }

    private unsafe GlamourerPreset CreateSoulThiefPreset(string name, SoulThiefTargetKind targetKind, CharacterStruct* character)
    {
        var presetName = $"Soul Thief: {GetSoulThiefDisplayKind(targetKind)} {name}";
        var preset = new GlamourerPreset
        {
            FileVersion = 2,
            Identifier = Guid.NewGuid().ToString(),
            Name = presetName,
            Description = $"Captured by Krangler Soul Thief from {GetSoulThiefDisplayKind(targetKind).ToLowerInvariant()} '{name}'.",
            ForcedRedraw = false,
            Customize = CreateSoulThiefCustomizeData(character, GetSoulThiefCaptureModelId(character)),
        };

        fixed (EquipmentModelId* equipmentModelPtr = &character->DrawData.EquipmentModelIds[0])
        {
            AddSoulThiefEquipmentSlot(preset, "Head", equipmentModelPtr + 0);
            AddSoulThiefEquipmentSlot(preset, "Body", equipmentModelPtr + 1);
            AddSoulThiefEquipmentSlot(preset, "Hands", equipmentModelPtr + 2);
            AddSoulThiefEquipmentSlot(preset, "Legs", equipmentModelPtr + 3);
            AddSoulThiefEquipmentSlot(preset, "Feet", equipmentModelPtr + 4);
            AddSoulThiefEquipmentSlot(preset, "Ears", equipmentModelPtr + 5);
            AddSoulThiefEquipmentSlot(preset, "Neck", equipmentModelPtr + 6);
            AddSoulThiefEquipmentSlot(preset, "Wrists", equipmentModelPtr + 7);
            AddSoulThiefEquipmentSlot(preset, "RFinger", equipmentModelPtr + 8);
            AddSoulThiefEquipmentSlot(preset, "LFinger", equipmentModelPtr + 9);
        }

        AddSoulThiefWeaponSlot(
            preset,
            "MainHand",
            character->DrawData.Weapon(DrawDataContainerStruct.WeaponSlot.MainHand).ModelId);
        AddSoulThiefWeaponSlot(
            preset,
            "OffHand",
            character->DrawData.Weapon(DrawDataContainerStruct.WeaponSlot.OffHand).ModelId);

        preset.Bonus["Glasses"] = new BonusItemData
        {
            BonusId = character->DrawData.GlassesIds[0],
            Apply = true,
        };
        preset.Bonus["Glasses1"] = new BonusItemData
        {
            BonusId = character->DrawData.GlassesIds[1],
            Apply = true,
        };

        preset.Equipment["Hat"] = new EquipmentSlotData
        {
            Apply = true,
            Show = !character->DrawData.IsHatHidden,
        };
        preset.Equipment["Weapon"] = new EquipmentSlotData
        {
            Apply = true,
            Show = !character->DrawData.IsWeaponHidden,
        };
        preset.Equipment["Visor"] = new EquipmentSlotData
        {
            Apply = true,
            IsToggled = character->DrawData.IsVisorToggled,
        };

        return preset;
    }

    private unsafe int GetSoulThiefCaptureModelId(CharacterStruct* character)
    {
        if (character == null || SupportsHumanCustomize(character))
            return 0;

        var modelCharaId = character->ModelContainer.ModelCharaId;
        return modelCharaId > 0 ? modelCharaId : 0;
    }

    private static unsafe Krangler.Models.CustomizeData CreateSoulThiefCustomizeData(CharacterStruct* character, int modelId)
    {
        var customize = character->DrawData.CustomizeData;
        return new Krangler.Models.CustomizeData
        {
            ModelId = modelId,
            Race = CreateAppliedCustomValue(customize.Race),
            Gender = CreateAppliedCustomValue(customize.Sex),
            BodyType = CreateAppliedCustomValue(customize.BodyType),
            Height = CreateAppliedCustomValue(customize.Height),
            Clan = CreateAppliedCustomValue(customize.Tribe),
            Face = CreateAppliedCustomValue(customize.Face),
            Hairstyle = CreateAppliedCustomValue(customize.Hairstyle),
            Highlights = CreateAppliedCustomValue(customize.Highlights ? (byte)1 : (byte)0),
            SkinColor = CreateAppliedCustomValue(customize.SkinColor),
            EyeColorRight = CreateAppliedCustomValue(customize.EyeColorRight),
            HairColor = CreateAppliedCustomValue(customize.HairColor),
            HighlightsColor = CreateAppliedCustomValue(customize.HighlightsColor),
            FacialFeature1 = CreateAppliedCustomValue(customize.FacialFeature1 ? (byte)1 : (byte)0),
            FacialFeature2 = CreateAppliedCustomValue(customize.FacialFeature2 ? (byte)1 : (byte)0),
            FacialFeature3 = CreateAppliedCustomValue(customize.FacialFeature3 ? (byte)1 : (byte)0),
            FacialFeature4 = CreateAppliedCustomValue(customize.FacialFeature4 ? (byte)1 : (byte)0),
            FacialFeature5 = CreateAppliedCustomValue(customize.FacialFeature5 ? (byte)1 : (byte)0),
            FacialFeature6 = CreateAppliedCustomValue(customize.FacialFeature6 ? (byte)1 : (byte)0),
            FacialFeature7 = CreateAppliedCustomValue(customize.FacialFeature7 ? (byte)1 : (byte)0),
            LegacyTattoo = CreateAppliedCustomValue(customize.LegacyTattoo ? (byte)1 : (byte)0),
            TattooColor = CreateAppliedCustomValue(customize.TattooColor),
            Eyebrows = CreateAppliedCustomValue(customize.Eyebrows),
            EyeColorLeft = CreateAppliedCustomValue(customize.EyeColorLeft),
            EyeShape = CreateAppliedCustomValue(customize.EyeShape),
            SmallIris = CreateAppliedCustomValue(customize.SmallIris ? (byte)1 : (byte)0),
            Nose = CreateAppliedCustomValue(customize.Nose),
            Jaw = CreateAppliedCustomValue(customize.Jaw),
            Mouth = CreateAppliedCustomValue(customize.Mouth),
            Lipstick = CreateAppliedCustomValue(customize.Lipstick ? (byte)1 : (byte)0),
            LipColor = CreateAppliedCustomValue(customize.LipColorFurPattern),
            MuscleMass = CreateAppliedCustomValue(customize.MuscleMass),
            TailShape = CreateAppliedCustomValue(customize.TailShape),
            BustSize = CreateAppliedCustomValue(customize.BustSize),
            FacePaint = CreateAppliedCustomValue(customize.FacePaint),
            FacePaintReversed = CreateAppliedCustomValue(customize.FacePaintReversed ? (byte)1 : (byte)0),
            FacePaintColor = CreateAppliedCustomValue(customize.FacePaintColor),
        };
    }

    private static CustomValue CreateAppliedCustomValue(byte value)
        => new()
        {
            Value = value,
            Apply = true,
        };

    private static unsafe void AddSoulThiefEquipmentSlot(GlamourerPreset preset, string slotName, EquipmentModelId* modelId)
    {
        preset.Equipment[slotName] = new EquipmentSlotData
        {
            ItemId = PackSoulThiefArmorItemId(modelId),
            Stain = modelId == null ? 0u : modelId->Stain0,
            Stain2 = modelId == null ? 0u : modelId->Stain1,
            Apply = true,
            ApplyStain = true,
            Crest = false,
            ApplyCrest = false,
            Show = true,
        };
    }

    private static void AddSoulThiefWeaponSlot(GlamourerPreset preset, string slotName, WeaponModelId modelId)
    {
        preset.Equipment[slotName] = new EquipmentSlotData
        {
            ItemId = PackSoulThiefWeaponItemId(modelId),
            Stain = modelId.Stain0,
            Stain2 = modelId.Stain1,
            Apply = true,
            ApplyStain = true,
            Crest = false,
            ApplyCrest = false,
            Show = true,
        };
    }

    private static unsafe ulong PackSoulThiefArmorItemId(EquipmentModelId* modelId)
    {
        if (modelId == null || modelId->Id == 0 && modelId->Variant == 0)
            return uint.MaxValue;

        return ((ulong)modelId->Variant << 48) | ((ulong)modelId->Id << 32);
    }

    private static ulong PackSoulThiefWeaponItemId(WeaponModelId modelId)
    {
        if (modelId.Id == 0 && modelId.Type == 0 && modelId.Variant == 0)
            return uint.MaxValue;

        return (1UL << 48) |
               ((ulong)modelId.Variant << 32) |
               ((ulong)modelId.Type << 16) |
               modelId.Id;
    }

    private static string GetSoulThiefCategory(SoulThiefTargetKind targetKind)
        => targetKind switch
        {
            SoulThiefTargetKind.Player => "players",
            SoulThiefTargetKind.Npc => "npcs",
            SoulThiefTargetKind.Chocobo => "chocobos",
            _ => string.Empty,
        };

    private static string GetSoulThiefDisplayKind(SoulThiefTargetKind targetKind)
        => targetKind switch
        {
            SoulThiefTargetKind.Player => "Player",
            SoulThiefTargetKind.Npc => "NPC",
            SoulThiefTargetKind.Chocobo => "Chocobo",
            _ => "Unknown",
        };

    private string GetSoulThiefFileName(IGameObject obj, string name, SoulThiefTargetKind targetKind)
        => targetKind switch
        {
            SoulThiefTargetKind.Player => BuildSoulThiefPlayerFileName(obj, name),
            SoulThiefTargetKind.Npc => $"npc_{SanitizeSoulThiefFileSegment(name)}.json",
            SoulThiefTargetKind.Chocobo => $"chocobo_{SanitizeSoulThiefFileSegment(name)}.json",
            _ => string.Empty,
        };

    private string GetSoulThiefPresetDisplayName(IGameObject obj, string name, SoulThiefTargetKind targetKind)
    {
        if (targetKind != SoulThiefTargetKind.Player || name.Contains('@'))
            return name;

        var worldName = GetPlayerWorldName(obj);
        return string.IsNullOrWhiteSpace(worldName) ? name : $"{name}@{worldName}";
    }

    private string BuildSoulThiefPlayerFileName(IGameObject obj, string name)
    {
        var serverName = GetPlayerWorldName(obj);
        var characterName = name;
        var atIndex = name.IndexOf('@');
        if (atIndex >= 0)
        {
            characterName = name[..atIndex];
            if (string.IsNullOrWhiteSpace(serverName) && atIndex + 1 < name.Length)
                serverName = name[(atIndex + 1)..];
        }

        var nameParts = characterName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : "unknown";
        var lastName = nameParts.Length > 1 ? nameParts[1] : "unknown";
        if (string.IsNullOrWhiteSpace(serverName))
            serverName = "unknown";

        return $"player_{SanitizeSoulThiefFileSegment(firstName)}_{SanitizeSoulThiefFileSegment(lastName)}_{SanitizeSoulThiefFileSegment(serverName)}.json";
    }

    private static string GetPlayerWorldName(IGameObject obj)
    {
        foreach (var propertyName in new[] { "HomeWorld", "CurrentWorld" })
        {
            try
            {
                var property = obj.GetType().GetProperty(propertyName);
                if (property == null || property.GetIndexParameters().Length != 0)
                    continue;

                var worldName = ExtractReflectedName(property.GetValue(obj));
                if (!string.IsNullOrWhiteSpace(worldName))
                    return worldName;
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private static string ExtractReflectedName(object? value, int depth = 0)
    {
        if (value == null || depth > 4)
            return string.Empty;

        if (value is string stringValue)
            return stringValue;

        foreach (var propertyName in new[] { "Value", "ValueNullable" })
        {
            var property = value.GetType().GetProperty(propertyName);
            if (property == null || property.GetIndexParameters().Length != 0)
                continue;

            try
            {
                var nestedName = ExtractReflectedName(property.GetValue(value), depth + 1);
                if (!string.IsNullOrWhiteSpace(nestedName))
                    return nestedName;
            }
            catch
            {
            }
        }

        foreach (var propertyName in new[] { "Name", "InternalName" })
        {
            var property = value.GetType().GetProperty(propertyName);
            if (property == null || property.GetIndexParameters().Length != 0)
                continue;

            try
            {
                var propertyValue = property.GetValue(value);
                var name = propertyValue?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private static string SanitizeSoulThiefFileSegment(string value)
    {
        var builder = new StringBuilder();
        var previousUnderscore = false;

        foreach (var rawChar in value.Trim().ToLowerInvariant())
        {
            var ch = rawChar;
            var isAsciiLetter = ch is >= 'a' and <= 'z';
            var isDigit = ch is >= '0' and <= '9';

            if (isAsciiLetter || isDigit)
            {
                builder.Append(ch);
                previousUnderscore = false;
            }
            else if (!previousUnderscore && builder.Length > 0)
            {
                builder.Append('_');
                previousUnderscore = true;
            }
        }

        var result = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }

    private void UpdateSoulThiefCaptureCounts(bool captureAttempted, int players, int npcs, int chocobos)
    {
        if (!captureAttempted)
            return;

        var changed = Configuration.SoulThiefLastCapturedPlayers != players ||
                      Configuration.SoulThiefLastCapturedNpcs != npcs ||
                      Configuration.SoulThiefLastCapturedChocobos != chocobos;

        if (changed)
        {
            Configuration.SoulThiefLastCapturedPlayers = players;
            Configuration.SoulThiefLastCapturedNpcs = npcs;
            Configuration.SoulThiefLastCapturedChocobos = chocobos;
            Configuration.Save();
        }

        var total = players + npcs + chocobos;
        if (total > 0)
            Log.Information($"[Krangler] Soul Thief captured {total} preset(s): players={players}, npcs={npcs}, chocobos={chocobos}");
    }

    private unsafe void ProcessRedrawQueue()
    {
        if (redrawCooldownFrames > 0)
        {
            redrawCooldownFrames--;
            return;
        }

        if (redrawQueue.Count == 0) return;

        var pendingRedraw = redrawQueue.Dequeue();
        if (ImaginaryFrenService.IsManagedActor(pendingRedraw.Address))
        {
            pendingRedrawAddresses.Remove(pendingRedraw.Address);
            return;
        }

        try
        {
            var gameObj = (GameObjectStruct*)pendingRedraw.Address;
            if (gameObj == null)
            {
                pendingRedrawAddresses.Remove(pendingRedraw.Address);
                return;
            }

            switch (pendingRedraw.Kind)
            {
                case PendingRedrawKind.InvisibleThenVisible:
                    WriteActorInvisible(gameObj);
                    redrawQueue.Enqueue(new PendingRedrawEntry(pendingRedraw.Address, PendingRedrawKind.VisibleOnly, pendingRedraw.RenderFlags));
                    break;
                case PendingRedrawKind.VisibleOnly:
                    if (pendingRedraw.RenderFlags.HasValue)
                        WriteRenderFlags(gameObj, pendingRedraw.RenderFlags.Value);
                    else
                        WriteActorVisible(gameObj);
                    pendingRedrawAddresses.Remove(pendingRedraw.Address);
                    break;
                case PendingRedrawKind.RestoreRenderFlags:
                    if (pendingRedraw.RenderFlags.HasValue)
                        WriteRenderFlags(gameObj, pendingRedraw.RenderFlags.Value);
                    else
                        WriteActorVisible(gameObj);

                    pendingRedrawAddresses.Remove(pendingRedraw.Address);
                    break;
                case PendingRedrawKind.ExactDisableDraw:
                    var disableCharacter = (CharacterStruct*)pendingRedraw.Address;
                    disableCharacter->DisableDraw();
                    redrawQueue.Enqueue(new PendingRedrawEntry(pendingRedraw.Address, PendingRedrawKind.ExactEnableDraw, pendingRedraw.RenderFlags));
                    break;
                case PendingRedrawKind.ExactEnableDraw:
                    var enableCharacter = (CharacterStruct*)pendingRedraw.Address;
                    enableCharacter->EnableDraw();

                    if (pendingRedraw.RenderFlags.HasValue)
                        WriteRenderFlags(gameObj, pendingRedraw.RenderFlags.Value);

                    pendingRedrawAddresses.Remove(pendingRedraw.Address);
                    break;
            }
        }
        catch (Exception ex)
        {
            pendingRedrawAddresses.Remove(pendingRedraw.Address);
            Log.Warning($"[Krangler] Local redraw failed: {ex.Message}");
        }

        redrawCooldownFrames = CalculateRedrawDelayFrames();
    }

    private int CalculateRedrawDelayFrames()
    {
        var baseDelay = Math.Max(1, Configuration.SuperKrangleBaseRedrawDelayFrames);
        var scaledDelay = baseDelay + Math.Min(18, (currentVisiblePlayerCount / 10) * 2);
        return Math.Clamp(scaledDelay, 1, 20);
    }

    private bool QueuePenumbraStyleRedraw(nint address, uint? finalVisibleRenderFlags = null)
        => QueueRedraw(address, PendingRedrawKind.InvisibleThenVisible, finalVisibleRenderFlags);

    private bool QueueExactNpcRedraw(nint address, uint? finalVisibleRenderFlags = null)
        => QueueRedraw(address, PendingRedrawKind.ExactDisableDraw, finalVisibleRenderFlags);

    private bool QueueVisibleOnlyRedraw(nint address)
        => QueueRedraw(address, PendingRedrawKind.VisibleOnly);

    private void QueueRenderFlagRestore(nint address, uint renderFlags)
        => QueueRedraw(address, PendingRedrawKind.RestoreRenderFlags, renderFlags);

    private bool QueueRedraw(nint address, PendingRedrawKind kind, uint? renderFlags = null)
    {
        if (address == 0 || ImaginaryFrenService.IsManagedActor(address) || !pendingRedrawAddresses.Add(address))
            return false;

        redrawQueue.Enqueue(new PendingRedrawEntry(address, kind, renderFlags));
        return true;
    }

    private void ClearPendingRedraws()
    {
        redrawQueue.Clear();
        pendingRedrawAddresses.Clear();
        redrawCooldownFrames = 0;
    }

    private void ClearPendingCreatedCharacterBaseReapplies()
    {
        pendingCreatedCharacterBaseQueue.Clear();
        pendingCreatedCharacterBaseAddresses.Clear();
        pendingAmongusObjectKeys.Clear();
        pendingAmongusNpcEntries.Clear();
        amongusKeepVisibleObjectKeys.Clear();
        loggedAmongusCustomizeRefreshFalseObjectKeys.Clear();
    }

    private static unsafe void WriteActorInvisible(GameObjectStruct* gameObj)
    {
        WriteRenderFlags(gameObj, ReadRenderFlags(gameObj) | InvisibilityDrawStateFlag);
    }

    private static unsafe void WriteActorVisible(GameObjectStruct* gameObj)
    {
        WriteRenderFlags(gameObj, ReadRenderFlags(gameObj) & ~InvisibilityDrawStateFlag);
    }

    private static unsafe uint? ReadExactNpcVisibleRenderFlags(nint address)
    {
        if (address == 0)
            return null;

        return ClearHiddenExactNpcRenderFlags(ReadRenderFlags((GameObjectStruct*)address));
    }

    private static unsafe uint ReadRenderFlags(GameObjectStruct* gameObj)
    {
        if (gameObj == null)
            return 0;

        var renderFlags = (uint*)&gameObj->RenderFlags;
        return *renderFlags;
    }

    private static unsafe void WriteRenderFlags(GameObjectStruct* gameObj, uint flags)
    {
        if (gameObj == null)
            return;

        var renderFlags = (uint*)&gameObj->RenderFlags;
        *renderFlags = flags;
    }

    private static unsafe void RestoreRenderFlags(nint address, uint renderFlags)
    {
        if (address == 0)
            return;

        WriteRenderFlags((GameObjectStruct*)address, renderFlags);
    }

    private unsafe void RevertAllAppearances()
    {
        isRevertingAppearances = true;
        ClearPendingRedraws();
        ClearPendingCreatedCharacterBaseReapplies();
        var reverted = 0;
        try
        {
            foreach (var obj in ObjectTable)
            {
                if (obj == null) continue;
                if (obj.Address != 0 && ImaginaryFrenService.IsManagedActor(obj.Address)) continue;

                var objectKey = GetAppearanceObjectKey(obj);
                if (!originalAppearanceData.TryGetValue(objectKey, out var originalData)) continue;

                try
                {
                    var character = (CharacterStruct*)obj.Address;
                    if (character == null)
                        continue;

                    var customizePtr = (byte*)&character->DrawData.CustomizeData;
                    for (var j = 0; j < CustomizeByteCount; j++)
                        customizePtr[j] = originalData.CustomizeData[j];

                    fixed (EquipmentModelId* equipmentModelPtr = &character->DrawData.EquipmentModelIds[0])
                    {
                        var equipmentPtr = (byte*)equipmentModelPtr;
                        for (var j = 0; j < EquipmentByteCount; j++)
                            equipmentPtr[j] = originalData.EquipmentData[j];
                    }

                    RestoreWeaponData(character, originalData);
                    RestoreBonusItems(character, originalData);
                    RestoreDrawMetaState(character, originalData);
                    RestoreModelContainerIds(character, originalData);
                    RefreshCharacterCustomize(character);
                    RefreshCharacterEquipment(character);

                    if (originalData.HasRenderFlags)
                    {
                        RestoreRenderFlags(obj.Address, originalData.RenderFlags);
                        if (IsAmongusObjectKind(obj.ObjectKind))
                            QueueExactNpcRedraw(obj.Address, originalData.RenderFlags);
                        else
                            QueuePenumbraStyleRedraw(obj.Address, originalData.RenderFlags);
                    }
                    else
                    {
                        if (IsAmongusObjectKind(obj.ObjectKind))
                            QueueExactNpcRedraw(obj.Address);
                        else
                            QueuePenumbraStyleRedraw(obj.Address);
                    }
                    reverted++;
                }
                catch { /* best effort revert */ }
            }
        }
        finally
        {
            if (reverted > 0)
                Log.Information($"[Krangler] Reverted {reverted} appearance changes");

            originalAppearanceData.Clear();
            AppearanceService.Reset();
            isRevertingAppearances = false;
        }
    }

    private unsafe void RevertLocalPlayerAppearanceIfApplied()
    {
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null || localPlayer.Address == 0)
            return;

        var objectKey = localPlayer.GameObjectId;
        if (!originalAppearanceData.TryGetValue(objectKey, out var originalData))
            return;

        try
        {
            var character = (CharacterStruct*)localPlayer.Address;
            if (character == null)
                return;

            var customizePtr = (byte*)&character->DrawData.CustomizeData;
            for (var j = 0; j < CustomizeByteCount; j++)
                customizePtr[j] = originalData.CustomizeData[j];

            fixed (EquipmentModelId* equipmentModelPtr = &character->DrawData.EquipmentModelIds[0])
            {
                var equipmentPtr = (byte*)equipmentModelPtr;
                for (var j = 0; j < EquipmentByteCount; j++)
                    equipmentPtr[j] = originalData.EquipmentData[j];
            }

            RestoreWeaponData(character, originalData);
            RestoreBonusItems(character, originalData);
            RestoreDrawMetaState(character, originalData);
            RestoreModelContainerIds(character, originalData);
            RefreshCharacterCustomize(character);
            RefreshCharacterEquipment(character);

            if (originalData.HasRenderFlags)
            {
                RestoreRenderFlags(localPlayer.Address, originalData.RenderFlags);
                QueuePenumbraStyleRedraw(localPlayer.Address, originalData.RenderFlags);
            }
            else
            {
                QueuePenumbraStyleRedraw(localPlayer.Address);
            }
            Log.Information("[Krangler] Reverted local player appearance after self-krangle opt-out");
        }
        catch (Exception ex)
        {
            Log.Warning($"[Krangler] Failed to revert local player appearance after self-krangle opt-out: {ex.Message}");
        }
        finally
        {
            originalAppearanceData.Remove(objectKey);
            AppearanceService.ClearApplied(objectKey);
        }
    }

    private unsafe void SaveOriginalAppearanceIfNeeded(ulong objectKey, CharacterStruct* character, nint gameObjectAddress = 0)
    {
        if (character == null)
            return;

        if (ImaginaryFrenService.IsManagedActor(gameObjectAddress) || ImaginaryFrenService.IsManagedActor((nint)character))
            return;

        if (originalAppearanceData.TryGetValue(objectKey, out var existingData))
        {
            SaveOriginalRenderFlagsIfNeeded(existingData, gameObjectAddress);
            SaveOriginalModelContainerIfNeeded(existingData, character);
            return;
        }

        var originalData = new OriginalAppearanceData();
        var customizePtr = (byte*)&character->DrawData.CustomizeData;
        for (var j = 0; j < CustomizeByteCount; j++)
            originalData.CustomizeData[j] = customizePtr[j];

        fixed (EquipmentModelId* equipmentModelPtr = &character->DrawData.EquipmentModelIds[0])
        {
            var equipmentPtr = (byte*)equipmentModelPtr;
            for (var j = 0; j < EquipmentByteCount; j++)
                originalData.EquipmentData[j] = equipmentPtr[j];
        }

        originalData.MainHandWeapon = character->DrawData.Weapon(DrawDataContainerStruct.WeaponSlot.MainHand).ModelId;
        originalData.OffHandWeapon = character->DrawData.Weapon(DrawDataContainerStruct.WeaponSlot.OffHand).ModelId;
        originalData.Glasses0 = character->DrawData.GlassesIds[0];
        originalData.Glasses1 = character->DrawData.GlassesIds[1];
        originalData.IsHatHidden = character->DrawData.IsHatHidden;
        originalData.IsWeaponHidden = character->DrawData.IsWeaponHidden;
        originalData.IsVisorToggled = character->DrawData.IsVisorToggled;
        originalData.VieraEarsHidden = character->DrawData.VieraEarsHidden;
        SaveOriginalModelContainerIfNeeded(originalData, character);
        SaveOriginalRenderFlagsIfNeeded(originalData, gameObjectAddress);

        originalAppearanceData[objectKey] = originalData;
    }

    private static unsafe void SaveOriginalModelContainerIfNeeded(OriginalAppearanceData originalData, CharacterStruct* character)
    {
        if (originalData.HasModelContainerIds || character == null)
            return;

        originalData.ModelCharaId = character->ModelContainer.ModelCharaId;
        originalData.ModelCharaId2 = character->ModelContainer.ModelCharaId_2;
        originalData.ModelSkeletonId = character->ModelContainer.ModelSkeletonId;
        originalData.ModelSkeletonId2 = character->ModelContainer.ModelSkeletonId_2;
        originalData.HasModelContainerIds = true;
    }

    private static unsafe void SaveOriginalRenderFlagsIfNeeded(OriginalAppearanceData originalData, nint gameObjectAddress)
    {
        if (originalData.HasRenderFlags || gameObjectAddress == 0)
            return;

        originalData.RenderFlags = ReadRenderFlags((GameObjectStruct*)gameObjectAddress);
        originalData.HasRenderFlags = true;
    }

    // ─── Party List Krangling ───────────────────────────────────────────────

    private unsafe void KranglePartyList()
    {
        // Log.Information("[Krangler] PartyList scan started - checking addon visibility");
        
        var addon = Instance()->GetAddonByName("_PartyList");
        if (addon == null)
        {
            Log.Information("[Krangler] _PartyList addon not found");
            return;
        }
        
        if (!addon->IsVisible) 
        {
            // Log.Information("[Krangler] _PartyList addon found but not visible");
            return;
        }
        
        // Log.Information("[Krangler] _PartyList addon found and visible - scanning party members");

        // Build lookup of original party member names -> krangled names
        var nameMap = new Dictionary<string, string>();
        // Log.Information($"[Krangler] PartyList.Length = {PartyList.Length}");
        
        // Check if we have party members
        var hasPartyMembers = false;
        for (int i = 0; i < PartyList.Length; i++)
        {
            var member = PartyList[i];
            if (member == null) 
            {
                Log.Information($"[Krangler] PartyList member {i} is null");
                continue;
            }
            var orig = member.Name.ToString();
            if (string.IsNullOrEmpty(orig))
            {
                Log.Information($"[Krangler] PartyList member {i} has empty name");
                continue;
            }
            
            hasPartyMembers = true;
            var replacementName = GetNameReplacement(orig);
            if (!string.Equals(orig, replacementName, StringComparison.Ordinal))
                nameMap[orig] = replacementName;

            var krangledName = KrangleService.KrangleName(orig);
            if (!string.Equals(krangledName, replacementName, StringComparison.Ordinal))
                nameMap[krangledName] = replacementName;

            if (IsLocalPlayerName(orig))
            {
                var configuredSelfName = GetConfiguredSelfDisplayName();
                if (!string.IsNullOrWhiteSpace(configuredSelfName) &&
                    !string.Equals(configuredSelfName, replacementName, StringComparison.Ordinal))
                {
                    nameMap[configuredSelfName] = replacementName;
                }
            }

            Log.Information($"[Krangler] PartyList member {i}: '{orig}' -> '{replacementName}'");
        }

        // SOLO PARTY: If no party members, try to krangle the player's own name
        if (!hasPartyMembers && ObjectTable.LocalPlayer != null)
        {
            var playerName = ObjectTable.LocalPlayer.Name.ToString();
            if (!string.IsNullOrEmpty(playerName))
            {
                var replacementName = GetNameReplacement(playerName);
                if (!string.Equals(playerName, replacementName, StringComparison.Ordinal))
                    nameMap[playerName] = replacementName;

                var krangledName = KrangleService.KrangleName(playerName);
                if (!string.Equals(krangledName, replacementName, StringComparison.Ordinal))
                    nameMap[krangledName] = replacementName;

                var configuredSelfName = GetConfiguredSelfDisplayName();
                if (!string.IsNullOrWhiteSpace(configuredSelfName) &&
                    !string.Equals(configuredSelfName, replacementName, StringComparison.Ordinal))
                {
                    nameMap[configuredSelfName] = replacementName;
                }

                if (nameMap.Count > 0)
                    Log.Information($"[Krangler] Solo party: '{playerName}' -> '{replacementName}'");
            }
        }

        if (nameMap.Count == 0)
        {
            // Log.Information("[Krangler] No valid party members found with names");
            return;
        }

        Log.Information($"[Krangler] Starting text node scan with {nameMap.Count} name mappings");

        // Diagnostic: Log party member names once so we know what we're looking for
        if (!hasLoggedPartyList)
        {
            foreach (var (orig, krangled) in nameMap)
                Log.Information($"[Krangler] PartyList name mapping: '{orig}' -> '{krangled}'");
        }

        // Walk all text nodes in the addon via UldManager NodeList
        var replacedCount = 0;
        var nodeCount = addon->UldManager.NodeListCount;

        Log.Information($"[Krangler] _PartyList addon: {nodeCount} nodes in UldManager NodeList");

        var textNodesFound = 0;
        for (var i = 0; i < nodeCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null) continue;

            // Check direct text nodes
            if (node->Type == NodeType.Text)
            {
                textNodesFound++;
                var textNode = (AtkTextNode*)node;
                var text = textNode->NodeText.ToString();
                if (string.IsNullOrEmpty(text)) continue;

                // Diagnostic: Log first batch of text node contents once
                if (!hasLoggedPartyList && text.Length > 1 && text.Length < 60)
                {
                    var cleanText = StripSeStringPayloads(text);
                    Log.Information($"[Krangler] PartyList text node [{i}] id={node->NodeId}: raw={text.Length}ch clean='{cleanText}'");
                }

                foreach (var (original, krangled) in nameMap)
                {
                    // Strip SeString payloads for matching (0x02...0x03 control bytes)
                    var cleanText = StripSeStringPayloads(text);
                    
                    // Find the first letter in clean text (skip icons/symbols)
                    var nameStartIndex = -1;
                    for (int k = 0; k < cleanText.Length; k++)
                    {
                        if (char.IsLetter(cleanText[k]))
                        {
                            nameStartIndex = k;
                            break;
                        }
                    }
                    
                    if (nameStartIndex >= 0 && cleanText.Length - nameStartIndex >= 5 && original.Length >= 5)
                    {
                        // Extract the actual text that exists in the node
                        var actualTextInNode = cleanText.Substring(nameStartIndex);
                        var minLength = Math.Min(5, Math.Min(actualTextInNode.Length, original.Length));
                        
                        // Check if the first 5+ characters match
                        if (actualTextInNode.Substring(0, minLength) == original.Substring(0, minLength))
                        {
                            // Create replacement text sized to match what we actually found
                            var partialLength = actualTextInNode.Length;
                            var replacementText = krangled.Length >= partialLength ? 
                                krangled.Substring(0, partialLength) : krangled;
                            
                            // Only log detailed matching for text that might contain names (longer than 10 chars)
                            if (!hasLoggedPartyList && cleanText.Length > 10)
                                Log.Information($"[Krangler] Matching: original='{original}' found='{actualTextInNode}' replace='{replacementText}'");
                            
                            // Replace the actual text we found, not the full original
                            var newText = text.Replace(actualTextInNode, replacementText);
                            textNode->SetText(newText);
                            Log.Information($"[Krangler] REPLACED: '{actualTextInNode}' -> '{replacementText}' in text node");
                            replacedCount++;
                            break;
                        }
                    }
                    
                    // Fallback to full contains match (for cases where full name exists)
                    if (cleanText.Contains(original))
                    {
                        // Only log detailed matching for text that might contain names (longer than 10 chars)
                        if (!hasLoggedPartyList && cleanText.Length > 10)
                            Log.Information($"[Krangler] Full Match: original='{original}' replace='{krangled}'");
                        
                        var newText = text.Replace(original, krangled);
                        textNode->SetText(newText);
                        Log.Information($"[Krangler] FULL REPLACED: '{original}' -> '{krangled}' in text node");
                        replacedCount++;
                        break;
                    }
                }
            }

            // Also check inside component nodes
            if ((int)node->Type >= 1000)
            {
                var comp = (AtkComponentNode*)node;
                if (comp->Component != null)
                {
                    var compNodeCount = comp->Component->UldManager.NodeListCount;
                    for (var j = 0; j < compNodeCount; j++)
                    {
                        var subNode = comp->Component->UldManager.NodeList[j];
                        if (subNode == null || subNode->Type != NodeType.Text) continue;

                        textNodesFound++;
                        var textNode = (AtkTextNode*)subNode;
                        var text = textNode->NodeText.ToString();
                        if (string.IsNullOrEmpty(text)) continue;

                        // Diagnostic: Log component text nodes once
                        if (!hasLoggedPartyList && text.Length > 1 && text.Length < 60)
                        {
                            var cleanText = StripSeStringPayloads(text);
                            Log.Information($"[Krangler] PartyList component [{i}] sub [{j}] id={subNode->NodeId}: raw={text.Length}ch clean='{cleanText}'");
                        }

                        foreach (var (original, krangled) in nameMap)
                        {
                            // Strip SeString payloads for matching (0x02...0x03 control bytes)
                            var cleanText = StripSeStringPayloads(text);
                            
                            // Find the first letter in clean text (skip icons/symbols)
                            var nameStartIndex = -1;
                            for (int k = 0; k < cleanText.Length; k++)
                            {
                                if (char.IsLetter(cleanText[k]))
                                {
                                    nameStartIndex = k;
                                    break;
                                }
                            }
                            
                            if (nameStartIndex >= 0 && cleanText.Length - nameStartIndex >= 5 && original.Length >= 5)
                            {
                                // Extract the actual text that exists in the node
                                var actualTextInNode = cleanText.Substring(nameStartIndex);
                                var minLength = Math.Min(5, Math.Min(actualTextInNode.Length, original.Length));
                                
                                // Check if the first 5+ characters match
                                if (actualTextInNode.Substring(0, minLength) == original.Substring(0, minLength))
                                {
                                    // Create replacement text sized to match what we actually found
                                    var partialLength = actualTextInNode.Length;
                                    var replacementText = krangled.Length >= partialLength ? 
                                        krangled.Substring(0, partialLength) : krangled;
                                    
                                    // Only log detailed matching for text that might contain names (longer than 10 chars)
                                    if (!hasLoggedPartyList && cleanText.Length > 10)
                                        Log.Information($"[Krangler] Component Matching: original='{original}' found='{actualTextInNode}' replace='{replacementText}'");
                                    
                                    // Replace the actual text we found, not the full original
                                    var newText = text.Replace(actualTextInNode, replacementText);
                                    textNode->SetText(newText);
                                    Log.Information($"[Krangler] COMPONENT REPLACED: '{actualTextInNode}' -> '{replacementText}'");
                                    replacedCount++;
                                    break;
                                }
                            }
                            
                            // Fallback to full contains match (for cases where full name exists)
                            if (cleanText.Contains(original))
                            {
                                // Only log detailed matching for text that might contain names (longer than 10 chars)
                                if (!hasLoggedPartyList && cleanText.Length > 10)
                                    Log.Information($"[Krangler] Component Full Match: original='{original}' replace='{krangled}'");
                                
                                var newText = text.Replace(original, krangled);
                                textNode->SetText(newText);
                                Log.Information($"[Krangler] COMPONENT FULL REPLACED: '{original}' -> '{krangled}'");
                                replacedCount++;
                                break;
                            }
                        }
                    }
                }
            }
        }

        if (!hasLoggedPartyList)
        {
            Log.Information($"[Krangler] Party list scan: {nameMap.Count} members, {textNodesFound} text nodes found, {replacedCount} text nodes replaced");
            hasLoggedPartyList = true;
        }
    }

    private unsafe void KranglePartyMemberList()
    {
        var addon = Instance()->GetAddonByName("PartyMemberList");
        if (addon == null || !addon->IsVisible)
            return;

        KranglePartyMemberList(addon);
    }

    private unsafe int KranglePartyMemberList(AtkUnitBase* addon)
    {
        var nameMap = BuildPlayerNameMap();
        if (nameMap.Count == 0)
            return 0;

        return TryKranglePartyMemberListNodes(addon, nameMap);
    }

    private unsafe int TryKranglePartyMemberListNodes(AtkUnitBase* addon, Dictionary<string, string> nameMap)
    {
        if (addon == null || nameMap.Count == 0)
            return 0;

        var replacedCount = 0;
        foreach (var nodePath in PartyMemberListTextNodePaths)
        {
            var node = FindNestedPartyMemberListNode(addon, nodePath);
            if (node == null || node->Type != NodeType.Text)
                continue;

            var textNode = (AtkTextNode*)node;
            var text = textNode->NodeText.ToString();
            if (!TryBuildUpdatedText(text, nameMap, out var newText) ||
                string.Equals(text, newText, StringComparison.Ordinal))
            {
                continue;
            }

            textNode->SetText(newText);
            replacedCount++;
        }

        if (replacedCount == 0)
            replacedCount += WalkUldManagerNodeListAndReplaceTextNodes(addon, nameMap);

        if (replacedCount == 0 && addon->UldManager.RootNode != null)
            replacedCount += WalkAndReplaceTextNodes(addon->UldManager.RootNode, nameMap);

        return replacedCount;
    }

    private unsafe int WalkUldManagerNodeListAndReplaceTextNodes(AtkUnitBase* addon, Dictionary<string, string> nameMap)
    {
        if (addon == null || nameMap.Count == 0)
            return 0;

        var replacedCount = 0;
        var visited = new HashSet<nint>();

        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null)
                continue;

            replacedCount += WalkAndReplaceTextNodes(node, nameMap, visited);
        }

        return replacedCount;
    }

    private unsafe AtkResNode* FindNestedPartyMemberListNode(AtkUnitBase* addon, uint[] nodePath)
    {
        if (addon == null || nodePath.Length == 0)
            return null;

        AtkResNode* currentNode = addon->GetNodeById(nodePath[0]);
        for (var i = 1; currentNode != null && i < nodePath.Length; i++)
        {
            currentNode = FindDescendantNodeById(currentNode, nodePath[i]);
        }

        return currentNode;
    }

    private unsafe AtkResNode* FindDescendantNodeById(AtkResNode* parentNode, uint targetNodeId)
    {
        if (parentNode == null)
            return null;

        if ((int)parentNode->Type >= 1000)
        {
            var componentNode = (AtkComponentNode*)parentNode;
            if (componentNode->Component != null)
            {
                var directMatch = componentNode->Component->UldManager.SearchNodeById(targetNodeId);
                if (directMatch != null)
                    return directMatch;

                return FindNodeByIdInChain(componentNode->Component->UldManager.RootNode, targetNodeId);
            }
        }

        return FindNodeByIdInChain(parentNode->ChildNode, targetNodeId);
    }

    private unsafe AtkResNode* FindNodeByIdInChain(AtkResNode* startNode, uint targetNodeId)
    {
        var node = startNode;
        while (node != null)
        {
            if (node->NodeId == targetNodeId)
                return node;

            var descendantMatch = FindDescendantNodeById(node, targetNodeId);
            if (descendantMatch != null)
                return descendantMatch;

            node = node->PrevSiblingNode;
        }

        return null;
    }

    private void OnPartyMemberListAddon(AddonEvent type, AddonArgs args)
    {
        switch (type)
        {
            case AddonEvent.PreFinalize:
                ResetPartyMemberListFallbackState();
                return;

            case AddonEvent.PostDraw:
                break;

            default:
                return;
        }

        if (!Configuration.Enabled ||
            (!Configuration.KrangleNames && !SuperKrangleMaster4000_Active))
        {
            ResetPartyMemberListFallbackState();
            return;
        }

        unsafe
        {
            var addon = (AtkUnitBase*)args.Addon.Address;
            if (addon == null || !addon->IsVisible)
                return;

            if (!hasShownPartyMemberListFallbackWarning)
            {
                const string warningMessage = "DO NOT CLICK ON PLAYERSEARCH OR FRIEND LIST";
                ToastGui.ShowNormal(new SeString(new TextPayload(warningMessage)));
                PrintStatus(warningMessage);
                hasShownPartyMemberListFallbackWarning = true;
            }

            KranglePartyMemberList(addon);
        }
    }

    private void ResetPartyMemberListFallbackState()
    {
        hasShownPartyMemberListFallbackWarning = false;
    }

    private unsafe void UpdateTargetInfoSurfaces()
    {
        UpdateTargetInfoAddon("_TargetInfo", 16, 7, TargetManager.Target);
        UpdateTargetInfoAddon("_TargetInfoMainTarget", 10, 7, TargetManager.Target);
        UpdateSingleTargetAddon("_FocusTargetInfo", 10, TargetManager.FocusTarget);
    }

    private unsafe void RestoreTargetInfoSurfaces()
    {
        UpdateTargetInfoAddon("_TargetInfo", 16, 7, TargetManager.Target, true);
        UpdateTargetInfoAddon("_TargetInfoMainTarget", 10, 7, TargetManager.Target, true);
        UpdateSingleTargetAddon("_FocusTargetInfo", 10, TargetManager.FocusTarget, true);
    }

    private unsafe void UpdateTargetInfoAddon(string addonName, uint targetNodeId, uint targetOfTargetNodeId, IGameObject? target, bool forceOriginal = false)
    {
        var addon = Instance()->GetAddonByName(addonName);
        if (addon == null || !addon->IsVisible)
            return;

        SetTargetInfoNodeText(addon, targetNodeId, target, forceOriginal);

        var targetOfTarget = target is ICharacter characterTarget
            ? characterTarget.TargetObject
            : null;
        if (targetOfTarget == null &&
            target != null &&
            IsLocalPlayerName(target.Name.ToString()))
        {
            targetOfTarget = target;
        }

        SetTargetInfoNodeText(addon, targetOfTargetNodeId, targetOfTarget, forceOriginal);
    }

    private unsafe void UpdateSingleTargetAddon(string addonName, uint targetNodeId, IGameObject? target, bool forceOriginal = false)
    {
        var addon = Instance()->GetAddonByName(addonName);
        if (addon == null || !addon->IsVisible)
            return;

        SetTargetInfoNodeText(addon, targetNodeId, target, forceOriginal);
    }

    private unsafe void SetTargetInfoNodeText(AtkUnitBase* addon, uint nodeId, IGameObject? target, bool forceOriginal)
    {
        if (addon == null)
            return;

        var node = addon->GetTextNodeById(nodeId);
        if (node == null)
            return;

        var desiredText = GetTargetSurfaceDisplayName(target, forceOriginal);
        if (string.IsNullOrWhiteSpace(desiredText))
            return;

        var currentText = node->NodeText.ToString();
        if (!string.Equals(currentText, desiredText, StringComparison.Ordinal))
            node->SetText(desiredText);
    }

    private string? GetTargetSurfaceDisplayName(IGameObject? target, bool forceOriginal)
    {
        if (target == null)
            return null;

        var originalName = target.Name.ToString();
        if (string.IsNullOrWhiteSpace(originalName))
            return null;

        if (forceOriginal)
            return originalName;

        return target.ObjectKind == ObjectKind.Pc
            ? GetNameReplacement(originalName)
            : originalName;
    }

    private Dictionary<string, string> BuildPlayerNameMap()
    {
        var nameMap = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < ObjectTable.Length; i++)
        {
            var obj = ObjectTable[i];
            if (obj == null || obj.ObjectKind != ObjectKind.Pc)
                continue;

            AddNameReplacement(nameMap, obj.Name.ToString());
        }

        for (var i = 0; i < PartyList.Length; i++)
        {
            var member = PartyList[i];
            if (member == null)
                continue;

            AddNameReplacement(nameMap, member.Name.ToString());
        }

        if (ObjectTable.LocalPlayer != null)
            AddNameReplacement(nameMap, ObjectTable.LocalPlayer.Name.ToString());

        return nameMap;
    }

    private void AddNameReplacement(Dictionary<string, string> nameMap, string originalName)
    {
        if (string.IsNullOrWhiteSpace(originalName))
            return;

        var replacementName = GetNameReplacement(originalName);
        if (!string.Equals(originalName, replacementName, StringComparison.Ordinal))
            nameMap[originalName] = replacementName;

        var krangledName = KrangleService.KrangleName(originalName);
        if (!string.Equals(krangledName, replacementName, StringComparison.Ordinal))
            nameMap[krangledName] = replacementName;

        if (IsLocalPlayerName(originalName))
        {
            var configuredSelfName = GetConfiguredSelfDisplayName();
            if (!string.IsNullOrWhiteSpace(configuredSelfName) &&
                !string.Equals(configuredSelfName, replacementName, StringComparison.Ordinal))
            {
                nameMap[configuredSelfName] = replacementName;
            }
        }
    }

    /// <summary>
    /// Strip FFXIV SeString payload bytes from text for plain-text matching.
    /// SeString payloads: 0x02 [type] [length] [data...] 0x03
    /// Critical for party list krangling - text nodes contain SeString control bytes.
    /// </summary>
    private static string StripSeStringPayloads(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (ch == '\x02' && i + 1 < text.Length)
            {
                // Skip payload: 0x02 type len data... 0x03
                // Find the matching 0x03
                i++; // skip 0x02
                while (i < text.Length && text[i] != '\x03')
                    i++;
                if (i < text.Length) i++; // skip 0x03
            }
            else if (ch >= ' ') // skip any other control chars
            {
                sb.Append(ch);
                i++;
            }
            else
            {
                i++;
            }
        }
        return sb.ToString();
    }

    private unsafe int WalkAndReplaceTextNodes(AtkResNode* node, Dictionary<string, string> nameMap)
    {
        return WalkAndReplaceTextNodes(node, nameMap, new HashSet<nint>());
    }

    private unsafe int WalkAndReplaceTextNodes(AtkResNode* node, Dictionary<string, string> nameMap, HashSet<nint> visited)
    {
        if (node == null || nameMap.Count == 0)
            return 0;

        var nodeAddress = (nint)node;
        if (!visited.Add(nodeAddress))
            return 0;

        var replacedCount = 0;

        // Check if this is a text node
        if (node->Type == NodeType.Text)
        {
            var textNode = (AtkTextNode*)node;
            var text = textNode->NodeText.ToString();
            if (TryBuildUpdatedText(text, nameMap, out var newText) &&
                !string.Equals(text, newText, StringComparison.Ordinal))
            {
                textNode->SetText(newText);
                replacedCount++;
            }
        }

        // Recurse into component nodes (Type >= 1000 = component)
        if ((int)node->Type >= 1000)
        {
            var comp = (AtkComponentNode*)node;
            if (comp->Component != null)
            {
                var child = comp->Component->UldManager.RootNode;
                if (child != null)
                    replacedCount += WalkAndReplaceTextNodes(child, nameMap, visited);
            }
        }

        // Walk siblings
        var sibling = node->PrevSiblingNode;
        if (sibling != null)
            replacedCount += WalkAndReplaceTextNodes(sibling, nameMap, visited);

        var childNode = node->ChildNode;
        if (childNode != null)
            replacedCount += WalkAndReplaceTextNodes(childNode, nameMap, visited);

        return replacedCount;
    }

    private static bool TryBuildUpdatedText(string text, Dictionary<string, string> nameMap, out string updatedText)
    {
        updatedText = text;
        if (string.IsNullOrEmpty(text) || nameMap.Count == 0)
            return false;

        var cleanText = StripSeStringPayloads(text);
        foreach (var (original, replacement) in nameMap)
        {
            if (TryReplaceNameInText(text, cleanText, original, replacement, out updatedText))
                return true;
        }

        return false;
    }

    private static bool TryReplaceNameInText(string rawText, string cleanText, string original, string replacement, out string updatedText)
    {
        updatedText = rawText;
        if (string.IsNullOrEmpty(rawText) ||
            string.IsNullOrEmpty(cleanText) ||
            string.IsNullOrEmpty(original) ||
            string.Equals(original, replacement, StringComparison.Ordinal))
        {
            return false;
        }

        var nameStartIndex = -1;
        for (var i = 0; i < cleanText.Length; i++)
        {
            if (char.IsLetter(cleanText[i]))
            {
                nameStartIndex = i;
                break;
            }
        }

        if (nameStartIndex >= 0 &&
            cleanText.Length - nameStartIndex >= 5 &&
            original.Length >= 5)
        {
            var actualTextInNode = cleanText.Substring(nameStartIndex);
            var minLength = Math.Min(5, Math.Min(actualTextInNode.Length, original.Length));
            if (actualTextInNode.Substring(0, minLength) == original.Substring(0, minLength))
            {
                var partialLength = actualTextInNode.Length;
                var replacementText = replacement.Length >= partialLength
                    ? replacement.Substring(0, partialLength)
                    : replacement;
                updatedText = rawText.Replace(actualTextInNode, replacementText, StringComparison.Ordinal);
                return !string.Equals(rawText, updatedText, StringComparison.Ordinal);
            }
        }

        if (cleanText.Contains(original, StringComparison.Ordinal))
        {
            updatedText = rawText.Replace(original, replacement, StringComparison.Ordinal);
            return !string.Equals(rawText, updatedText, StringComparison.Ordinal);
        }

        return false;
    }

    private void OnNamePlateUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        if (!Configuration.Enabled)
            return;

        var playerCount = 0;
        for (var i = 0; i < handlers.Count; i++)
        {
            var handler = handlers[i];

            // Only krangle player character nameplates
            if (handler.NamePlateKind != NamePlateKind.PlayerCharacter)
                continue;

            playerCount++;
            var originalName = handler.Name.ToString();
            var skipSelfKrangling = ShouldSkipSelfKrangling(originalName);

            // Krangle name
            if (Configuration.KrangleNames)
            {
                if (!string.IsNullOrEmpty(originalName))
                {
                    var krangled = GetNameReplacement(originalName);
                    if (!hasLoggedNameplateUpdate)
                        Log.Information($"[Krangler] Name: '{originalName}' -> '{krangled}'");
                    handler.Name = krangled;
                }
            }

            if (skipSelfKrangling)
                continue;

            // Krangle FC tag
            try
            {
                var originalFc = handler.FreeCompanyTag.ToString();
                if (!string.IsNullOrEmpty(originalFc))
                {
                    var krangledFc = KrangleService.KrangleFCTag(originalFc);
                    if (!hasLoggedNameplateUpdate)
                        Log.Information($"[Krangler] FC: '{originalFc}' -> '{krangledFc}'");
                    handler.FreeCompanyTag = krangledFc;
                }
            }
            catch (Exception ex)
            {
                if (!hasLoggedNameplateUpdate)
                    Log.Warning($"[Krangler] FreeCompanyTag not available: {ex.Message}");
            }

            // Krangle title
            try
            {
                var originalTitle = handler.Title.ToString();
                if (!string.IsNullOrEmpty(originalTitle))
                {
                    var krangledTitle = KrangleService.KrangleTitle(originalTitle);
                    if (!hasLoggedNameplateUpdate)
                        Log.Information($"[Krangler] Title: '{originalTitle}' -> '{krangledTitle}'");
                    handler.Title = krangledTitle;
                }
            }
            catch (Exception ex)
            {
                if (!hasLoggedNameplateUpdate)
                    Log.Warning($"[Krangler] Title not available: {ex.Message}");
            }
        }

        if (!hasLoggedNameplateUpdate && playerCount > 0)
        {
            Log.Information($"[Krangler] OnNamePlateUpdate: {handlers.Count} handlers, {playerCount} players");
            hasLoggedNameplateUpdate = true;
        }
    }

    public void SetupDtrBar()
    {
        try
        {
            dtrEntry = DtrBar.Get("Krangler");
            dtrEntry.Shown = Configuration.DtrBarEnabled;
            dtrEntry.Text = new SeString(new TextPayload("KR: Off"));
            dtrEntry.OnClick = (_) =>
            {
                Configuration.Enabled = !Configuration.Enabled;
                if (!Configuration.Enabled)
                {
                    KrangleService.ClearCache();
                    AppearanceService.Reset();
                }
                Configuration.Save();
            };
        }
        catch (Exception ex)
        {
            Log.Error($"[Krangler] Failed to setup DTR bar: {ex.Message}");
        }
    }

    public void UpdateDtrBar()
    {
        if (dtrEntry == null) return;

        dtrEntry.Shown = Configuration.DtrBarEnabled;
        if (!Configuration.DtrBarEnabled) return;

        var iconEnabled = string.IsNullOrEmpty(Configuration.DtrIconEnabled) ? "\uE044" : Configuration.DtrIconEnabled;
        var iconDisabled = string.IsNullOrEmpty(Configuration.DtrIconDisabled) ? "\uE04C" : Configuration.DtrIconDisabled;
        var glyph = Configuration.Enabled ? iconEnabled : iconDisabled;

        switch (Configuration.DtrBarMode)
        {
            case 1: // icon+text
                dtrEntry.Text = new SeString(new TextPayload($"{glyph} KR"));
                break;
            case 2: // icon-only
                dtrEntry.Text = new SeString(new TextPayload(glyph));
                break;
            default: // text-only
                var statusText = Configuration.Enabled ? "KR: On" : "KR: Off";
                dtrEntry.Text = new SeString(new TextPayload(statusText));
                break;
        }

        dtrEntry.Tooltip = new SeString(new TextPayload(
            Configuration.Enabled
                ? "Krangler active — Click to disable"
                : "Krangler disabled — Click to enable"));
    }

    public void ToggleMainUi() => MainWindow.Toggle();

    public bool ShowDebugOptions => Configuration.ShowDebugOptions;
    public bool DisableDateBasedSuperKrangleEvent => Configuration.DisableDateBasedSuperKrangleEvent;
    public bool IsDateBasedSuperKrangleWindowActive => IsSuperKrangleEventWindowActive;
    public bool IsDateBasedSuperKrangleEventCurrentlyForced => IsSuperKrangleEventActive && !Configuration.SuperKrangleMaster4000;

    public void ToggleDebugOptions()
    {
        Configuration.ShowDebugOptions = !Configuration.ShowDebugOptions;
        Configuration.Save();
        MainWindow.IsOpen = true;

        var debugState = Configuration.ShowDebugOptions ? "ON" : "OFF";
        Log.Information($"[Krangler] Debug controls toggled: {debugState}");
        PrintStatus($"Debug controls: {debugState}.");
    }

    public void SetDateBasedSuperKrangleEventSuppressed(bool suppressed)
    {
        if (Configuration.DisableDateBasedSuperKrangleEvent == suppressed)
            return;

        Configuration.DisableDateBasedSuperKrangleEvent = suppressed;
        Configuration.Save();

        hasLoggedEventActivation = false;
        hasLoggedAppearanceScan = false;
        hasLoggedPartyList = false;
        lastAppearanceScan = DateTime.MinValue;
        lastPartyListScan = DateTime.MinValue;

        if (Configuration.Enabled)
            RevertAllAppearances();

        var message = suppressed
            ? "Date-based Wuk Lamat auto-event disabled for debugging."
            : "Date-based Wuk Lamat auto-event re-enabled.";
        Log.Information($"[Krangler] {message}");
        PrintStatus(message);
    }

    public void SetSkipSelfKrangling(bool skipSelfKrangling)
    {
        if (Configuration.SkipSelfKrangling == skipSelfKrangling)
            return;

        Configuration.SkipSelfKrangling = skipSelfKrangling;
        RefreshSelfKrangleState(skipSelfKrangling);
        Configuration.Save();

        var message = skipSelfKrangling
            ? "Self krangling disabled."
            : "Self krangling re-enabled.";
        Log.Information($"[Krangler] {message}");
        PrintStatus(message);
    }

    public void SetCustomSelfDisplayName(string customSelfDisplayName)
    {
        var sanitized = SanitizeCustomSelfDisplayName(customSelfDisplayName);
        if (string.Equals(Configuration.CustomSelfDisplayName, sanitized, StringComparison.Ordinal))
            return;

        Configuration.CustomSelfDisplayName = sanitized;
        RefreshNameKrangleSurfaces();
        Configuration.Save();
    }

    public void ResetMainWindowPosition()
    {
        MainWindow.QueueResetToOrigin();
        MainWindow.IsOpen = true;
        PrintStatus("Queued Krangler main window reset to 1,1.");
    }

    public void JumpMainWindowToRandomVisibleLocation()
    {
        MainWindow.QueueRandomVisibleJump();
        MainWindow.IsOpen = true;
        PrintStatus("Queued a random visible jump for the Krangler main window.");
    }

    private void PrintStatus(string message)
    {
        ChatGui.Print($"[Krangler] {message}");
    }

    private void PrintImaginaryFrenStatus()
    {
        var status = ImaginaryFrenService.GetStatus();
        var spawned = status.Spawned ? "spawned" : "not spawned";
        var error = string.IsNullOrWhiteSpace(status.Error) ? string.Empty : $" error='{status.Error}'";
        PrintStatus($"Fren: enabled={status.Enabled}, {spawned}, name='{status.Name}', preset='{status.PresetKey}', source={status.Source}, persist={status.Persist}, status='{status.Status}'{error}");
    }

    private unsafe bool IsManagedImaginaryFrenDrawObject(nint drawObjectAddress)
    {
        if (drawObjectAddress == 0)
            return false;

        foreach (var obj in ObjectTable)
        {
            if (obj == null || obj.Address == 0 || !ImaginaryFrenService.IsManagedActor(obj.Address))
                continue;

            var character = (CharacterStruct*)obj.Address;
            return character != null && (nint)character->DrawObject == drawObjectAddress;
        }

        return false;
    }

    private void RefreshNameKrangleSurfaces()
    {
        hasLoggedNameplateUpdate = false;
        hasLoggedPartyList = false;
        lastPartyListScan = DateTime.MinValue;
    }

    private void RefreshSelfKrangleState(bool revertLocalAppearance)
    {
        RefreshNameKrangleSurfaces();
        hasLoggedAppearanceScan = false;
        lastAppearanceScan = DateTime.MinValue;

        if (revertLocalAppearance && Configuration.Enabled)
            RevertLocalPlayerAppearanceIfApplied();
    }

    private bool IsLocalPlayerObject(ulong objectKey, nint address)
    {
        if (!Configuration.SkipSelfKrangling)
            return false;

        var localPlayer = ObjectTable.LocalPlayer;
        return localPlayer != null &&
               (localPlayer.GameObjectId == objectKey || localPlayer.Address == address);
    }

    private bool ShouldSkipSelfKrangling(string playerName)
    {
        return Configuration.SkipSelfKrangling && IsLocalPlayerName(playerName);
    }

    private bool IsLocalPlayerName(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return false;

        var localName = ObjectTable.LocalPlayer?.Name.ToString();
        return !string.IsNullOrWhiteSpace(localName) &&
               string.Equals(localName, playerName, StringComparison.OrdinalIgnoreCase);
    }

    private string GetNameReplacement(string originalName)
    {
        if (ShouldSkipSelfKrangling(originalName))
            return GetResolvedSelfDisplayName(originalName);

        return KrangleService.KrangleName(originalName);
    }

    private string GetResolvedSelfDisplayName(string fallbackName)
    {
        var configuredSelfName = GetConfiguredSelfDisplayName();
        return string.IsNullOrWhiteSpace(configuredSelfName) ? fallbackName : configuredSelfName;
    }

    private string GetConfiguredSelfDisplayName()
        => SanitizeCustomSelfDisplayName(Configuration.CustomSelfDisplayName);

    private static string SanitizeCustomSelfDisplayName(string? customSelfDisplayName)
    {
        var sanitized = customSelfDisplayName?.Trim() ?? string.Empty;
        return sanitized.Length > 22 ? sanitized[..22] : sanitized;
    }

    // ─── Glamourer Preset Application ─────────────────────────────────────

    /// <summary>
    /// Apply the appearance portion of a Glamourer preset to a character.
    /// DrawDataContainer layout (from FFXIVClientStructs):
    ///   +0x010: WeaponData[3]
    ///   +0x1D0: EquipmentModelIds[10] (8 bytes each)
    ///   +0x220: CustomizeData (26 bytes)
    /// </summary>
    internal unsafe bool TryPreparePresetForImaginaryFren(CharacterStruct* character, GlamourerPreset preset, out string status)
    {
        status = string.Empty;
        if (character == null)
        {
            status = "Character pointer was null.";
            return false;
        }

        if (preset.Customize.ModelId > 0)
        {
            status = $"Blocked preset '{preset.Name}' because Imaginary Fren cannot apply exact NPC modelId {preset.Customize.ModelId}.";
            return false;
        }

        var customizeRequested = PresetRequestsCustomize(preset, forceAppearance: true);
        var customizeSeeded = customizeRequested && ApplyCustomizeData(&character->DrawData.CustomizeData, preset, forceAppearance: true);
        int equipmentSeeded;
        fixed (EquipmentModelId* equipmentModelPtr = &character->DrawData.EquipmentModelIds[0])
        {
            equipmentSeeded = ApplyEquipmentData(
                equipmentModelPtr,
                preset,
                character: null,
                exactNpcReplacement: false,
                logDetails: false,
                forceAllSlots: true);
        }

        status = $"seededCustomize={customizeSeeded}, requestedCustomize={customizeRequested}, seededEquipment={equipmentSeeded}";
        return true;
    }

    internal unsafe bool TryApplyPresetToImaginaryFren(CharacterStruct* character, GlamourerPreset preset, out string status, out bool customizeRefreshFailed)
    {
        status = string.Empty;
        customizeRefreshFailed = false;
        if (character == null)
        {
            status = "Character pointer was null.";
            return false;
        }

        if (preset.Customize.ModelId > 0)
        {
            status = $"Blocked preset '{preset.Name}' because Imaginary Fren cannot apply exact NPC modelId {preset.Customize.ModelId}.";
            return false;
        }

        var customizeRequested = PresetRequestsCustomize(preset, forceAppearance: true);
        var result = ApplySuperKranglePresetDetailed(
            character,
            preset,
            logRefreshResult: false,
            exactNpcReplacement: false,
            logDetails: false,
            preAppliedDuringCreate: false,
            forceAllSlots: true);

        customizeRefreshFailed = customizeRequested && !result.CustomizeRefreshed;
        status = $"appearance={result.CustomizeApplied}, requestedCustomize={customizeRequested}, refresh={result.CustomizeRefreshed}, equipment={result.EquipmentApplied}, weapons={result.WeaponsApplied}, bonus={result.BonusApplied}, meta={result.MetaApplied}";
        return result.GeneralSuccess;
    }

    private unsafe bool ApplySuperKranglePreset(CharacterStruct* character, GlamourerPreset preset, bool logRefreshResult, bool exactNpcReplacement = false, bool logDetails = true)
        => exactNpcReplacement
            ? ApplySuperKranglePresetDetailed(character, preset, logRefreshResult, exactNpcReplacement, logDetails).ExactSuccess
            : ApplySuperKranglePresetDetailed(character, preset, logRefreshResult, exactNpcReplacement, logDetails).GeneralSuccess;

    private unsafe PresetApplyResult ApplySuperKranglePresetDetailed(
        CharacterStruct* character,
        GlamourerPreset preset,
        bool logRefreshResult,
        bool exactNpcReplacement = false,
        bool logDetails = true,
        bool preAppliedDuringCreate = false,
        bool forceAllSlots = false)
    {
        if (character == null)
            return new PresetApplyResult(false, false, 0, 0, false, false, false);

        var customizePtr = (byte*)&character->DrawData.CustomizeData;
        var appliedAppearance = preAppliedDuringCreate && exactNpcReplacement
            ? PresetRequestsCustomize(preset, forceAllSlots)
            : ApplyGlamourerPreset(character, preset, customizePtr, logDetails, forceAllSlots);
        var refreshedAppearance = appliedAppearance && (preAppliedDuringCreate && exactNpcReplacement || RefreshCharacterCustomize(character));
        var appliedEquipment = ApplyGlamourerEquipment(character, preset, exactNpcReplacement, logDetails, forceAllSlots);
        var appliedWeapons = ApplyGlamourerWeapons(character, preset, logDetails, forceAllSlots);
        var appliedBonus = ApplyGlamourerBonusItems(character, preset, logDetails);
        var appliedMetaState = ApplyGlamourerMetaState(character, preset, logDetails);
        
        // Refresh equipment after applying it to ensure it's properly loaded
        if (appliedEquipment > 0)
            RefreshCharacterEquipment(character);
        
        if (logDetails && logRefreshResult && !hasLoggedAppearanceScan && appliedAppearance)
            Log.Information($"[Krangler] Native customize refresh for preset '{preset.Name}' returned {refreshedAppearance}");

        return new PresetApplyResult(
            appliedAppearance,
            refreshedAppearance,
            appliedEquipment,
            appliedWeapons,
            appliedBonus,
            appliedMetaState,
            preAppliedDuringCreate);
    }

    private bool PresetRequestsCustomize(GlamourerPreset preset, bool forceAppearance = false)
        => (forceAppearance || Configuration.SuperKrangleApplyAppearance) &&
           (preset.Customize.ModelId > 0 ||
            preset.Customize.Race.Apply ||
            preset.Customize.Gender.Apply ||
            preset.Customize.BodyType.Apply ||
            preset.Customize.Height.Apply ||
            preset.Customize.Clan.Apply ||
            preset.Customize.Face.Apply ||
            preset.Customize.Hairstyle.Apply ||
            preset.Customize.Highlights.Apply ||
            preset.Customize.SkinColor.Apply ||
            preset.Customize.EyeColorRight.Apply ||
            preset.Customize.HairColor.Apply ||
            preset.Customize.HighlightsColor.Apply ||
            preset.Customize.FacialFeature1.Apply ||
            preset.Customize.FacialFeature2.Apply ||
            preset.Customize.FacialFeature3.Apply ||
            preset.Customize.FacialFeature4.Apply ||
            preset.Customize.FacialFeature5.Apply ||
            preset.Customize.FacialFeature6.Apply ||
            preset.Customize.FacialFeature7.Apply ||
            preset.Customize.LegacyTattoo.Apply ||
            preset.Customize.TattooColor.Apply ||
            preset.Customize.Eyebrows.Apply ||
            preset.Customize.EyeColorLeft.Apply ||
            preset.Customize.EyeShape.Apply ||
            preset.Customize.SmallIris.Apply ||
            preset.Customize.Nose.Apply ||
            preset.Customize.Jaw.Apply ||
            preset.Customize.Mouth.Apply ||
            preset.Customize.Lipstick.Apply ||
            preset.Customize.LipColor.Apply ||
            preset.Customize.MuscleMass.Apply ||
            preset.Customize.TailShape.Apply ||
            preset.Customize.BustSize.Apply ||
            preset.Customize.FacePaint.Apply ||
            preset.Customize.FacePaintReversed.Apply ||
            preset.Customize.FacePaintColor.Apply);

    private unsafe bool ApplyCustomizeData(GameCustomizeData* customizeData, GlamourerPreset preset, bool forceAppearance = false)
    {
        if (customizeData == null || (!forceAppearance && !Configuration.SuperKrangleApplyAppearance))
            return false;

        ref var customize = ref *customizeData;
        var c = preset.Customize;
        byte? targetRace = null;
        if (c.Clan.Apply && TryGetRaceForClan(c.Clan.Value, out var clanRace))
        {
            targetRace = clanRace;
        }
        else if (c.Race.Apply)
        {
            targetRace = c.Race.Value;
        }

        if (targetRace.HasValue) customize.Race = targetRace.Value;
        if (c.Gender.Apply) customize.Sex = c.Gender.Value;
        if (c.BodyType.Apply) customize.BodyType = c.BodyType.Value;
        if (c.Height.Apply) customize.Height = c.Height.Value;
        if (c.Clan.Apply) customize.Tribe = c.Clan.Value;
        if (c.Face.Apply) customize.Face = c.Face.Value;
        if (c.Hairstyle.Apply) customize.Hairstyle = c.Hairstyle.Value;
        if (c.Highlights.Apply) customize.Highlights = c.Highlights.Value != 0;
        if (c.SkinColor.Apply) customize.SkinColor = c.SkinColor.Value;
        if (c.EyeColorRight.Apply) customize.EyeColorRight = c.EyeColorRight.Value;
        if (c.HairColor.Apply) customize.HairColor = c.HairColor.Value;
        if (c.HighlightsColor.Apply) customize.HighlightsColor = c.HighlightsColor.Value;
        if (c.FacialFeature1.Apply) customize.FacialFeature1 = c.FacialFeature1.Value != 0;
        if (c.FacialFeature2.Apply) customize.FacialFeature2 = c.FacialFeature2.Value != 0;
        if (c.FacialFeature3.Apply) customize.FacialFeature3 = c.FacialFeature3.Value != 0;
        if (c.FacialFeature4.Apply) customize.FacialFeature4 = c.FacialFeature4.Value != 0;
        if (c.FacialFeature5.Apply) customize.FacialFeature5 = c.FacialFeature5.Value != 0;
        if (c.FacialFeature6.Apply) customize.FacialFeature6 = c.FacialFeature6.Value != 0;
        if (c.FacialFeature7.Apply) customize.FacialFeature7 = c.FacialFeature7.Value != 0;
        if (c.LegacyTattoo.Apply) customize.LegacyTattoo = c.LegacyTattoo.Value != 0;
        if (c.TattooColor.Apply) customize.TattooColor = c.TattooColor.Value;
        if (c.Eyebrows.Apply) customize.Eyebrows = c.Eyebrows.Value;
        if (c.EyeColorLeft.Apply) customize.EyeColorLeft = c.EyeColorLeft.Value;
        if (c.EyeShape.Apply) customize.EyeShape = c.EyeShape.Value;
        if (c.SmallIris.Apply) customize.SmallIris = c.SmallIris.Value != 0;
        if (c.Nose.Apply) customize.Nose = c.Nose.Value;
        if (c.Jaw.Apply) customize.Jaw = c.Jaw.Value;
        if (c.Mouth.Apply) customize.Mouth = c.Mouth.Value;
        if (c.Lipstick.Apply) customize.Lipstick = c.Lipstick.Value != 0;
        if (c.LipColor.Apply) customize.LipColorFurPattern = c.LipColor.Value;
        if (c.MuscleMass.Apply) customize.MuscleMass = c.MuscleMass.Value;
        if (c.TailShape.Apply) customize.TailShape = c.TailShape.Value;
        if (c.BustSize.Apply) customize.BustSize = c.BustSize.Value;
        if (c.FacePaint.Apply) customize.FacePaint = c.FacePaint.Value;
        if (c.FacePaintReversed.Apply) customize.FacePaintReversed = c.FacePaintReversed.Value != 0;
        if (c.FacePaintColor.Apply) customize.FacePaintColor = c.FacePaintColor.Value;

        return true;
    }

    private unsafe bool ApplyGlamourerPreset(CharacterStruct* character, GlamourerPreset preset, byte* customizePtr, bool logDetails = true, bool forceAppearance = false)
    {
        // ── Apply customize data (26 bytes) ──
        if (character == null || customizePtr == null || (!forceAppearance && !Configuration.SuperKrangleApplyAppearance))
            return false;

        var customizeRequested = PresetRequestsCustomize(preset, forceAppearance);
        var applied = customizeRequested && ApplyCustomizeData((GameCustomizeData*)customizePtr, preset, forceAppearance);

        if (logDetails && preset.Customize.ModelId != 0 && !hasLoggedAppearanceScan)
            Log.Warning($"[Krangler] Preset '{preset.Name}' requests CharacterBase.Create modelId override {preset.Customize.ModelId}; post-create apply cannot change it.");

        // ── Equipment modification DISABLED ──
        // CRASH FIX: Glamourer's packed ItemId (ulong) is NOT the raw EquipmentModelId format.
        // Glamourer encodes: game item row ID + model set + variant + stain + flags into a single ulong.
        // The game's EquipmentModelId at DrawData+0x1D0 is: ushort SetId + byte Variant + byte Stain1 + byte Stain2 + padding.
        // Writing the packed ItemId directly corrupts the model data and crashes on redraw.
        // TODO: Decode Glamourer ItemId → extract (SetId, Variant, Stain) → write correct EquipmentModelId.
        // Alternative: Use LoadEquipment() with properly decoded model IDs.
        return applied;
    }

    // ─── Super Krangle Master 4000 Methods ─────────────────────────────────────

    private static bool TryGetRaceForClan(byte clan, out byte race)
    {
        race = clan switch
        {
            1 or 2 => 1,
            3 or 4 => 2,
            5 or 6 => 3,
            7 or 8 => 4,
            9 or 10 => 5,
            11 or 12 => 6,
            13 or 14 => 7,
            15 or 16 => 8,
            _ => 0,
        };

        return race != 0;
    }

    /// <summary>
    /// Get special NPC appearance data for Super Krangle Master 4000 mode.
    /// Returns (race, tribe, gender) for iconic NPCs like Gaius, Nero, Louisoix, etc.
    /// </summary>
    private unsafe int ApplyGlamourerEquipment(CharacterStruct* character, GlamourerPreset preset, bool exactNpcReplacement = false, bool logDetails = true, bool forceAllSlots = false)
    {
        if (character == null || preset.Equipment.Count == 0)
            return 0;

        if (logDetails && !hasLoggedAppearanceScan)
            Log.Information($"[Krangler] Processing preset '{preset.Name}' with {preset.Equipment.Count} equipment entries");

        int appliedCount;
        fixed (EquipmentModelId* equipmentModelPtr = &character->DrawData.EquipmentModelIds[0])
        {
            appliedCount = ApplyEquipmentData(equipmentModelPtr, preset, character, exactNpcReplacement, logDetails, forceAllSlots);
        }

        if (logDetails && !hasLoggedAppearanceScan && appliedCount > 0)
            Log.Information($"[Krangler] Applied {appliedCount} Super Krangle equipment slot(s) from preset '{preset.Name}'");

        return appliedCount;
    }

    private unsafe int ApplyEquipmentData(EquipmentModelId* equipmentModelPtr, GlamourerPreset preset, CharacterStruct* character, bool exactNpcReplacement = false, bool logDetails = true, bool forceAllSlots = false)
    {
        if (equipmentModelPtr == null || preset.Equipment.Count == 0)
            return 0;

        var appliedCount = 0;
        var preserveExistingCoreArmor = !forceAllSlots && !exactNpcReplacement && ShouldPreserveExistingCoreArmor(preset);

        if (preserveExistingCoreArmor && logDetails && !hasLoggedAppearanceScan)
            Log.Information($"[Krangler] Preset '{preset.Name}' uses deprecated special-NPC core armor encoding. Preserving existing Head/Body/Hands/Legs/Feet while still applying accessories, weapons, bonus items, and meta state.");

        foreach (var (slotName, slotData) in preset.Equipment)
        {
            var isCoreArmorSlot = IsCoreArmorSlot(slotName);
            if (!slotData.Apply || (!forceAllSlots && !ShouldApplyEquipmentSlot(slotName)) || (preserveExistingCoreArmor && isCoreArmorSlot))
            {
                if (logDetails && !hasLoggedAppearanceScan && !slotData.Apply)
                    Log.Information($"[Krangler] Skipping preset slot '{slotName}' - Apply flag is false");
                if (logDetails && !hasLoggedAppearanceScan && !forceAllSlots && !ShouldApplyEquipmentSlot(slotName))
                    Log.Information($"[Krangler] Skipping preset slot '{slotName}' - ShouldApplyEquipmentSlot returned false");
                if (logDetails && !hasLoggedAppearanceScan && preserveExistingCoreArmor && isCoreArmorSlot)
                    Log.Information($"[Krangler] Skipping preset slot '{slotName}' for '{preset.Name}' - preserving existing equipped armor for deprecated special-NPC armor path");
                continue;
            }

            var slotIndex = GetEquipmentSlotIndex(slotName);
            if (!slotIndex.HasValue)
            {
                if (logDetails && !hasLoggedAppearanceScan)
                    Log.Warning($"[Krangler] Could not get slot index for '{slotName}'");
                continue;
            }

            if (!TryDecodeGlamourerItemId(slotName, slotData.ItemId, out var setId, out var variant, out var isEmpty))
            {
                if (logDetails && !hasLoggedAppearanceScan)
                    Log.Warning($"[Krangler] Could not decode preset slot '{slotName}' item id {slotData.ItemId}");
                continue;
            }

            var modelId = equipmentModelPtr + slotIndex.Value;
            var oldSetId = modelId->Id;
            var oldVariant = modelId->Variant;
            modelId->Id = setId;
            modelId->Variant = variant;

            if (isEmpty)
            {
                modelId->Stain0 = 0;
                modelId->Stain1 = 0;

                if (logDetails && !hasLoggedAppearanceScan)
                    Log.Information($"[Krangler] Preset slot '{slotName}' uses empty-slot marker itemId={slotData.ItemId}");
            }
            else if (slotData.ApplyStain)
            {
                modelId->Stain0 = (byte)Math.Min(slotData.Stain, byte.MaxValue);
                modelId->Stain1 = (byte)Math.Min(slotData.Stain2, byte.MaxValue);
            }

            if (character != null)
                ApplyNativeEquipmentSlot(character, slotIndex.Value, modelId);

            if (logDetails && !hasLoggedAppearanceScan)
                Log.Information($"[Krangler] Applied preset slot '{slotName}': itemId={slotData.ItemId}, setId={setId}, variant={variant}, stains={modelId->Stain0}/{modelId->Stain1} (was setId={oldSetId}, variant={oldVariant})");

            appliedCount++;
        }

        return appliedCount;
    }

    private unsafe int ApplyGlamourerWeapons(CharacterStruct* character, GlamourerPreset preset, bool logDetails = true, bool forceWeapons = false)
    {
        if (character == null || preset.Equipment.Count == 0 || (!forceWeapons && !Configuration.SuperKrangleApplyWeapons))
            return 0;

        var appliedCount = 0;

        if (preset.Equipment.TryGetValue("MainHand", out var mainHandData) && mainHandData.Apply)
        {
            if (TryDecodeGlamourerWeaponItemId(mainHandData.ItemId, out var mainHandWeapon, out var mainHandEmpty))
            {
                if (mainHandEmpty)
                {
                    mainHandWeapon.Stain0 = 0;
                    mainHandWeapon.Stain1 = 0;
                }
                else if (mainHandData.ApplyStain)
                {
                    mainHandWeapon.Stain0 = (byte)Math.Min(mainHandData.Stain, byte.MaxValue);
                    mainHandWeapon.Stain1 = (byte)Math.Min(mainHandData.Stain2, byte.MaxValue);
                }

                character->DrawData.LoadWeapon(DrawDataContainerStruct.WeaponSlot.MainHand, mainHandWeapon, 1, 0, 1, 0, true);
                appliedCount++;

                if (logDetails && !hasLoggedAppearanceScan)
                    Log.Information($"[Krangler] Applied preset weapon 'MainHand': itemId={mainHandData.ItemId}, id={mainHandWeapon.Id}, type={mainHandWeapon.Type}, variant={mainHandWeapon.Variant}, stains={mainHandWeapon.Stain0}/{mainHandWeapon.Stain1}");
            }
            else if (logDetails && !hasLoggedAppearanceScan)
            {
                Log.Warning($"[Krangler] Could not decode preset weapon 'MainHand' item id {mainHandData.ItemId}");
            }
        }

        if (preset.Equipment.TryGetValue("OffHand", out var offHandData) && offHandData.Apply)
        {
            if (TryDecodeGlamourerWeaponItemId(offHandData.ItemId, out var offHandWeapon, out var offHandEmpty))
            {
                if (offHandEmpty)
                {
                    offHandWeapon.Stain0 = 0;
                    offHandWeapon.Stain1 = 0;
                }
                else if (offHandData.ApplyStain)
                {
                    offHandWeapon.Stain0 = (byte)Math.Min(offHandData.Stain, byte.MaxValue);
                    offHandWeapon.Stain1 = (byte)Math.Min(offHandData.Stain2, byte.MaxValue);
                }

                character->DrawData.LoadWeapon(DrawDataContainerStruct.WeaponSlot.OffHand, offHandWeapon, 1, 0, 1, 0, true);
                appliedCount++;

                if (logDetails && !hasLoggedAppearanceScan)
                    Log.Information($"[Krangler] Applied preset weapon 'OffHand': itemId={offHandData.ItemId}, id={offHandWeapon.Id}, type={offHandWeapon.Type}, variant={offHandWeapon.Variant}, stains={offHandWeapon.Stain0}/{offHandWeapon.Stain1}");
            }
            else if (logDetails && !hasLoggedAppearanceScan)
            {
                Log.Warning($"[Krangler] Could not decode preset weapon 'OffHand' item id {offHandData.ItemId}");
            }
        }

        return appliedCount;
    }

    private unsafe bool ApplyGlamourerBonusItems(CharacterStruct* character, GlamourerPreset preset, bool logDetails = true)
    {
        if (character == null || preset.Bonus.Count == 0)
            return false;

        var applied = false;

        if (preset.Bonus.TryGetValue("Glasses", out var glassesData) && glassesData.Apply)
        {
            character->DrawData.SetGlasses(0, (ushort)Math.Min(glassesData.BonusId, ushort.MaxValue));
            applied = true;
        }

        if (preset.Bonus.TryGetValue("Glasses1", out var secondGlassesData) && secondGlassesData.Apply)
        {
            character->DrawData.SetGlasses(1, (ushort)Math.Min(secondGlassesData.BonusId, ushort.MaxValue));
            applied = true;
        }

        if (logDetails && !hasLoggedAppearanceScan && applied)
            Log.Information($"[Krangler] Applied preset bonus items '{preset.Name}': glasses={character->DrawData.GlassesIds[0]}");

        return applied;
    }

    private unsafe bool ApplyGlamourerMetaState(CharacterStruct* character, GlamourerPreset preset, bool logDetails = true)
    {
        if (character == null || preset.Equipment.Count == 0)
            return false;

        var applied = false;

        if (preset.Equipment.TryGetValue("Hat", out var hatData) && hatData.Apply)
        {
            character->DrawData.HideHeadgear(0, !hatData.Show);
            applied = true;
        }

        if (preset.Equipment.TryGetValue("Weapon", out var weaponData) && weaponData.Apply)
        {
            character->DrawData.HideWeapons(!weaponData.Show);
            applied = true;
        }

        if (preset.Equipment.TryGetValue("Visor", out var visorData) && visorData.Apply)
        {
            character->DrawData.SetVisor(visorData.IsToggled);
            applied = true;
        }

        if (logDetails && !hasLoggedAppearanceScan && applied)
            Log.Information($"[Krangler] Applied preset meta state '{preset.Name}': hatHidden={character->DrawData.IsHatHidden}, weaponHidden={character->DrawData.IsWeaponHidden}, visorToggled={character->DrawData.IsVisorToggled}");

        return applied;
    }

    private unsafe bool RefreshCharacterCustomize(CharacterStruct* character)
    {
        var characterBase = GetCharacterBaseDrawObject(character);
        if (characterBase == null || characterBase->GetModelType() != CharacterBaseStruct.ModelType.Human)
            return false;

        var human = (HumanStruct*)characterBase;
        var drawData = new HumanDrawData
        {
            CustomizeData = character->DrawData.CustomizeData,
            AnimationVariant = human->AnimationVariant,
        };

        for (var i = 0; i < EquipmentSlotCount; i++)
            drawData.Equipments[i] = character->DrawData.EquipmentModelIds[i];

        drawData.Glasses[0] = new EquipmentModelId { Id = character->DrawData.GlassesIds[0] };
        drawData.Glasses[1] = new EquipmentModelId { Id = character->DrawData.GlassesIds[1] };

        return human->UpdateDrawData(&drawData, true);
    }

    private unsafe void RefreshCharacterEquipment(CharacterStruct* character)
    {
        if (character == null)
            return;

        fixed (EquipmentModelId* equipmentModelPtr = &character->DrawData.EquipmentModelIds[0])
        {
            for (var i = 0; i < EquipmentSlotCount; i++)
                ApplyNativeEquipmentSlot(character, i, equipmentModelPtr + i);
        }
    }

    private unsafe CharacterBaseStruct* GetCharacterBaseDrawObject(CharacterStruct* character)
    {
        if (character == null || character->DrawObject == null)
            return null;

        return character->DrawObject->GetObjectType() == FFXIVClientStructs.FFXIV.Client.Graphics.Scene.ObjectType.CharacterBase
            ? (CharacterBaseStruct*)character->DrawObject
            : null;
    }

    private bool ShouldProcessAppearanceTarget(bool isPlayer, bool isNpc, bool isChocobo, bool isMinion)
    {
        if (SuperKrangleMaster4000_Active)
            return isPlayer;

        return isPlayer && (Configuration.KrangleRaces || Configuration.KrangleGenders || Configuration.KrangleAppearance);
    }

    private static bool IsAppearanceNpc(ObjectKind objectKind, bool isChocobo, bool isMinion)
    {
        if (isChocobo || isMinion)
            return false;

        return objectKind == ObjectKind.BattleNpc || objectKind == ObjectKind.EventNpc;
    }

    private unsafe bool SupportsHumanCustomize(CharacterStruct* character)
    {
        var characterBase = GetCharacterBaseDrawObject(character);
        return characterBase != null && characterBase->GetModelType() == CharacterBaseStruct.ModelType.Human;
    }

    private static string GetAppearanceTargetLabel(bool isNpc, bool isChocobo, bool isMinion)
        => isNpc ? "npc" : isChocobo ? "chocobo" : isMinion ? "minion" : "player";

    private unsafe void ApplyNativeEquipmentSlot(CharacterStruct* character, int slotIndex, EquipmentModelId* modelId)
    {
        if (character == null || modelId == null)
            return;

        character->DrawData.LoadEquipment((DrawDataContainerStruct.EquipmentSlot)slotIndex, modelId, true);

        var characterBase = GetCharacterBaseDrawObject(character);
        if (characterBase == null)
            return;

        characterBase->SetEquipmentSlotModel((uint)slotIndex, modelId);
    }

    private static unsafe void RestoreDrawMetaState(CharacterStruct* character, OriginalAppearanceData originalData)
    {
        if (character == null)
            return;

        character->DrawData.HideHeadgear(0, originalData.IsHatHidden);
        character->DrawData.HideWeapons(originalData.IsWeaponHidden);
        character->DrawData.SetVisor(originalData.IsVisorToggled);
        character->DrawData.HideVieraEars(originalData.VieraEarsHidden);
    }

    private static unsafe void RestoreModelContainerIds(CharacterStruct* character, OriginalAppearanceData originalData)
    {
        if (character == null || !originalData.HasModelContainerIds)
            return;

        character->ModelContainer.ModelCharaId = originalData.ModelCharaId;
        character->ModelContainer.ModelCharaId_2 = originalData.ModelCharaId2;
        character->ModelContainer.ModelSkeletonId = originalData.ModelSkeletonId;
        character->ModelContainer.ModelSkeletonId_2 = originalData.ModelSkeletonId2;
    }

    private static unsafe void RestoreWeaponData(CharacterStruct* character, OriginalAppearanceData originalData)
    {
        if (character == null)
            return;

        character->DrawData.LoadWeapon(DrawDataContainerStruct.WeaponSlot.MainHand, originalData.MainHandWeapon, 1, 0, 1, 0, true);
        character->DrawData.LoadWeapon(DrawDataContainerStruct.WeaponSlot.OffHand, originalData.OffHandWeapon, 1, 0, 1, 0, true);
    }

    private static unsafe void RestoreBonusItems(CharacterStruct* character, OriginalAppearanceData originalData)
    {
        if (character == null)
            return;

        character->DrawData.SetGlasses(0, originalData.Glasses0);
        character->DrawData.SetGlasses(1, originalData.Glasses1);
    }

    private bool ShouldApplyEquipmentSlot(string slotName)
        => slotName.ToLowerInvariant() switch
        {
            "head" => Configuration.SuperKrangleApplyHead,
            "body" => Configuration.SuperKrangleApplyBody,
            "hands" => Configuration.SuperKrangleApplyHands,
            "legs" => Configuration.SuperKrangleApplyLegs,
            "feet" => Configuration.SuperKrangleApplyFeet,
            "ears" => Configuration.SuperKrangleApplyAccessories,
            "neck" => Configuration.SuperKrangleApplyAccessories,
            "wrists" => Configuration.SuperKrangleApplyAccessories,
            "rfinger" => Configuration.SuperKrangleApplyAccessories,
            "lfinger" => Configuration.SuperKrangleApplyAccessories,
            _ => false,
        };

    private static bool IsCoreArmorSlot(string slotName)
        => slotName.ToLowerInvariant() switch
        {
            "head" => true,
            "body" => true,
            "hands" => true,
            "legs" => true,
            "feet" => true,
            _ => false,
        };

    private static bool ShouldPreserveExistingCoreArmor(GlamourerPreset preset)
    {
        foreach (var (slotName, slotData) in preset.Equipment)
        {
            if (!slotData.Apply || !IsCoreArmorSlot(slotName))
                continue;

            if (UsesDeprecatedSpecialNpcArmorEncoding(slotName, slotData.ItemId))
                return true;
        }

        return false;
    }

    private static bool UsesDeprecatedSpecialNpcArmorEncoding(string slotName, ulong itemId)
    {
        if (!IsCoreArmorSlot(slotName))
            return false;

        if (TryDecodePackedArmorItemId(itemId, out _, out _))
            return true;

        return TryDecodeSpecialArmorItemId(slotName, itemId, out _, out _, out _);
    }

    private static int? GetEquipmentSlotIndex(string slotName)
        => slotName.ToLowerInvariant() switch
        {
            "head" => 0,
            "body" => 1,
            "hands" => 2,
            "legs" => 3,
            "feet" => 4,
            "ears" => 5,
            "neck" => 6,
            "wrists" => 7,
            "rfinger" => 8,
            "lfinger" => 9,
            _ => null,
        };

    private bool TryDecodeGlamourerItemId(string slotName, ulong itemId, out ushort setId, out byte variant, out bool isEmpty)
    {
        setId = 0;
        variant = 0;
        isEmpty = false;

        if (itemId == 0)
            return false;

        if (TryDecodePackedArmorItemId(itemId, out setId, out variant))
            return true;

        if (TryDecodeSpecialArmorItemId(slotName, itemId, out setId, out variant, out isEmpty))
            return true;

        if (itemId >= (ulong)(uint.MaxValue - 512))
        {
            isEmpty = true;
            return true;
        }

        return TryDecodeStandardItemId(itemId, out setId, out variant);
    }

    private bool TryDecodeGlamourerWeaponItemId(ulong itemId, out WeaponModelId weaponModelId, out bool isEmpty)
    {
        weaponModelId = default;
        isEmpty = false;

        if (itemId >= (ulong)(uint.MaxValue - 512))
        {
            isEmpty = true;
            return true;
        }

        if (itemId <= uint.MaxValue)
            return TryDecodeStandardWeaponItemId(itemId, out weaponModelId, out isEmpty);

        return TryDecodePackedWeaponItemId(itemId, out weaponModelId, out isEmpty);
    }

    private static bool TryDecodePackedArmorItemId(ulong itemId, out ushort setId, out byte variant)
    {
        setId = 0;
        variant = 0;

        if (itemId <= uint.MaxValue)
            return false;

        setId = (ushort)((itemId >> 32) & 0xFFFF);
        variant = (byte)((itemId >> 48) & 0xFF);
        return setId != 0;
    }

    private static bool TryDecodePackedWeaponItemId(ulong itemId, out WeaponModelId weaponModelId, out bool isEmpty)
    {
        weaponModelId = default;
        isEmpty = false;

        if ((itemId >> 48) == 0)
            return false;

        weaponModelId.Id = (ushort)(itemId & 0xFFFF);
        weaponModelId.Type = (ushort)((itemId >> 16) & 0xFFFF);
        weaponModelId.Variant = (ushort)((itemId >> 32) & 0xFFFF);
        isEmpty = weaponModelId.Id == 0 && weaponModelId.Type == 0 && weaponModelId.Variant == 0;
        return true;
    }

    private bool TryDecodeStandardItemId(ulong itemId, out ushort setId, out byte variant)
    {
        setId = 0;
        variant = 0;

        if (itemId > uint.MaxValue)
            return false;

        var itemSheet = DataManager.GetExcelSheet<Item>();
        if (itemSheet == null || !itemSheet.TryGetRow((uint)itemId, out var item))
            return false;

        var modelMain = (ulong)item.ModelMain;
        setId = (ushort)(modelMain & 0xFFFF);
        variant = (byte)((modelMain >> 32) & 0xFF);
        return setId != 0;
    }

    private bool TryDecodeStandardWeaponItemId(ulong itemId, out WeaponModelId weaponModelId, out bool isEmpty)
    {
        weaponModelId = default;
        isEmpty = false;

        var itemSheet = DataManager.GetExcelSheet<Item>();
        if (itemSheet == null || !itemSheet.TryGetRow((uint)itemId, out var item))
            return false;

        var modelMain = (ulong)item.ModelMain;
        weaponModelId.Id = (ushort)(modelMain & 0xFFFF);
        weaponModelId.Type = (ushort)((modelMain >> 16) & 0xFFFF);
        weaponModelId.Variant = (ushort)((modelMain >> 32) & 0xFFFF);
        isEmpty = weaponModelId.Id == 0 && weaponModelId.Type == 0 && weaponModelId.Variant == 0;
        return weaponModelId.Id != 0 || weaponModelId.Type != 0 || weaponModelId.Variant != 0;
    }

    private static bool TryDecodeSpecialArmorItemId(string slotName, ulong itemId, out ushort setId, out byte variant, out bool isEmpty)
    {
        setId = 0;
        variant = 0;
        isEmpty = false;

        if (!TryGetSpecialArmorSlotValue(slotName, out var slotValue))
            return false;

        var nothingId = (ulong)(uint.MaxValue - 128u - slotValue);
        if (itemId == nothingId)
        {
            isEmpty = true;
            return true;
        }

        var smallClothesId = (ulong)(uint.MaxValue - 256u - slotValue);
        if (itemId == smallClothesId)
        {
            setId = SmallClothesNpcModelId;
            variant = 1;
            return true;
        }

        return false;
    }

    private static bool TryGetSpecialArmorSlotValue(string slotName, out uint slotValue)
    {
        switch (slotName.ToLowerInvariant())
        {
            case "head":
                slotValue = 3;
                return true;
            case "body":
                slotValue = 4;
                return true;
            case "hands":
                slotValue = 5;
                return true;
            case "legs":
                slotValue = 7;
                return true;
            case "feet":
                slotValue = 8;
                return true;
            case "ears":
                slotValue = 9;
                return true;
            case "neck":
                slotValue = 10;
                return true;
            case "wrists":
                slotValue = 11;
                return true;
            case "rfinger":
            case "lfinger":
                slotValue = 12;
                return true;
            default:
                slotValue = 0;
                return false;
        }
    }

    private static bool TryGetPackedEquipmentSlotId(string slotName, out byte slotId)
    {
        slotId = slotName.ToLowerInvariant() switch
        {
            "head" => 1,
            "body" => 2,
            "hands" => 3,
            "legs" => 4,
            "feet" => 5,
            _ => 0,
        };

        return slotId != 0;
    }

    private string GetDateBasedForcedPreset()
    {
        var today = DateTime.Today;
        var year = today.Year;
        var month = today.Month;
        var day = today.Day;

        // SPECIAL EVENT: March 31 through April 2
        if ((month == 3 && day >= 31) || (month == 4 && day <= 2))
            return "Wuk Lamat";

        // Fanfest 2024 dates (when Wuk Lamat was revealed)
        if (year == 2024 && month == 3 && (day == 15 || day == 16 || day == 17))
            return "Wuk Lamat";

        // Dawntrail launch date (June 27, 2024)
        if (year == 2024 && month == 6 && day == 27)
            return "Wuk Lamat";

        // Wuk Lamat's birthday (if known) - using character reveal anniversary
        if (month == 3 && day == 15) // Annual anniversary of Fanfest reveal
            return "Wuk Lamat";

        // Special events - can add more dates as needed
        // Example: New Year's celebration
        if (month == 1 && day == 1)
            return "Wuk Lamat";

        return string.Empty;
    }

    private string GetActiveDateBasedForcedPreset(bool isChocobo = false, bool isMinion = false)
    {
        if (!IsSuperKrangleEventActive || isChocobo || isMinion)
            return string.Empty;

        return GetDateBasedForcedPreset();
    }

    private string ResolveSuperKrangleSelection(string playerName, bool isNpc = false, bool isChocobo = false, bool isMinion = false)
    {
        // SPECIAL EVENT: Force Wuk Lamat preset on specific dates
        var forcedPreset = GetActiveDateBasedForcedPreset(isChocobo, isMinion);
        if (!string.IsNullOrEmpty(forcedPreset))
        {
            if (!hasLoggedAppearanceScan)
                Log.Information($"[Krangler] Date-based override forcing preset: {forcedPreset}");
            return forcedPreset;
        }

        if (isNpc)
        {
            return string.IsNullOrWhiteSpace(Configuration.SuperKrangleNpcSelection)
                ? "Random"
                : Configuration.SuperKrangleNpcSelection;
        }

        if (isChocobo)
        {
            return string.IsNullOrWhiteSpace(Configuration.SuperKrangleChocoboSelection)
                ? "Random"
                : Configuration.SuperKrangleChocoboSelection;
        }

        if (isMinion)
        {
            return string.IsNullOrWhiteSpace(Configuration.SuperKrangleMinionSelection)
                ? "Random"
                : Configuration.SuperKrangleMinionSelection;
        }

        // Player: Check party slot overrides first
        var defaultSelection = string.IsNullOrWhiteSpace(Configuration.SuperKrangleSelection)
            ? "Random"
            : Configuration.SuperKrangleSelection;

        var selectionIndex = GetPartySlotSelectionIndex(playerName);
        if (!selectionIndex.HasValue ||
            selectionIndex.Value < 0 ||
            selectionIndex.Value >= Configuration.SuperKranglePartySlotSelections.Count)
        {
            return defaultSelection;
        }

        var slotSelection = Configuration.SuperKranglePartySlotSelections[selectionIndex.Value];
        if (string.IsNullOrWhiteSpace(slotSelection) ||
            string.Equals(slotSelection, "Use Global", StringComparison.OrdinalIgnoreCase))
        {
            return defaultSelection;
        }

        return slotSelection;
    }

    private int? GetPartySlotSelectionIndex(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return null;

        var localName = ObjectTable.LocalPlayer?.Name.ToString();
        if (!string.IsNullOrWhiteSpace(localName) &&
            string.Equals(localName, playerName, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var maxSlots = Math.Min(Configuration.SuperKranglePartySlotSelections.Count, PartyList.Length);
        for (var i = 1; i < maxSlots; i++)
        {
            var memberName = PartyList[i]?.Name.ToString();
            if (!string.IsNullOrWhiteSpace(memberName) &&
                string.Equals(memberName, playerName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return null;
    }

    private static (byte race, byte tribe, byte gender) GetSuperKrangleAppearance(string playerName)
    {
        var hash = GetStableHash(playerName + "_super");
        var rng = new Random(hash);

        // Special NPC appearances - iconic characters
        var npcAppearances = new[]
        {
            ((byte)1, (byte)1, (byte)0),   // Hyur Midlander Male (Gaius)
            ((byte)1, (byte)1, (byte)1),   // Hyur Midlander Female (Minfilia)
            ((byte)1, (byte)2, (byte)0),   // Hyur Highlander Male (Raubahn)
            ((byte)1, (byte)1, (byte)0),   // Hyur Midlander Male (Nero)
            ((byte)2, (byte)3, (byte)0),   // Elezen Wildwood Male (Louisoix)
            ((byte)2, (byte)4, (byte)1),   // Elezen Duskwight Female (Urianger)
            ((byte)6, (byte)11, (byte)0),  // Au Ra Raen Male (Hien)
            ((byte)6, (byte)12, (byte)1),  // Au Ra Xaela Female (Lyse)
            ((byte)7, (byte)13, (byte)0),  // Hrothgar Helions Male (Varis)
            ((byte)8, (byte)15, (byte)1),  // Viera Rava Female (Y'shtola)
        };

        return npcAppearances[rng.Next(npcAppearances.Length)];
    }

    /// <summary>
    /// Get full customize data for Super Krangle Master 4000 mode.
    /// Returns complete 26-byte customize array for iconic NPCs.
    /// </summary>
    private static Dictionary<int, byte> GetSuperKrangleFullAppearance(string playerName)
    {
        var hash = GetStableHash(playerName + "_super_full");
        var rng = new Random(hash);

        // Select a base NPC template
        var templates = new[]
        {
            // Gaius van Baelsrar - Hyur Midlander Male
            new Dictionary<int, byte>
            {
                {0, 1}, {1, 0}, {2, 1}, {3, 50}, {4, 1}, {5, 4}, {6, 4}, {7, 0},
                {8, 8}, {9, 24}, {10, 24}, {11, 24}, {12, 0}, {13, 0}, {14, 0},
                {15, 1}, {16, 0}, {17, 1}, {18, 1}, {19, 1}, {20, 1}, {21, 0},
                {22, 0}, {23, 0}, {24, 0}, {25, 50}
            },
            // Nero tol Scaeva - Hyur Midlander Male
            new Dictionary<int, byte>
            {
                {0, 1}, {1, 0}, {2, 1}, {3, 75}, {4, 1}, {5, 2}, {6, 11}, {7, 0},
                {8, 12}, {9, 120}, {10, 120}, {11, 120}, {12, 0}, {13, 0}, {14, 0},
                {15, 2}, {16, 0}, {17, 2}, {18, 2}, {19, 2}, {20, 2}, {21, 0},
                {22, 0}, {23, 0}, {24, 0}, {25, 50}
            },
            // Louisoix Leveilleur - Elezen Wildwood Male
            new Dictionary<int, byte>
            {
                {0, 2}, {1, 0}, {2, 1}, {3, 60}, {4, 3}, {5, 6}, {6, 1}, {7, 0},
                {8, 95}, {9, 180}, {10, 180}, {11, 180}, {12, 0}, {13, 0}, {14, 0},
                {15, 3}, {16, 0}, {17, 3}, {18, 3}, {19, 3}, {20, 3}, {21, 0},
                {22, 0}, {23, 0}, {24, 0}, {25, 50}
            },
            // Y'shtola Rhul - Viera Rava Female
            new Dictionary<int, byte>
            {
                {0, 8}, {1, 1}, {2, 1}, {3, 25}, {4, 15}, {5, 3}, {6, 1}, {7, 0},
                {8, 140}, {9, 160}, {10, 160}, {11, 160}, {12, 0}, {13, 0}, {14, 0},
                {15, 4}, {16, 0}, {17, 4}, {18, 4}, {19, 4}, {20, 4}, {21, 0},
                {22, 0}, {23, 0}, {24, 0}, {25, 25}
            },
            // Minfilia Warde - Hyur Midlander Female
            new Dictionary<int, byte>
            {
                {0, 1}, {1, 1}, {2, 1}, {3, 30}, {4, 1}, {5, 1}, {6, 2}, {7, 0},
                {8, 20}, {9, 90}, {10, 90}, {11, 90}, {12, 0}, {13, 0}, {14, 0},
                {15, 5}, {16, 0}, {17, 5}, {18, 5}, {19, 5}, {20, 5}, {21, 0},
                {22, 0}, {23, 0}, {24, 0}, {25, 30}
            }
        };

        var baseTemplate = templates[rng.Next(templates.Length)];
        
        // Add some randomization to make it interesting
        var result = new Dictionary<int, byte>(baseTemplate);
        result[3] = (byte)rng.Next(20, 80); // Height variation
        result[25] = (byte)rng.Next(20, 80); // Bust variation for females
        
        return result;
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

    public void Dispose()
    {
        createCharacterBaseHook?.Disable();
        createCharacterBaseHook?.Dispose();
        ImaginaryFrenService.Dispose();
        IpcService.Dispose();
        Framework.Update -= Framework_OnUpdate;
        AddonLifecycle.UnregisterListener(OnPartyMemberListAddon);
        NamePlateGui.OnNamePlateUpdate -= OnNamePlateUpdate;
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        try 
        {
            ChatGui.ChatMessage -= OnChatMessage;
        }
        catch (Exception ex)
        {
            Log.Error($"[Krangler] Failed to unsubscribe from ChatMessage event: {ex.Message}");
        }

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();

        // Revert any appearance changes
        RevertAllAppearances();

        dtrEntry?.Remove();

        CommandManager.RemoveHandler(AliasCommandName);
        CommandManager.RemoveHandler(CommandName);

        // Clear krangled name cache
        KrangleService.ClearCache();

        Log.Information("[Krangler] Plugin unloaded!");
    }
}
