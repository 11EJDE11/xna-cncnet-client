using System;

using Avalonia.Threading;

using ClientCore.Extensions;


using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer.CnCNet;
using DTAClient.Online;

namespace AvClientView.ViewModels;

/// <summary>
/// The XNA TopBar: switches between the top "primary" screen (main menu, skirmish lobby or CnCNet game room) and the
/// CnCNet lobby, opens the options, logs out, and shows the connection status and CnCNet player count. In LAN mode
/// the switch buttons can't be used.
/// </summary>
public sealed partial class TopBarViewModel : ObservableObject
{
    public TopBarViewModel(CnCNetManager connectionManager)
    {
        connectionManager.Connected += (_, _) => CanLogOut = true;
        connectionManager.Disconnected += (_, _) =>
        {
            CanLogOut = false;
            if (!LanMode)
                ConnectionEvent("OFFLINE".L10N("Client:Main:StatusOffline"));
        };
        connectionManager.ConnectionLost += (_, _) =>
        {
            if (!LanMode)
                ConnectionEvent("OFFLINE".L10N("Client:Main:StatusOffline"));
        };
        connectionManager.ConnectAttemptFailed += (_, _) =>
        {
            if (!LanMode)
                ConnectionEvent("OFFLINE".L10N("Client:Main:StatusOffline"));
        };
        connectionManager.AttemptedServerChanged += (_, _) =>
        {
            ConnectionEvent("CONNECTING...".L10N("Client:Main:StatusConnecting"));
            BringDownRequested?.Invoke(this, EventArgs.Empty);
        };
        connectionManager.WelcomeMessageReceived += (_, _) => ConnectionEvent("CONNECTED".L10N("Client:Main:StatusConnected"));

        CnCNetPlayerCountTask.CnCNetGameCountUpdated += (_, e) => Dispatcher.UIThread.Post(() =>
            PlayerCount = e.PlayerCount == -1 ? "N/A".L10N("Client:Main:N/A") : e.PlayerCount.ToString());
    }

    [ObservableProperty]
    private string mainButtonText = "Main Menu (F2)".L10N("Client:Main:MainMenuF2");

    [ObservableProperty]
    private string connectionStatus = "OFFLINE".L10N("Client:Main:StatusOffline");

    [ObservableProperty]
    private string playerCount = CnCNetPlayerCountTask.PlayerCount > 0 ? CnCNetPlayerCountTask.PlayerCount.ToString() : "-";

    [ObservableProperty]
    private bool canLogOut;

    [ObservableProperty]
    private bool canSwitch = true;

    [ObservableProperty]
    private bool canOpenOptions = true;

    /// <summary>Unread private messages (XNA doesn't show the count on its button: it doesn't fit).</summary>
    [ObservableProperty]
    private int unreadPrivateMessages;

    public bool LanMode { get; private set; }

    /// <summary>A connection event: the bar comes down for a while (EVENT_DOWN_TIME_WAIT_SECONDS).</summary>
    public event EventHandler ConnectionEventOccurred;

    /// <summary>The bar should come down now (connecting).</summary>
    public event EventHandler BringDownRequested;

    public event EventHandler MainRequested;

    public event EventHandler CnCNetLobbyRequested;

    public event EventHandler PrivateMessagesRequested;

    public event EventHandler OptionsRequested;

    public event EventHandler LogOutRequested;

    /// <summary>The top primary screen's name, as ISwitchable.GetSwitchName, on the main button.</summary>
    public void SetPrimaryName(string name) => MainButtonText = name + " (F2)";

    public void SetLanMode(bool lanMode)
    {
        LanMode = lanMode;
        CanSwitch = !lanMode;
        ConnectionEvent(lanMode ? "LAN MODE".L10N("Client:Main:StatusLanMode") : "OFFLINE".L10N("Client:Main:StatusOffline"));
    }

    public void Press(string button)
    {
        switch (button)
        {
            case "btnMainButton" when CanSwitch:
                MainRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "btnCnCNetLobby" when CanSwitch:
                CnCNetLobbyRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "btnPrivateMessages" when CanSwitch:
                PrivateMessagesRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "btnOptions" when CanOpenOptions:
                OptionsRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "btnLogout" when CanLogOut:
                LogOutRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void ConnectionEvent(string text)
    {
        ConnectionStatus = text;
        ConnectionEventOccurred?.Invoke(this, EventArgs.Empty);
    }
}
