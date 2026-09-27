using System;
using System.Collections.Generic;
using System.ComponentModel;

using Avalonia.Threading;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Launch;
using ClientLogic.Settings;
using ClientLogic.UI;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain;
using DTAClient.Domain.Multiplayer.CnCNet;

using Rampastring.Tools;

namespace AvClientView.ViewModels;

/// <summary>
/// The options window, as DXMainClient's OptionsWindow: the Display, Audio, Game, CnCNet and Storage tabs (Updater
/// and Components can't be selected, as in XNA when there are no update mirrors: the Avalonia client has no updater
/// yet). Opening loads every tab; Save refreshes the file settings (and stops with a notice if one changed), saves
/// every tab and the settings INI, and offers a restart when a change needs one.
/// </summary>
public sealed partial class OptionsWindowViewModel : ObservableObject
{
    public const int DISPLAY_INDEX = 0;
    public const int AUDIO_INDEX = 1;
    public const int GAME_INDEX = 2;
    public const int CNCNET_INDEX = 3;
    public const int STORAGE_INDEX = 4;
    public const int UPDATER_INDEX = 5;
    public const int COMPONENTS_INDEX = 6;

    private readonly IDialogService dialogs;

    public OptionsWindowViewModel(DirectDrawWrapperManager directDrawWrapperManager, GameCollection gameCollection,
        TunnelHandler tunnelHandler, GameProcessService gameProcess, IDialogService dialogs, HotkeyWindowViewModel hotkeys)
    {
        this.dialogs = dialogs;
        Hotkeys = hotkeys;

        Display = new DisplayOptionsModel(directDrawWrapperManager, new ScreenResolutions(new Services.WindowsDisplayModeSource()));
        Audio = new AudioOptionsModel();
        Game = new GameOptionsModel();
        CnCNet = new CnCNetOptionsModel(gameCollection, tunnelHandler);
        Storage = new StorageOptionsModel();
        Panels = [Display, Audio, Game, CnCNet, Storage];

        Audio.PropertyChanged += Audio_PropertyChanged;
        CnCNet.PropertyChanged += CnCNet_PropertyChanged;

        gameProcess.GameProcessExited += () => Dispatcher.UIThread.Post(() =>
        {
            foreach (OptionsPanelModel panel in Panels)
                panel.OnGameExited();
        });
    }

    /// <summary>The Game tab's Configure Hotkeys window.</summary>
    public HotkeyWindowViewModel Hotkeys { get; }

    public DisplayOptionsModel Display { get; }

    public AudioOptionsModel Audio { get; }

    public GameOptionsModel Game { get; }

    public CnCNetOptionsModel CnCNet { get; }

    public StorageOptionsModel Storage { get; }

    public IReadOnlyList<OptionsPanelModel> Panels { get; }

    /// <summary>The tab names, in the XNA tab control's order.</summary>
    public static IReadOnlyList<string> TabNames { get; } =
    [
        "Display".L10N("Client:DTAConfig:TabDisplay"),
        "Audio".L10N("Client:DTAConfig:TabAudio"),
        "Game".L10N("Client:DTAConfig:TabGame"),
        "CnCNet".L10N("Client:DTAConfig:TabCnCNet"),
        "Storage".L10N("Client:DTAConfig:TabStorage"),
        "Updater".L10N("Client:DTAConfig:TabUpdater"),
        "Components".L10N("Client:DTAConfig:TabComponents"),
    ];

    public static bool IsTabSelectable(int index) => index < UPDATER_INDEX;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private int selectedTab;

    /// <summary>The client volume while the window is open (the client's sounds follow the slider), or null.</summary>
    public static double? PreviewClientVolume { get; private set; }

    /// <summary>The user chose to restart the client after saving.</summary>
    public event EventHandler RestartRequested;

    private bool suppressP2PWarning;

    private void Audio_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioOptionsModel.ClientVolume) && IsOpen)
            PreviewClientVolume = Audio.ClientVolume / 10.0;
    }

    private void CnCNet_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CnCNetOptionsModel.EnableP2P) || suppressP2PWarning || !IsOpen || !CnCNet.EnableP2P)
            return;

        // The XNA warning: P2P stays off unless the user agrees
        SetP2PQuietly(false);
        dialogs.Confirm("Direct P2P Warning".L10N("Client:DTAConfig:P2PWarningTitle"),
            ("Enabling P2P allows players to connect directly to each other\n" +
             "instead of routing traffic through CnCNet relay servers.\n\n" +
             "This will share your IP address with all other players\n" +
             "in your game session. Your IP address can reveal your\n" +
             "approximate location and may expose you to risks if\n" +
             "shared with someone with malicious intent.\n\n" +
             "Only enable this if you trust the players you play with.\n\n" +
             "Do you want to enable P2P connections?").L10N("Client:DTAConfig:P2PWarningText"),
            () =>
            {
                SetP2PQuietly(true);
                CnCNet.ConfirmP2P();
            });
    }

    private void SetP2PQuietly(bool value)
    {
        suppressP2PWarning = true;
        CnCNet.EnableP2P = value;
        suppressP2PWarning = false;
    }

    public void Open()
    {
        suppressP2PWarning = true;
        foreach (OptionsPanelModel panel in Panels)
            panel.Load();
        suppressP2PWarning = false;

        RefreshOptionPanels();
        PreviewClientVolume = null;
        IsOpen = true;
    }

    public void Cancel()
    {
        PreviewClientVolume = null;
        IsOpen = false;
    }

    public void Save()
    {
        if (RefreshOptionPanels())
            return;

        bool restartRequired = false;
        try
        {
            foreach (OptionsPanelModel panel in Panels)
                restartRequired = panel.Save() || restartRequired;

            UserINISettings.Instance.SaveSettings();
        }
        catch (Exception ex)
        {
            Logger.Log("Saving settings failed! Error message: " + ex);
            dialogs.ShowMessage("Saving Settings Failed".L10N("Client:DTAConfig:SaveSettingFailTitle"),
                "Saving settings failed! Error message:".L10N("Client:DTAConfig:SaveSettingFailText") + " " + ex.Message);
        }

        PreviewClientVolume = null;
        IsOpen = false;

        if (restartRequired)
        {
            dialogs.Confirm("Restart Required".L10N("Client:DTAConfig:RestartClientTitle"),
                ("The client needs to be restarted for some of the changes to take effect.\n\n" +
                "Do you want to restart now?").L10N("Client:DTAConfig:RestartClientText"),
                () => RestartRequested?.Invoke(this, EventArgs.Empty));
        }
    }

    /// <summary>Re-checks the file settings; shows the XNA notice and returns true if a value had to change.</summary>
    private bool RefreshOptionPanels()
    {
        bool optionValuesChanged = false;
        foreach (OptionsPanelModel panel in Panels)
            optionValuesChanged = panel.Refresh() || optionValuesChanged;

        if (optionValuesChanged)
        {
            dialogs.ShowMessage("Setting Value(s) Changed".L10N("Client:DTAConfig:SettingChangedTitle"),
                ("One or more setting values are\n" +
                "no longer available and were changed.\n\n" +
                "You may want to verify the new setting\n" +
                "values in client's options window.").L10N("Client:DTAConfig:SettingChangedText"));
            return true;
        }

        return false;
    }
}
