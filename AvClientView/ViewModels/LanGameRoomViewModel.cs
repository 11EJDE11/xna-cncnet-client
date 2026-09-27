using System;
using System.Collections.ObjectModel;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Lan;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DTAClient.Domain.Multiplayer;

namespace AvClientView.ViewModels;

/// <summary>The LAN game room screen, over a <see cref="LanGameRoom"/>.</summary>
public sealed partial class LanGameRoomViewModel : LobbyViewModelBase
{
    private readonly LanGameRoom room;

    public LanGameRoomViewModel(LanGameRoom room, MapLoader mapLoader)
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

    public string LaunchText => room.IsHost ? "Launch Game" : "I'm Ready";

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

    /// <summary>Shows a room that was just opened.</summary>
    public void Start()
    {
        Messages.Clear();
        Messages.Add(new ChatLineViewModel("Type / to view a list of available chat commands.".L10N("Client:Main:ChatCommandTip"), Avalonia.Media.Brushes.Silver));
        Refresh();
    }

    protected override void OnRefreshed()
    {
        OnPropertyChanged(nameof(IsHost));
        OnPropertyChanged(nameof(IsPlayer));
        OnPropertyChanged(nameof(LaunchText));
        OnPropertyChanged(nameof(LockText));
        OnPropertyChanged(nameof(AutoReady));
    }

    protected override bool CanEditRow(PlayerInfo pInfo, bool isFreeRow) =>
        room.IsHost || (pInfo != null && !pInfo.IsAI && pInfo.Name == ProgramConstants.PLAYERNAME);

    protected override string GetPlayerStatus(PlayerInfo pInfo) =>
        pInfo.IsInGame ? "In game" : pInfo.Ready ? "Ready" : string.Empty;

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
    private void ToggleLock() => room.ToggleLock();

    [RelayCommand]
    private void Leave() => room.Leave();
}
