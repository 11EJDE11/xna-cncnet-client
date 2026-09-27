using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

using Avalonia.Media.Imaging;

using ClientCore;
using ClientCore.Caching;
using ClientCore.Extensions;

using ClientLogic.Lobby;
using ClientLogic.MapPreview;
using ClientLogic.Options;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer;

using SixLabors.ImageSharp;

namespace AvClientView.ViewModels;

/// <summary>
/// What every game lobby screen shows of a <see cref="LobbySession"/>: the map list, the player rows, the map preview
/// with start markers and the game options.
/// </summary>
public abstract partial class LobbyViewModelBase : ObservableObject
{
    public const double PreviewWidth = 520;
    public const double PreviewHeight = 330;

    /// <summary>The map preview's size (a themed layout sets the XNA MapPreviewBox size).</summary>
    public double PreviewAreaWidth { get; private set; } = PreviewWidth;

    public double PreviewAreaHeight { get; private set; } = PreviewHeight;

    /// <summary>Sets the map preview's size and redraws it.</summary>
    public void SetPreviewArea(double width, double height)
    {
        PreviewAreaWidth = width;
        PreviewAreaHeight = height;
        Refresh();
    }

    /// <summary>Selects a random map of the current game mode (the XNA Pick Random Map button).</summary>
    public void PickRandomMap()
    {
        if (CanChangeMap && currentMaps.Count > 0)
            session.ChangeMap(currentMaps[Random.Shared.Next(currentMaps.Count)]);
    }

    private readonly LobbySession session;
    private readonly MapLoader mapLoader;
    private List<GameModeMap> currentMaps = [];

    protected LobbyViewModelBase(LobbySession session, MapLoader mapLoader)
    {
        this.session = session;
        this.mapLoader = mapLoader;

        GameModes = mapLoader.GameModes.Where(gm => gm.Maps.Count > 0).Select(gm => gm.UIName).Distinct().ToList();

        foreach (GameOption option in session.Options.CheckBoxes)
            CheckBoxOptions.Add(new CheckBoxOptionViewModel(option, () => CanChangeOptions));

        foreach (GameOption option in session.Options.DropDowns)
            DropDownOptions.Add(new DropDownOptionViewModel(option, () => CanChangeOptions));

        SideItems = ["Random", .. session.RandomSelectorNames, .. session.Sides, "Spectator"];
        ColorItems = ["Random", .. session.MPColors.Select(c => c.Name)];
        TeamItems = ["-", .. ProgramConstants.TEAMS];
        NameItems = ["-", .. ProgramConstants.AI_PLAYER_NAMES];

        session.Changed += (_, _) => Refresh();
    }

    /// <summary>True while the view shows the session's state; control changes then aren't user input.</summary>
    protected bool Refreshing { get; private set; }

    public IReadOnlyList<string> GameModes { get; }

    public ObservableCollection<string> Maps { get; } = [];

    public ObservableCollection<PlayerRowViewModel> Rows { get; } = [];

    public ObservableCollection<CheckBoxOptionViewModel> CheckBoxOptions { get; } = [];

    public ObservableCollection<DropDownOptionViewModel> DropDownOptions { get; } = [];

    public ObservableCollection<StartMarkerViewModel> StartMarkers { get; } = [];

    public IReadOnlyList<string> SideItems { get; }

    public IReadOnlyList<string> ColorItems { get; }

    public IReadOnlyList<string> TeamItems { get; }

    public IReadOnlyList<string> NameItems { get; }

    /// <summary>The local player can pick the map (the host, or the skirmish player).</summary>
    public virtual bool CanChangeMap => true;

    /// <summary>The local player can change the game options.</summary>
    public virtual bool CanChangeOptions => true;

    [ObservableProperty]
    private IReadOnlyList<string> startItems = ["???"];

    [ObservableProperty]
    private string selectedGameMode;

    [ObservableProperty]
    private int selectedMapIndex = -1;

    [ObservableProperty]
    private Bitmap preview;

    [ObservableProperty]
    private double previewX;

    [ObservableProperty]
    private double previewY;

    [ObservableProperty]
    private double previewImageWidth;

    [ObservableProperty]
    private double previewImageHeight;

    [ObservableProperty]
    private string mapInfo;

    // The XNA lobby's map labels under the preview
    [ObservableProperty]
    private string mapNameText = "Map: Unknown".L10N("Client:Main:MapUnknown");

    [ObservableProperty]
    private string mapAuthorText = "By Unknown Author".L10N("Client:Main:AuthorByUnknown");

    [ObservableProperty]
    private string gameModeText = "Game mode: Unknown".L10N("Client:Main:GameModeUnknown");

    [ObservableProperty]
    private string mapSizeText = "Size: Not available".L10N("Client:Main:MapSizeUnknown");

    /// <summary>The selected game mode's index in <see cref="GameModes"/>.</summary>
    public int SelectedGameModeIndex
    {
        get => GameModes.ToList().IndexOf(SelectedGameMode);
        set
        {
            if (value >= 0 && value < GameModes.Count)
                SelectedGameMode = GameModes[value];
        }
    }

    partial void OnSelectedGameModeChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedGameModeIndex));

        if (Refreshing)
            return;

        ListMaps(value);
        session.ChangeMap(currentMaps.FirstOrDefault());
    }

    partial void OnSelectedMapIndexChanged(int value)
    {
        if (Refreshing || value < 0 || value >= currentMaps.Count || currentMaps[value] == session.GameModeMap)
            return;

        session.ChangeMap(currentMaps[value]);
    }

    private void ListMaps(string gameMode)
    {
        currentMaps = session.GameModeMapsOf(gameMode);
        Maps.Clear();
        foreach (GameModeMap gmm in currentMaps)
            Maps.Add(gmm.Map.Name);
    }

    /// <summary>Shows the session's state.</summary>
    protected void Refresh()
    {
        Refreshing = true;
        try
        {
            GameModeMap gmm = session.GameModeMap;

            if (gmm != null && SelectedGameMode != gmm.GameMode.UIName)
            {
                SelectedGameMode = gmm.GameMode.UIName;
                ListMaps(gmm.GameMode.UIName);
            }

            SelectedMapIndex = gmm == null ? -1 : currentMaps.IndexOf(gmm);
            MapInfo = gmm == null ? string.Empty : $"{gmm.Map.Name} ({gmm.GameMode.UIName}), {gmm.MaxPlayers} players, by {gmm.Map.Author}";

            MapNameText = gmm == null ? "Map: Unknown".L10N("Client:Main:MapUnknown") : "Map:".L10N("Client:Main:Map") + " " + gmm.Map.Name;
            MapAuthorText = gmm == null ? "By Unknown Author".L10N("Client:Main:AuthorByUnknown") : "By".L10N("Client:Main:AuthorBy") + " " + gmm.Map.Author;
            GameModeText = gmm == null ? "Game mode: Unknown".L10N("Client:Main:GameModeUnknown") : "Game mode:".L10N("Client:Main:GameModeLabel") + " " + gmm.GameMode.UIName;
            MapSizeText = gmm == null ? "Size: Not available".L10N("Client:Main:MapSizeUnknown") : "Size:".L10N("Client:Main:MapSize") + " " + gmm.Map.GetSizeString();

            List<bool> starts = gmm == null ? [] : PlayerSlotRules.ComputeStartItems(MapSlotRules.FromGameModeMap(gmm), LobbySession.MAX_PLAYER_COUNT).ToList();
            StartItems = ["???", .. Enumerable.Range(1, starts.Count).Select(i => i.ToString())];

            RefreshRows();
            RefreshPreview(gmm);

            foreach (CheckBoxOptionViewModel option in CheckBoxOptions)
                option.Refresh();

            foreach (DropDownOptionViewModel option in DropDownOptions)
                option.Refresh();

            OnPropertyChanged(nameof(CanChangeMap));
            OnPropertyChanged(nameof(CanChangeOptions));
            OnRefreshed();
        }
        finally
        {
            Refreshing = false;
        }
    }

    /// <summary>Shows the screen's own state, after the lobby's.</summary>
    protected virtual void OnRefreshed()
    {
    }

    /// <summary>
    /// The texture of each player slot's status indicator (8 entries), or null if the lobby has none (skirmish).
    /// </summary>
    public virtual IReadOnlyList<string> SlotStatusTextures => null;

    /// <summary>Whether the local player can change a player row.</summary>
    protected virtual bool CanEditRow(PlayerInfo pInfo, bool isFreeRow) => true;

    /// <summary>A short status shown next to a human player's name (e.g. ready).</summary>
    protected virtual string GetPlayerStatus(PlayerInfo pInfo) => string.Empty;

    private void RefreshRows()
    {
        Rows.Clear();
        int row = 0;

        foreach (PlayerInfo pInfo in session.Players)
            Rows.Add(new PlayerRowViewModel(this, row++, pInfo, isHuman: true, isFreeRow: false, CanEditRow(pInfo, false), GetPlayerStatus(pInfo)));

        foreach (PlayerInfo aiInfo in session.AIPlayers)
            Rows.Add(new PlayerRowViewModel(this, row++, aiInfo, isHuman: false, isFreeRow: false, CanEditRow(aiInfo, false), string.Empty));

        if (row < LobbySession.MAX_PLAYER_COUNT && (session.GameModeMap == null || !session.GameModeMap.HumanPlayersOnly))
            Rows.Add(new PlayerRowViewModel(this, row, null, isHuman: false, isFreeRow: true, CanEditRow(null, true), string.Empty));
    }

    private void RefreshPreview(GameModeMap gmm)
    {
        StartMarkers.Clear();
        Preview = null;

        if (gmm == null)
            return;

        using CacheLease<Image> lease = mapLoader.GetCachedPreviewImageFromMap(gmm.Map, syncLoadOnCacheMiss: true);
        if (lease == null)
            return;

        using var stream = new MemoryStream();
        lease.Value.SaveAsPng(stream);
        stream.Position = 0;
        Preview = new Bitmap(stream);

        var previewSize = new MapPoint(lease.Value.Width, lease.Value.Height);
        MapPreviewLayout layout = MapPreviewLayout.Fit((int)PreviewAreaWidth, (int)PreviewAreaHeight, previewSize.X, previewSize.Y);
        PreviewX = layout.X;
        PreviewY = layout.Y;
        PreviewImageWidth = layout.Width;
        PreviewImageHeight = layout.Height;

        IReadOnlyList<MapPoint?> markers = layout.StartMarkers(gmm.Map, previewSize, gmm.AllowedStartingLocations);
        for (int i = 0; i < markers.Count; i++)
        {
            if (markers[i] is MapPoint marker)
            {
                string players = string.Join(", ", session.Players.Concat(session.AIPlayers)
                    .Where(p => p.StartingLocation == i + 1).Select(p => p.Name));
                StartMarkers.Add(new StartMarkerViewModel(i + 1, marker.X, marker.Y, players));
            }
        }
    }

    internal void ChangeSlot(int row, SlotField field, int index)
    {
        if (!Refreshing)
            session.ChangeSlot(row, field, index);
    }
}

/// <summary>One player row: name (AI level), side, colour, start and team.</summary>
public sealed partial class PlayerRowViewModel : ObservableObject
{
    private readonly LobbyViewModelBase owner;
    private readonly int row;

    public PlayerRowViewModel(LobbyViewModelBase owner, int row, PlayerInfo pInfo, bool isHuman, bool isFreeRow,
        bool isEditable, string status)
    {
        this.owner = owner;
        this.row = row;
        IsHuman = isHuman;
        IsFreeRow = isFreeRow;
        IsEditable = isEditable;
        PlayerName = pInfo?.Name ?? string.Empty;
        Status = status;

        nameIndex = isFreeRow ? 0 : SlotIndexMapper.AiLevelToNameItem(pInfo?.AILevel ?? 0);
        sideIndex = pInfo?.SideId ?? -1;
        colorIndex = pInfo?.ColorId ?? -1;
        startIndex = pInfo?.StartingLocation ?? -1;
        teamIndex = pInfo?.TeamId ?? -1;
    }

    public bool IsHuman { get; }

    public bool IsAiOrFree => !IsHuman;

    public bool IsFreeRow { get; }

    public bool IsEditable { get; }

    /// <summary>The side, colour, start and team can be changed.</summary>
    public bool CanChangeOptions => IsEditable && !IsFreeRow;

    public string PlayerName { get; }

    public string Status { get; }

    public IReadOnlyList<string> NameItems => owner.NameItems;

    /// <summary>The name drop-down's items: the player's name for humans (as XNA shows it), the AI levels otherwise.</summary>
    public IReadOnlyList<string> DisplayNameItems => IsHuman ? [PlayerName] : owner.NameItems;

    public int DisplayNameIndex
    {
        get => IsHuman ? 0 : NameIndex;
        set
        {
            if (!IsHuman && value >= 0)
                NameIndex = value;
        }
    }

    /// <summary>The name drop-down can be changed (AI and free rows the local player may edit).</summary>
    public bool CanChangeName => IsEditable && !IsHuman;

    public IReadOnlyList<string> SideItems => IsHuman ? owner.SideItems : owner.SideItems.Take(owner.SideItems.Count - 1).ToList();

    public IReadOnlyList<string> ColorItems => owner.ColorItems;

    public IReadOnlyList<string> StartItems => owner.StartItems;

    public IReadOnlyList<string> TeamItems => owner.TeamItems;

    [ObservableProperty]
    private int nameIndex;

    [ObservableProperty]
    private int sideIndex;

    [ObservableProperty]
    private int colorIndex;

    [ObservableProperty]
    private int startIndex;

    [ObservableProperty]
    private int teamIndex;

    partial void OnNameIndexChanged(int value) => owner.ChangeSlot(row, SlotField.Name, value);

    partial void OnSideIndexChanged(int value) => owner.ChangeSlot(row, SlotField.Side, value);

    partial void OnColorIndexChanged(int value) => owner.ChangeSlot(row, SlotField.Color, value);

    partial void OnStartIndexChanged(int value) => owner.ChangeSlot(row, SlotField.Start, value);

    partial void OnTeamIndexChanged(int value) => owner.ChangeSlot(row, SlotField.Team, value);
}

/// <summary>A check-box game option.</summary>
public sealed partial class CheckBoxOptionViewModel : ObservableObject
{
    private readonly Func<bool> canChange;

    public CheckBoxOptionViewModel(GameOption option, Func<bool> canChange)
    {
        Option = option;
        this.canChange = canChange;
        option.PropertyChanged += Option_PropertyChanged;
    }

    public GameOption Option { get; }

    public string Label => Option.Definition.Label;

    public bool IsChecked
    {
        get => Option.IsChecked;
        set
        {
            if (canChange())
                Option.Value = value ? 1 : 0;
        }
    }

    public bool IsEnabled => !Option.ForcedLocked && canChange();

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(IsEnabled));
    }

    private void Option_PropertyChanged(object sender, PropertyChangedEventArgs e) => Refresh();
}

/// <summary>A drop-down game option.</summary>
public sealed partial class DropDownOptionViewModel : ObservableObject
{
    private readonly Func<bool> canChange;

    public DropDownOptionViewModel(GameOption option, Func<bool> canChange)
    {
        Option = option;
        this.canChange = canChange;
        Items = option.Definition.Items.Select(i => i.Label).ToList();
        option.PropertyChanged += Option_PropertyChanged;
    }

    public GameOption Option { get; }

    public string Label => Option.Definition.OptionName ?? Option.Definition.Label ?? Option.Name;

    public IReadOnlyList<string> Items { get; }

    public int SelectedIndex
    {
        get => Option.Value;
        set
        {
            if (value >= 0 && canChange())
                Option.Value = value;
        }
    }

    public bool IsEnabled => !Option.ForcedLocked && canChange();

    public void Refresh()
    {
        OnPropertyChanged(nameof(SelectedIndex));
        OnPropertyChanged(nameof(IsEnabled));
    }

    private void Option_PropertyChanged(object sender, PropertyChangedEventArgs e) => Refresh();
}

/// <summary>A start location marker on the map preview.</summary>
public sealed record StartMarkerViewModel(int Number, double X, double Y, string Players)
{
    public double Left => X - 10;

    public double Top => Y - 10;
}
