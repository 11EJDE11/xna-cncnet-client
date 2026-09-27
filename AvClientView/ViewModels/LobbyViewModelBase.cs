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

        // Maps added, changed or removed while the client runs (e.g. a downloaded map) are listed
        session.MapsChanged += (_, _) => OnMapSearchTextChanged(MapSearchText);

        // A finished game can change the maps' ranks (the XNA skirmish lobby lists its maps again)
        ClientCore.Statistics.StatisticsManager.Instance.GameAdded += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnMapSearchTextChanged(MapSearchText));

        // The XNA map filter drop-down: "Favorites" first, then the game modes
        GameModes = [FavoriteMapsLabel, .. mapLoader.GameModes.Where(gm => gm.Maps.Count > 0).Select(gm => gm.UIName).Distinct()];

        foreach (GameOption option in session.Options.CheckBoxes)
            CheckBoxOptions.Add(new CheckBoxOptionViewModel(option, () => CanChangeOptions));

        foreach (GameOption option in session.Options.DropDowns)
            DropDownOptions.Add(new DropDownOptionViewModel(option, () => CanChangeOptions));

        SideItems = ["Random", .. session.RandomSelectorNames, .. session.Sides, "Spectator"];
        ColorItems = ["Random", .. session.MPColors.Select(c => c.Name)];
        ColorValues = [null, .. session.MPColors.Select(c => ((byte)c.R, (byte)c.G, (byte)c.B))];
        TeamItems = ["-", .. ProgramConstants.TEAMS];
        NameItems = ["-", .. ProgramConstants.AI_PLAYER_NAMES];

        session.Changed += (_, _) => Refresh();
    }

    /// <summary>True while the view shows the session's state; control changes then aren't user input.</summary>
    protected bool Refreshing { get; private set; }

    /// <summary>The complete snapshot is ready; themed views must not rebuild for each inserted row or marker.</summary>
    public event EventHandler Refreshed;

    public IReadOnlyList<string> GameModes { get; }

    public ObservableCollection<MapListItem> Maps { get; } = [];

    /// <summary>The rank the current setup can earn (the launch button's stars; 0 for none).</summary>
    public int Rank => session.Rank;

    /// <summary>The map sharing confirmation panel over the map preview (multiplayer rooms only).</summary>
    public MapSharingPanelState MapSharingState => (session as MultiplayerLobbySession)?.MapSharingState ?? MapSharingPanelState.Hidden;

    /// <summary>The map sharing panel's Download button.</summary>
    public void ConfirmMapDownload() => (session as MultiplayerLobbySession)?.ConfirmMapDownload();

    public ObservableCollection<PlayerRowViewModel> Rows { get; } = [];

    public ObservableCollection<CheckBoxOptionViewModel> CheckBoxOptions { get; } = [];

    public ObservableCollection<DropDownOptionViewModel> DropDownOptions { get; } = [];

    public ObservableCollection<StartMarkerViewModel> StartMarkers { get; } = [];

    public IReadOnlyList<string> SideItems { get; }

    public IReadOnlyList<string> ColorItems { get; }

    /// <summary>The colour of each <see cref="ColorItems"/> entry (null for Random).</summary>
    public IReadOnlyList<(byte R, byte G, byte B)?> ColorValues { get; }

    public IReadOnlyList<string> TeamItems { get; }

    public IReadOnlyList<string> NameItems { get; }

    /// <summary>The local player can pick the map (the host, or the skirmish player).</summary>
    public virtual bool CanChangeMap => true;

    /// <summary>The local player can change the game options.</summary>
    public virtual bool CanChangeOptions => true;

    /// <summary>The extra player options (force random sides, auto allying, ...).</summary>
    public PlayerExtraOptionsState ExtraOptions => session.ExtraOptions;

    /// <summary>The local player can change the extra player options (the host, or the skirmish player).</summary>
    public virtual bool CanChangeExtraOptions => true;

    /// <summary>The selected game mode and map, for the extra options' team-start mappings.</summary>
    public GameModeMap CurrentGameModeMap => session.GameModeMap;

    /// <summary>The extra options panel's team-start mappings, once the theme's lobby has the panel.</summary>
    public TeamStartMappingsEditor ExtraOptionsEditor { get; private set; }

    /// <summary>
    /// The theme has the extra player options panel: extra option changes now apply (see LobbySession), and the
    /// panel's mappings follow the host and the map as PlayerExtraOptionsPanel does.
    /// </summary>
    public void EnableExtraOptionsPanel()
    {
        if (ExtraOptionsEditor != null)
            return;

        session.EnableExtraOptionsPanel();
        ExtraOptionsEditor = new TeamStartMappingsEditor(session.ExtraOptions);
        ExtraOptionsEditor.SetIsHost(CanChangeExtraOptions);
        ExtraOptionsEditor.UpdateForGameModeMap(session.GameModeMap);
        session.Changed += (_, _) =>
        {
            if (ExtraOptionsEditor.IsHost != CanChangeExtraOptions)
                ExtraOptionsEditor.SetIsHost(CanChangeExtraOptions);

            ExtraOptionsEditor.UpdateForGameModeMap(session.GameModeMap);
        };
    }

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

        // DdGameModeMapFilter_SelectedIndexChanged: the search is cleared; the map stays if the filter has it
        mapSearchText = string.Empty;
        OnPropertyChanged(nameof(MapSearchText));
        ListMaps(value);

        GameModeMap current = session.GameModeMap;
        session.ChangeMap(current != null && currentMaps.Contains(current) ? current : currentMaps.FirstOrDefault());
    }

    partial void OnSelectedMapIndexChanged(int value)
    {
        if (Refreshing || value < 0 || value >= currentMaps.Count || currentMaps[value] == session.GameModeMap)
            return;

        session.ChangeMap(currentMaps[value]);
    }

    /// <summary>The map search text (the XNA lobby's tbMapSearch).</summary>
    [ObservableProperty]
    private string mapSearchText = string.Empty;

    partial void OnMapSearchTextChanged(string value)
    {
        Refreshing = true;
        try
        {
            ListMaps(SelectedGameMode);
            SelectedMapIndex = session.GameModeMap == null ? -1 : currentMaps.IndexOf(session.GameModeMap);
        }
        finally
        {
            Refreshing = false;
        }
    }

    /// <summary>The map list's sort order (UserINISettings.MapSortState: 0 none, 1 A-Z, 2 Z-A).</summary>
    public int MapSortState => UserINISettings.Instance.MapSortState.Value;

    /// <summary>The XNA sort button: none, A-Z, Z-A, saved in the settings.</summary>
    public void CycleMapSort()
    {
        UserINISettings.Instance.MapSortState.Value = (MapSortState + 1) % 3;
        UserINISettings.Instance.SaveSettings();
        OnPropertyChanged(nameof(MapSortState));
        OnMapSearchTextChanged(MapSearchText);
    }

    private void ListMaps(string gameMode)
    {
        List<GameModeMap> maps = MapsOfFilter(gameMode);
        maps = MapSortState switch
        {
            1 => maps.OrderBy(gmm => gmm.Map.Name).ToList(),
            2 => maps.OrderByDescending(gmm => gmm.Map.Name).ToList(),
            _ => maps,
        };

        currentMaps = MapSearch.Filter(maps, MapSearchText);
        Maps.Clear();
        foreach (GameModeMap gmm in currentMaps)
            Maps.Add(new MapListItem(MapListText(gmm), session.MapListRankIndex(gmm)));
    }

    /// <summary>Shows the session's state.</summary>
    protected void Refresh()
    {
        Refreshing = true;
        try
        {
            GameModeMap gmm = session.GameModeMap;

            // The filter follows the map's game mode, unless the current filter (e.g. the favourites) has the map
            if (gmm != null && SelectedGameMode != gmm.GameMode.UIName && !MapsOfFilter(SelectedGameMode).Contains(gmm))
            {
                SelectedGameMode = gmm.GameMode.UIName;
                ListMaps(gmm.GameMode.UIName);
            }
            else if (gmm != null && !currentMaps.Contains(gmm))
            {
                ListMaps(SelectedGameMode);
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
            OnPropertyChanged(nameof(Rank));
            OnPropertyChanged(nameof(MapSharingState));
            OnRefreshed();
            Refreshed?.Invoke(this, EventArgs.Empty);
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

    /// <summary>Which of a row's drop-downs can be changed, as GameLobbyBase.ComputeSlotControls decides.</summary>
    private SlotControls ComputeSlotControls(bool isAi, bool isEditable)
    {
        GameModeMap gmm = session.GameModeMap;
        return PlayerSlotRules.ComputeControls(isAi, allowOptionsChange: isEditable, isLocalPlayer: false,
            session.ExtraOptions.ToPlayerExtraOptions(), hasMap: gmm != null,
            mapForcesRandomStarts: gmm?.ForceRandomStartLocations ?? false,
            mapForbidsTeams: gmm != null && (gmm.IsCoop || gmm.ForceNoTeams));
    }

    private void RefreshRows()
    {
        Rows.Clear();
        int row = 0;

        foreach (PlayerInfo pInfo in session.Players)
        {
            bool editable = CanEditRow(pInfo, false);
            Rows.Add(new PlayerRowViewModel(this, row++, pInfo, isHuman: true, isFreeRow: false, editable, GetPlayerStatus(pInfo))
            {
                Controls = ComputeSlotControls(isAi: false, editable),
            });
        }

        foreach (PlayerInfo aiInfo in session.AIPlayers)
        {
            bool editable = CanEditRow(aiInfo, false);
            Rows.Add(new PlayerRowViewModel(this, row++, aiInfo, isHuman: false, isFreeRow: false, editable, string.Empty)
            {
                Controls = ComputeSlotControls(isAi: true, editable),
            });
        }

        if (row < LobbySession.MAX_PLAYER_COUNT && (session.GameModeMap == null || !session.GameModeMap.HumanPlayersOnly))
            Rows.Add(new PlayerRowViewModel(this, row, null, isHuman: false, isFreeRow: true, CanEditRow(null, true), string.Empty));
    }

    private void RefreshPreview(GameModeMap gmm)
    {
        StartMarkers.Clear();

        if (gmm == null)
        {
            SetPreview(null, null);
            RefreshPreviewParts(null, null, default);
            return;
        }

        if (previewMap != gmm.Map || Preview == null)
        {
            using CacheLease<Image> lease = mapLoader.GetCachedPreviewImageFromMap(gmm.Map, syncLoadOnCacheMiss: true);
            if (lease == null)
            {
                SetPreview(null, null);
                RefreshPreviewParts(null, null, default);
                return;
            }

            // Copy pixels directly: PNG compression and decoding on the UI thread is unnecessary.
            using var rgba = lease.Value.CloneAs<SixLabors.ImageSharp.PixelFormats.Rgba32>();
            byte[] pixels = new byte[checked(rgba.Width * rgba.Height * 4)];
            rgba.CopyPixelDataTo(pixels);
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                SetPreview(gmm.Map, new Bitmap(Avalonia.Platform.PixelFormat.Rgba8888,
                    Avalonia.Platform.AlphaFormat.Unpremul, handle.AddrOfPinnedObject(),
                    new Avalonia.PixelSize(rgba.Width, rgba.Height), new Avalonia.Vector(96, 96), rgba.Width * 4));
            }
            finally
            {
                handle.Free();
            }
        }

        var previewSize = new MapPoint(Preview.PixelSize.Width, Preview.PixelSize.Height);
        MapPreviewLayout layout = MapPreviewLayout.Fit((int)PreviewAreaWidth, (int)PreviewAreaHeight, previewSize.X, previewSize.Y);
        PreviewX = layout.X;
        PreviewY = layout.Y;
        PreviewImageWidth = layout.Width;
        PreviewImageHeight = layout.Height;

        RefreshPreviewParts(gmm, layout, previewSize);
    }

    private Map previewMap;

    private void SetPreview(Map map, Bitmap bitmap)
    {
        Bitmap previous = Preview;
        previewMap = map;
        Preview = bitmap;
        previous?.Dispose();
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

    /// <summary>The XNA lobby's per-drop-down rules (forced extra options, the map's forced starts / no teams); null for the free row.</summary>
    public SlotControls Controls { get; init; }

    public bool CanChangeSide => CanChangeOptions && (Controls?.Side ?? true);

    public bool CanChangeColor => CanChangeOptions && (Controls?.Color ?? true);

    public bool CanChangeStart => CanChangeOptions && (Controls?.Start ?? true);

    public bool CanChangeTeam => CanChangeOptions && (Controls?.Team ?? true);

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
/// <summary>A map list row: the map's text and its rank icon (an index into LobbyStatistics.RankTextureNames).</summary>
public sealed record MapListItem(string Text, int RankIndex)
{
    public override string ToString() => Text;
}

public sealed record StartMarkerViewModel(int Number, double X, double Y, string Players)
{
    /// <summary>The players on this start, with their team tags and colours.</summary>
    public IReadOnlyList<StartMarkerPlayer> PlayersOnStart { get; init; } = [];

    public double Left => X - 10;

    public double Top => Y - 10;
}
