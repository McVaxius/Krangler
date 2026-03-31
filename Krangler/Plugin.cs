using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
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
    
    private const string CommandName = "/krangler";
    private const string AliasCommandName = "/kr";

    public Configuration Configuration { get; init; }
    public AppearanceService AppearanceService { get; init; }
    public GlamourerPresetService GlamourerPresetService { get; init; }

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
    private DateTime lastEventFlagReset = DateTime.MinValue;
    private const int CustomizeByteCount = 26;
    private const int EquipmentSlotByteCount = 8;
    private const int EquipmentSlotCount = 10;
    private const int EquipmentByteCount = EquipmentSlotByteCount * EquipmentSlotCount;
    private const uint InvisibilityDrawStateFlag = 0x00000002;
    private const ushort SmallClothesNpcModelId = 9903;
    private const uint PartyMemberListComponentNodeId = 12;
    private const uint PartyMemberListTextNodeBaseId = 51001;
    private const int PartyMemberListTextNodeCount = 15;

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
    }

    private readonly struct PendingRedrawEntry
    {
        public PendingRedrawEntry(nint address, bool makeVisible)
        {
            Address = address;
            MakeVisible = makeVisible;
        }

        public nint Address { get; }
        public bool MakeVisible { get; }
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
    private Hook<CreateCharacterBaseDelegate>? createCharacterBaseHook;
    private int redrawCooldownFrames = 0;
    private int currentVisiblePlayerCount;
    private bool isRevertingAppearances;
    private int partyMemberListFailedDraws;
    private bool hasShownPartyMemberListFallbackWarning;
    private const int CharacterBaseReapplyAttempts = 5;

    private unsafe delegate CharacterBaseStruct* CreateCharacterBaseDelegate(uint modelId, GameCustomizeData* customize, EquipmentModelId* equipment, byte unk);

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        AppearanceService = new AppearanceService(Log, ObjectTable, Configuration);
        GlamourerPresetService = new GlamourerPresetService(Log, PluginInterface);

        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Krangler window."
        });

        CommandManager.AddHandler(AliasCommandName, new CommandInfo(OnAliasCommand)
        {
            HelpMessage = "Krangler: /kr [on|off|debug|ws|j] to control the plugin, or /kr to open UI."
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

        if (!Configuration.Enabled)
        {
            UpdateDtrBar();
            return;
        }
        
        // Appearance krangling via direct memory (throttled to every 5 seconds)
        if (Configuration.KrangleGenders || Configuration.KrangleRaces || Configuration.KrangleAppearance || SuperKrangleMaster4000_Active)
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

    private void OnTerritoryChanged(ushort territory)
    {
        if (!Configuration.Enabled) return;
        
        Log.Information($"[Krangler] Territory changed to {territory}, re-applying krangle mode");

        RevertAllAppearances();
        
        // Force immediate scan to re-apply krangling
        lastAppearanceScan = DateTime.MinValue;
        lastPartyListScan = DateTime.MinValue;
        hasLoggedAppearanceScan = false;
        hasLoggedPartyList = false;
    }

    private unsafe CharacterBaseStruct* CreateCharacterBaseDetour(uint modelId, GameCustomizeData* customize, EquipmentModelId* equipment, byte unk)
    {
        var createModelId = modelId;
        if (Configuration.Enabled &&
            SuperKrangleMaster4000_Active &&
            !isRevertingAppearances &&
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
            !SuperKrangleMaster4000_Active ||
            isRevertingAppearances ||
            createdCharacterBase == null ||
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
            if (obj == null || obj.ObjectKind != ObjectKind.Player || obj.Address == 0)
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
            if (obj == null || obj.ObjectKind != ObjectKind.Player || obj.Address == 0)
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

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (!Configuration.Enabled || !Configuration.KrangleChat)
            return;

        try
        {
            var messageText = message.TextValue;
            var senderText = sender.TextValue;
            var garbledMessage = GenerateGarbledText(messageText.Length);
            var garbledSender = ShouldSkipSelfKrangling(senderText)
                ? GetResolvedSelfDisplayName(senderText)
                : GenerateGarbledText(senderText.Length);

            message = new SeString(new List<Payload> { new TextPayload(garbledMessage) });
            sender = new SeString(new List<Payload> { new TextPayload(garbledSender) });
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

            var name = obj.Name.ToString();
            if (string.IsNullOrEmpty(name))
                continue;

            var objectKey = obj.GameObjectId;
            if (AppearanceService.IsApplied(objectKey))
                continue;

            var isPlayer = obj.ObjectKind == ObjectKind.Player;
            var isChocobo = obj.ObjectKind == ObjectKind.BattleNpc && obj.Name.ToString().Contains("Companion", StringComparison.OrdinalIgnoreCase);
            var isMinion = obj.ObjectKind == ObjectKind.Companion;
            var isNpc = IsAppearanceNpc(obj.ObjectKind, isChocobo, isMinion);
            var targetLabel = GetAppearanceTargetLabel(isNpc, isChocobo, isMinion);

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

            if (!ShouldProcessAppearanceTarget(isPlayer, isNpc, isChocobo, isMinion))
                continue;

            try
            {
                var character = (CharacterStruct*)obj.Address;
                if (character == null)
                    continue;

                if (!SupportsHumanCustomize(character))
                {
                    if (!hasLoggedAppearanceScan)
                        Log.Warning($"[Krangler] Skipping unsupported {targetLabel} appearance target '{name}' - local appearance krangling currently requires a human CharacterBase draw object.");
                    continue;
                }

                var customizePtr = (byte*)&character->DrawData.CustomizeData;

                var (race, tribe, gender) = SuperKrangleMaster4000_Active
                    ? GetSuperKrangleAppearance(name)
                    : AppearanceService.GetRandomRaceGender(name);
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

    private unsafe void ProcessRedrawQueue()
    {
        if (redrawCooldownFrames > 0)
        {
            redrawCooldownFrames--;
            return;
        }

        if (redrawQueue.Count == 0) return;

        var pendingRedraw = redrawQueue.Dequeue();
        try
        {
            var gameObj = (GameObjectStruct*)pendingRedraw.Address;
            if (gameObj == null)
            {
                pendingRedrawAddresses.Remove(pendingRedraw.Address);
                return;
            }

            if (pendingRedraw.MakeVisible)
            {
                WriteActorVisible(gameObj);
                pendingRedrawAddresses.Remove(pendingRedraw.Address);
            }
            else
            {
                WriteActorInvisible(gameObj);
                redrawQueue.Enqueue(new PendingRedrawEntry(pendingRedraw.Address, true));
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

    private void QueuePenumbraStyleRedraw(nint address)
    {
        if (address == 0 || !pendingRedrawAddresses.Add(address))
            return;

        redrawQueue.Enqueue(new PendingRedrawEntry(address, false));
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
    }

    private static unsafe void WriteActorInvisible(GameObjectStruct* gameObj)
    {
        var renderFlags = (uint*)&gameObj->RenderFlags;
        *renderFlags |= InvisibilityDrawStateFlag;
    }

    private static unsafe void WriteActorVisible(GameObjectStruct* gameObj)
    {
        var renderFlags = (uint*)&gameObj->RenderFlags;
        *renderFlags &= ~InvisibilityDrawStateFlag;
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
                if (!originalAppearanceData.TryGetValue(obj.GameObjectId, out var originalData)) continue;

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
                    RefreshCharacterCustomize(character);
                    RefreshCharacterEquipment(character);

                    QueuePenumbraStyleRedraw(obj.Address);
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
            RefreshCharacterCustomize(character);
            RefreshCharacterEquipment(character);

            QueuePenumbraStyleRedraw(localPlayer.Address);
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

    private unsafe void SaveOriginalAppearanceIfNeeded(ulong objectKey, CharacterStruct* character)
    {
        if (character == null || originalAppearanceData.ContainsKey(objectKey))
            return;

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

        originalAppearanceData[objectKey] = originalData;
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

        var componentNode = addon->GetNodeById(PartyMemberListComponentNodeId);
        var component = componentNode != null
            ? componentNode->GetComponent()
            : null;
        if (component == null)
            return 0;

        var replacedCount = 0;
        for (var i = 0; i < PartyMemberListTextNodeCount; i++)
        {
            var nodeId = PartyMemberListTextNodeBaseId + (uint)i;
            var node = component->UldManager.SearchNodeById(nodeId);
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

        return replacedCount;
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

            var replacedCount = KranglePartyMemberList(addon);
            if (replacedCount > 0)
            {
                ResetPartyMemberListFallbackState();
                return;
            }

            if (!ShouldClosePartyMemberListFallback())
                return;

            partyMemberListFailedDraws++;
            if (partyMemberListFailedDraws < 2 || hasShownPartyMemberListFallbackWarning)
                return;

            addon->Close(true);
            hasShownPartyMemberListFallbackWarning = true;

            var message = "PartyMemberList cannot be safely krangled yet. It was closed; disable Krangle Names to view it for now.";
            ToastGui.ShowNormal(new SeString(new TextPayload(message)));
            PrintStatus(message);
            Log.Warning("[Krangler] Closed PartyMemberList after repeated failed name replacements while krangling was active.");
        }
    }

    private bool ShouldClosePartyMemberListFallback()
    {
        if (PartyList.Length > 0)
            return true;

        return !Configuration.SkipSelfKrangling;
    }

    private void ResetPartyMemberListFallbackState()
    {
        partyMemberListFailedDraws = 0;
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

        return target.ObjectKind == ObjectKind.Player
            ? GetNameReplacement(originalName)
            : originalName;
    }

    private Dictionary<string, string> BuildPlayerNameMap()
    {
        var nameMap = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < ObjectTable.Length; i++)
        {
            var obj = ObjectTable[i];
            if (obj == null || obj.ObjectKind != ObjectKind.Player)
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

    private unsafe void WalkAndReplaceTextNodes(AtkResNode* node, Dictionary<string, string> nameMap)
    {
        if (node == null || nameMap.Count == 0) return;

        // Check if this is a text node
        if (node->Type == NodeType.Text)
        {
            var textNode = (AtkTextNode*)node;
            var text = textNode->NodeText.ToString();
            if (TryBuildUpdatedText(text, nameMap, out var newText))
            {
                textNode->SetText(newText);
            }
        }

        // Recurse into component nodes (Type >= 1000 = component)
        if ((int)node->Type >= 1000)
        {
            var comp = (AtkComponentNode*)node;
            if (comp->Component != null)
            {
                var child = comp->Component->UldManager.RootNode;
                while (child != null)
                {
                    WalkAndReplaceTextNodes(child, nameMap);
                    child = child->PrevSiblingNode;
                }
            }
        }

        // Walk siblings
        var sibling = node->PrevSiblingNode;
        if (sibling != null)
            WalkAndReplaceTextNodes(sibling, nameMap);
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
    private unsafe bool ApplySuperKranglePreset(CharacterStruct* character, GlamourerPreset preset, bool logRefreshResult)
    {
        if (character == null)
            return false;

        var customizePtr = (byte*)&character->DrawData.CustomizeData;
        var appliedAppearance = ApplyGlamourerPreset(character, preset, customizePtr);
        var refreshedAppearance = appliedAppearance && RefreshCharacterCustomize(character);
        var appliedEquipment = ApplyGlamourerEquipment(character, preset);
        var appliedWeapons = ApplyGlamourerWeapons(character, preset);
        var appliedBonus = ApplyGlamourerBonusItems(character, preset);
        var appliedMetaState = ApplyGlamourerMetaState(character, preset);
        
        // Refresh equipment after applying it to ensure it's properly loaded
        if (appliedEquipment > 0)
            RefreshCharacterEquipment(character);
        
        var changed = refreshedAppearance || appliedEquipment > 0 || appliedWeapons > 0 || appliedBonus || appliedMetaState;

        if (logRefreshResult && !hasLoggedAppearanceScan && appliedAppearance)
            Log.Information($"[Krangler] Native customize refresh for preset '{preset.Name}' returned {refreshedAppearance}");

        return changed;
    }

    private unsafe bool ApplyCustomizeData(GameCustomizeData* customizeData, GlamourerPreset preset)
    {
        if (customizeData == null || !Configuration.SuperKrangleApplyAppearance)
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

    private unsafe bool ApplyGlamourerPreset(CharacterStruct* character, GlamourerPreset preset, byte* customizePtr)
    {
        // ── Apply customize data (26 bytes) ──
        if (character == null || customizePtr == null || !Configuration.SuperKrangleApplyAppearance)
            return false;

        var applied = ApplyCustomizeData((GameCustomizeData*)customizePtr, preset);

        if (preset.Customize.ModelId != 0 && !hasLoggedAppearanceScan)
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
    private unsafe int ApplyGlamourerEquipment(CharacterStruct* character, GlamourerPreset preset)
    {
        if (character == null || preset.Equipment.Count == 0)
            return 0;

        if (!hasLoggedAppearanceScan)
            Log.Information($"[Krangler] Processing preset '{preset.Name}' with {preset.Equipment.Count} equipment entries");

        int appliedCount;
        fixed (EquipmentModelId* equipmentModelPtr = &character->DrawData.EquipmentModelIds[0])
        {
            appliedCount = ApplyEquipmentData(equipmentModelPtr, preset, character);
        }

        if (!hasLoggedAppearanceScan && appliedCount > 0)
            Log.Information($"[Krangler] Applied {appliedCount} Super Krangle equipment slot(s) from preset '{preset.Name}'");

        return appliedCount;
    }

    private unsafe int ApplyEquipmentData(EquipmentModelId* equipmentModelPtr, GlamourerPreset preset, CharacterStruct* character)
    {
        if (equipmentModelPtr == null || preset.Equipment.Count == 0)
            return 0;

        var appliedCount = 0;
        var preserveExistingCoreArmor = ShouldPreserveExistingCoreArmor(preset);

        if (preserveExistingCoreArmor && !hasLoggedAppearanceScan)
            Log.Information($"[Krangler] Preset '{preset.Name}' uses deprecated special-NPC core armor encoding. Preserving existing Head/Body/Hands/Legs/Feet while still applying accessories, weapons, bonus items, and meta state.");

        foreach (var (slotName, slotData) in preset.Equipment)
        {
            var isCoreArmorSlot = IsCoreArmorSlot(slotName);
            if (!slotData.Apply || !ShouldApplyEquipmentSlot(slotName) || (preserveExistingCoreArmor && isCoreArmorSlot))
            {
                if (!hasLoggedAppearanceScan && !slotData.Apply)
                    Log.Information($"[Krangler] Skipping preset slot '{slotName}' - Apply flag is false");
                if (!hasLoggedAppearanceScan && !ShouldApplyEquipmentSlot(slotName))
                    Log.Information($"[Krangler] Skipping preset slot '{slotName}' - ShouldApplyEquipmentSlot returned false");
                if (!hasLoggedAppearanceScan && preserveExistingCoreArmor && isCoreArmorSlot)
                    Log.Information($"[Krangler] Skipping preset slot '{slotName}' for '{preset.Name}' - preserving existing equipped armor for deprecated special-NPC armor path");
                continue;
            }

            var slotIndex = GetEquipmentSlotIndex(slotName);
            if (!slotIndex.HasValue)
            {
                if (!hasLoggedAppearanceScan)
                    Log.Warning($"[Krangler] Could not get slot index for '{slotName}'");
                continue;
            }

            if (!TryDecodeGlamourerItemId(slotName, slotData.ItemId, out var setId, out var variant, out var isEmpty))
            {
                if (!hasLoggedAppearanceScan)
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

                if (!hasLoggedAppearanceScan)
                    Log.Information($"[Krangler] Preset slot '{slotName}' uses empty-slot marker itemId={slotData.ItemId}");
            }
            else if (slotData.ApplyStain)
            {
                modelId->Stain0 = (byte)Math.Min(slotData.Stain, byte.MaxValue);
                modelId->Stain1 = (byte)Math.Min(slotData.Stain2, byte.MaxValue);
            }

            if (character != null)
                ApplyNativeEquipmentSlot(character, slotIndex.Value, modelId);

            if (!hasLoggedAppearanceScan)
                Log.Information($"[Krangler] Applied preset slot '{slotName}': itemId={slotData.ItemId}, setId={setId}, variant={variant}, stains={modelId->Stain0}/{modelId->Stain1} (was setId={oldSetId}, variant={oldVariant})");

            appliedCount++;
        }

        return appliedCount;
    }

    private unsafe int ApplyGlamourerWeapons(CharacterStruct* character, GlamourerPreset preset)
    {
        if (character == null || preset.Equipment.Count == 0 || !Configuration.SuperKrangleApplyWeapons)
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

                character->DrawData.LoadWeapon(DrawDataContainerStruct.WeaponSlot.MainHand, mainHandWeapon, 1, 0, 1, 0);
                appliedCount++;

                if (!hasLoggedAppearanceScan)
                    Log.Information($"[Krangler] Applied preset weapon 'MainHand': itemId={mainHandData.ItemId}, id={mainHandWeapon.Id}, type={mainHandWeapon.Type}, variant={mainHandWeapon.Variant}, stains={mainHandWeapon.Stain0}/{mainHandWeapon.Stain1}");
            }
            else if (!hasLoggedAppearanceScan)
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

                character->DrawData.LoadWeapon(DrawDataContainerStruct.WeaponSlot.OffHand, offHandWeapon, 1, 0, 1, 0);
                appliedCount++;

                if (!hasLoggedAppearanceScan)
                    Log.Information($"[Krangler] Applied preset weapon 'OffHand': itemId={offHandData.ItemId}, id={offHandWeapon.Id}, type={offHandWeapon.Type}, variant={offHandWeapon.Variant}, stains={offHandWeapon.Stain0}/{offHandWeapon.Stain1}");
            }
            else if (!hasLoggedAppearanceScan)
            {
                Log.Warning($"[Krangler] Could not decode preset weapon 'OffHand' item id {offHandData.ItemId}");
            }
        }

        return appliedCount;
    }

    private unsafe bool ApplyGlamourerBonusItems(CharacterStruct* character, GlamourerPreset preset)
    {
        if (character == null || preset.Bonus.Count == 0)
            return false;

        var applied = false;

        if (preset.Bonus.TryGetValue("Glasses", out var glassesData) && glassesData.Apply)
        {
            character->DrawData.SetGlasses(0, (ushort)Math.Min(glassesData.BonusId, ushort.MaxValue));
            applied = true;
        }

        if (!hasLoggedAppearanceScan && applied)
            Log.Information($"[Krangler] Applied preset bonus items '{preset.Name}': glasses={character->DrawData.GlassesIds[0]}");

        return applied;
    }

    private unsafe bool ApplyGlamourerMetaState(CharacterStruct* character, GlamourerPreset preset)
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

        if (!hasLoggedAppearanceScan && applied)
            Log.Information($"[Krangler] Applied preset meta state '{preset.Name}': hatHidden={character->DrawData.IsHatHidden}, weaponHidden={character->DrawData.IsWeaponHidden}, visorToggled={character->DrawData.IsVisorToggled}");

        return applied;
    }

    private unsafe bool RefreshCharacterCustomize(CharacterStruct* character)
    {
        var characterBase = GetCharacterBaseDrawObject(character);
        if (characterBase == null || characterBase->GetModelType() != CharacterBaseStruct.ModelType.Human)
            return false;

        var human = (HumanStruct*)characterBase;
        return human->UpdateDrawData((byte*)&character->DrawData.CustomizeData, true);
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
        {
            return isPlayer ||
                   (isNpc && (Configuration.SuperKrangleNpcs || IsSuperKrangleEventActive)) ||
                   (isChocobo && Configuration.SuperKrangleChocobos) ||
                   (isMinion && Configuration.SuperKrangleMinions);
        }

        return (isPlayer && (Configuration.KrangleRaces || Configuration.KrangleGenders || Configuration.KrangleAppearance)) ||
               (isNpc && Configuration.KrangleNpcs) ||
               (isChocobo && Configuration.KrangleChocobos) ||
               (isMinion && Configuration.KrangleMinions);
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

    private static unsafe void RestoreWeaponData(CharacterStruct* character, OriginalAppearanceData originalData)
    {
        if (character == null)
            return;

        character->DrawData.LoadWeapon(DrawDataContainerStruct.WeaponSlot.MainHand, originalData.MainHandWeapon, 1, 0, 1, 0);
        character->DrawData.LoadWeapon(DrawDataContainerStruct.WeaponSlot.OffHand, originalData.OffHandWeapon, 1, 0, 1, 0);
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
            ((byte)4, (byte)7, (byte)0),   // Roegadyn Sea Wolves Male (Nero)
            ((byte)5, (byte)9, (byte)0),   // Elezen Wildwood Male (Louisoix)
            ((byte)5, (byte)10, (byte)1),  // Elezen Duskwight Female (Urianger)
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
                {0, 5}, {1, 0}, {2, 1}, {3, 60}, {4, 9}, {5, 6}, {6, 1}, {7, 0},
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
