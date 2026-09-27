using System.Collections.Generic;
using System.Linq;

using ClientCore;
using ClientCore.Enums;
using ClientCore.Extensions;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer.CnCNet;

namespace ClientLogic.Settings;

/// <summary>A game in the CnCNet tab's "Show game rooms from the following games" list.</summary>
public sealed partial class FollowedGameSetting(CnCNetGame game) : ObservableObject
{
    public CnCNetGame Game { get; } = game;

    /// <summary>The check-box name (and the [Channels] key): the game's upper-case internal name.</summary>
    public string Name { get; } = game.InternalName.ToUpperInvariant();

    [ObservableProperty]
    private bool isFollowed;

    /// <summary>False for the local game, which is always followed.</summary>
    [ObservableProperty]
    private bool canChange = true;
}

/// <summary>The options window's CnCNet tab, as DXMainClient's CnCNetOptionsPanel.</summary>
public sealed partial class CnCNetOptionsModel : OptionsPanelModel
{
    private readonly TunnelHandler tunnelHandler;

    public CnCNetOptionsModel(GameCollection gameCollection, TunnelHandler tunnelHandler) : base("CnCNetOptionsPanel")
    {
        this.tunnelHandler = tunnelHandler;

        FollowedGames = gameCollection.GameList
            .Where(game => game.Supported && !string.IsNullOrEmpty(game.GameBroadcastChannel))
            .Select(game => new FollowedGameSetting(game))
            .ToList();
    }

    /// <summary>The "Allow private messages from" items, in the XNA drop-down's order.</summary>
    public static IReadOnlyList<(string Text, AllowPrivateMessagesFromEnum Value)> PrivateMessageOptions { get; } =
    [
        ("All".L10N("Client:DTAConfig:PMAll"), AllowPrivateMessagesFromEnum.All),
        ("Current channel".L10N("Client:DTAConfig:PMCurrentChannel"), AllowPrivateMessagesFromEnum.CurrentChannel),
        ("Friends".L10N("Client:DTAConfig:PMFriends"), AllowPrivateMessagesFromEnum.Friends),
        ("None".L10N("Client:DTAConfig:PMNone"), AllowPrivateMessagesFromEnum.None),
    ];

    /// <summary>The "Tunnel mode when hosting" items, in the XNA drop-down's order.</summary>
    public static IReadOnlyList<(string Text, TunnelMode Value)> TunnelModeOptions { get; } =
    [
        ("Dynamic (V3)".L10N("Client:Main:TunnelSelModeDynamic"), TunnelMode.V3Dynamic),
        ("Static (V3)".L10N("Client:Main:TunnelSelModeStatic"), TunnelMode.V3Static),
        ("Legacy (V2)".L10N("Client:Main:TunnelSelModeLegacy"), TunnelMode.V2Legacy),
    ];

    public IReadOnlyList<FollowedGameSetting> FollowedGames { get; }

    public bool DiscordIntegrationGloballyDisabled => ClientConfiguration.Instance.DiscordIntegrationGloballyDisabled;

    [ObservableProperty]
    private bool pingUnofficialTunnels;

    [ObservableProperty]
    private bool writeInstallPathToRegistry;

    [ObservableProperty]
    private bool disableMainMenuHotkeys;

    [ObservableProperty]
    private bool notifyOnUserListChange;

    [ObservableProperty]
    private bool disablePrivateMessagePopup;

    [ObservableProperty]
    private int allowPrivateMessagesFromIndex;

    [ObservableProperty]
    private bool skipLoginWindow;

    [ObservableProperty]
    private bool persistentMode;

    [ObservableProperty]
    private bool connectOnStartup;

    /// <summary>"Connect automatically" needs both "Skip login dialog" and "Stay connected".</summary>
    [ObservableProperty]
    private bool canConnectOnStartup;

    [ObservableProperty]
    private bool discordIntegration;

    [ObservableProperty]
    private bool allowGameInvitesFromFriendsOnly;

    [ObservableProperty]
    private bool steamIntegration;

    [ObservableProperty]
    private int tunnelModeIndex;

    [ObservableProperty]
    private bool enableP2P;

    partial void OnSkipLoginWindowChanged(bool value) => CheckConnectOnStartupAllowance();

    partial void OnPersistentModeChanged(bool value) => CheckConnectOnStartupAllowance();

    private void CheckConnectOnStartupAllowance()
    {
        if (!SkipLoginWindow || !PersistentMode)
        {
            CanConnectOnStartup = false;
            ConnectOnStartup = false;
            return;
        }

        CanConnectOnStartup = true;
    }

    /// <summary>The user agreed to the P2P warning: the XNA panel clears the P2P endpoint cache.</summary>
    public void ConfirmP2P() => tunnelHandler.ClearP2PEndpointCache();

    public override void Load()
    {
        base.Load();
        PingUnofficialTunnels = IniSettings.PingUnofficialCnCNetTunnels;
        WriteInstallPathToRegistry = IniSettings.WritePathToRegistry;
        NotifyOnUserListChange = IniSettings.NotifyOnUserListChange;
        DisablePrivateMessagePopup = IniSettings.DisablePrivateMessagePopups;
        DisableMainMenuHotkeys = IniSettings.DisableMainMenuHotkeys;

        int pmIndex = PrivateMessageOptions.ToList().FindIndex(o => (int)o.Value == IniSettings.AllowPrivateMessagesFromState);
        AllowPrivateMessagesFromIndex = pmIndex >= 0 ? pmIndex : 0;

        // As the XNA panel: set before the two it depends on, which then clear it if either is off
        ConnectOnStartup = IniSettings.AutomaticCnCNetLogin;
        SkipLoginWindow = IniSettings.SkipConnectDialog;
        PersistentMode = IniSettings.PersistentMode;
        SteamIntegration = IniSettings.SteamIntegration;

        var mode = (TunnelMode)IniSettings.TunnelMode.Value;
        int tunnelIndex = TunnelModeOptions.ToList().FindIndex(o => o.Value == mode);
        TunnelModeIndex = tunnelIndex >= 0 ? tunnelIndex : TunnelModeOptions.ToList().FindIndex(o => o.Value == TunnelMode.V3Static);

        EnableP2P = IniSettings.EnableP2P;
        DiscordIntegration = !DiscordIntegrationGloballyDisabled && IniSettings.DiscordIntegration;
        AllowGameInvitesFromFriendsOnly = IniSettings.AllowGameInvitesFromFriendsOnly;

        string localGame = ClientConfiguration.Instance.LocalGame.ToUpperInvariant();
        foreach (FollowedGameSetting followed in FollowedGames)
        {
            if (followed.Name == localGame)
            {
                followed.CanChange = false;
                followed.IsFollowed = true;
                IniSettings.SettingsIni.SetBooleanValue("Channels", localGame, true);
                continue;
            }

            followed.IsFollowed = IniSettings.IsGameFollowed(followed.Name);
        }
    }

    public override bool Save()
    {
        bool restartRequired = base.Save();
        IniSettings.PingUnofficialCnCNetTunnels.Value = PingUnofficialTunnels;
        IniSettings.WritePathToRegistry.Value = WriteInstallPathToRegistry;
        IniSettings.NotifyOnUserListChange.Value = NotifyOnUserListChange;
        IniSettings.DisablePrivateMessagePopups.Value = DisablePrivateMessagePopup;
        IniSettings.DisableMainMenuHotkeys.Value = DisableMainMenuHotkeys;
        IniSettings.AllowPrivateMessagesFromState.Value = (int)PrivateMessageOptions[AllowPrivateMessagesFromIndex].Value;
        IniSettings.AutomaticCnCNetLogin.Value = ConnectOnStartup;
        IniSettings.SkipConnectDialog.Value = SkipLoginWindow;
        IniSettings.PersistentMode.Value = PersistentMode;
        IniSettings.SteamIntegration.Value = SteamIntegration;
        IniSettings.TunnelMode.Value = (int)(TunnelModeIndex >= 0 ? TunnelModeOptions[TunnelModeIndex].Value : TunnelMode.V3Static);
        IniSettings.EnableP2P.Value = EnableP2P;

        if (!DiscordIntegrationGloballyDisabled)
            IniSettings.DiscordIntegration.Value = DiscordIntegration;

        IniSettings.AllowGameInvitesFromFriendsOnly.Value = AllowGameInvitesFromFriendsOnly;

        foreach (FollowedGameSetting followed in FollowedGames)
            IniSettings.SettingsIni.SetBooleanValue("Channels", followed.Name, followed.IsFollowed);

        return restartRequired;
    }
}
