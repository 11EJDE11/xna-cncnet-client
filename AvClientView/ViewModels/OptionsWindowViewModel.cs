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
/// The options window, as DXMainClient's OptionsWindow: the Display, Audio, Game, CnCNet, Storage and Updater tabs
/// (Updater and Components need update mirrors, Components also custom components). Opening loads every tab; Save refreshes the file settings (and stops with a notice if one changed), saves
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
    private readonly ClientLogic.Updates.IUpdater updater;

    public OptionsWindowViewModel(DirectDrawWrapperManager directDrawWrapperManager, GameCollection gameCollection,
        TunnelHandler tunnelHandler, GameProcessService gameProcess, IDialogService dialogs, HotkeyWindowViewModel hotkeys,
        ClientLogic.Updates.IUpdater updater, IUiDispatcher uiDispatcher)
    {
        this.dialogs = dialogs;
        this.updater = updater;
        Hotkeys = hotkeys;

        Display = new DisplayOptionsModel(directDrawWrapperManager, new ScreenResolutions(new Services.WindowsDisplayModeSource()));
        Audio = new AudioOptionsModel();
        Game = new GameOptionsModel();
        CnCNet = new CnCNetOptionsModel(gameCollection, tunnelHandler);
        Storage = new StorageOptionsModel();
        Updater = new UpdaterOptionsModel(updater);
        Components = new ComponentsOptionsModel(updater, dialogs, uiDispatcher);
        Panels = [Display, Audio, Game, CnCNet, Storage, Updater, Components];

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

    public UpdaterOptionsModel Updater { get; }

    public ComponentsOptionsModel Components { get; }

    /// <summary>Opens the window on the Components tab (the main menu's custom component update question).</summary>
    public void OpenComponents(bool mainMenuOnlyOptions)
    {
        Open(mainMenuOnlyOptions);
        SelectedTab = COMPONENTS_INDEX;
    }

    /// <summary>The user confirmed Force Update: the window closed and the main menu updates (OnForceUpdate).</summary>
    public event EventHandler ForceUpdateRequested;

    /// <summary>The Force Update button: asks first (UpdaterOptionsPanel).</summary>
    public void ConfirmForceUpdate() =>
        dialogs.Confirm("Force Update Confirmation".L10N("Client:DTAConfig:ForceUpdateConfirmTitle"), UpdaterOptionsModel.ForceUpdateConfirmText, () =>
        {
            Updater.PrepareForceUpdate();
            PreviewClientVolume = null;
            IsOpen = false;
            ForceUpdateRequested?.Invoke(this, EventArgs.Empty);
        });

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

    /// <summary>
    /// The tabs that can be selected: Updater only with update mirrors (not in ModMode); Components isn't in the
    /// Avalonia client yet.
    /// </summary>
    public bool IsTabSelectable(int index) => index switch
    {
        UPDATER_INDEX => !ClientConfiguration.Instance.ModMode && updater.HasUpdateMirrors,
        COMPONENTS_INDEX => !ClientConfiguration.Instance.ModMode && updater.HasUpdateMirrors && updater.HasCustomComponents,
        _ => true,
    };

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

    /// <param name="mainMenuOnlyOptions">Opened with only the main menu open (not in LAN mode or a game room): Force
    /// Update can be used.</param>
    public void Open(bool mainMenuOnlyOptions = true)
    {
        suppressP2PWarning = true;
        foreach (OptionsPanelModel panel in Panels)
        {
            panel.Load();
            panel.ToggleMainMenuOnlyOptions(mainMenuOnlyOptions);
        }
        suppressP2PWarning = false;

        RefreshOptionPanels();
        PreviewClientVolume = null;
        IsOpen = true;
    }

    public void Cancel()
    {
        if (ComponentsOptionsModel.IsDownloadInProgress)
        {
            ConfirmCancellingDownloads(Close);
            return;
        }

        Close();
    }

    private void Close()
    {
        PreviewClientVolume = null;
        IsOpen = false;
    }

    /// <summary>Closing the window cancels the component downloads: asks first (OptionsWindow).</summary>
    private void ConfirmCancellingDownloads(Action then)
    {
        (string title, string text) = ComponentsOptionsModel.DownloadsInProgressQuestion;
        dialogs.Confirm(title, text, () =>
        {
            Components.CancelAllDownloads();
            then();
        });
    }

    public void Save()
    {
        if (ComponentsOptionsModel.IsDownloadInProgress)
        {
            ConfirmCancellingDownloads(SaveSettings);
            return;
        }

        SaveSettings();
    }

    private void SaveSettings()
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
