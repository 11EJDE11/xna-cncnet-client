using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;

using Rampastring.Tools;

namespace ClientLogic.MapSharing;

/// <summary>Sends map sharing messages to the other players of a game lobby.</summary>
public interface IMapSharingTransport
{
    /// <summary>Sends a map sharing message ("COMMAND payload") to the other players.</summary>
    void SendMapSharingMessage(string message);
}

/// <summary>The parts of a game lobby that map sharing reads.</summary>
public interface IMapSharingLobby
{
    bool IsHost { get; }

    string HostName { get; }

    /// <summary>The selected map, or null.</summary>
    Map Map { get; }

    /// <summary>Finds an installed map by SHA1, or returns null.</summary>
    Map FindMap(string sha1);
}

/// <summary>
/// Shares custom maps between the players of a game lobby through the CnCNet map database:
/// a player without the host's map asks the host to upload it (MAPREQ), the host uploads it and says so (MAPOK)
/// or reports a failure (MAPFAIL), and players who can't get it say why (MAPFAIL, MAPSDISABLED).
/// Also downloads maps by ID for the chat command.
/// </summary>
/// <remarks>
/// Call <see cref="Start"/> when a lobby session starts and <see cref="Stop"/> when it ends. <see cref="MapSharer"/>
/// events arrive on worker threads and are handled on the UI thread.
/// </remarks>
public sealed class MapSharingService
{
    public const string MAP_SHARING_FAIL_MESSAGE = "MAPFAIL";
    public const string MAP_SHARING_DOWNLOAD_REQUEST = "MAPOK";
    public const string MAP_SHARING_UPLOAD_REQUEST = "MAPREQ";
    public const string MAP_SHARING_DISABLED_MESSAGE = "MAPSDISABLED";

    private readonly IUiDispatcher uiDispatcher;
    private readonly IMapSharingLobby lobby;
    private readonly IMapSharingTransport transport;
    private readonly INoticeSink notices;
    private readonly string localGame;

    /// <summary>Maps the host has reported as uploaded (MAPOK) or failed to upload (MAPFAIL).</summary>
    private readonly List<string> hostUploadedMaps = [];

    /// <summary>Maps being downloaded through the chat command.</summary>
    private readonly List<string> chatCommandDownloadedMaps = [];

    private bool started;

    public MapSharingService(IUiDispatcher uiDispatcher, IMapSharingLobby lobby, IMapSharingTransport transport,
        INoticeSink notices, string localGame)
    {
        this.uiDispatcher = uiDispatcher;
        this.lobby = lobby;
        this.transport = transport;
        this.notices = notices;
        this.localGame = localGame;
    }

    /// <summary>The SHA1 of the map the host last selected.</summary>
    public string LastMapSHA1 { get; private set; }

    /// <summary>The name of the map the host last selected, used as its file name when downloaded.</summary>
    public string LastMapName { get; private set; }

    /// <summary>A map download failed and the lobby can't get the map.</summary>
    public event Action DownloadFailed;

    /// <summary>Starts a lobby session: forgets the previous session's maps and listens to map transfers.</summary>
    public void Start()
    {
        hostUploadedMaps.Clear();
        chatCommandDownloadedMaps.Clear();

        if (started)
            return;

        started = true;
        MapSharer.MapDownloadFailed += MapSharer_MapDownloadFailed;
        MapSharer.MapDownloadComplete += MapSharer_MapDownloadComplete;
        MapSharer.MapUploadFailed += MapSharer_MapUploadFailed;
        MapSharer.MapUploadComplete += MapSharer_MapUploadComplete;
    }

    /// <summary>Ends a lobby session: stops listening to map transfers.</summary>
    public void Stop()
    {
        if (!started)
            return;

        started = false;
        MapSharer.MapDownloadFailed -= MapSharer_MapDownloadFailed;
        MapSharer.MapDownloadComplete -= MapSharer_MapDownloadComplete;
        MapSharer.MapUploadFailed -= MapSharer_MapUploadFailed;
        MapSharer.MapUploadComplete -= MapSharer_MapUploadComplete;
    }

    /// <summary>Remembers the map the host selected, in case it has to be downloaded.</summary>
    public void SetHostMap(string sha1, string mapName)
    {
        LastMapSHA1 = sha1;
        LastMapName = mapName;
    }

    /// <summary>The player agreed to download the host's map.</summary>
    public void DownloadHostMap()
    {
        Logger.Log("Map sharing confirmed.");
        notices.AddNotice("Attempting to download map.".L10N("Client:Main:DownloadingMap"), NoticeSeverity.Info);
        MapSharer.DownloadMap(LastMapSHA1, localGame, LastMapName);
    }

    /// <summary>Tells the players that this player can't get the host's custom map because map sharing is off.</summary>
    public void ReportMapSharingDisabled()
    {
        notices.AddNotice("The game host has selected a map that doesn't exist on your installation.".L10N("Client:Main:MapNotExist") + " " +
            ("Because you've disabled map sharing, it cannot be transferred. The game host needs " +
            "to change the map or you will be unable to participate in the match.").L10N("Client:Main:MapSharingDisabledNotice"),
            NoticeSeverity.Info);
        transport.SendMapSharingMessage(MAP_SHARING_DISABLED_MESSAGE);
    }

    /// <summary>Tells the players that this player doesn't have the host's official map.</summary>
    public void ReportOfficialMapMissing(string sha1)
    {
        notices.AddNotice(("The game host has selected an official map that doesn't exist on your installation. " +
            "This could mean that the game host has modified game files, or is running a different game version. " +
            "They need to change the map or you will be unable to participate in the match.").L10N("Client:Main:OfficialMapNotExist"),
            NoticeSeverity.Info);
        transport.SendMapSharingMessage(MAP_SHARING_FAIL_MESSAGE + " " + sha1);
    }

    /// <summary>Whether a newly installed map is one this lobby was downloading (and should now select).</summary>
    public bool IsExpectedDownload(string sha1) =>
        chatCommandDownloadedMaps.Contains(sha1) || LastMapSHA1 == sha1;

    /// <summary>A downloaded map was installed and selected.</summary>
    public void OnDownloadedMapInstalled(string sha1) => chatCommandDownloadedMaps.Remove(sha1);

    private void MapSharer_MapDownloadFailed(object sender, SHA1EventArgs e) => uiDispatcher.Post(() => HandleMapDownloadFailed(e));

    private void HandleMapDownloadFailed(SHA1EventArgs e)
    {
        // If the host has already communicated their upload result (MAPOK or MAPFAIL),
        // we should not request them to re-upload the map — it won't help.
        // Notify the channel that this player cannot get the map.
        if (hostUploadedMaps.Contains(e.SHA1))
        {
            notices.AddNotice("Download of the custom map failed. The host needs to change the map or you will be unable to participate in this match.".L10N("Client:Main:DownloadCustomMapFailed"), NoticeSeverity.Info);
            DownloadFailed?.Invoke();

            transport.SendMapSharingMessage(MAP_SHARING_FAIL_MESSAGE + " " + e.SHA1);
            return;
        }
        else if (chatCommandDownloadedMaps.Contains(e.SHA1))
        {
            // Notify the user that their chat command map download failed.
            // Do not notify other users with a CTCP message as this is irrelevant to them.
            notices.AddNotice("Downloading map via chat command has failed. Check the map ID and try again.".L10N("Client:Main:DownloadMapCommandFailedGeneric"), NoticeSeverity.Info);
            DownloadFailed?.Invoke();
            return;
        }

        notices.AddNotice("Requesting the game host to upload the map to the CnCNet map database.".L10N("Client:Main:RequestHostUploadMapToDB"), NoticeSeverity.Info);

        transport.SendMapSharingMessage(MAP_SHARING_UPLOAD_REQUEST + " " + e.SHA1);
    }

    private void MapSharer_MapDownloadComplete(object sender, SHA1EventArgs e) => uiDispatcher.Post(() => HandleMapDownloadComplete(e));

    private static void HandleMapDownloadComplete(SHA1EventArgs e)
    {
        string mapFileName = MapSharer.GetMapFileName(e.SHA1, e.MapName);
        Logger.Log("Map " + mapFileName + " downloaded successfully.");

        // The lobby selects the map when the map loader reports it as added.
    }

    private void MapSharer_MapUploadFailed(object sender, MapEventArgs e) => uiDispatcher.Post(() => HandleMapUploadFailed(e));

    private void HandleMapUploadFailed(MapEventArgs e)
    {
        Map map = e.Map;

        notices.AddNotice(string.Format("Uploading map {0} to the CnCNet map database failed.".L10N("Client:Main:UpdateMapToDBFailed"), map.Name), NoticeSeverity.Info);
        if (map == lobby.Map)
        {
            notices.AddNotice("You need to change the map or some players won't be able to participate in this match.".L10N("Client:Main:YouMustReplaceMap"), NoticeSeverity.Info);
            transport.SendMapSharingMessage(MAP_SHARING_FAIL_MESSAGE + " " + map.SHA1);
        }
    }

    private void MapSharer_MapUploadComplete(object sender, MapEventArgs e) => uiDispatcher.Post(() => HandleMapUploadComplete(e));

    private void HandleMapUploadComplete(MapEventArgs e)
    {
        notices.AddNotice(string.Format("Uploading map {0} to the CnCNet map database complete.".L10N("Client:Main:UpdateMapToDBSuccess"), e.Map.Name), NoticeSeverity.Info);
        if (e.Map == lobby.Map)
        {
            transport.SendMapSharingMessage(MAP_SHARING_DOWNLOAD_REQUEST + " " + lobby.Map.SHA1);
        }
    }

    /// <summary>
    /// Handles a map upload request sent by a player.
    /// </summary>
    /// <param name="sender">The sender of the request.</param>
    /// <param name="mapSHA1">The SHA1 of the requested map.</param>
    public void HandleMapUploadRequest(string sender, string mapSHA1)
    {
        // If the map was already successfully uploaded, send a download notification
        // immediately instead of re-uploading it.
        if (MapSharer.IsMapUploaded(mapSHA1))
        {
            Logger.Log("HandleMapUploadRequest: Map " + mapSHA1 + " is already uploaded, sending download notification.");

            if (lobby.Map != null && lobby.Map.SHA1 == mapSHA1)
                transport.SendMapSharingMessage(MAP_SHARING_DOWNLOAD_REQUEST + " " + mapSHA1);

            return;
        }

        Map map = lobby.FindMap(mapSHA1);

        if (map == null)
        {
            Logger.Log("Unknown map upload request from " + sender + ": " + mapSHA1);
            return;
        }

        if (map.Official)
        {
            Logger.Log("HandleMapUploadRequest: Map is official, so skip request");

            notices.AddNotice(string.Format(("{0} doesn't have the map '{1}' on their local installation. " +
                "The map needs to be changed or {0} is unable to participate in the match.").L10N("Client:Main:PlayerMissingMap"),
                sender, map.Name), NoticeSeverity.Info);

            return;
        }

        if (!lobby.IsHost)
            return;

        notices.AddNotice(string.Format(("{0} doesn't have the map '{1}' on their local installation. " +
            "Attempting to upload the map to the CnCNet map database.").L10N("Client:Main:UpdateMapToDBPrompt"),
            sender, map.Name), NoticeSeverity.Info);

        MapSharer.UploadMap(map, localGame);
    }

    /// <summary>
    /// Handles a map transfer failure message sent by either the player or the game host.
    /// </summary>
    public void HandleMapTransferFailMessage(string sender, string sha1)
    {
        if (sender == lobby.HostName)
        {
            notices.AddNotice("The game host failed to upload the map to the CnCNet map database.".L10N("Client:Main:HostUpdateMapToDBFailed"), NoticeSeverity.Info);

            hostUploadedMaps.Add(sha1);

            if (LastMapSHA1 == sha1 && lobby.Map == null)
            {
                notices.AddNotice("The game host needs to change the map or you won't be able to participate in this match.".L10N("Client:Main:HostMustChangeMap"), NoticeSeverity.Info);
            }

            return;
        }

        if (LastMapSHA1 == sha1)
        {
            if (!lobby.IsHost)
            {
                notices.AddNotice(string.Format("{0} has failed to download the map from the CnCNet map database.".L10N("Client:Main:PlayerDownloadMapFailed") + " " +
                    "The host needs to change the map or {0} won't be able to participate in this match.".L10N("Client:Main:HostNeedChangeMapForPlayer"), sender), NoticeSeverity.Info);
            }
            else
            {
                notices.AddNotice(string.Format("{0} has failed to download the map from the CnCNet map database.".L10N("Client:Main:PlayerDownloadMapFailed") + " " +
                    "You need to change the map or {0} won't be able to participate in this match.".L10N("Client:Main:YouNeedChangeMapForPlayer"), sender), NoticeSeverity.Info);
            }
        }
    }

    /// <summary>Handles the host's MAPOK message: the map is in the map database now.</summary>
    public void HandleMapDownloadRequest(string sender, string sha1)
    {
        if (sender != lobby.HostName)
            return;

        hostUploadedMaps.Add(sha1);

        if (LastMapSHA1 == sha1 && lobby.Map == null)
        {
            Logger.Log("The game host has uploaded the map into the database. Re-attempting download...");
            MapSharer.DownloadMap(sha1, localGame, LastMapName);
        }
    }

    /// <summary>Handles a player's MAPSDISABLED message.</summary>
    public void HandleMapSharingBlockedMessage(string sender)
    {
        notices.AddNotice(string.Format(("The selected map doesn't exist on {0}'s installation, and they " +
            "have map sharing disabled in settings. The game host needs to change to a non-custom map or " +
            "they will be unable to participate in this match.").L10N("Client:Main:PlayerMissingMapDisabledSharing"), sender), NoticeSeverity.Info);
    }

    /// <summary>
    /// Download a map from CNCNet using a map hash ID.
    ///
    /// Users and testers can get map hash IDs from this URL template:
    ///
    /// - http://mapdb.cncnet.org/search.php?game=GAME_ID&amp;search=MAP_NAME_SEARCH_STRING
    ///
    /// </summary>
    /// <param name="parameters">
    /// This is a string beginning with the sha1 hash map ID, and (optionally) the name to use as a local filename for the map file.
    /// Every character after the first space will be treated as part of the map name.
    ///
    /// "?" characters are removed from the sha1 due to weird copy and paste behavior from the map search endpoint.
    /// </param>
    public void DownloadMapById(string parameters)
    {
        string sha1;
        string mapName;
        string message;

        // Make sure no spaces at the beginning or end of the string will mess up arg parsing.
        parameters = parameters.Trim();
        // Check if the parameter's contain spaces.
        // The presence of spaces indicates a user-specified map name.
        int firstSpaceIndex = parameters.IndexOf(' ');

        if (firstSpaceIndex == -1)
        {
            // The user did not supply a map name.
            sha1 = parameters;
            mapName = "user_chat_command_download";
        }
        else
        {
            // User supplied a map name.
            sha1 = parameters.Substring(0, firstSpaceIndex);
            mapName = parameters.Substring(firstSpaceIndex + 1);
            mapName = mapName.Trim();
        }

        // Remove erroneous "?". These sneak in when someone double-clicks a map ID and copies it from the cncnet search endpoint.
        // There is some weird whitespace that gets copied to chat as a "?" at the end of the hash. It's hard to spot, so just hold the user's hand.
        sha1 = sha1.Replace("?", "");

        // See if the user already has this map, with any filename, before attempting to download it.
        Map loadedMap = lobby.FindMap(sha1);

        if (loadedMap != null)
        {
            message = String.Format(
                "The map for ID \"{0}\" is already loaded from \"{1}.{2}\", delete the existing file before trying again.".L10N("Client:Main:DownloadMapCommandSha1AlreadyExists"),
                sha1,
                loadedMap.BaseFilePath,
                ClientConfiguration.Instance.MapFileExtension);
            notices.AddNotice(message, NoticeSeverity.Warning);
            Logger.Log(message);
            return;
        }

        // Replace any characters that are not safe for filenames.
        char replaceUnsafeCharactersWith = '-';
        // Use a hashset instead of an array for quick lookups in `invalidChars.Contains()`.
        HashSet<char> invalidChars = new HashSet<char>(Path.GetInvalidFileNameChars());
        string safeMapName = new String(mapName.Select(c => invalidChars.Contains(c) ? replaceUnsafeCharactersWith : c).ToArray());

        chatCommandDownloadedMaps.Add(sha1);

        message = String.Format("Attempting to download map via chat command: sha1={0}, mapName={1}".L10N("Client:Main:DownloadMapCommandStartingDownload"), sha1, mapName);
        Logger.Log(message);
        notices.AddNotice(message, NoticeSeverity.Info);

        MapSharer.DownloadMap(sha1, localGame, safeMapName);
    }
}
