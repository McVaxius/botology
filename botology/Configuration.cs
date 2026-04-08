using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace botology;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool PluginEnabled { get; set; } = true;
    public bool DtrBarEnabled { get; set; } = true;
    public int DtrBarMode { get; set; } = 1;
    public string DtrIconEnabled { get; set; } = "\uE044";
    public string DtrIconDisabled { get; set; } = "\uE04C";
    public bool ToastNotifications { get; set; } = true;
    public bool BlockingPopupNotifications { get; set; } = false;
    public bool OpenWindowOnAssessmentChange { get; set; } = false;
    public bool OpenMainWindowOnLoad { get; set; } = false;
    public bool HideUninstalledPlugins { get; set; } = false;
    public bool ShowInstalledColumn { get; set; } = true;
    public bool ShowUpdateColumn { get; set; } = true;
    public bool ShowRepoColumn { get; set; } = true;
    public bool ShowDtrColumn { get; set; } = true;
    public bool ShowNotesColumn { get; set; } = true;
    public bool ShowIgnoreColumn { get; set; } = true;
    public bool ShowDownloadsColumn { get; set; } = false;
    public bool ShowLastUpdateColumn { get; set; } = false;
    public bool ShowDalamudApiLevelColumn { get; set; } = false;
    public bool ShowAuthorColumn { get; set; } = false;
    public List<string> IgnoredPluginIds { get; set; } = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
