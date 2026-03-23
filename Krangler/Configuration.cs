using Dalamud.Configuration;
using System;

namespace Krangler;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Master toggle
    public bool Enabled { get; set; } = false;

    // Feature toggles (all enabled by default)
    public bool KrangleNames { get; set; } = true;
    public bool KrangleGenders { get; set; } = true;
    public bool KrangleRaces { get; set; } = true;
    public bool KrangleAppearance { get; set; } = true;

    // Special mode (disabled by default)
    public bool SuperKrangleMaster4000 { get; set; } = false;

    // DTR bar settings
    public bool DtrBarEnabled { get; set; } = true;
    public int DtrBarMode { get; set; } = 0; // 0=text-only, 1=icon+text, 2=icon-only
    public string DtrIconEnabled { get; set; } = "\uE03C";
    public string DtrIconDisabled { get; set; } = "\uE03D";

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
