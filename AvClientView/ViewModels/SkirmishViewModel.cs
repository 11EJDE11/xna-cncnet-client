using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

using Avalonia.Media.Imaging;

using ClientCore;
using ClientCore.Caching;

using ClientLogic.Lobby;
using ClientLogic.MapPreview;
using ClientLogic.Options;
using ClientLogic.Skirmish;
using ClientLogic.UI;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DTAClient.Domain.Multiplayer;

using SixLabors.ImageSharp;

namespace AvClientView.ViewModels;

/// <summary>The skirmish lobby screen, over a <see cref="SkirmishSession"/>.</summary>
public sealed partial class SkirmishViewModel : ObservableObject
{
    public const double PreviewWidth = 520;
    public const double PreviewHeight = 330;

    private readonly SkirmishSession session;
    private readonly MapLoader mapLoader;
    private readonly IDialogService dialogs;
    private bool refreshing;

    public SkirmishViewModel(SkirmishSession session, MapLoader mapLoader, IDialogService dialogs)
    {
        this.session = session;
        this.mapLoader = mapLoader;
        this.dialogs = dialogs;

        GameModes = mapLoader.GameModes.Where(gm => gm.Maps.Count > 0).Select(gm => gm.UIName).Distinct().ToList();

        foreach (GameOption option in session.Options.CheckBoxes)
            CheckBoxOptions.Add(new CheckBoxOptionViewModel(option));

        foreach (GameOption option in session.Options.DropDowns)
            DropDownOptions.Add(new DropDownOptionViewModel(option));

        SideItems = ["Random", .. session.RandomSelectorNames, .. session.Sides, "Spectator"];
        ColorItems = ["Random", .. session.MPColors.Select(c => c.Name)];
        TeamItems = ["-", .. ProgramConstants.TEAMS];
        NameItems = ["-", .. ProgramConstants.AI_PLAYER_NAMES];

        session.Changed += (_, _) => Refresh();
        session.LoadSettings();
        Refresh();
    }

    public event EventHandler BackRequested;

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

    private List<GameModeMap> currentMaps = [];

    partial void OnSelectedGameModeChanged(string value)
    {
        if (refreshing)
            return;

        ListMaps(value);
        session.ChangeMap(currentMaps.FirstOrDefault());
    }

    partial void OnSelectedMapIndexChanged(int value)
    {
        if (refreshing || value < 0 || value >= currentMaps.Count || currentMaps[value] == session.GameModeMap)
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
    private void Refresh()
    {
        refreshing = true;
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

            List<bool> starts = gmm == null ? [] : PlayerSlotRules.ComputeStartItems(MapSlotRules.FromGameModeMap(gmm), SkirmishSession.MAX_PLAYER_COUNT).ToList();
            StartItems = ["???", .. Enumerable.Range(1, starts.Count).Select(i => i.ToString())];

            RefreshRows();
            RefreshPreview(gmm);

            foreach (CheckBoxOptionViewModel option in CheckBoxOptions)
                option.Refresh();

            foreach (DropDownOptionViewModel option in DropDownOptions)
                option.Refresh();
        }
        finally
        {
            refreshing = false;
        }
    }

    private void RefreshRows()
    {
        Rows.Clear();
        int row = 0;

        foreach (PlayerInfo pInfo in session.Players)
            Rows.Add(new PlayerRowViewModel(this, row++, pInfo, isHuman: true, isFreeRow: false));

        foreach (PlayerInfo aiInfo in session.AIPlayers)
            Rows.Add(new PlayerRowViewModel(this, row++, aiInfo, isHuman: false, isFreeRow: false));

        if (row < SkirmishSession.MAX_PLAYER_COUNT && (session.GameModeMap == null || !session.GameModeMap.HumanPlayersOnly))
            Rows.Add(new PlayerRowViewModel(this, row, null, isHuman: false, isFreeRow: true));
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
        MapPreviewLayout layout = MapPreviewLayout.Fit((int)PreviewWidth, (int)PreviewHeight, previewSize.X, previewSize.Y);
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
        if (!refreshing)
            session.ChangeSlot(row, field, index);
    }

    [RelayCommand]
    private void Launch()
    {
        string error = session.Launch();
        if (error != null)
            dialogs.ShowMessage("Cannot launch game", error);
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);
}

/// <summary>One player row: name (AI level), side, colour, start and team.</summary>
public sealed partial class PlayerRowViewModel : ObservableObject
{
    private readonly SkirmishViewModel owner;
    private readonly int row;

    public PlayerRowViewModel(SkirmishViewModel owner, int row, PlayerInfo pInfo, bool isHuman, bool isFreeRow)
    {
        this.owner = owner;
        this.row = row;
        IsHuman = isHuman;
        IsFreeRow = isFreeRow;
        PlayerName = pInfo?.Name ?? string.Empty;

        nameIndex = isFreeRow ? 0 : SlotIndexMapper.AiLevelToNameItem(pInfo?.AILevel ?? 0);
        sideIndex = pInfo?.SideId ?? -1;
        colorIndex = pInfo?.ColorId ?? -1;
        startIndex = pInfo?.StartingLocation ?? -1;
        teamIndex = pInfo?.TeamId ?? -1;
    }

    public bool IsHuman { get; }

    public bool IsAiOrFree => !IsHuman;

    public bool IsFreeRow { get; }

    public bool HasPlayer => !IsFreeRow;

    public string PlayerName { get; }

    public IReadOnlyList<string> NameItems => owner.NameItems;

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
    public CheckBoxOptionViewModel(GameOption option)
    {
        Option = option;
        option.PropertyChanged += Option_PropertyChanged;
    }

    public GameOption Option { get; }

    public string Label => Option.Definition.Label;

    public bool IsChecked
    {
        get => Option.IsChecked;
        set => Option.Value = value ? 1 : 0;
    }

    public bool IsEnabled => !Option.ForcedLocked;

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
    public DropDownOptionViewModel(GameOption option)
    {
        Option = option;
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
            if (value >= 0)
                Option.Value = value;
        }
    }

    public bool IsEnabled => !Option.ForcedLocked;

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
