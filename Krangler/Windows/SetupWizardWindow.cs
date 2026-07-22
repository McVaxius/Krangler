using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Krangler.Windows;

public sealed class SetupWizardWindow : Window
{
    private sealed class WizardDraft
    {
        public bool Enabled { get; init; }
        public bool KrangleNames { get; init; }
        public bool KrangleChat { get; init; }
        public bool KrangleRaces { get; init; }
        public bool KrangleGenders { get; init; }
        public bool KrangleAppearance { get; init; }
        public bool SkipSelfKrangling { get; init; }
        public string CustomSelfDisplayName { get; init; } = string.Empty;
        public bool DtrBarEnabled { get; init; }
        public int DtrBarMode { get; init; }

        public static WizardDraft From(Configuration configuration)
            => new()
            {
                Enabled = configuration.Enabled,
                KrangleNames = configuration.KrangleNames,
                KrangleChat = configuration.KrangleChat,
                KrangleRaces = configuration.KrangleRaces,
                KrangleGenders = configuration.KrangleGenders,
                KrangleAppearance = configuration.KrangleAppearance,
                SkipSelfKrangling = configuration.SkipSelfKrangling,
                CustomSelfDisplayName = configuration.CustomSelfDisplayName ?? string.Empty,
                DtrBarEnabled = configuration.DtrBarEnabled,
                DtrBarMode = configuration.DtrBarMode,
            };

        public WizardDraft With(
            bool? enabled = null,
            bool? krangleNames = null,
            bool? krangleChat = null,
            bool? krangleRaces = null,
            bool? krangleGenders = null,
            bool? krangleAppearance = null,
            bool? skipSelfKrangling = null,
            string? customSelfDisplayName = null,
            bool? dtrBarEnabled = null,
            int? dtrBarMode = null)
            => new()
            {
                Enabled = enabled ?? Enabled,
                KrangleNames = krangleNames ?? KrangleNames,
                KrangleChat = krangleChat ?? KrangleChat,
                KrangleRaces = krangleRaces ?? KrangleRaces,
                KrangleGenders = krangleGenders ?? KrangleGenders,
                KrangleAppearance = krangleAppearance ?? KrangleAppearance,
                SkipSelfKrangling = skipSelfKrangling ?? SkipSelfKrangling,
                CustomSelfDisplayName = customSelfDisplayName ?? CustomSelfDisplayName,
                DtrBarEnabled = dtrBarEnabled ?? DtrBarEnabled,
                DtrBarMode = dtrBarMode ?? DtrBarMode,
            };
    }

    private readonly Plugin plugin;
    private WizardDraft? draft;
    private int step;

    public SetupWizardWindow(Plugin plugin)
        : base("Krangler Setup###KranglerSetupWizard")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize;
        Size = new Vector2(520, 0);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void OpenWithFreshDraft()
    {
        draft = WizardDraft.From(plugin.Configuration);
        step = 0;
        IsOpen = true;
    }

    public override void OnClose()
    {
        draft = null;
        step = 0;
    }

    public override void Draw()
    {
        draft ??= WizardDraft.From(plugin.Configuration);

        ImGui.Text($"Step {step + 1} of 3");
        ImGui.Separator();
        ImGui.Spacing();

        switch (step)
        {
            case 0:
                DrawFeatureStep();
                break;
            case 1:
                DrawSelfAndDtrStep();
                break;
            default:
                DrawReviewStep();
                break;
        }

        ImGui.Spacing();
        ImGui.Separator();
        DrawNavigation();
    }

    private void DrawFeatureStep()
    {
        ImGui.Text("Core privacy choices");
        ImGui.TextWrapped("Choose the broad local-only surfaces you want Krangler to change. You can refine every option later in the main window.");
        ImGui.Spacing();

        DrawCheckbox("Enable Krangler", draft!.Enabled, value => draft = draft.With(enabled: value));
        DrawCheckbox("Krangle player names", draft.KrangleNames, value => draft = draft.With(krangleNames: value));
        DrawCheckbox("Garble chat", draft.KrangleChat, value => draft = draft.With(krangleChat: value));
        DrawCheckbox("Randomize races and clans", draft.KrangleRaces, value => draft = draft.With(krangleRaces: value));
        DrawCheckbox("Randomize genders", draft.KrangleGenders, value => draft = draft.With(krangleGenders: value));
        DrawCheckbox("Randomize appearance details", draft.KrangleAppearance, value => draft = draft.With(krangleAppearance: value));
    }

    private void DrawSelfAndDtrStep()
    {
        ImGui.Text("Self display and DTR");
        ImGui.TextWrapped("Keep your own character stable if desired, and choose how Krangler appears in the server info bar.");
        ImGui.Spacing();

        DrawCheckbox("Do Not Krangle Self", draft!.SkipSelfKrangling, value => draft = draft.With(skipSelfKrangling: value));

        ImGui.BeginDisabled(!draft.SkipSelfKrangling);
        var selfName = draft.CustomSelfDisplayName;
        ImGui.SetNextItemWidth(260);
        if (ImGui.InputText("Custom Self Display Name", ref selfName, 64))
            draft = draft.With(customSelfDisplayName: selfName);
        ImGui.EndDisabled();

        ImGui.Spacing();
        DrawCheckbox("Show DTR Bar Entry", draft.DtrBarEnabled, value => draft = draft.With(dtrBarEnabled: value));

        ImGui.BeginDisabled(!draft.DtrBarEnabled);
        var dtrMode = draft.DtrBarMode;
        ImGui.SetNextItemWidth(180);
        if (ImGui.Combo("DTR Mode", ref dtrMode, "Text Only\0Icon + Text\0Icon Only\0"))
            draft = draft.With(dtrBarMode: dtrMode);
        ImGui.EndDisabled();
    }

    private void DrawReviewStep()
    {
        ImGui.Text("Review and apply");
        ImGui.TextWrapped("Finish saves these choices once. Presets, Soul Thief, Amongus, Imaginary Fren, advanced appearance settings, and Racism rules are not changed by this wizard.");
        ImGui.Spacing();

        DrawReviewLine("Master", draft!.Enabled);
        DrawReviewLine("Names", draft.KrangleNames);
        DrawReviewLine("Chat", draft.KrangleChat);
        DrawReviewLine("Race / clan", draft.KrangleRaces);
        DrawReviewLine("Gender", draft.KrangleGenders);
        DrawReviewLine("Appearance", draft.KrangleAppearance);
        DrawReviewLine("Do Not Krangle Self", draft.SkipSelfKrangling);
        ImGui.Text($"Self display name: {(string.IsNullOrWhiteSpace(draft.CustomSelfDisplayName) ? "Keep original" : draft.CustomSelfDisplayName)}");
        DrawReviewLine("DTR entry", draft.DtrBarEnabled);
        ImGui.Text($"DTR mode: {GetDtrModeName(draft.DtrBarMode)}");
    }

    private void DrawNavigation()
    {
        if (ImGui.Button("Cancel"))
        {
            draft = null;
            IsOpen = false;
            return;
        }

        if (step > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Back"))
                step--;
        }

        ImGui.SameLine();
        if (step < 2)
        {
            if (ImGui.Button("Next"))
                step++;
            return;
        }

        if (!ImGui.Button("Finish"))
            return;

        var completed = draft!;
        plugin.ApplySetupWizardSettings(
            completed.Enabled,
            completed.KrangleNames,
            completed.KrangleChat,
            completed.KrangleRaces,
            completed.KrangleGenders,
            completed.KrangleAppearance,
            completed.SkipSelfKrangling,
            completed.CustomSelfDisplayName,
            completed.DtrBarEnabled,
            completed.DtrBarMode);
        draft = null;
        IsOpen = false;
    }

    private static void DrawCheckbox(string label, bool currentValue, System.Action<bool> update)
    {
        var value = currentValue;
        if (ImGui.Checkbox(label, ref value))
            update(value);
    }

    private static void DrawReviewLine(string label, bool enabled)
        => ImGui.Text($"{label}: {(enabled ? "On" : "Off")}");

    private static string GetDtrModeName(int mode)
        => mode switch
        {
            1 => "Icon + Text",
            2 => "Icon Only",
            _ => "Text Only",
        };
}
