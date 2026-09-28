using System.Collections.ObjectModel;

using ClientCore.Extensions;

using ClientLogic.Updates;

using ClientUpdater;

using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientLogic.Settings;

/// <summary>
/// The options window's Updater tab (DXMainClient's UpdaterOptionsPanel): the download mirrors in priority order,
/// moved up and down, automatic update checks, and the force update's version reset.
/// </summary>
public sealed partial class UpdaterOptionsModel(IUpdater updater) : OptionsPanelModel("UpdaterOptionsPanel")
{
    /// <summary>The mirrors as the list shows them: "name (location)".</summary>
    public ObservableCollection<string> Mirrors { get; } = [];

    [ObservableProperty]
    private int selectedMirrorIndex = -1;

    [ObservableProperty]
    private bool autoCheck;

    /// <summary>Force Update can be used (only from the main menu).</summary>
    [ObservableProperty]
    private bool canForceUpdate = true;

    public override void Load()
    {
        base.Load();

        Mirrors.Clear();
        foreach (UpdateMirror mirror in updater.UpdateMirrors)
            Mirrors.Add(MirrorText(mirror));

        SelectedMirrorIndex = -1;
        AutoCheck = IniSettings.CheckForUpdates;
    }

    public override bool Save()
    {
        bool restartRequired = base.Save();

        IniSettings.CheckForUpdates.Value = AutoCheck;
        IniSettings.SettingsIni.EraseSectionKeys("DownloadMirrors");

        int id = 0;
        foreach (UpdateMirror mirror in updater.UpdateMirrors)
        {
            IniSettings.SettingsIni.SetStringValue("DownloadMirrors", id.ToString(), mirror.Name);
            id++;
        }

        return restartRequired;
    }

    public override void ToggleMainMenuOnlyOptions(bool enable) => CanForceUpdate = enable;

    public void MoveUp()
    {
        int index = SelectedMirrorIndex;
        if (index < 1 || index >= Mirrors.Count)
            return;

        Mirrors.Move(index, index - 1);
        SelectedMirrorIndex = index - 1;
        updater.MoveMirrorUp(index);
    }

    public void MoveDown()
    {
        int index = SelectedMirrorIndex;
        if (index < 0 || index > Mirrors.Count - 2)
            return;

        Mirrors.Move(index, index + 1);
        SelectedMirrorIndex = index + 1;
        updater.MoveMirrorDown(index);
    }

    /// <summary>The user confirmed the force update: the next check sees every file as outdated.</summary>
    public void PrepareForceUpdate() => updater.ClearVersionInfo();

    private static string MirrorText(UpdateMirror mirror)
    {
        string name = mirror.Name.L10N($"INI:UpdateMirrors:{mirror.Name}:Name");
        string location = mirror.Location.L10N($"INI:UpdateMirrors:{mirror.Name}:Location");
        return name + (!string.IsNullOrEmpty(location) ? $" ({location})" : string.Empty);
    }

    /// <summary>The force update's confirmation text (UpdaterOptionsPanel).</summary>
    public static string ForceUpdateConfirmText =>
        ("WARNING: Force update will result in files being re-verified\n" +
        "and re-downloaded. While this may fix problems with game\n" +
        "files, this also may delete some custom modifications\n" +
        "made to this installation. Use at your own risk!\n\n" +
        "If you proceed, the options window will close and the\n" +
        "client will proceed to checking for updates.\n\n" +
        "Do you really want to force update?").L10N("Client:DTAConfig:ForceUpdateConfirmText") + "\n";
}
