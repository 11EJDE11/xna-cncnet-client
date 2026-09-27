using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using ClientCore;
using ClientCore.Enums;
using ClientCore.Extensions;

using ClientLogic.GameList;
using ClientLogic.Launch;
using ClientLogic.Protocol;
using ClientLogic.UI;

using DTAClient.Domain.Multiplayer.CnCNet;
using DTAClient.Online;
using DTAClient.Online.EventArguments;

using Rampastring.Tools;

namespace ClientLogic.CnCNet;

/// <summary>
/// The CnCNet lobby without a user interface, as the XNA client's CnCNet lobby does it: connecting, the chat
/// channels, the game list from the broadcast channels, and hosting and joining game rooms (a
/// <see cref="CnCNetGameRoom"/>), and game invitations. The front end calls <see cref="Update"/> regularly.
/// Saved-game rooms and the update check are not supported yet.
/// </summary>
public sealed class CnCNetLobbyService
{
    private readonly CnCNetManager connectionManager;
    private readonly TunnelHandler tunnelHandler;
    private readonly GameCollection gameCollection;
    private readonly CnCNetUserData cncnetUserData;
    private readonly Random random;
    private readonly string localGameID;
    private readonly CnCNetGame localGame;
    private readonly List<(string Name, Channel Channel)> chatChannels = [];
    private readonly List<string> followedGames = [];

    private CancellationTokenSource gameCheckCancellation;
    private bool ctcpInvalidGameMessageShown;
    private bool ctcpNoTunnelMessageShown;
    private bool ctcpNoTunnelForGamesMessageShown;
    private bool channelsInitialized;

    public CnCNetLobbyService(CnCNetManager connectionManager, TunnelHandler tunnelHandler, GameCollection gameCollection,
        CnCNetUserData cncnetUserData, CnCNetGameRoom room, CnCNetGameLoadingRoom loadingRoom, GameProcessService gameProcess, Random random)
    {
        this.connectionManager = connectionManager;
        LoadingRoom = loadingRoom;
        loadingRoom.Left += (_, _) => LoadingRoom_Left();
        this.tunnelHandler = tunnelHandler;
        this.gameCollection = gameCollection;
        this.cncnetUserData = cncnetUserData;
        this.random = random;
        Room = room;

        localGameID = ClientConfiguration.Instance.LocalGame;
        localGame = gameCollection.GameList.Find(g => g.InternalName.ToUpper() == localGameID.ToUpper());
        State = new CnCNetLobbyState(connectionManager, gameCollection, localGameID);

        ChatColors = connectionManager.GetIRCColors().Where(c => c.Selectable).ToList();
        int selectedColor = UserINISettings.Instance.ChatColor;
        chatColorIndex = selectedColor >= ChatColors.Count || selectedColor < 0
            ? ClientConfiguration.Instance.DefaultPersonalChatColorIndex : selectedColor;
        ApplyChatColor();

        connectionManager.WelcomeMessageReceived += ConnectionManager_WelcomeMessageReceived;
        connectionManager.Connected += ConnectionManager_Connected;
        connectionManager.Disconnected += ConnectionManager_Disconnected;
        connectionManager.ConnectionLost += ConnectionManager_ConnectionLost;
        connectionManager.BannedFromChannel += ConnectionManager_BannedFromChannel;
        connectionManager.PrivateCTCPReceived += ConnectionManager_PrivateCTCPReceived;
        UserINISettings.Instance.SettingsSaved += (_, _) => FollowedGamesChanged();

        room.Left += (_, _) => Room_Left();

        gameProcess.GameProcessStarted += () =>
            connectionManager.SendCustomMessage(new QueuedMessage("AWAY " + (char)58 + "In-game", QueuedMessageType.SYSTEM_MESSAGE, 0));
        gameProcess.GameProcessExited += () =>
            connectionManager.SendCustomMessage(new QueuedMessage("AWAY", QueuedMessageType.SYSTEM_MESSAGE, 0));
    }

    public CnCNetGameRoom Room { get; }

    public CnCNetLobbyState State { get; }

    public bool IsConnected => connectionManager.IsConnected;

    public bool IsAttemptingConnection => connectionManager.IsAttemptingConnection;

    /// <summary>The chat colours the player can pick.</summary>
    public IReadOnlyList<IRCColor> ChatColors { get; }

    /// <summary>The chat channels (one per supported game), in the XNA lobby's order.</summary>
    public IReadOnlyList<string> ChatChannelNames => chatChannels.Select(c => c.Name).ToList();

    public Channel CurrentChannel { get; private set; }

    public int CurrentChannelIndex => chatChannels.FindIndex(c => c.Channel == CurrentChannel);

    /// <summary>A message for the current chat channel's list.</summary>
    public event EventHandler<ChatMessage> MessageAdded;

    /// <summary>The chat list must be shown again (the channel changed).</summary>
    public event EventHandler MessagesReset;

    /// <summary>The current channel's user list changed.</summary>
    public event EventHandler UsersChanged;

    /// <summary>The game list changed.</summary>
    public event EventHandler GamesChanged;

    /// <summary>
    /// A new, unlocked game of the local game was hosted while not in a game, and the user wants a sound for it
    /// (PlaySoundOnGameHosted); the XNA lobby plays gamecreated.wav.
    /// </summary>
    public event EventHandler GameHostedNotification;

    /// <summary>
    /// A game invitation to show (the XNA lobby's ChoiceNotificationBox); answer it with
    /// <see cref="AcceptInvitation"/> or <see cref="DeclineInvitation"/>.
    /// </summary>
    public event EventHandler<GameInvitation> InvitationReceived;

    /// <summary>An open invitation's game is no longer listed: its notification goes away.</summary>
    public event EventHandler<GameInvitation> InvitationDismissed;

    /// <summary>The connection state changed.</summary>
    public event EventHandler ConnectionChanged;

    /// <summary>The local player entered the game room.</summary>
    public event EventHandler RoomEntered;

    /// <summary>The local player left the game room.</summary>
    public event EventHandler RoomLeft;

    /// <summary>The local player entered the saved game room.</summary>
    public event EventHandler LoadingRoomEntered;

    /// <summary>The local player left the saved game room.</summary>
    public event EventHandler LoadingRoomLeft;

    /// <summary>The room for loading saved multiplayer games (CnCNetGameLoadingLobby).</summary>
    public CnCNetGameLoadingRoom LoadingRoom { get; }

    public int ChatColorIndex
    {
        get => chatColorIndex;
        set
        {
            if (value < 0 || value >= ChatColors.Count || value == chatColorIndex)
                return;

            chatColorIndex = value;
            ApplyChatColor();
            UserINISettings.Instance.ChatColor.Value = value;
            UserINISettings.Instance.SaveSettings();
        }
    }

    private int chatColorIndex;

    private void ApplyChatColor()
    {
        IRCColor color = ChatColors.Count > chatColorIndex ? ChatColors[chatColorIndex] : null;
        State.ChatColor = color;
        Room.IrcChatColor = color;
        if (LoadingRoom != null)
            LoadingRoom.IrcChatColor = color;
    }

    /// <summary>The messages of the current chat channel, as the list shows them.</summary>
    public IEnumerable<ChatMessage> CurrentMessages => CurrentChannel?.Messages.Select(FilterMessage) ?? [];

    /// <summary>The users of the current chat channel.</summary>
    public IEnumerable<ChannelUser> CurrentUsers
    {
        get
        {
            if (CurrentChannel == null)
                yield break;

            var current = CurrentChannel.Users.GetFirst();
            while (current != null)
            {
                ChannelUser user = current.Value;
                user.IRCUser.IsFriend = cncnetUserData.IsFriend(user.IRCUser.Name);
                user.IRCUser.IsIgnored = cncnetUserData.IsIgnored(user.IRCUser.Ident);
                yield return user;
                current = current.Next;
            }
        }
    }

    /// <summary>The hosted games in display order.</summary>
    public IEnumerable<HostedCnCNetGame> Games => GameListState.Sort(State.GameList.Games, localGameID,
        ProgramConstants.GAME_VERSION, (SortDirection)UserINISettings.Instance.SortState.Value).Cast<HostedCnCNetGame>();

    /// <summary>Creates the chat and broadcast channels (the XNA lobby's InitializeGameList); call once.</summary>
    /// <param name="clientVersion">The front end's version, shown in the chat.</param>
    public void Initialize(string clientVersion)
    {
        if (channelsInitialized)
            return;

        channelsInitialized = true;
        int selected = -1;

        foreach (CnCNetGame game in gameCollection.GameList)
        {
            if (!game.Supported || string.IsNullOrEmpty(game.ChatChannel))
                continue;

            Channel chatChannel = connectionManager.FindChannel(game.ChatChannel);

            if (chatChannel == null)
            {
                chatChannel = connectionManager.CreateChannel(game.UIName, game.ChatChannel, true, true, "ra1-derp");
                connectionManager.AddChannel(chatChannel);
            }

            chatChannels.Add((game.UIName, chatChannel));

            if (!string.IsNullOrEmpty(game.GameBroadcastChannel))
            {
                Channel gameBroadcastChannel = connectionManager.FindChannel(game.GameBroadcastChannel);

                if (gameBroadcastChannel == null)
                {
                    gameBroadcastChannel = connectionManager.CreateChannel(
                        string.Format("{0} Broadcast Channel".L10N("Client:Main:BroadcastChannel"), game.UIName),
                        game.GameBroadcastChannel, true, false, null);
                    connectionManager.AddChannel(gameBroadcastChannel);
                }

                gameBroadcastChannel.CTCPReceived += GameBroadcastChannel_CTCPReceived;
                gameBroadcastChannel.UserLeft += GameBroadcastChannel_UserLeftOrQuit;
                gameBroadcastChannel.UserQuitIRC += GameBroadcastChannel_UserLeftOrQuit;
                gameBroadcastChannel.UserKicked += GameBroadcastChannel_UserLeftOrQuit;
            }

            if (game.InternalName.ToUpper() == localGameID.ToUpper())
                selected = chatChannels.Count - 1;
        }

        SelectChannel(selected >= 0 ? selected : chatChannels.Count - 1);

        connectionManager.MainChannel?.AddMessage(new ChatMessage(ChatColor.White,
            string.Format("*** CnCNet Client version {0} ***".L10N("Client:Main:CnCNetClientVersionMessageV2"), clientVersion)));
    }

    /// <summary>Switches the chat to another channel (the XNA lobby's channel drop-down).</summary>
    public void SelectChannel(int index)
    {
        if (index < 0 || index >= chatChannels.Count)
            return;

        if (CurrentChannel != null)
        {
            CurrentChannel.UserAdded -= CurrentChannel_UsersChanged;
            CurrentChannel.UserLeft -= CurrentChannel_UsersChanged;
            CurrentChannel.UserQuitIRC -= CurrentChannel_UsersChanged;
            CurrentChannel.UserKicked -= CurrentChannel_UsersChanged;
            CurrentChannel.UserListReceived -= CurrentChannel_UsersChanged;
            CurrentChannel.MessageAdded -= CurrentChannel_MessageAdded;

            if (CurrentChannel.ChannelName != "#cncnet" &&
                CurrentChannel.ChannelName != gameCollection.GetGameChatChannelNameFromIdentifier(localGameID))
            {
                // Remove the channel from the users so the PM user list has no ghost users
                Channel oldChannel = CurrentChannel;
                oldChannel.Users.DoForAllUsers(user => connectionManager.RemoveChannelFromUser(user.IRCUser.Name, oldChannel.ChannelName));
                oldChannel.Leave();
            }
        }

        CurrentChannel = chatChannels[index].Channel;

        CurrentChannel.UserAdded += CurrentChannel_UsersChanged;
        CurrentChannel.UserLeft += CurrentChannel_UsersChanged;
        CurrentChannel.UserQuitIRC += CurrentChannel_UsersChanged;
        CurrentChannel.UserKicked += CurrentChannel_UsersChanged;
        CurrentChannel.UserListReceived += CurrentChannel_UsersChanged;
        CurrentChannel.MessageAdded += CurrentChannel_MessageAdded;
        connectionManager.SetMainChannel(CurrentChannel);

        ctcpInvalidGameMessageShown = false;
        ctcpNoTunnelMessageShown = false;
        ctcpNoTunnelForGamesMessageShown = false;

        MessagesReset?.Invoke(this, EventArgs.Empty);
        UsersChanged?.Invoke(this, EventArgs.Empty);

        if (IsConnected && CurrentChannel.ChannelName != "#cncnet" &&
            CurrentChannel.ChannelName != gameCollection.GetGameChatChannelNameFromIdentifier(localGameID))
        {
            CurrentChannel.Join();
        }
    }

    private void CurrentChannel_UsersChanged(object sender, EventArgs e) => UsersChanged?.Invoke(this, EventArgs.Empty);

    private void CurrentChannel_MessageAdded(object sender, IRCMessageEventArgs e) =>
        MessageAdded?.Invoke(this, FilterMessage(e.Message));

    private ChatMessage FilterMessage(ChatMessage message)
    {
        if (!string.IsNullOrEmpty(message.SenderIdent) &&
            cncnetUserData.IsIgnored(message.SenderIdent) &&
            !message.SenderIsAdmin)
        {
            return new ChatMessage(new ChatColor(192, 192, 192), string.Format("Message blocked from - {0}".L10N("Client:Main:PMBlockedFrom"), message.SenderName));
        }

        return message;
    }

    /// <summary>Adds a notice to the current chat channel.</summary>
    public void AddNotice(string message, ChatColor? color = null) =>
        connectionManager.MainChannel?.AddMessage(new ChatMessage(color ?? ChatColor.White, message));

    public void SendChatMessage(string text)
    {
        if (!string.IsNullOrEmpty(text) && CurrentChannel != null && State.ChatColor != null)
            CurrentChannel.SendChatMessage(text, State.ChatColor);
    }

    #region Connection

    /// <summary>Connects with a player name (the XNA login window's Connect).</summary>
    /// <returns>Why the name can't be used, or null.</returns>
    public string Connect(string playerName, bool rememberMe, bool persistentMode, bool autoConnect)
    {
        NameValidationError validationError = NameValidator.IsNameValid(playerName, out string errorMessage);

        if (validationError != NameValidationError.None)
            return errorMessage;

        ProgramConstants.PLAYERNAME = playerName;

        UserINISettings.Instance.SkipConnectDialog.Value = rememberMe;
        UserINISettings.Instance.PersistentMode.Value = persistentMode;
        UserINISettings.Instance.AutomaticCnCNetLogin.Value = autoConnect;
        UserINISettings.Instance.PlayerName.Value = ProgramConstants.PLAYERNAME;

        UserINISettings.Instance.SaveSettings();

        connectionManager.Connect();
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>Logs out, unless the player stays connected outside the lobby (persistent mode).</summary>
    public void LogOut()
    {
        if (connectionManager.IsConnected && !UserINISettings.Instance.PersistentMode)
            connectionManager.Disconnect();
    }

    /// <summary>Disconnects (the client is closing).</summary>
    public void Shutdown()
    {
        if (State.IsInGameRoom)
            Room.Leave();

        if (connectionManager.IsConnected)
            connectionManager.Disconnect();
    }

    private void ConnectionManager_Connected(object sender, EventArgs e)
    {
        tunnelHandler.OnConnected();
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ConnectionManager_ConnectionLost(object sender, ConnectionLostEventArgs e) => OnDisconnected();

    private void ConnectionManager_Disconnected(object sender, EventArgs e) => OnDisconnected();

    private void OnDisconnected()
    {
        tunnelHandler.OnDisconnected();

        State.GameList.Games.Clear();
        followedGames.Clear();
        gameCheckCancellation?.Cancel();

        int localIndex = chatChannels.FindIndex(c => c.Name == localGame?.UIName);
        if (localIndex > -1 && localIndex != CurrentChannelIndex)
            SelectChannel(localIndex);

        GamesChanged?.Invoke(this, EventArgs.Empty);
        UsersChanged?.Invoke(this, EventArgs.Empty);
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ConnectionManager_WelcomeMessageReceived(object sender, ServerMessageEventArgs e)
    {
        connectionManager.FindChannel("#cncnet")?.Join();

        string localGameChatChannelName = gameCollection.GetGameChatChannelNameFromIdentifier(localGameID);
        Channel localGameChatChannel = connectionManager.FindChannel(localGameChatChannelName)
            ?? throw new Exception("Could not find local game chat channel: " + localGameChatChannelName);
        localGameChatChannel.Join();

        string localGameBroadcastChannelName = gameCollection.GetGameBroadcastingChannelNameFromIdentifier(localGameID);
        Channel localGameBroadcastChannel = connectionManager.FindChannel(localGameBroadcastChannelName)
            ?? throw new Exception("Could not find local game broadcast channel: " + localGameBroadcastChannelName);
        localGameBroadcastChannel.Join();

        foreach (CnCNetGame game in gameCollection.GameList)
        {
            if (!game.Supported || game.InternalName.ToUpper() == localGameID)
                continue;

            if (UserINISettings.Instance.IsGameFollowed(game.InternalName.ToUpper()))
            {
                connectionManager.FindChannel(game.GameBroadcastChannel).Join();
                followedGames.Add(game.InternalName);
            }
        }

        gameCheckCancellation = new CancellationTokenSource();
        CnCNetGameCheck.Instance.InitializeService(gameCheckCancellation);

        if (UserINISettings.Instance.EnableP2P)
        {
            AddNotice(("Direct P2P connections are enabled. Your IP address may be shared with other " +
                "players when a P2P connection is used in a match. You can change this in Options.")
                    .L10N("Client:Main:P2PEnabledOnlineNotice"), new ChatColor(255, 165, 0));
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ConnectionManager_BannedFromChannel(object sender, ChannelEventArgs e)
    {
        HostedCnCNetGame game = FindGameByChannelName(e.ChannelName);

        if (game == null)
        {
            Channel chatChannel = connectionManager.FindChannel(e.ChannelName);
            chatChannel?.AddMessage(new ChatMessage(ChatColor.White, string.Format(
                "Cannot join chat channel {0}, you're banned!".L10N("Client:Main:PlayerBannedByChannel"), chatChannel.UIName)));
            return;
        }

        AddNotice(string.Format("Cannot join game {0}, you've been banned by the game host!".L10N("Client:Main:PlayerBannedByHost"), game.RoomName));

        State.IsJoiningGame = false;
        if (State.GameOfLastJoinAttempt != null)
        {
            if (State.GameOfLastJoinAttempt.IsLoadedGame)
                LoadingRoom.Clear();
            else
                Room.Clear();
        }
    }

    /// <summary>Called regularly by the front end: drives the tunnels, the room and the game list.</summary>
    public void Update(TimeSpan elapsed)
    {
        if (IsConnected)
            tunnelHandler.Update();

        Room.Update(elapsed);
        LoadingRoom.Update(elapsed);

        int count = State.GameList.Games.Count;
        State.GameList.RemoveExpired(DateTime.Now);
        if (State.GameList.Games.Count != count)
            GamesChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Game list

    private HostedCnCNetGame FindGameByChannelName(string channelName) =>
        (HostedCnCNetGame)State.GameList.Games.Find(hg => ((HostedCnCNetGame)hg).ChannelName == channelName);

    private void GameBroadcastChannel_UserLeftOrQuit(object sender, UserNameEventArgs e)
    {
        if (State.GameList.RemoveByHost(e.UserName))
        {
            GamesChanged?.Invoke(this, EventArgs.Empty);
            DismissInvalidInvitations();
        }
    }

    private void GameBroadcastChannel_CTCPReceived(object sender, ChannelCTCPEventArgs e)
    {
        var channel = (Channel)sender;

        ChannelUser channelUser = channel.Users.Find(e.UserName);

        if (channelUser == null)
            return;

        if (!e.Message.StartsWith("GAME "))
            return;

        string msg = e.Message.Substring(5); // Cut out GAME part
        GameBroadcastMessage broadcast;

        try
        {
            if (!GameBroadcastMessage.TryDecode(msg, out broadcast))
                broadcast = null;
        }
        catch (Exception ex)
        {
            Logger.Log("Game parsing error: " + ex);
            return;
        }

        if (broadcast == null)
        {
            Logger.Log("Ignoring CTCP game message because of an invalid amount of parameters.");

            // Remind users that the network is good but the client is outdated or newer
            if (State.GameList.Games.Count == 0 && !ctcpInvalidGameMessageShown)
            {
                ctcpInvalidGameMessageShown = true;

                AddNotice(("There are no games listed but you are indeed connected. The client did receive a game message but can't add it to the list because the message is invalid. " +
                    "You can ignore this prompt if there are games listed later. " +
                    "Otherwise, this usually means that your client is outdated, or, in a rare case, newer than others. Please check for updates.").L10N("Client:Main:InvalidGameMessage"), ChatColor.Gray);
            }

            return;
        }

        try
        {
            if (broadcast.Revision != ProgramConstants.CNCNET_PROTOCOL_REVISION)
                return;

            CnCNetTunnel tunnel = null;
            if (!broadcast.IsDynamicTunnels)
            {
                if (tunnelHandler.Tunnels.Count == 0)
                {
                    Logger.Log("Ignoring CTCP game message because there are no tunnels at all. Available tunnel count: 0. Is the connection to CnCNet HTTP service broken?");

                    if (State.GameList.Games.Count == 0 && !ctcpNoTunnelMessageShown)
                    {
                        ctcpNoTunnelMessageShown = true;
                        AddNotice(("There are no games listed. The client did receive a valid game message but can't add it to the list because there are no available tunnels. " +
                            "You can ignore this prompt if there are games listed later. Otherwise, it might indicate a network problem to CnCNet HTTP service.").L10N("Client:Main:NoTunnels"), ChatColor.Gray);
                    }

                    return;
                }

                string[] tunnelAddressAndPort = broadcast.Tunnel.Split(':');
                string tunnelAddress = tunnelAddressAndPort[0];
                int tunnelPort = int.Parse(tunnelAddressAndPort[1]);
                tunnel = tunnelHandler.Tunnels.Find(t => t.Address == tunnelAddress && t.Port == tunnelPort);

                if (tunnel == null)
                {
                    Logger.Log(string.Format("Ignoring CTCP game message because the specified tunnel {0}:{1} is not available. Available tunnel count: {2}",
                        tunnelAddress, tunnelPort, tunnelHandler.Tunnels.Count));

                    if (State.GameList.Games.Count == 0 && !ctcpNoTunnelForGamesMessageShown)
                    {
                        ctcpNoTunnelForGamesMessageShown = true;
                        AddNotice(string.Format(("There are no games listed. The client did receive a valid game message but can't add it to the list because the specified tunnel is not available. " +
                            "You can ignore this prompt if there are games listed later. Otherwise, please contact support at {0}.").L10N("Client:Main:NoTunnelForGames"), ClientConfiguration.Instance.LongSupportURL), ChatColor.Gray);
                    }

                    return;
                }
            }

            int skillLevel = ClientConfiguration.Instance.NormalizeSkillLevel(
                Conversions.IntFromString(broadcast.SkillLevel, ClientConfiguration.Instance.DefaultSkillLevelIndex));

            int[] gameOptionValues = null;

            // Games with different versions may have different option counts, so ignore
            if (broadcast.GameVersion == ProgramConstants.GAME_VERSION && channel.ChannelName == localGame?.GameBroadcastChannel)
            {
                int checkBoxCount = Room.Options.CheckBoxes.Count(o => o.BroadcastToLobby);
                int dropDownCount = Room.Options.DropDowns.Count(o => o.BroadcastToLobby);

                if (checkBoxCount + dropDownCount > 0)
                    gameOptionValues = BroadcastedGameOptionValues.Decode(broadcast.GameOptionValues, checkBoxCount, dropDownCount);
            }

            CnCNetGame cncnetGame = gameCollection.GameList.Find(g => g.GameBroadcastChannel == channel.ChannelName);

            if (cncnetGame == null)
                return;

            var game = new HostedCnCNetGame(broadcast.ChannelName, broadcast.Revision, broadcast.GameVersion, broadcast.MaxPlayers,
                broadcast.RoomName, broadcast.IsCustomPassword, !broadcast.IsDynamicTunnels, broadcast.Players.ToArray(),
                e.UserName, broadcast.MapName, broadcast.GameMode, broadcast.MapHash);
            game.IsLoadedGame = broadcast.IsLoadedGame;
            game.MatchID = broadcast.LoadedGameId;
            game.LastRefreshTime = DateTime.Now;
            game.IsLadder = broadcast.IsLadder;
            game.Game = cncnetGame;
            game.Locked = broadcast.Locked || (game.IsLoadedGame && !game.Players.Contains(ProgramConstants.PLAYERNAME));
            game.Incompatible = cncnetGame == localGame && game.GameVersion != ProgramConstants.GAME_VERSION;
            game.TunnelServer = tunnel;
            game.SkillLevel = skillLevel;
            game.BroadcastedGameOptionValues = gameOptionValues;

            if (broadcast.IsClosed)
            {
                if (State.GameList.RemoveByHost(e.UserName))
                {
                    GamesChanged?.Invoke(this, EventArgs.Empty);
                    DismissInvalidInvitations();
                }

                return;
            }

            bool isNew = State.GameList.AddOrUpdate(game);
            if (isNew && UserINISettings.Instance.PlaySoundOnGameHosted &&
                cncnetGame.InternalName == localGameID.ToLower() &&
                !ProgramConstants.IsInGame && !game.Locked)
            {
                GameHostedNotification?.Invoke(this, EventArgs.Empty);
            }

            GamesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Log("Game parsing error: " + ex);
        }
    }

    #endregion

    #region Invitations

    private void ConnectionManager_PrivateCTCPReceived(object sender, PrivateCTCPEventArgs e)
    {
        string invite = ProgramConstants.GAME_INVITE_CTCP_COMMAND;
        if (e.Message.Length >= invite.Length + 1 && e.Message.StartsWith(invite))
        {
            HandleGameInvite(e.Sender, e.Message.Substring(invite.Length + 1));
            return;
        }

        if (e.Message == ProgramConstants.GAME_INVITATION_FAILED_CTCP_COMMAND)
        {
            HandleGameInvitationFailed(e.Sender);
            return;
        }

        Logger.Log("Unhandled private CTCP command: " + e.Message + " from " + e.Sender);
    }

    /// <summary>The game a user is in (their IRC user's game index), or -1 if unknown.</summary>
    public int FindUserGameId(string name) => connectionManager.UserList.Find(u => u.Name == name)?.GameID ?? -1;

    /// <summary>The XNA lobby's HandleGameInviteCommand.</summary>
    private void HandleGameInvite(string sender, string arguments)
    {
        // arguments are semicolon-delimited: a channel name, a (human-friendly) game name and optionally a password
        if (!GameInvitation.TryParse(sender, arguments, out GameInvitation invitation))
            return;

        IRCUser inviter = connectionManager.UserList.Find(u => u.Name == sender);
        int gameIndex = State.GameList.Games.FindIndex(hg => ((HostedCnCNetGame)hg).ChannelName == invitation.ChannelName);

        InvitationResponse response = State.Invitations.Decide(invitation,
            senderIsKnown: inviter != null,
            senderIsIgnored: inviter != null && cncnetUserData.IsIgnored(inviter.Ident),
            joinError: State.JoinErrorByIndex(gameIndex),
            friendsOnly: UserINISettings.Instance.AllowGameInvitesFromFriendsOnly,
            senderIsFriend: cncnetUserData.IsFriend(sender));

        if (response == InvitationResponse.Decline)
        {
            // let the host know that we can't accept
            connectionManager.SendCustomMessage(new QueuedMessage("PRIVMSG " + sender + " :" +
                ProgramConstants.GAME_INVITATION_FAILED_CTCP_COMMAND + "",
                QueuedMessageType.CHAT_MESSAGE, 0));

            return;
        }

        // an invitation from this user for this game is still shown: we don't show another, and the host
        // isn't told, since their old invitation is still available to us
        if (response != InvitationResponse.Show)
            return;

        State.Invitations.Add(invitation, invitation);
        InvitationReceived?.Invoke(this, invitation);
    }

    private void HandleGameInvitationFailed(string sender)
    {
        IRCUser user = connectionManager.UserList.Find(u => u.Name == sender);

        // no messages from people we share no channel with, or have blocked
        if (user == null || cncnetUserData.IsIgnored(user.Ident))
            return;

        if (State.IsInGameRoom && !ProgramConstants.IsInGame)
        {
            Room.AddNotice(string.Format(("{0} could not receive your invitation. They might be in game " +
                "or only accepting invitations from friends. Ensure your game is " +
                "unlocked and visible in the lobby before trying again.").L10N("Client:Main:InviteNotDelivered"), sender),
                new ChatColor(255, 255, 0));
        }
    }

    /// <summary>
    /// The invitation's Yes: leaves the current game room and joins the invited game with the invitation's password.
    /// </summary>
    /// <returns><see cref="JoinResult.Failed"/> if the game can't be joined: the front end says so.</returns>
    public JoinResult AcceptInvitation(GameInvitation invitation)
    {
        State.Invitations.Remove(invitation.Sender, invitation.ChannelName);

        // if we're currently in a game lobby, first leave that channel
        if (State.IsInGameRoom)
            Room.Leave();

        int gameIndex = State.GameList.Games.FindIndex(hg => ((HostedCnCNetGame)hg).ChannelName == invitation.ChannelName);
        string error = State.JoinErrorByIndex(gameIndex);
        if (!string.IsNullOrEmpty(error))
        {
            AddNotice(error);
            return JoinResult.Failed;
        }

        return JoinGame((HostedCnCNetGame)State.GameList.Games[gameIndex], invitation.Password);
    }

    /// <summary>The invitation's No.</summary>
    public void DeclineInvitation(GameInvitation invitation) =>
        State.Invitations.Remove(invitation.Sender, invitation.ChannelName);

    private void DismissInvalidInvitations()
    {
        foreach ((string sender, string channel) in State.Invitations.FindInvalid(State.GameList.Games.Cast<HostedCnCNetGame>()))
        {
            if (State.Invitations.Remove(sender, channel) is GameInvitation invitation)
                InvitationDismissed?.Invoke(this, invitation);
        }
    }

    #endregion

    #region Joining and hosting

    /// <summary>What happened to a join attempt.</summary>
    public enum JoinResult
    {
        /// <summary>The game can't be joined; a notice says why.</summary>
        Failed,

        /// <summary>The game needs a password: ask for it and call <see cref="JoinGame"/> again with it.</summary>
        NeedsPassword,

        /// <summary>The JOIN was sent; <see cref="RoomEntered"/> follows if it succeeds.</summary>
        Joining,
    }

    /// <summary>Joins a listed game (the XNA lobby's JoinGame). Saved-game rooms can't be joined yet.</summary>
    public JoinResult JoinGame(HostedCnCNetGame hg, string password)
    {
        string error = State.JoinError(hg, ClientConfiguration.Instance.DisallowJoiningIncompatibleGames);
        if (!string.IsNullOrEmpty(error))
        {
            AddNotice(error);
            return JoinResult.Failed;
        }

        if (State.IsInGameRoom)
            return JoinResult.Failed;

        if (hg.GameVersion != ProgramConstants.GAME_VERSION)
            AddNotice("The game host is on a different game version than you. Version incompatibilities may cause issues.".L10N("Client:Main:JoinGameVersionMismatch"), new ChatColor(255, 255, 0));

        if (JoinGameRules.NeedsPasswordPrompt(hg, password))
            return JoinResult.NeedsPassword;

        password = JoinGameRules.JoinPassword(hg, password, () =>
            new IniFile(SafePath.CombineFilePath(ProgramConstants.GamePath, "Saved Games", "spawnSG.ini"))
                .GetStringValue("Settings", "GameID", string.Empty));

        AddNotice(string.Format("Attempting to join game {0} ...".L10N("Client:Main:AttemptJoin"), hg.RoomName));
        State.BeginJoin(hg);

        Channel gameChannel = connectionManager.CreateChannel(hg.RoomName, hg.ChannelName, false, true, password);
        connectionManager.AddChannel(gameChannel);

        if (hg.IsLoadedGame)
        {
            LoadingRoom.SetUp(false, hg.TunnelServer, gameChannel, hg.HostName);
            gameChannel.UserAdded += GameLoadingChannel_UserAdded;
            gameChannel.InvalidPasswordEntered += GameChannel_InvalidPasswordEntered_LoadedGame;
            State.IsJoiningGame = false;
            State.SendJoin(hg, password);
            return JoinResult.Joining;
        }

        Room.SetUp(gameChannel, false, hg.MaxPlayers, hg.TunnelServer, hg.HostName, hg.Passworded, hg.SkillLevel);
        gameChannel.UserAdded += GameChannel_UserAdded;
        gameChannel.InvalidPasswordEntered += GameChannel_InvalidPasswordEntered;
        gameChannel.InviteOnlyErrorOnJoin += GameChannel_InviteOnlyErrorOnJoin;
        gameChannel.ChannelFull += GameChannel_InviteOnlyErrorOnJoin;
        gameChannel.TargetChangeTooFast += GameChannel_TargetChangeTooFast;

        State.SendJoin(hg, password);
        return JoinResult.Joining;
    }

    private void GameChannel_TargetChangeTooFast(object sender, MessageEventArgs e)
    {
        AddNotice(e.Message);
        ClearGameJoinAttempt((Channel)sender);
    }

    private void GameChannel_InviteOnlyErrorOnJoin(object sender, EventArgs e)
    {
        var channel = (Channel)sender;

        HostedCnCNetGame game = FindGameByChannelName(channel.ChannelName);
        if (game != null)
        {
            AddNotice(string.Format("The game {0} is locked!".L10N("Client:Main:GameLockedWithName"), game.RoomName));
            game.Locked = true;
            GamesChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            AddNotice("The selected game is locked!".L10N("Client:Main:GameLocked"));
        }

        ClearGameJoinAttempt(channel);
    }

    private void GameChannel_InvalidPasswordEntered(object sender, EventArgs e)
    {
        AddNotice("Incorrect password!".L10N("Client:Main:PasswordWrong"));
        ClearGameJoinAttempt((Channel)sender);
    }

    private void GameChannel_UserAdded(object sender, ChannelUserEventArgs e)
    {
        var gameChannel = (Channel)sender;

        if (e.User.IRCUser.Name == ProgramConstants.PLAYERNAME)
        {
            ClearGameChannelEvents(gameChannel);
            Room.OnJoined();
            State.IsInGameRoom = true;
            RoomEntered?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ClearGameJoinAttempt(Channel channel)
    {
        ClearGameChannelEvents(channel);
        Room.Clear();
    }

    private void ClearGameChannelEvents(Channel channel)
    {
        channel.UserAdded -= GameChannel_UserAdded;
        channel.InvalidPasswordEntered -= GameChannel_InvalidPasswordEntered;
        channel.InviteOnlyErrorOnJoin -= GameChannel_InviteOnlyErrorOnJoin;
        channel.ChannelFull -= GameChannel_InviteOnlyErrorOnJoin;
        channel.TargetChangeTooFast -= GameChannel_TargetChangeTooFast;
        State.IsJoiningGame = false;
    }

    /// <summary>
    /// Whether the local player can host the saved multiplayer game (GameCreationWindow.AllowLoadingGame): they hosted
    /// it.
    /// </summary>
    public static bool CanHostLoadedGame()
    {
        System.IO.FileInfo savedGameSpawnIniFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SAVED_GAME_SPAWN_INI);

        if (!savedGameSpawnIniFile.Exists)
            return false;

        var iniFile = new IniFile(savedGameSpawnIniFile.FullName);

        if (iniFile.GetStringValue("Settings", "Name", string.Empty) != ProgramConstants.PLAYERNAME)
            return false;

        if (!iniFile.GetBooleanValue("Settings", "Host", false))
            return false;

        return true;
    }

    /// <summary>
    /// Hosts the saved game (the creation window's Load Game and the lobby's Gcw_LoadedGameCreated): its password
    /// comes from the saved game's ID.
    /// </summary>
    /// <returns>Why the game can't be created, or null.</returns>
    public string CreateLoadedGame(string roomName, CnCNetTunnel selectedTunnel = null)
    {
        if (State.IsInGameRoom || State.IsJoiningGame || LoadingRoom.IsActive)
            return null;

        string gameName = NameValidator.GetSanitizedGameName(roomName);

        NameValidationError validationError = NameValidator.IsGameNameValid(gameName, out string errorMessage);
        if (validationError != NameValidationError.None)
            return errorMessage;

        CnCNetTunnel tunnel = null;
        var tunnelMode = (TunnelMode)UserINISettings.Instance.TunnelMode.Value;
        if (tunnelMode != TunnelMode.V3Dynamic)
        {
            tunnel = selectedTunnel ?? PickBestTunnel(tunnelMode == TunnelMode.V2Legacy ? 2 : 3);
            if (tunnel == null)
                return "No tunnel server is available. Try again in a moment, or use dynamic tunnels.";
        }

        var spawnSGIni = new IniFile(SafePath.CombineFilePath(ProgramConstants.GamePath, ProgramConstants.SAVED_GAME_SPAWN_INI));
        string password = Utilities.CalculateSHA1ForString(spawnSGIni.GetStringValue("Settings", "GameID", string.Empty)).Substring(0, 10);

        string channelName = RandomizeChannelName();

        Channel gameLoadingChannel = connectionManager.CreateChannel(gameName, channelName, false, true, password);
        connectionManager.AddChannel(gameLoadingChannel);
        LoadingRoom.SetUp(true, tunnel, gameLoadingChannel, ProgramConstants.PLAYERNAME);
        gameLoadingChannel.UserAdded += GameLoadingChannel_UserAdded;
        connectionManager.SendCustomMessage(new QueuedMessage("JOIN " + channelName + " " + password,
            QueuedMessageType.INSTANT_MESSAGE, 0));
        AddNotice(string.Format("Creating a game named {0} ...".L10N("Client:Main:CreateGameNamed"), gameName));

        return null;
    }

    private void GameChannel_InvalidPasswordEntered_LoadedGame(object sender, EventArgs e)
    {
        var channel = (Channel)sender;
        channel.UserAdded -= GameLoadingChannel_UserAdded;
        channel.InvalidPasswordEntered -= GameChannel_InvalidPasswordEntered_LoadedGame;
        LoadingRoom.Clear();
        State.IsJoiningGame = false;
    }

    private void GameLoadingChannel_UserAdded(object sender, ChannelUserEventArgs e)
    {
        var gameLoadingChannel = (Channel)sender;

        if (e.User.IRCUser.Name == ProgramConstants.PLAYERNAME)
        {
            gameLoadingChannel.UserAdded -= GameLoadingChannel_UserAdded;
            gameLoadingChannel.InvalidPasswordEntered -= GameChannel_InvalidPasswordEntered_LoadedGame;

            LoadingRoom.OnJoined();
            State.IsInGameRoom = true;
            State.IsJoiningGame = false;
            LoadingRoomEntered?.Invoke(this, EventArgs.Empty);
        }
    }

    private void LoadingRoom_Left()
    {
        State.IsJoiningGame = false;

        if (!State.IsInGameRoom)
            return;

        State.IsInGameRoom = false;
        LoadingRoomLeft?.Invoke(this, EventArgs.Empty);
    }

    private void Room_Left()
    {
        State.IsJoiningGame = false;

        if (!State.IsInGameRoom)
            return;

        State.IsInGameRoom = false;
        RoomLeft?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The settings were saved (Instance_SettingsSaved): the broadcast channels of games followed or unfollowed in the
    /// options are joined or left.
    /// </summary>
    private void FollowedGamesChanged()
    {
        if (!connectionManager.IsConnected)
            return;

        foreach (CnCNetGame game in gameCollection.GameList)
        {
            if (!game.Supported)
                continue;

            if (game.InternalName.ToUpper() == localGameID)
                continue;

            if (followedGames.Contains(game.InternalName) &&
                !UserINISettings.Instance.IsGameFollowed(game.InternalName.ToUpper()))
            {
                connectionManager.FindChannel(game.GameBroadcastChannel).Leave();
                followedGames.Remove(game.InternalName);
            }
            else if (!followedGames.Contains(game.InternalName) &&
                UserINISettings.Instance.IsGameFollowed(game.InternalName.ToUpper()))
            {
                connectionManager.FindChannel(game.GameBroadcastChannel).Join();
                followedGames.Add(game.InternalName);
            }
        }
    }

    /// <summary>The default room name (the XNA game creation window's).</summary>
    public static string DefaultRoomName => string.Format("{0}'s Game", UserINISettings.Instance.PlayerName.Value);

    /// <summary>
    /// Hosts a new game room (the XNA lobby's GameCreated): the tunnel comes from the tunnel mode setting (the best
    /// official or recommended tunnel for the static modes).
    /// </summary>
    /// <returns>Why the game can't be created, or null.</returns>
    /// <param name="selectedTunnel">The tunnel picked in the creation window's list, or null for the best one.</param>
    public string CreateGame(string roomName, string password, int maxPlayers, int skillLevel, CnCNetTunnel selectedTunnel = null)
    {
        if (State.IsInGameRoom || State.IsJoiningGame)
            return null;

        string gameName = NameValidator.GetSanitizedGameName(roomName);

        NameValidationError validationError = NameValidator.IsGameNameValid(gameName, out string errorMessage);
        if (validationError != NameValidationError.None)
            return errorMessage;

        CnCNetTunnel tunnel = null;
        var tunnelMode = (TunnelMode)UserINISettings.Instance.TunnelMode.Value;
        if (tunnelMode != TunnelMode.V3Dynamic)
        {
            tunnel = selectedTunnel ?? PickBestTunnel(tunnelMode == TunnelMode.V2Legacy ? 2 : 3);
            if (tunnel == null)
                return "No tunnel server is available. Try again in a moment, or use dynamic tunnels.";
        }

        string channelName = RandomizeChannelName();
        bool isCustomPassword = true;
        if (string.IsNullOrEmpty(password))
        {
            password = Utilities.CalculateSHA1ForString(channelName).Substring(0, 10);
            isCustomPassword = false;
        }

        Channel gameChannel = connectionManager.CreateChannel(gameName, channelName, false, true, password);
        connectionManager.AddChannel(gameChannel);
        Room.SetUp(gameChannel, true, maxPlayers, tunnel, ProgramConstants.PLAYERNAME, isCustomPassword, skillLevel);
        gameChannel.UserAdded += GameChannel_UserAdded;
        connectionManager.SendCustomMessage(new QueuedMessage("JOIN " + channelName + " " + password,
            QueuedMessageType.INSTANT_MESSAGE, 0));
        AddNotice(string.Format("Creating a game named {0} ...".L10N("Client:Main:CreateGameNamed"), gameName));

        return null;
    }

    /// <summary>The tunnel list's automatic choice: the official or recommended tunnel with the best rating.</summary>
    private CnCNetTunnel PickBestTunnel(int version)
    {
        List<CnCNetTunnel> tunnels = tunnelHandler.Tunnels.Where(t => t.Version == version).ToList();

        CnCNetTunnel best = null;
        int lowestRating = int.MaxValue;

        foreach (CnCNetTunnel tunnel in tunnels)
        {
            if ((tunnel.Official || tunnel.Recommended) && tunnel.Ping.IsValid())
            {
                double usageRatio = (double)tunnel.Clients / tunnel.MaxClients;
                if (usageRatio == 0)
                    usageRatio = 0.1;

                int rating = Convert.ToInt32(Math.Pow(tunnel.Ping.Milliseconds, 2.0) * usageRatio * 100.0);
                if (rating < lowestRating)
                {
                    best = tunnel;
                    lowestRating = rating;
                }
            }
        }

        return best ?? tunnels.FirstOrDefault();
    }

    private string RandomizeChannelName()
    {
        const int maxTries = 10000;
        for (int i = 0; i < maxTries; i++)
        {
            string channelName = gameCollection.GetGameChatChannelNameFromIdentifier(localGameID) + "-game" + random.Next(1000000, 9999999);
            if (FindGameByChannelName(channelName) == null)
                return channelName;
        }

        throw new Exception(string.Format("Could not find a random channel name after {0} retries", maxTries));
    }

    #endregion
}
