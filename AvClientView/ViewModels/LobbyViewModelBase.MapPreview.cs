using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Lobby;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer;

using Rampastring.Tools;

namespace AvClientView.ViewModels;

/// <summary>A player shown next to a start location in the map preview.</summary>
/// <param name="Name">The player's name.</param>
/// <param name="TeamTag">"[A]" etc., or empty.</param>
/// <param name="Color">The player's colour, or null (white).</param>
public sealed record StartMarkerPlayer(string Name, string TeamTag, (byte R, byte G, byte B)? Color);

/// <summary>An entry of a start location's menu: a player or AI to put there.</summary>
public sealed record StartMenuItem(int Row, string Text, (byte R, byte G, byte B)? Color, bool Selectable);

/// <summary>An extra texture drawn on the map preview (a map's [ExtraPreviewTextures]).</summary>
public sealed record ExtraTextureViewModel(string TextureName, double X, double Y, bool Toggleable);

/// <summary>
/// The map preview's parts of a lobby, as the XNA MapPreviewBox: start location indicators with the players on them,
/// their menus, the extra textures, the co-op briefing, favourites; and the "Favorites" map filter.
/// </summary>
public abstract partial class LobbyViewModelBase
{
    public static readonly string FavoriteMapsLabel = "Favorites".L10N("Client:Main:Favorites");

    private static readonly string[] TeamTags = [string.Empty, .. ProgramConstants.TEAMS.Select(team => $"[{team}]")];

    public ObservableCollection<ExtraTextureViewModel> ExtraTextures { get; } = [];

    /// <summary>GameOptions.ini (the start indicators' turning speeds).</summary>
    public IniFile GameOptionsIni => session.GameOptionsIni;

    /// <summary>The preview is scaled with nearest-neighbour sampling (MapPreviewLayout.UseNearestNeighbour).</summary>
    [ObservableProperty]
    private bool useNearestNeighbour;

    /// <summary>The co-op map's briefing, shown over the preview while the cursor isn't on it; empty if none.</summary>
    [ObservableProperty]
    private string briefing = string.Empty;

    [ObservableProperty]
    private bool isFavoriteMap;

    /// <summary>The map has toggleable extra textures (the preview then has the extra icons button).</summary>
    [ObservableProperty]
    private bool hasToggleableExtraTextures;

    public bool ShowToggleableExtraTextures => UserINISettings.Instance.DisplayToggleableExtraTextures;

    /// <summary>
    /// Clicking a start location opens the menu of players to put there (MapPreviewBox.EnableContextMenu): the
    /// skirmish player, or the host, unless starts are forced random.
    /// </summary>
    public virtual bool EnableStartMenu
    {
        get
        {
            GameModeMap gmm = session.GameModeMap;
            return gmm != null && !(gmm.ForceRandomStartLocations || session.ExtraOptions.ToPlayerExtraOptions().IsForceRandomStarts);
        }
    }

    /// <summary>Start locations can be clicked at all (MapPreviewBox.EnableStartLocationSelection).</summary>
    public virtual bool EnableStartLocationSelection => EnableStartMenu;

    /// <summary>The menu of a start location: every player and AI, numbered, with their team and colour.</summary>
    public IReadOnlyList<StartMenuItem> StartMenuItems(int start)
    {
        var items = new List<StartMenuItem>();
        List<PlayerInfo> all = session.Players.Concat(session.AIPlayers).ToList();

        for (int i = 0; i < all.Count; i++)
        {
            PlayerInfo pInfo = all[i];
            string text = pInfo.TeamId > 0 ? TeamTags[pInfo.TeamId] + " " + pInfo.Name : pInfo.Name;
            bool selectable = pInfo.StartingLocation != start && pInfo.SideId < session.SlotIndices.SpectatorSide;
            items.Add(new StartMenuItem(i, (i + 1) + ". " + text, ColorOf(pInfo), selectable));
        }

        return items;
    }

    private (byte R, byte G, byte B)? ColorOf(PlayerInfo pInfo) =>
        pInfo.ColorId > 0 && pInfo.ColorId < ColorValues.Count ? ColorValues[pInfo.ColorId] : null;

    /// <summary>A player picked from a start location's menu.</summary>
    public void AssignStart(int row, int start) => session.AssignStartFromMapPreview(row, start);

    /// <summary>
    /// A start location was right-clicked: with the menu, everyone there moves to a random start; otherwise the
    /// local player leaves it if they are on it.
    /// </summary>
    public void RightClickStart(int start)
    {
        if (EnableStartMenu)
        {
            session.ClearStartFromMapPreview(start);
            return;
        }

        PlayerInfo me = session.Players.Find(p => p.Name == ProgramConstants.PLAYERNAME);
        if (me != null && me.StartingLocation == start)
            session.SelectLocalStartFromMapPreview(0);
    }

    /// <summary>A start location was clicked without the menu: the local player asks for it.</summary>
    public void SelectLocalStart(int start) => session.SelectLocalStartFromMapPreview(start);

    /// <summary>Adds or removes the selected map from the favourites (GameLobbyBase.ToggleFavoriteMap).</summary>
    public virtual void ToggleFavoriteMap()
    {
        GameModeMap gmm = session.GameModeMap;
        if (gmm == null)
            return;

        gmm.IsFavorite = UserINISettings.Instance.ToggleFavoriteMap(gmm.Map.SHA1, gmm.GameMode.Name, gmm.IsFavorite);
        IsFavoriteMap = gmm.IsFavorite;

        if (!gmm.IsFavorite && CanChangeMap && SelectedGameMode == FavoriteMapsLabel)
            RefreshForFavoriteMapRemoved();
    }

    /// <summary>A favourite was removed while viewing the favourites: the list is refreshed, or the default filter taken.</summary>
    private void RefreshForFavoriteMapRemoved()
    {
        if (!FavoriteGameModeMaps().Any())
        {
            // LoadDefaultGameModeMap: the first non-empty filter
            SelectedGameMode = GameModes.Skip(1).FirstOrDefault();
            return;
        }

        ListMaps(SelectedGameMode);
        if (currentMaps.Count > 0)
            session.ChangeMap(currentMaps[0]);
    }

    public void ToggleExtraTextures()
    {
        UserINISettings.Instance.DisplayToggleableExtraTextures.Value = !UserINISettings.Instance.DisplayToggleableExtraTextures;
        OnPropertyChanged(nameof(ShowToggleableExtraTextures));
    }

    public void ShowMapInFolder() => session.GameModeMap?.Map.OpenContainingFolder();

    /// <summary>Ctrl + click on the preview opens the preview image.</summary>
    public void OpenPreviewFile()
    {
        GameModeMap gmm = session.GameModeMap;
        if (gmm == null)
            return;

        FileInfo previewFile = SafePath.GetFile(ProgramConstants.GamePath, gmm.Map.PreviewPath);
        if (previewFile.Exists)
            ProcessLauncher.StartShellProcess(previewFile.FullName);
    }

    /// <summary>The map list's menu can delete the selected map (a custom map, outside multiplayer).</summary>
    public bool CanDeleteMap => session.LobbyState.CanDeleteMap(session.IsMultiplayerLobby);

    public string SelectedMapName => session.GameModeMap?.Map.Name;

    public string SelectedMapUntranslatedName => session.GameModeMap?.Map.UntranslatedName;

    /// <summary>Asks to delete the selected custom map, then deletes it and picks another (GameLobbyBase.DeleteSelectedMap).</summary>
    public void DeleteSelectedMap(ClientLogic.UI.IDialogService dialogs)
    {
        session.LobbyState.DeleteMap(dialogs, () =>
        {
            GameModeMap gmm = session.GameModeMap;
            string gameModeName = gmm?.GameMode.Name;
            int index = SelectedMapIndex;

            try
            {
                mapLoader.DeleteCustomMap(gmm);
            }
            catch (IOException ex)
            {
                Logger.Log($"Deleting map {gmm?.Map.BaseFilePath} failed! Message: {ex}");
                dialogs.ShowMessage("Deleting Map Failed".L10N("Client:Main:DeleteMapFailedTitle"),
                    "Deleting map failed! Reason:".L10N("Client:Main:DeleteMapFailedText") + " " + ex.Message);
                return;
            }

            MapSearchText = string.Empty;
            if (!session.GameModeMaps.Any(m => m.GameMode.Name == gameModeName))
            {
                session.ChangeMap(session.GameModeMaps.FirstOrDefault(m => m.GameMode.Maps.Count > 0));
                return;
            }

            ListMaps(SelectedGameMode);
            int newIndex = index == 0 ? 0 : index - 1;
            session.ChangeMap(newIndex < currentMaps.Count ? currentMaps[newIndex] : currentMaps.FirstOrDefault());
        });
    }

    private IEnumerable<GameModeMap> FavoriteGameModeMaps() => session.GameModeMaps.Where(gmm => gmm.IsFavorite);

    /// <summary>The maps of a filter: the favourites, or a game mode's maps.</summary>
    private List<GameModeMap> MapsOfFilter(string filter) =>
        SearchAllGameModes ? session.GameModeMaps.ToList()
        : filter == FavoriteMapsLabel ? FavoriteGameModeMaps().ToList() : session.GameModeMapsOf(filter);

    /// <summary>The map list lists every game mode's maps (the search box menu's "Search all modes").</summary>
    public bool SearchAllGameModes => UserINISettings.Instance.SearchAllGameModes.Value;

    /// <summary>The search box menu (SetSearchAllGameModes).</summary>
    public void SetSearchAllGameModes(bool value)
    {
        UserINISettings.Instance.SearchAllGameModes.Value = value;
        UserINISettings.Instance.SaveSettings();
        OnPropertyChanged(nameof(SearchAllGameModes));
        OnMapSearchTextChanged(MapSearchText);
    }

    /// <summary>The map list's text for a map: with the game mode while viewing the favourites.</summary>
    private string MapListText(GameModeMap gmm) =>
        SelectedGameMode == FavoriteMapsLabel || SearchAllGameModes ? gmm.Map.Name + " - " + gmm.GameMode.UIName : gmm.Map.Name;

    /// <summary>Fills the preview's start markers, extra textures and briefing (MapPreviewBox.UpdateMap).</summary>
    private void RefreshPreviewParts(GameModeMap gmm, ClientLogic.MapPreview.MapPreviewLayout layout, MapPoint previewSize)
    {
        ExtraTextures.Clear();
        UseNearestNeighbour = layout?.UseNearestNeighbour ?? false;
        Briefing = gmm?.Map.Briefing ?? string.Empty;
        IsFavoriteMap = gmm != null && UserINISettings.Instance.IsFavoriteMap(gmm.Map.SHA1, gmm.Map.UntranslatedName, gmm.GameMode.Name);
        HasToggleableExtraTextures = false;

        if (gmm == null || layout == null)
            return;

        IReadOnlyList<MapPoint?> markers = layout.StartMarkers(gmm.Map, previewSize, gmm.AllowedStartingLocations);
        for (int i = 0; i < markers.Count; i++)
        {
            if (markers[i] is not MapPoint marker)
                continue;

            int start = i + 1;
            List<StartMarkerPlayer> players = session.Players.Concat(session.AIPlayers)
                .Where(p => p.StartingLocation == start)
                .Select(p => new StartMarkerPlayer(p.Name, p.TeamId > 0 && p.TeamId < TeamTags.Length ? TeamTags[p.TeamId] : string.Empty, ColorOf(p)))
                .ToList();

            StartMarkers.Add(new StartMarkerViewModel(start, marker.X, marker.Y, string.Join(", ", players.Select(p => p.Name)))
            {
                PlayersOnStart = players,
            });
        }

        foreach (ExtraMapPreviewTexture extra in gmm.Map.GetExtraMapPreviewTextures())
        {
            if (AvClientView.Theme.ThemeAssets.TextureSize(extra.TextureName) is not (int width, int height))
                continue;

            MapPoint position = layout.ExtraTexturePosition(gmm.Map, extra.Point, extra.Level, previewSize, width, height);
            ExtraTextures.Add(new ExtraTextureViewModel(extra.TextureName, position.X, position.Y, extra.Toggleable));
            HasToggleableExtraTextures |= extra.Toggleable;
        }
    }
}
