using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Krangler.Models;

namespace Krangler.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly AethertekUI.Dalamud.MaterialSupportLog supportLog = new();
    private readonly AethertekUI.Dalamud.MaterialWindowMotion windowMotion = new();
    private readonly Plugin plugin;
    private string presetSearch = string.Empty;
    private Vector2? queuedPosition;
    private bool queuedRandomVisibleJump;
    private bool identityRuleDraftLoaded;
    private bool identityRuleDraftEnabled;
    private List<PlayerIdentityRule> identityRuleDraft = new();
    private int selectedSection;
    private static readonly string[] Sections = ["Overview", "Names", "Appearance", "Racism", "Presets", "Imaginary Fren", "Soul Thief", "Debug"];
    private static readonly MaterialIcon[] SectionIcons = [MaterialIcon.Home, MaterialIcon.Person, MaterialIcon.Palette, MaterialIcon.Shield, MaterialIcon.Document, MaterialIcon.None, MaterialIcon.None, MaterialIcon.Settings];

    public MainWindow(Plugin plugin)
        : base("Krangler###KranglerMain")
    {
        this.plugin = plugin;

        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(1414, 963);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new(900, 640), MaximumSize = new(2200, 1600) };
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Palette, Priority = 0, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) selectedSection = 2; },
            ShowTooltip = () => UiGui.SetTooltip("Appearance"),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Wrench, Priority = -10, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.OpenSetupWizard(); },
            ShowTooltip = () => UiGui.SetTooltip("Open Setup Wizard"),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.PowerOff, Priority = -20, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) SetMasterEnabled(!plugin.Configuration.Enabled); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("Enable Krangler") + ": " + UiText.T(plugin.Configuration.Enabled ? "Enabled" : "Disabled")),
        });
    }

    public override void PreDraw()
    {
        windowMotion.Prepare(this, reducedMotion: false, roundedCorners: true);
    }

    public override void PostDraw()
    {
        windowMotion.Restore(this);
        var title = WindowName.Split("##", 2)[0];
        UiGui.ImageTitle(this, $"{UiText.T(title)} {typeof(Plugin).Assembly.GetName().Version}", plugin.OriginalIcon);
    }

    public override void Draw()
    {
        windowMotion.DrawChrome();
        ApplyQueuedWindowPlacement();
        DrawTabbedInterface();
    }

    private void DrawTabbedInterface()
    {
        var config = plugin.Configuration;
        var presetNames = plugin.GlamourerPresetService.GetPresetNames();
        var configChanged = EnsureSlotSelections(config);
        configChanged |= config.Sanitize();
        if (configChanged)
            config.Save();

        var scale = MaterialTheme.Metrics.Scale;
        var window = ImGuiP.GetCurrentWindow();
        ImGui.PushClipRect(window.InnerRect.Min,window.InnerRect.Max,false);
        var body = new Vector2(window.Pos.X, window.InnerRect.Min.Y);
        var bottom = window.InnerRect.Max.Y;
        var windowRootId = ImGui.GetID("");
        var tabsRootId = ImGui.GetID("KranglerTabs");
        float sidebarWidth;
        using (UiText.Font(UiFontRole.Heading))
            sidebarWidth = Math.Max(KranglerPresentation.SidebarWidth * scale,
                Sections.Max(section=>MaterialText.Measure(UiText.T(section)).X)+(KranglerPresentation.Compact?87:85)*scale);
        var navigationBottom=(KranglerPresentation.Compact?92:102)*scale
            +(Sections.Length-1)*(KranglerPresentation.NavigationHeight*scale+ImGui.GetStyle().ItemSpacing.Y)
            +KranglerPresentation.NavigationHeight*scale+ImGui.GetStyle().ItemSpacing.Y*.5f+(KranglerPresentation.Compact?30:32)*scale;
        if (navigationBottom>bottom-body.Y) sidebarWidth+=ImGui.GetStyle().ScrollbarSize;
        ImGui.SetCursorScreenPos(body);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,new Vector2(0,KranglerPresentation.Compact?30:32)*scale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg,MaterialTheme.Current.Colors.Background);
        if (ImGui.BeginChild("##KranglerNavigation", new Vector2(sidebarWidth, bottom-body.Y), false,ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            ImGuiP.PushOverrideID(windowRootId);
            ImGui.SetCursorPosX((KranglerPresentation.Compact?25:26)*scale);
            using (UiText.Font(UiFontRole.Action))
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(6,4)*scale);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(18,4)*scale);
                DrawMasterToggle(config);
                ImGui.PopStyleVar(2);
            }
            var firstNavigationY=body.Y+(KranglerPresentation.Compact?92:102)*scale-ImGui.GetScrollY();
            for (var index = 0; index < Sections.Length; index++)
            {
                using var font = UiText.Font(UiFontRole.Heading);
                ImGui.SetCursorScreenPos(new Vector2(body.X+3*scale+ImGui.GetStyle().ItemSpacing.X*.5f,
                    firstNavigationY+ImGui.GetStyle().ItemSpacing.Y*.5f+index*(KranglerPresentation.NavigationHeight*scale+ImGui.GetStyle().ItemSpacing.Y)));
                var size = new Vector2(sidebarWidth-6*scale-ImGui.GetStyle().ItemSpacing.X, KranglerPresentation.NavigationHeight * scale);
                var label = UiText.T(Sections[index]);
                ImGui.PushStyleColor(ImGuiCol.Header,MaterialTheme.Current.Colors.TertiaryContainer);
                ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
                if (ImGui.Selectable(Sections[index]+"##navigation", selectedSection == index, size: size)) selectedSection = index;
                ImGui.PopStyleColor(2);
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                var color = selectedSection == index ? MaterialTheme.Current.Colors.OnSurface : MaterialTheme.Current.Colors.OnSurfaceVariant;
                var dl = ImGui.GetWindowDrawList();
                if (selectedSection == index) dl.AddRectFilled(min, new Vector2(min.X+4*scale, max.Y), MaterialCanvas.Color(MaterialTheme.Current.Colors.Primary), 2*scale);
                DrawSectionIcon(index,min+new Vector2(20,((max.Y-min.Y)/scale-38)*.5f)*scale,38*scale,
                    selectedSection==index?MaterialTheme.Current.Colors.InversePrimary:color);
                MaterialText.AddText(dl,new Vector2(min.X+(KranglerPresentation.Compact?79:77)*scale,min.Y+(max.Y-min.Y-MaterialText.Measure(label).Y)*.5f),ImGui.GetColorU32(color),label);
                if (MaterialText.Measure(label).X > max.X-min.X-87*scale && ImGui.IsItemHovered()) MaterialText.SetTooltip(label);
            }
            ImGui.PopID();
        }
        ImGui.EndChild();ImGui.PopStyleColor();ImGui.PopStyleVar();
        ImGui.GetWindowDrawList().AddLine(new Vector2(body.X+sidebarWidth,body.Y),new Vector2(body.X+sidebarWidth,bottom),MaterialCanvas.Color(MaterialTheme.Current.Colors.OutlineVariant),scale);
        var contentOrigin=body+new Vector2(sidebarWidth/scale+21,KranglerPresentation.ContentTop)*scale;
        ImGui.SetCursorScreenPos(contentOrigin);
        if (ImGui.BeginChild("##KranglerContent", new Vector2(Math.Max(1,window.Pos.X+window.Size.X-21*scale-contentOrigin.X),Math.Max(1,bottom-contentOrigin.Y-21*scale)), false, ImGuiWindowFlags.HorizontalScrollbar))
        {
            ImGuiP.PushOverrideID(windowRootId);
            DrawHeader();
            // Native tabs used these two ID roots. Preserve them inside the new sidebar content child.
            ImGuiP.PushOverrideID(tabsRootId);
            ImGuiP.PushOverrideID(ImGui.GetID(Sections[selectedSection]));
            switch (selectedSection)
            {
                case 0: DrawOverviewTab(config); break;
                case 1: DrawNamesTab(config); break;
                case 2: DrawAppearanceTab(config); break;
                case 3: DrawRacismTab(config); break;
                case 4: DrawPresetsTab(config, presetNames); break;
                case 5: DrawImaginaryFrenTab(config, presetNames); break;
                case 6: DrawSoulThiefTab(config); break;
                case 7: DrawDebugTab(config); break;
            }
            ImGui.PopID();ImGui.PopID();ImGui.PopID();
        }
        ImGui.EndChild();
        // Overflow belongs to the two children; their full body rectangles must not grow the parent scroll range.
        window.DC.CursorMaxPos=Vector2.Max(window.DC.CursorStartPos,Vector2.Min(window.DC.CursorMaxPos,window.InnerRect.Max-ImGui.GetStyle().WindowPadding));
        ImGui.PopClipRect();
    }

    private void DrawHeader()
    {
        var scale=MaterialTheme.Metrics.Scale;
        var origin=ImGui.GetCursorScreenPos();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX()+(KranglerPresentation.Compact?5:12)*scale);
        using (UiText.Font(KranglerPresentation.Compact?UiFontRole.Heading:UiFontRole.Title))
        {
            var icon = plugin.OriginalIcon;
            var imageMin = ImGui.GetCursorScreenPos();
            var imageSize = new Vector2(ImGui.GetTextLineHeight());
            MaterialCanvas.DrawImage(ImGui.GetWindowDrawList(), icon.Handle, icon.Size, imageMin, imageMin + imageSize);
            ImGui.Dummy(imageSize);
            ImGui.SameLine();
            MaterialText.Text("Krangler");
        }
        if (plugin.Configuration.UiCompactVisibleOnMainWindow) { SameLineIfFits(32); plugin.DrawCompactPreference(); }
        var languageName=UiText.Languages.FirstOrDefault(language=>language.Code==plugin.Configuration.UiLanguage).Name??"English";
        var transparencyWidth = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + MaterialText.Measure(UiText.T("Transparency")).X;
        var controlsWidth = transparencyWidth + ImGui.GetStyle().ItemSpacing.X
            + (plugin.Configuration.UiLanguageVisibleOnMainWindow ? (42 + 16) * scale + Math.Max((KranglerPresentation.Compact ? 188 : 182) * scale,
                MathF.Ceiling(MaterialText.Measure(languageName).X + KranglerPresentation.ActionHeight * scale + 56 * scale)) : 0);
        var supportWidth=(KranglerPresentation.Compact?138:134)*scale;
        var supportGap=(KranglerPresentation.Compact?36:38)*scale;
        var right=origin.X+ImGui.GetContentRegionAvail().X;
        var groupWidth=supportWidth+supportGap+controlsWidth;
        if (ImGui.GetItemRectMax().X+ImGui.GetStyle().ItemSpacing.X+groupWidth<=right)
        {
            ImGui.SameLine();
            ImGui.SetCursorScreenPos(new Vector2(right-groupWidth,origin.Y+(KranglerPresentation.Compact?0:4)*scale));
        }
        using (UiText.Font(UiFontRole.Action))
        {
        var supportMin=ImGui.GetCursorScreenPos();
        if (UiGui.Button("\u2661 Ko-fi \u2661",new Vector2(supportWidth,KranglerPresentation.ActionHeight*scale),display:""))
            Process.Start(new ProcessStartInfo { FileName="https://ko-fi.com/mcvaxius", UseShellExecute=true });
        MaterialIcons.Draw(MaterialIcon.Heart,supportMin+new Vector2(14,(KranglerPresentation.ActionHeight-28)*.5f)*scale,28*scale,KranglerPresentation.Rgb(0xFF687D));
        MaterialText.AddText(ImGui.GetWindowDrawList(),supportMin+new Vector2(56*scale,(KranglerPresentation.ActionHeight*scale-ImGui.GetTextLineHeight())*.5f),ImGui.GetColorU32(ImGuiCol.Text),"Ko-fi");
        }
        if (ImGui.IsItemHovered()) UiGui.SetTooltip("Support development on Ko-fi");
        if (SameLineIfFits((controlsWidth+supportGap-ImGui.GetStyle().ItemSpacing.X)/scale)) ImGui.SameLine(0,supportGap);
        if (plugin.Configuration.UiLanguageVisibleOnMainWindow) plugin.DrawAppearanceSelector(includeAccent: false);
        SameLineIfFits(transparencyWidth / scale); plugin.DrawTransparencyToggle();
        ImGui.SetCursorScreenPos(new Vector2(origin.X,Math.Max(origin.Y+KranglerPresentation.HeaderHeight*scale,ImGui.GetItemRectMax().Y+12*scale)));
    }

    private static bool SameLineIfFits(float logicalWidth)
    {
        var cursor=ImGui.GetCursorScreenPos();
        var right=cursor.X+ImGui.GetContentRegionAvail().X;
        var itemMax=ImGui.GetItemRectMax();
        if (cursor.Y>itemMax.Y+ImGui.GetStyle().ItemSpacing.Y+.5f) return false;
        var end=Math.Max(itemMax.X,ImGuiP.GetCurrentWindow().DC.CursorPosPrevLine.X);
        if (end+ImGui.GetStyle().ItemSpacing.X+logicalWidth*MaterialTheme.Metrics.Scale>right) return false;
        ImGui.SameLine();return true;
    }

    private static float CheckboxWidth(string label)
        => (ImGui.GetFrameHeight()+ImGui.GetStyle().ItemInnerSpacing.X+MaterialText.Measure(UiText.T(label)).X)/MaterialTheme.Metrics.Scale;

    private static void DrawSectionIcon(int index,Vector2 origin,float size,Vector4 color)
    {
        if (index is not (5 or 6)) { MaterialIcons.Draw(SectionIcons[index],origin,size,color);return; }
        // Imaginary Fren and Soul Thief artwork belongs to Krangler.
        var dl=ImGui.GetWindowDrawList();var ink=ImGui.GetColorU32(color);
        Vector2 P(float x,float y)=>origin+new Vector2(x,y)*size;
        if (index==5)
        {
            dl.AddCircleFilled(P(.5f,.58f),size*.34f,ink,24);
            dl.AddTriangleFilled(P(.18f,.43f),P(.2f,.08f),P(.43f,.32f),ink);
            dl.AddTriangleFilled(P(.57f,.32f),P(.8f,.08f),P(.82f,.43f),ink);
        }
        else
        {
            dl.PathArcTo(P(.5f,.42f),size*.34f,MathF.PI,MathF.Tau,20);
            dl.PathLineTo(P(.84f,.77f));dl.PathLineTo(P(.16f,.77f));dl.PathFillConvex(ink);
            for(var segment=0;segment<3;segment++)
                dl.AddTriangleFilled(P(.16f+segment*.226f,.75f),P(.386f+segment*.226f,.75f),P(.273f+segment*.226f,.9f),ink);
        }
        var eye=ImGui.GetColorU32(MaterialTheme.Current.Colors.Background);
        dl.AddCircleFilled(P(.38f,.5f),size*.05f,eye,12);dl.AddCircleFilled(P(.62f,.5f),size*.05f,eye,12);
    }

    private static void Panel(string id,float logicalHeight,Action draw)
    {
        var scale=MaterialTheme.Metrics.Scale;
        var rootId=ImGui.GetID("");
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,new Vector2(KranglerPresentation.PanelPadding,0)*scale);
        ImGui.PushStyleColor(ImGuiCol.ChildBg,KranglerPresentation.Compact?MaterialTheme.Current.Colors.SurfaceContainerLowest:MaterialTheme.Current.Colors.Surface);
        if (ImGui.BeginChild(id,new Vector2(0,logicalHeight*scale),true,ImGuiWindowFlags.AlwaysUseWindowPadding|ImGuiWindowFlags.HorizontalScrollbar))
        {
            ImGuiP.PushOverrideID(rootId);draw();ImGui.PopID();
        }
        ImGui.EndChild();ImGui.PopStyleColor();ImGui.PopStyleVar();
    }

    private void DrawOverviewTab(Configuration config)
    {
        Panel("##KranglerOverview",KranglerPresentation.OverviewHeight,()=>
        {
            var scale=MaterialTheme.Metrics.Scale;var panel=ImGui.GetWindowPos()-new Vector2(ImGui.GetScrollX(),ImGui.GetScrollY());
            ImGui.SetCursorScreenPos(panel+new Vector2(KranglerPresentation.PanelPadding,KranglerPresentation.Compact?12:17)*scale);
            using (UiText.Font(KranglerPresentation.Compact?UiFontRole.Heading:UiFontRole.Title)) UiGui.Text("Overview");
            ImGui.SetCursorScreenPos(panel+new Vector2(KranglerPresentation.PanelPadding,KranglerPresentation.Compact?56:72)*scale);ImGui.Separator();
            ImGui.SetCursorScreenPos(panel+new Vector2(KranglerPresentation.Compact?24:26,KranglerPresentation.Compact?78:107)*scale);
            DrawStatus(config);
            ImGui.SetCursorScreenPos(panel+new Vector2(KranglerPresentation.PanelPadding,KranglerPresentation.Compact?142:187)*scale);ImGui.Separator();
            ImGui.SetCursorScreenPos(panel+new Vector2(KranglerPresentation.Compact?28:34,KranglerPresentation.Compact?154:204)*scale);
            using (UiText.Font(UiFontRole.Heading))
            {
            UiGui.Text(UiText.F("Presets loaded: {0}",plugin.GlamourerPresetService.PresetCount));
            UiGui.TextWrapped(UiText.F("Soul Thief last capture: {0} players, {1} NPCs, {2} chocobos",config.SoulThiefLastCapturedPlayers,config.SoulThiefLastCapturedNpcs,config.SoulThiefLastCapturedChocobos));
            }
            ImGui.SetCursorScreenPos(new Vector2(panel.X+(KranglerPresentation.Compact?28:34)*scale,
                Math.Max(panel.Y+(KranglerPresentation.Compact?246:317)*scale,ImGui.GetCursorScreenPos().Y+8*scale)));
            var colors=MaterialTheme.Current.Colors;
            ImGui.PushStyleColor(ImGuiCol.Button,colors.PrimaryContainer);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered,MaterialColor.Layer(colors.PrimaryContainer,colors.OnPrimaryContainer,.08f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive,MaterialColor.Layer(colors.PrimaryContainer,colors.OnPrimaryContainer,.14f));
            using (UiText.Font(UiFontRole.Heading))
                if (UiGui.Button("Open Setup Wizard",new Vector2((KranglerPresentation.Compact?326:337)*scale,
                    MaterialControlMetrics.Measure(MaterialTheme.Metrics, ImGui.GetTextLineHeight(), MaterialControlContext.Toolbar).Height))) plugin.OpenSetupWizard();
            ImGui.PopStyleColor(3);
            if (ImGui.IsItemHovered()) UiGui.SetTooltip("Reopen the three-step quick setup without changing advanced settings or Racism rules.");
        });
        ImGui.SetCursorPosY(ImGui.GetCursorPosY()-ImGui.GetStyle().ItemSpacing.Y);
        ImGui.Dummy(new Vector2(0,(KranglerPresentation.Compact?22:20)*MaterialTheme.Metrics.Scale-ImGui.GetStyle().ItemSpacing.Y));
        Panel("##KranglerDtr",KranglerPresentation.DtrHeight,()=>DrawDtrSection(config));
    }

    private void DrawMasterToggle(Configuration config)
    {
        var enabled = config.Enabled;
        if (UiGui.Checkbox("Enable Krangler", ref enabled))
            SetMasterEnabled(enabled);
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Master toggle - enables or disables all krangling.");

        ImGui.Spacing();
    }

    private void SetMasterEnabled(bool enabled)
    {
        plugin.Configuration.Enabled = enabled;
        if (!enabled) Services.KrangleService.ClearCache();
        plugin.Configuration.Save();
    }

    private void DrawDtrSection(Configuration config)
    {
        var scale=MaterialTheme.Metrics.Scale;var panel=ImGui.GetWindowPos()-new Vector2(ImGui.GetScrollX(),ImGui.GetScrollY());
        ImGui.SetCursorScreenPos(panel+new Vector2(KranglerPresentation.PanelPadding,KranglerPresentation.Compact?12:17)*scale);
        using (UiText.Font(KranglerPresentation.Compact?UiFontRole.Heading:UiFontRole.Title)) UiGui.Text("DTR Bar");
        ImGui.SetCursorScreenPos(panel+new Vector2(KranglerPresentation.PanelPadding,KranglerPresentation.Compact?55:70)*scale);ImGui.Separator();
        ImGui.SetCursorScreenPos(panel+new Vector2(KranglerPresentation.Compact?28:34,KranglerPresentation.Compact?70:92)*scale);

        var dtrEnabled = config.DtrBarEnabled;
        if (UiGui.Checkbox("Show DTR Bar Entry", ref dtrEnabled,null,UiFontRole.Action))
        {
            config.DtrBarEnabled = dtrEnabled;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Show Krangler status in the server info bar. Click the DTR entry to toggle enable or disable.");

        ImGui.BeginDisabled(!config.DtrBarEnabled);

        ImGui.SetCursorScreenPos(panel+new Vector2(KranglerPresentation.Compact?28:34,KranglerPresentation.Compact?112:144)*scale);
        var dtrMode = config.DtrBarMode;
        using (UiText.Font(UiFontRole.Action))
        {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(12*scale,Math.Max(0,((KranglerPresentation.Compact?48:54)*scale-ImGui.GetTextLineHeight())*.5f)));
        var modeWidth=Math.Max((KranglerPresentation.Compact?242:280)*scale,
            new[]{"Text Only","Icon + Text","Icon Only"}.Max(option=>MaterialText.Measure(UiText.T(option)).X)+ImGui.GetFrameHeight()+2*ImGui.GetStyle().FramePadding.X);
        var right=ImGui.GetCursorScreenPos().X+ImGui.GetContentRegionAvail().X;
        var labelStart=ImGui.GetCursorScreenPos().X;
        ImGui.AlignTextToFramePadding();UiGui.Text("DTR Mode");
        var fieldStart=Math.Max(labelStart+(KranglerPresentation.Compact?124:134)*scale,ImGui.GetItemRectMax().X+32*scale);
        if (fieldStart+modeWidth<=right) { ImGui.SameLine();ImGui.SetCursorScreenPos(new Vector2(fieldStart,ImGui.GetCursorScreenPos().Y)); }
        ImGui.SetNextItemWidth(modeWidth);
        if (UiGui.ComboWithoutLabel("DTR Mode", ref dtrMode, "Text Only\0Icon + Text\0Icon Only\0"))
        {
            config.DtrBarMode = dtrMode;
            config.Save();
        }
        ImGui.PopStyleVar();
        }

        ImGui.SetCursorScreenPos(new Vector2(panel.X+(KranglerPresentation.Compact?28:34)*scale,
            Math.Max(panel.Y+(KranglerPresentation.Compact?174:224)*scale,ImGui.GetCursorScreenPos().Y+8*scale)));
        var enabledIcon = config.DtrIconEnabled;
        if (DrawIconInputs("Enabled", ref enabledIcon, "\uE03C"))
        {
            config.DtrIconEnabled = enabledIcon;
            config.Save();
        }

        if (SameLineIfFits(IconInputWidth("Disabled")+24))
        {
            var min=ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddLine(min,min+new Vector2(0,(KranglerPresentation.Compact?56:80)*scale),MaterialCanvas.Color(MaterialTheme.Current.Colors.Outline),scale);
            ImGui.Dummy(new Vector2(8*scale,(KranglerPresentation.Compact?56:80)*scale));ImGui.SameLine();
        }
        else { ImGui.Spacing();ImGui.Separator();ImGui.Spacing(); }
        var disabledIcon = config.DtrIconDisabled;
        if (DrawIconInputs("Disabled", ref disabledIcon, "\uE03D"))
        {
            config.DtrIconDisabled = disabledIcon;
            config.Save();
        }

        using (UiText.Font(UiFontRole.Action))
        {
        var guideWidth=MaterialText.Measure(UiText.T("Copy Icon Guide Link")).X+40*scale;
        SameLineIfFits(guideWidth/scale);
        var guideMin=ImGui.GetCursorScreenPos();
        var guideMetrics=MaterialControlMetrics.Measure(MaterialTheme.Metrics, ImGui.GetTextLineHeight(), MaterialControlContext.Toolbar);
        var guideHeight=Math.Max(guideMetrics.Height,32*scale+(KranglerPresentation.Compact?4:8)*scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(ImGui.GetStyle().FramePadding.X,(guideHeight-ImGui.GetTextLineHeight())*.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize,0);
        ImGui.PushStyleColor(ImGuiCol.Button,Vector4.Zero);ImGui.PushStyleColor(ImGuiCol.ButtonHovered,Vector4.Zero);ImGui.PushStyleColor(ImGuiCol.ButtonActive,Vector4.Zero);
        if (UiGui.Button("Copy Icon Guide Link",new Vector2(guideWidth,guideHeight),display:""))
        {
            ImGui.SetClipboardText("https://na.finalfantasyxiv.com/lodestone/character/22423564/blog/4393835");
            Plugin.Log.Information("Copied icon guide link to clipboard");
        }
        ImGui.PopStyleColor(3);ImGui.PopStyleVar(2);
        MaterialIcons.Draw(MaterialIcon.Link,guideMin+new Vector2(0,(guideHeight-32*scale)*.5f),32*scale,MaterialTheme.Current.Colors.InversePrimary,ImGui.GetStyle().Alpha);
        MaterialText.AddText(ImGui.GetWindowDrawList(),guideMin+new Vector2(40*scale,(guideHeight-MaterialText.Measure(UiText.T("Copy Icon Guide Link")).Y)*.5f),ImGui.GetColorU32(MaterialTheme.Current.Colors.InversePrimary),UiText.T("Copy Icon Guide Link"));
        if (ImGui.IsItemHovered()) UiGui.SetTooltip("Copies the Lodestone blog link with suggested glyphs.");
        }

        ImGui.EndDisabled();
    }

    private void DrawNamesTab(Configuration config)
    {
        ImGui.Spacing();
        UiGui.Text("Names");
        ImGui.Separator();

        var krangleNames = config.KrangleNames;
        if (UiGui.Checkbox("Krangle Names", ref krangleNames))
        {
            config.KrangleNames = krangleNames;
            if (!krangleNames)
                Services.KrangleService.ClearCache();
            config.Save();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Randomize visible player names and party list names.");

        var skipSelfKrangling = config.SkipSelfKrangling;
        if (UiGui.Checkbox("Do Not Krangle Self", ref skipSelfKrangling))
            plugin.SetSkipSelfKrangling(skipSelfKrangling);
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Keep your own character's appearance stable and optionally use a fixed self display name instead of a randomized one.");

        ImGui.BeginDisabled(!config.SkipSelfKrangling);
        var customSelfDisplayName = config.CustomSelfDisplayName ?? string.Empty;
        if (UiGui.InputText("Custom Self Display Name", ref customSelfDisplayName, 64))
            plugin.SetCustomSelfDisplayName(customSelfDisplayName);
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Optional fixed name to use for your own character. Leave blank to keep your real name.");
        ImGui.EndDisabled();

        var krangleChat = config.KrangleChat;
        if (UiGui.Checkbox("Krangle Chat", ref krangleChat))
        {
            config.KrangleChat = krangleChat;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Garble chat text for screenshot privacy.");
    }

    private void DrawAppearanceTab(Configuration config)
    {
        UiGui.Text("Window appearance");
        ImGui.Separator();
        plugin.DrawCompactPreference(); SameLineIfFits(230); plugin.DrawAppearanceSelector();
        plugin.DrawWindowSettings();
        ImGui.Separator();
        ImGui.Spacing();
        UiGui.Text("Appearance");
        ImGui.Separator();

        var krangleGenders = config.KrangleGenders;
        if (UiGui.Checkbox("Krangle Genders", ref krangleGenders))
        {
            config.KrangleGenders = krangleGenders;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Randomize genders for visible player characters.");

        var krangleRaces = config.KrangleRaces;
        if (UiGui.Checkbox("Krangle Races", ref krangleRaces))
        {
            config.KrangleRaces = krangleRaces;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Randomize races and subraces for visible player characters.");

        var krangleAppearance = config.KrangleAppearance;
        if (UiGui.Checkbox("Krangle Appearance", ref krangleAppearance))
        {
            config.KrangleAppearance = krangleAppearance;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Randomize hair, face, eyes, and other appearance fields.");

        ImGui.Spacing();
        UiGui.Text("Non-Player Targets");
        ImGui.Separator();
        UiGui.TextWrapped("Broad non-player native mutation is currently blocked for crash safety.");

        ImGui.BeginDisabled();
        var krangleNpcs = false;
        UiGui.Checkbox("Krangle NPCs", ref krangleNpcs);

        var krangleChocobos = false;
        UiGui.Checkbox("Krangle Chocobos", ref krangleChocobos);

        var krangleMinions = false;
        UiGui.Checkbox("Krangle Minions", ref krangleMinions);
        ImGui.EndDisabled();
    }

    private void DrawRacismTab(Configuration config)
    {
        if (!identityRuleDraftLoaded)
            ReloadIdentityRuleDraft(config);

        ImGui.Spacing();
        UiGui.Text("Exact Race / Clan / Gender Rules");
        UiGui.TextWrapped("Rules match the actor's original local identity. Hide removes the matching 3D actor and in-world nameplate; Replace pseudonymizes supported names and applies the chosen clan and gender after other appearance work.");
        ImGui.Spacing();

        UiGui.Checkbox("Enable Racism Rules", ref identityRuleDraftEnabled);
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("This is part of the draft. Use Apply Rules to save or disable the tab.");

        var tableFlags = ImGuiTableFlags.Borders |
                         ImGuiTableFlags.RowBg |
                         ImGuiTableFlags.ScrollY |
                         ImGuiTableFlags.ScrollX |
                         ImGuiTableFlags.SizingFixedFit;
        using var tightRows = plugin.Configuration.UiCompact ? MaterialTable.PushTightRows() : default;
        if (ImGui.BeginTable("##PlayerIdentityRules", 8, tableFlags, new Vector2(0, Math.Max(160,ImGui.GetContentRegionAvail().Y-110*MaterialTheme.Metrics.Scale))))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Active");
            ImGui.TableSetupColumn("Race");
            ImGui.TableSetupColumn("Clan/Subrace");
            ImGui.TableSetupColumn("Gender");
            ImGui.TableSetupColumn("Hide");
            ImGui.TableSetupColumn("Replace");
            ImGui.TableSetupColumn("Replacement Clan");
            ImGui.TableSetupColumn("Replacement Gender");
            UiGui.TableHeadersRow();

            for (var index = 0; index < identityRuleDraft.Count; index++)
            {
                var rule = identityRuleDraft[index];
                var descriptor = PlayerIdentityCatalog.Entries[index];

                ImGui.PushID(index);
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                var active = rule.Active;
                if (UiGui.Checkbox("##active", ref active))
                {
                    rule.Active = active;
                    if (active)
                        rule.Action = PlayerIdentityRuleAction.Hide;
                }

                ImGui.TableSetColumnIndex(1);
                UiGui.TextUnformatted(descriptor.RaceName);

                ImGui.TableSetColumnIndex(2);
                UiGui.TextUnformatted(descriptor.ClanName);

                ImGui.TableSetColumnIndex(3);
                UiGui.TextUnformatted(descriptor.GenderName);

                ImGui.TableSetColumnIndex(4);
                if (UiGui.RadioButton("##hide", rule.Action == PlayerIdentityRuleAction.Hide))
                    rule.Action = PlayerIdentityRuleAction.Hide;

                ImGui.TableSetColumnIndex(5);
                if (UiGui.RadioButton("##replace", rule.Action == PlayerIdentityRuleAction.Replace))
                    rule.Action = PlayerIdentityRuleAction.Replace;

                var replacementEnabled = rule.Active && rule.Action == PlayerIdentityRuleAction.Replace;
                ImGui.BeginDisabled(!replacementEnabled);

                ImGui.TableSetColumnIndex(6);
                DrawReplacementClanCombo(rule);

                ImGui.TableSetColumnIndex(7);
                DrawReplacementGenderCombo(rule);

                ImGui.EndDisabled();
                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        var activeRules = identityRuleDraft.Count(rule => rule.Active);
        UiGui.TextWrapped(UiText.F($"Draft: {activeRules} active rule(s). Currently hidden by Krangler: {plugin.IdentityRuleService.HiddenActorCount} actor(s)."));

        if (UiGui.Button("Apply Rules"))
        {
            plugin.ApplyPlayerIdentityRules(identityRuleDraftEnabled, identityRuleDraft);
            ReloadIdentityRuleDraft(config);
        }

        SameLineIfFits((MaterialText.Measure(UiText.T("Discard Edits")).X+ImGui.GetStyle().FramePadding.X*2)/MaterialTheme.Metrics.Scale);
        if (UiGui.Button("Discard Edits"))
            ReloadIdentityRuleDraft(config);
    }

    private static void DrawReplacementClanCombo(PlayerIdentityRule rule)
    {
        PlayerIdentityCatalog.TryGetRaceForClan(rule.ReplacementClan, out var replacementRace);
        var raceName = PlayerIdentityCatalog.Entries.First(entry => entry.Race == replacementRace).RaceName;
        var preview = $"{raceName} / {PlayerIdentityCatalog.GetClanName(rule.ReplacementClan)}";

        ImGui.SetNextItemWidth(180*MaterialTheme.Metrics.Scale);
        if (!UiGui.BeginCombo("##replacementClan", preview))
            return;

        foreach (var (clan, clanName) in PlayerIdentityCatalog.ClanOptions)
        {
            PlayerIdentityCatalog.TryGetRaceForClan(clan, out var race);
            var optionRaceName = PlayerIdentityCatalog.Entries.First(entry => entry.Race == race).RaceName;
            var selected = rule.ReplacementClan == clan;
            if (UiGui.Selectable($"{optionRaceName} / {clanName}", selected))
                rule.ReplacementClan = clan;
            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private static void DrawReplacementGenderCombo(PlayerIdentityRule rule)
    {
        var replacementGender = (int)rule.ReplacementGender;
        ImGui.SetNextItemWidth(100*MaterialTheme.Metrics.Scale);
        if (UiGui.Combo("##replacementGender", ref replacementGender, "Male\0Female\0"))
            rule.ReplacementGender = (byte)replacementGender;
    }

    private void ReloadIdentityRuleDraft(Configuration config)
    {
        identityRuleDraftEnabled = config.RaceGenderRulesEnabled;
        identityRuleDraft = PlayerIdentityCatalog.CreateDraftRules(config.PlayerIdentityRules);
        identityRuleDraftLoaded = true;
    }

    private void DrawPresetsTab(Configuration config, IReadOnlyList<string> presetNames)
    {
        ImGui.Spacing();
        UiGui.Text(UiText.F($"Presets loaded: {plugin.GlamourerPresetService.PresetCount}"));

        DrawAmongusSection(config, presetNames);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawSuperKrangleSection(config, presetNames);
    }

    private void DrawImaginaryFrenTab(Configuration config, IReadOnlyList<string> presetNames)
    {
        ImGui.Spacing();
        UiGui.Text("Imaginary Fren");
        ImGui.Separator();
        UiGui.Text(UiText.F($"Presets loaded: {plugin.GlamourerPresetService.PresetCount}"));
        ImGui.Spacing();

        var status = plugin.ImaginaryFrenService.GetStatus();
        var enabled = config.ImaginaryFrenEnabled;
        if (UiGui.Checkbox("Enabled##ImaginaryFrenEnabled", ref enabled))
        {
            config.ImaginaryFrenEnabled = enabled;
            config.Save();
            plugin.ImaginaryFrenService.UseConfigDesired();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Spawn one local-only, non-targetable fake NPC follower while Krangler is enabled.");

        ImGui.SameLine();
        UiGui.TextDisabled(status.Spawned ? "Spawned" : "Not spawned");

        var displayName = config.ImaginaryFrenName ?? string.Empty;
        ImGui.SetNextItemWidth(220f*MaterialTheme.Metrics.Scale);
        if (UiGui.InputText("Display Name##ImaginaryFrenName", ref displayName, 64))
        {
            config.ImaginaryFrenName = displayName;
            config.Sanitize();
            config.Save();
            plugin.ImaginaryFrenService.UseConfigDesired();
        }

        var presetKey = string.IsNullOrWhiteSpace(config.ImaginaryFrenPresetKey)
            ? Configuration.DefaultImaginaryFrenPresetKey
            : config.ImaginaryFrenPresetKey;
        ImGui.SetNextItemWidth(260f*MaterialTheme.Metrics.Scale);
        if (DrawPresetSelectionCombo("Preset##ImaginaryFrenPreset", ref presetKey, presetNames, false, false))
        {
            config.ImaginaryFrenPresetKey = presetKey;
            config.Save();
            plugin.ImaginaryFrenService.UseConfigDesired();
        }

        if (UiGui.SmallButton("Test Spawn"))
        {
            plugin.ImaginaryFrenService.RequestSpawnFromConfig();
            plugin.ImaginaryFrenService.Update();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Enable and try to spawn the configured follower now. Krangler's master toggle still gates spawning.");

        ImGui.SameLine();
        if (UiGui.SmallButton("Despawn"))
        {
            plugin.ImaginaryFrenService.DisableFromConfig();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Disable and remove the current local-only follower.");

        UiGui.TextWrapped(UiText.F($"Status: {UiText.Status(status.Status)}"));
        if (!string.IsNullOrWhiteSpace(status.Error))
            UiGui.TextWrapped(UiText.F($"Warning: {UiText.FrenError(status.Status,status.Error)}"));
        if (!string.Equals(status.Source, "config", StringComparison.OrdinalIgnoreCase))
            UiGui.TextWrapped(UiText.F($"Runtime source: {status.Source}"));
    }

    private void DrawSuperKrangleSection(Configuration config, IReadOnlyList<string> presetNames)
    {
        var superKrangle = config.SuperKrangleMaster4000;
        if (UiGui.Checkbox("Super Krangle Master 4000", ref superKrangle))
        {
            config.SuperKrangleMaster4000 = superKrangle;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            UiGui.SetTooltip(
                UiText.T("Use imported Glamourer presets in place of normal appearance krangling.\n") +
                UiText.T("Selection can be global, random, or overridden by party slot.\n\n") +
                UiText.F("Presets loaded: {0}",plugin.GlamourerPresetService.PresetCount));
        }

        ImGui.BeginDisabled(!config.SuperKrangleMaster4000);

        SameLineIfFits(MaterialText.Measure(UiText.F($"({plugin.GlamourerPresetService.PresetCount} presets)")).X/MaterialTheme.Metrics.Scale);
        UiGui.TextColored(MaterialTheme.Current.Colors.OnSurfaceVariant, UiText.F($"({plugin.GlamourerPresetService.PresetCount} presets)"));

        if (presetNames.Count == 0)
            UiGui.TextColored(KranglerPresentation.Pending, "No preset files are loaded. Built-in NPC looks will be used instead.");

        var globalSelection = string.IsNullOrWhiteSpace(config.SuperKrangleSelection)
            ? "Random"
            : config.SuperKrangleSelection;
        if (DrawPresetSelectionCombo("Global Preset", ref globalSelection, presetNames, false))
        {
            config.SuperKrangleSelection = globalSelection;
            config.Save();
        }

        ImGui.Spacing();
        UiGui.Text("Non-Player Preset Targets");
        ImGui.Separator();
        UiGui.TextWrapped("Broad non-player native mutation is currently blocked for crash safety.");

        ImGui.BeginDisabled();
        var superKrangleNpcs = false;
        UiGui.Checkbox("NPCs", ref superKrangleNpcs);
        ImGui.EndDisabled();

        ImGui.Spacing();
        UiGui.Text("Companion Preset Targets");
        ImGui.Separator();
        UiGui.TextWrapped("Broad non-player native mutation is currently blocked for crash safety.");

        ImGui.BeginDisabled();
        var superKrangleChocobos = false;
        UiGui.Checkbox("Chocobos", ref superKrangleChocobos);

        var superKrangleMinions = false;
        UiGui.Checkbox("Minions", ref superKrangleMinions);
        ImGui.EndDisabled();

        ImGui.Spacing();
        UiGui.Text("Party Slot Overrides");
        ImGui.Separator();

        for (var i = 0; i < config.SuperKranglePartySlotSelections.Count; i++)
        {
            var slotSelection = string.IsNullOrWhiteSpace(config.SuperKranglePartySlotSelections[i])
                ? "Use Global"
                : config.SuperKranglePartySlotSelections[i];

            if (DrawPresetSelectionCombo(GetPartySlotLabel(i), ref slotSelection, presetNames, true))
            {
                config.SuperKranglePartySlotSelections[i] = slotSelection;
                config.Save();
            }
        }

        ImGui.Spacing();
        UiGui.Text("Apply From Preset");
        ImGui.Separator();

        DrawApplyFromPresetOptions(config);

        ImGui.Spacing();
        UiGui.Text("Propagation Control");
        ImGui.Separator();

        var maxPlayersPerCycle = config.SuperKrangleMaxPlayersPerCycle;
        if (UiGui.SliderInt("Max Players Per Cycle", ref maxPlayersPerCycle, 1, 24))
        {
            config.SuperKrangleMaxPlayersPerCycle = maxPlayersPerCycle;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Limit how many visible players are processed during one scan pass.");

        var redrawDelay = config.SuperKrangleBaseRedrawDelayFrames;
        if (UiGui.SliderInt("Base Redraw Delay", ref redrawDelay, 1, 10))
        {
            config.SuperKrangleBaseRedrawDelayFrames = redrawDelay;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Base frame delay before the next queued redraw. Actual delay scales with crowd size.");

        ImGui.EndDisabled();
    }

    private static void DrawApplyFromPresetOptions(Configuration config)
    {
        var applyAppearance = config.SuperKrangleApplyAppearance;
        if (UiGui.Checkbox("Appearance", ref applyAppearance))
        {
            config.SuperKrangleApplyAppearance = applyAppearance;
            config.Save();
        }
        SameLineIfFits(CheckboxWidth("Head"));
        var applyHead = config.SuperKrangleApplyHead;
        if (UiGui.Checkbox("Head", ref applyHead))
        {
            config.SuperKrangleApplyHead = applyHead;
            config.Save();
        }
        SameLineIfFits(CheckboxWidth("Body"));
        var applyBody = config.SuperKrangleApplyBody;
        if (UiGui.Checkbox("Body", ref applyBody))
        {
            config.SuperKrangleApplyBody = applyBody;
            config.Save();
        }

        var applyHands = config.SuperKrangleApplyHands;
        if (UiGui.Checkbox("Hands", ref applyHands))
        {
            config.SuperKrangleApplyHands = applyHands;
            config.Save();
        }
        SameLineIfFits(CheckboxWidth("Legs"));
        var applyLegs = config.SuperKrangleApplyLegs;
        if (UiGui.Checkbox("Legs", ref applyLegs))
        {
            config.SuperKrangleApplyLegs = applyLegs;
            config.Save();
        }
        SameLineIfFits(CheckboxWidth("Feet"));
        var applyFeet = config.SuperKrangleApplyFeet;
        if (UiGui.Checkbox("Feet", ref applyFeet))
        {
            config.SuperKrangleApplyFeet = applyFeet;
            config.Save();
        }

        var applyAccessories = config.SuperKrangleApplyAccessories;
        if (UiGui.Checkbox("Accessories", ref applyAccessories))
        {
            config.SuperKrangleApplyAccessories = applyAccessories;
            config.Save();
        }
        SameLineIfFits(CheckboxWidth("Weapons"));
        var applyWeapons = config.SuperKrangleApplyWeapons;
        if (UiGui.Checkbox("Weapons", ref applyWeapons))
        {
            config.SuperKrangleApplyWeapons = applyWeapons;
            config.Save();
        }
    }

    private void DrawSoulThiefTab(Configuration config)
    {
        ImGui.Spacing();
        UiGui.Text("Soul Thief");
        ImGui.Separator();

        var soulThiefEnabled = config.SoulThiefEnabled;
        if (UiGui.Checkbox("Enable Soul Thief", ref soulThiefEnabled))
        {
            config.SoulThiefEnabled = soulThiefEnabled;
            config.Save();
        }

        ImGui.BeginDisabled(!config.SoulThiefEnabled);

        var capturePlayers = config.SoulThiefCapturePlayers;
        if (UiGui.Checkbox("Capture Players", ref capturePlayers))
        {
            config.SoulThiefCapturePlayers = capturePlayers;
            config.Save();
        }

        var captureNpcs = config.SoulThiefCaptureNpcs;
        if (UiGui.Checkbox("Capture NPCs", ref captureNpcs))
        {
            config.SoulThiefCaptureNpcs = captureNpcs;
            config.Save();
        }

        var captureChocobos = config.SoulThiefCaptureChocobos;
        if (UiGui.Checkbox("Capture Chocobos", ref captureChocobos))
        {
            config.SoulThiefCaptureChocobos = captureChocobos;
            config.Save();
        }

        var intervalSeconds = config.SoulThiefCaptureIntervalSeconds;
        if (UiGui.SliderInt("Capture Interval", ref intervalSeconds, Configuration.MinSoulThiefCaptureIntervalSeconds, Configuration.MaxSoulThiefCaptureIntervalSeconds))
        {
            config.SoulThiefCaptureIntervalSeconds = intervalSeconds;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Seconds between Soul Thief capture passes. Appearance scanning still runs on Krangler's 5-second cadence.");

        ImGui.EndDisabled();

        ImGui.Spacing();
        UiGui.TextWrapped(UiText.F($"Last capture: {config.SoulThiefLastCapturedPlayers} players, {config.SoulThiefLastCapturedNpcs} NPCs, {config.SoulThiefLastCapturedChocobos} chocobos"));
        UiGui.TextWrapped(UiText.F($"Preset folders: {plugin.GlamourerPresetService.UserPresetsDir}\\players, \\npcs, \\chocobos"));
    }

    private void DrawDebugTab(Configuration config)
    {
        supportLog.Draw(Plugin.PluginInterface, key => UiText.T(key),
            path => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true }), ex => Plugin.Log.Error(ex, "Dalamud log export failed."), Plugin.CommandManager);
        ImGui.Spacing();
        UiGui.Text("Debug");
        ImGui.Separator();

        if (!plugin.ShowDebugOptions)
        {
            UiGui.TextDisabled("Debug controls hidden. Use /kr debug to toggle.");
            return;
        }

        var disableEventOverride = config.DisableDateBasedSuperKrangleEvent;
        if (UiGui.Checkbox("Disable date-based Wuk Lamat auto-event", ref disableEventOverride))
            plugin.SetDateBasedSuperKrangleEventSuppressed(disableEventOverride);

        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Suppress the March 31 through April 2 Wuk Lamat auto-event so normal Super Krangle testing is possible.");

        if (plugin.IsDateBasedSuperKrangleWindowActive)
        {
            var message = plugin.IsDateBasedSuperKrangleEventCurrentlyForced
                ? "The date-based Wuk Lamat override is currently active."
                : "The date-based Wuk Lamat override is currently suppressed by debug settings.";
            UiGui.TextColored(KranglerPresentation.Pending, message);
        }
    }

    private static void DrawStatus(Configuration config)
    {
        using var font=UiText.Font(UiFontRole.Heading);
        var color=config.Enabled?KranglerPresentation.Ready:MaterialTheme.Current.Colors.OnSurfaceVariant;
        var min=ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddCircleFilled(min+new Vector2(KranglerPresentation.Compact?22:27,KranglerPresentation.Compact?21:27)*MaterialTheme.Metrics.Scale,(KranglerPresentation.Compact?16:18)*MaterialTheme.Metrics.Scale,MaterialCanvas.Color(color));
        ImGui.Dummy(new Vector2((KranglerPresentation.Compact?50:55)*MaterialTheme.Metrics.Scale,ImGui.GetTextLineHeight()));ImGui.SameLine();
        UiGui.TextColored(color,config.Enabled?"KRANGLING ACTIVE":"Disabled");
    }

    public void QueueResetToOrigin()
    {
        queuedPosition = new Vector2(1f, 1f);
        queuedRandomVisibleJump = false;
    }

    public void QueueRandomVisibleJump()
    {
        queuedPosition = null;
        queuedRandomVisibleJump = true;
    }

    private void ApplyQueuedWindowPlacement()
    {
        if (!queuedPosition.HasValue && !queuedRandomVisibleJump)
            return;

        var targetPosition = queuedPosition ?? BuildRandomVisiblePosition();
        ImGui.SetWindowPos(targetPosition, ImGuiCond.Always);

        queuedPosition = null;
        queuedRandomVisibleJump = false;
    }

    private Vector2 BuildRandomVisiblePosition()
    {
        var viewport = ImGui.GetMainViewport();
        var workPos = viewport.WorkPos;
        var workSize = viewport.WorkSize;
        var windowSize = windowMotion.GetLogicalSize();

        var fallbackSize = Size ?? new Vector2(520f, 760f);
        var width = windowSize.X > 0f ? windowSize.X : fallbackSize.X;
        var height = windowSize.Y > 0f ? windowSize.Y : fallbackSize.Y;
        var margin = 24f;

        var minX = workPos.X + margin;
        var minY = workPos.Y + margin;
        var maxX = MathF.Max(minX, workPos.X + workSize.X - width - margin);
        var maxY = MathF.Max(minY, workPos.Y + workSize.Y - height - margin);

        if (maxX <= minX || maxY <= minY)
            return new Vector2(1f, 1f);

        var x = minX + (Random.Shared.NextSingle() * (maxX - minX));
        var y = minY + (Random.Shared.NextSingle() * (maxY - minY));
        return new Vector2(x, y);
    }

    private static float IconInputWidth(string label)
    {
        using var font=UiText.Font(UiFontRole.Action);
        var labelWidth=MaterialText.Measure(UiText.F("{0} Icon",UiText.T(label))).X;
        return (labelWidth+ImGui.GetStyle().ItemSpacing.X*2+(KranglerPresentation.Compact?64:80)*MaterialTheme.Metrics.Scale+
            Math.Max(100*MaterialTheme.Metrics.Scale,UiGui.TextMinimum(64)))/MaterialTheme.Metrics.Scale;
    }

    private bool DrawIconInputs(string label, ref string value, string fallback)
    {
        using var font=UiText.Font(UiFontRole.Action);
        var scale=MaterialTheme.Metrics.Scale;
        var width=IconInputWidth(label)*scale;
        MaterialLayout.FitNextItemWidth(width,width);
        ImGui.BeginGroup();
        var origin=ImGui.GetCursorScreenPos();var height=(KranglerPresentation.Compact?56:80)*scale;
        using (UiText.Font(UiFontRole.Action))
        {
            var caption=UiText.F("{0} Icon",UiText.T(label));
            var captionSize=MaterialText.Measure(caption);
            ImGui.Dummy(new Vector2(captionSize.X,height));
            MaterialText.AddText(ImGui.GetWindowDrawList(),origin+new Vector2(0,(height-captionSize.Y)*.5f),ImGui.GetColorU32(ImGuiCol.Text),caption);
        }
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X,origin.Y));
        var updated = false;
        var glyph = value;
        var glyphWidth=(KranglerPresentation.Compact?64:80)*scale;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(ImGui.GetStyle().FramePadding.X,Math.Max(0,(height-ImGui.GetTextLineHeight())*.5f)));
        ImGui.SetNextItemWidth(glyphWidth);
        if (UiGui.InputText($"{label} Icon", ref glyph, 8,ImGuiInputTextFlags.None,false,glyphWidth))
        {
            value = SanitizeIconInput(glyph, fallback);
            updated = true;
        }
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered()) UiGui.SetTooltip(UiText.F("Shown when Krangler is {0}",UiText.T(label))+"\n"+UiText.T("DTR Icons (max 3 characters)"));

        var code = FormatIconCode(value);
        ImGui.SameLine();
        var codeHeight=(KranglerPresentation.Compact?54:64)*scale;
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X,origin.Y+(height-codeHeight)*.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(ImGui.GetStyle().FramePadding.X,Math.Max(0,(codeHeight-ImGui.GetTextLineHeight())*.5f)));
        ImGui.SetNextItemWidth(Math.Max(100*scale,UiGui.TextMinimum(64)));
        if (UiGui.InputText($"{label} Icon Code", ref code, 64,showLabel:false))
        {
            var parsed = ParseIconCode(code, value);
            value = SanitizeIconInput(parsed, fallback);
            updated = true;
        }
        ImGui.PopStyleVar();
        if (ImGui.IsItemHovered()) UiGui.SetTooltip("Customize the glyphs used for enabled and disabled icon modes.");
        ImGui.EndGroup();

        return updated;
    }

    private static string SanitizeIconInput(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var trimmed = value.Trim();
        return trimmed.Length > 3 ? trimmed.Substring(0, 3) : trimmed;
    }

    private static string FormatIconCode(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append("\\u");
            sb.Append(rune.Value.ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static string ParseIconCode(string input, string fallback)
    {
        if (string.IsNullOrWhiteSpace(input))
            return fallback;

        var parts = input.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            if (sb.Length >= 3) break;

            var token = part.Trim();
            if (token.StartsWith("\\u", StringComparison.OrdinalIgnoreCase))
                token = token[2..];
            else if (token.StartsWith("u", StringComparison.OrdinalIgnoreCase))
                token = token[1..];
            else if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                token = token[2..];

            if (int.TryParse(token, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var codepoint))
            {
                sb.Append(char.ConvertFromUtf32(codepoint));
            }
        }

        return sb.Length == 0 ? fallback : sb.ToString();
    }

    private static void HelpMarker(string desc)
    {
        ImGui.SameLine();
        UiGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20.0f);
            UiGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    private bool DrawPresetSelectionCombo(string label, ref string value, IReadOnlyList<string> presetNames, bool includeUseGlobal)
        => DrawPresetSelectionCombo(label, ref value, presetNames, includeUseGlobal, true);

    private bool DrawPresetSelectionCombo(string label, ref string value, IReadOnlyList<string> presetNames, bool includeUseGlobal, bool includeRandom)
    {
        var fallbackPreview = includeUseGlobal ? "Use Global" : includeRandom ? "Random" : "Select preset";
        var preview = string.IsNullOrWhiteSpace(value)
            ? fallbackPreview
            : value;
        var changed = false;

        var translatePreview=string.IsNullOrWhiteSpace(value) || (includeUseGlobal && value=="Use Global") || (includeRandom && value=="Random");
        if (UiGui.BeginCombo(label, preview,translatePreview:translatePreview))
        {
            ImGui.SetNextItemWidth(-1f);
            UiGui.InputTextWithHint($"##PresetSearch_{label}", "Search presets...", ref presetSearch, 128);
            ImGui.Separator();

            if (includeUseGlobal)
            {
                changed |= DrawSelectionOption("Use Global", ref value);
            }

            if (includeRandom)
            {
                changed |= DrawSelectionOption("Random", ref value);
            }

            var filteredPresetNames = string.IsNullOrWhiteSpace(presetSearch)
                ? presetNames
                : presetNames.Where(presetName =>
                    presetName.Contains(presetSearch, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var presetName in filteredPresetNames)
            {
                changed |= DrawSelectionOption(presetName, ref value, external:true);
            }

            if (!filteredPresetNames.Any())
                UiGui.TextDisabled("No presets match the current search.");

            ImGui.EndCombo();
        }

        return changed;
    }

    private void DrawAmongusSection(Configuration config, IReadOnlyList<string> presetNames)
    {
        ImGui.Spacing();
        UiGui.Text("Amongus");
        ImGui.Separator();

        var amongusEnabled = config.AmongusEnabled;
        if (UiGui.Checkbox("Amongus", ref amongusEnabled))
        {
            config.AmongusEnabled = amongusEnabled;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            UiGui.SetTooltip("Replace exact battle and event NPC names with imported local presets.");
        }

        ImGui.BeginDisabled(!config.AmongusEnabled);

        UiGui.TextDisabled(UiText.F($"{config.AmongusNpcReplacements.Count}/{Configuration.MaxAmongusNpcReplacements}"));
        ImGui.SameLine();

        if (config.AmongusNpcReplacements.Count < Configuration.MaxAmongusNpcReplacements)
        {
            if (UiGui.SmallButton("+"))
            {
                config.AmongusNpcReplacements.Add(new AmongusNpcReplacement());
                config.Save();
            }
        }
        else
        {
            UiGui.TextDisabled("+");
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip("Maximum 100 NPC replacements.");
        }

        var removeIndex = -1;
        for (var i = 0; i < config.AmongusNpcReplacements.Count; i++)
        {
            var replacement = config.AmongusNpcReplacements[i];
            ImGui.PushID(i);

            var rowEnabled = replacement.Enabled;
            if (UiGui.Checkbox("##AmongusRowEnabled", ref rowEnabled))
            {
                replacement.Enabled = rowEnabled;
                config.Save();
            }
            if (ImGui.IsItemHovered())
            {
                UiGui.SetTooltip("Enable this exact NPC replacement.");
            }

            SameLineIfFits(150);
            ImGui.SetNextItemWidth(150f*MaterialTheme.Metrics.Scale);
            var npcName = replacement.NpcName ?? string.Empty;
            if (UiGui.InputTextWithHint("##AmongusNpcName", "NPC name", ref npcName, 64))
            {
                replacement.NpcName = npcName;
                config.Save();
            }

            SameLineIfFits(220+(MaterialText.Measure(UiText.T("Preset")).X+ImGui.GetStyle().ItemInnerSpacing.X)/MaterialTheme.Metrics.Scale);
            ImGui.SetNextItemWidth(220f*MaterialTheme.Metrics.Scale);
            var presetKey = replacement.PresetKey ?? string.Empty;
            if (DrawPresetSelectionCombo("Preset##AmongusPreset", ref presetKey, presetNames, false, false))
            {
                replacement.PresetKey = presetKey;
                config.Save();
            }

            SameLineIfFits((MaterialText.Measure("-").X+ImGui.GetStyle().FramePadding.X*2)/MaterialTheme.Metrics.Scale);
            if (UiGui.SmallButton("-"))
                removeIndex = i;

            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            config.AmongusNpcReplacements.RemoveAt(removeIndex);
            config.Save();
        }

        ImGui.EndDisabled();
    }

    private static bool DrawSelectionOption(string option, ref string value,bool external=false)
    {
        var isSelected = string.Equals(value, option, StringComparison.OrdinalIgnoreCase);
        if (!(external?MaterialText.Selectable(option,isSelected):UiGui.Selectable(option, isSelected)))
            return false;

        value = option;
        return true;
    }

    private static bool EnsureSlotSelections(Configuration config)
    {
        var changed = false;

        while (config.SuperKranglePartySlotSelections.Count > 8)
        {
            config.SuperKranglePartySlotSelections.RemoveAt(config.SuperKranglePartySlotSelections.Count - 1);
            changed = true;
        }

        while (config.SuperKranglePartySlotSelections.Count < 8)
        {
            config.SuperKranglePartySlotSelections.Add("Use Global");
            changed = true;
        }

        return changed;
    }

    private static string GetPartySlotLabel(int index)
    {
        if (index == 0)
        {
            var localName = Plugin.ObjectTable.LocalPlayer?.Name.ToString();
            return string.IsNullOrWhiteSpace(localName) ? "You" : $"You ({localName})";
        }

        if (index < Plugin.PartyList.Length)
        {
            var memberName = Plugin.PartyList[index]?.Name.ToString();
            if (!string.IsNullOrWhiteSpace(memberName))
                return $"Party {index + 1} ({memberName})";
        }

        return $"Party {index + 1}";
    }

    public void Dispose() { }
}
