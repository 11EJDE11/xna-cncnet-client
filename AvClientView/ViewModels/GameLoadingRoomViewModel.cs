using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using ClientLogic.Lobby;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvClientView.ViewModels;

/// <summary>A room for loading a saved multiplayer game (LAN or CnCNet), over a <see cref="GameLoadingSession"/>.</summary>
public partial class GameLoadingRoomViewModel : ObservableObject
{
    private bool refreshing;

    public GameLoadingRoomViewModel(GameLoadingSession room)
    {
        Room = room;
        room.Changed += (_, _) => Refresh();
        room.MessageAdded += (_, message) => Messages.Add(ChatLineViewModel.From(message));
    }

    public GameLoadingSession Room { get; }

    public ObservableCollection<ChatLineViewModel> Messages { get; } = [];

    public ObservableCollection<SavedGamePlayerRow> PlayerRows { get; } = [];

    public IReadOnlyList<string> SavedGames { get; private set; } = [];

    public string MapNameText => Room.MapNameText;

    public string GameModeText => Room.GameModeText;

    public string LoadButtonText => Room.LoadButtonText;

    /// <summary>Only the host picks the saved game.</summary>
    public bool CanSelectSavedGame => Room.IsHost;

    public bool IsHost => Room.IsHost;

    /// <summary>The room has the host's Change Tunnel button (CnCNet).</summary>
    public virtual bool HasChangeTunnelButton => false;

    public int SelectedSavedGameIndex
    {
        get => Room.SelectedSavedGameIndex;
        set
        {
            if (!refreshing)
                Room.SelectSavedGame(value);
        }
    }

    [ObservableProperty]
    private string chatInput = string.Empty;

    /// <summary>The room is shown for a new session: the chat starts empty.</summary>
    public void Start() => Messages.Clear();

    public void Refresh()
    {
        refreshing = true;
        try
        {
            SavedGames = [.. Room.SavedGames];
            OnPropertyChanged(nameof(SavedGames));

            PlayerRows.Clear();
            foreach (SavedGamePlayerRow row in Room.PlayerRows())
                PlayerRows.Add(row);

            OnPropertyChanged(nameof(SelectedSavedGameIndex));
            OnPropertyChanged(nameof(MapNameText));
            OnPropertyChanged(nameof(GameModeText));
            OnPropertyChanged(nameof(LoadButtonText));
            OnPropertyChanged(nameof(CanSelectSavedGame));
            OnPropertyChanged(nameof(IsHost));
            Refreshed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            refreshing = false;
        }
    }

    /// <summary>The state was shown again.</summary>
    public event EventHandler Refreshed;

    [RelayCommand]
    private void SendChat()
    {
        Room.SendChatMessage(ChatInput);
        ChatInput = string.Empty;
    }

    public void Load() => Room.LoadButtonClicked();

    public void Leave() => Room.Leave();
}
