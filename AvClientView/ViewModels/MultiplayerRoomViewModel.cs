using System.Collections.ObjectModel;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Lobby;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DTAClient.Domain.Multiplayer;

namespace AvClientView.ViewModels;

/// <summary>A multiplayer game room screen (LAN or CnCNet), over a <see cref="MultiplayerLobbySession"/>.</summary>
public abstract partial class MultiplayerRoomViewModel : LobbyViewModelBase
{
    private readonly MultiplayerLobbySession room;

    protected MultiplayerRoomViewModel(MultiplayerLobbySession room, MapLoader mapLoader)
        : base(room, mapLoader)
    {
        this.room = room;
        room.MessageAdded += (_, message) => Messages.Add(ChatLineViewModel.From(message));
    }

    public ObservableCollection<ChatLineViewModel> Messages { get; } = [];

    public override bool CanChangeMap => room.IsHost;

    public override bool CanChangeOptions => room.IsHost;

    public bool IsHost => room.IsHost;

    public bool IsPlayer => !room.IsHost;

    public string LaunchText => room.IsHost ? "Launch Game" : PlayerLaunchText;

    /// <summary>The launch button's text for a player (the CnCNet room toggles ready; the LAN room doesn't).</summary>
    protected virtual string PlayerLaunchText => "I'm Ready";

    /// <summary>The layout INI the XNA lobby of this room type reads (LANGameLobby or CnCNetGameLobby).</summary>
    public abstract string LayoutIniName { get; }

    /// <summary>The room is being opened (the view rebuilds its layout for the local player's role).</summary>
    public event System.EventHandler Opened;

    /// <summary>A line about the room shown above the chat.</summary>
    public virtual string RoomInfo => string.Empty;

    public string LockText => room.Locked ? "Unlock Game" : "Lock Game";

    [ObservableProperty]
    private string chatInput = string.Empty;

    public bool AutoReady
    {
        get => room.AutoReady;
        set
        {
            if (room.AutoReady == value)
                return;

            room.AutoReady = value;
            room.RequestReady();
            OnPropertyChanged();
        }
    }

    /// <summary>Shows a room that is being opened.</summary>
    public void Start()
    {
        Messages.Clear();
        Messages.Add(new ChatLineViewModel("Type / to view a list of available chat commands.".L10N("Client:Main:ChatCommandTip"), Avalonia.Media.Brushes.Silver));
        Refresh();
        Opened?.Invoke(this, System.EventArgs.Empty);
    }

    protected override void OnRefreshed()
    {
        OnPropertyChanged(nameof(IsHost));
        OnPropertyChanged(nameof(IsPlayer));
        OnPropertyChanged(nameof(LaunchText));
        OnPropertyChanged(nameof(LockText));
        OnPropertyChanged(nameof(AutoReady));
        OnPropertyChanged(nameof(RoomInfo));
        OnPropertyChanged(nameof(SlotStatusTextures));
    }

    /// <summary>The status indicators, as MultiplayerGameLobby.RenderPlayerSlots sets them.</summary>
    public override System.Collections.Generic.IReadOnlyList<string> SlotStatusTextures
    {
        get
        {
            var textures = new string[LobbySession.MAX_PLAYER_COUNT];
            int slot = 0;

            for (int i = 0; i < room.Players.Count && slot < textures.Length; i++, slot++)
            {
                PlayerInfo pInfo = room.Players[i];
                textures[slot] = pInfo.IsInGame ? "statusInProgress.png"
                    : i == 0 ? (room.Locked ? "statusOk.png" : "statusClear.png")
                    : pInfo.Ready ? "statusOk.png" : "statusClear.png";
            }

            foreach (PlayerInfo aiInfo in room.AIPlayers)
            {
                if (slot >= textures.Length)
                    break;

                textures[slot++] = aiInfo.SideId == room.SlotIndices.SpectatorSide ? "statusError.png" : "statusAI.png";
            }

            for (; slot < textures.Length; slot++)
                textures[slot] = slot < PlayerLimit ? "statusEmpty.png" : "statusUnavailable.png";

            return textures;
        }
    }

    /// <summary>The room's player limit (CnCNet rooms can have fewer slots).</summary>
    protected virtual int PlayerLimit => LobbySession.MAX_PLAYER_COUNT;

    protected override bool CanEditRow(PlayerInfo pInfo, bool isFreeRow) =>
        room.IsHost || (pInfo != null && !pInfo.IsAI && pInfo.Name == ProgramConstants.PLAYERNAME);

    protected override string GetPlayerStatus(PlayerInfo pInfo) =>
        pInfo.IsInGame ? "In game" : pInfo.Ready ? "Ready" : string.Empty;

    /// <summary>Host: locks or unlocks the room.</summary>
    protected abstract void ToggleRoomLock();

    [RelayCommand]
    private void SendChat()
    {
        string text = ChatInput;
        ChatInput = string.Empty;
        room.SubmitChatInput(text);
    }

    [RelayCommand]
    private void Launch() => room.Launch();

    [RelayCommand]
    private void ToggleLock() => ToggleRoomLock();

    [RelayCommand]
    private void Leave() => room.Leave();
}
