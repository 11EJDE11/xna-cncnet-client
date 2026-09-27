using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.LAN;

using Rampastring.Tools;

namespace ClientLogic.Lan;

/// <summary>
/// The TCP side of a LAN game room, as the XNA client's LAN game lobby does it: the host listens for players (and
/// connects to itself), every player reads the host's messages. Messages end with
/// <see cref="ProgramConstants.LAN_MESSAGE_SEPARATOR"/>. All events are raised on the UI thread.
/// </summary>
public sealed class LanGameConnection
{
    public const string PLAYER_JOIN_COMMAND = "JOIN";

    private readonly IUiDispatcher uiDispatcher;
    private readonly Encoding encoding = Encoding.UTF8;

    private TcpListener listener;
    private TcpClient client;
    private IPEndPoint hostEndPoint;
    private volatile bool leaving;
    private int sessionId;
    private string overMessage = string.Empty;

    public LanGameConnection(IUiDispatcher uiDispatcher)
    {
        this.uiDispatcher = uiDispatcher;
    }

    /// <summary>
    /// The saved game's ID, sent as a third JOIN part by the saved game room (LANGameLoadingLobby); null for a game
    /// room. A player whose JOIN doesn't carry the same ID is refused.
    /// </summary>
    public string JoinGameId { get; set; }

    /// <summary>Host: whether a new player may join now (checked on the listener thread, as the XNA lobby does).</summary>
    public Func<bool> CanAcceptPlayer { get; set; } = () => true;

    /// <summary>Host: a player connected and sent JOIN.</summary>
    public event EventHandler<LANPlayerInfo> PlayerJoined;

    /// <summary>Host: a message from a player.</summary>
    public event EventHandler<(LANPlayerInfo Player, string Message)> PlayerMessage;

    /// <summary>Host: a player's connection dropped.</summary>
    public event EventHandler<LANPlayerInfo> PlayerConnectionLost;

    /// <summary>A message from the host.</summary>
    public event EventHandler<string> HostMessage;

    /// <summary>The connection to the host failed; the argument is the notice to show.</summary>
    public event EventHandler<string> HostConnectionFailed;

    public Encoding Encoding => encoding;

    public IPEndPoint HostEndPoint => hostEndPoint;

    public bool IsLeaving => leaving;

    /// <summary>
    /// Host: starts listening on <see cref="ProgramConstants.LAN_GAME_LOBBY_PORT"/> and connects to itself.
    /// </summary>
    /// <returns>An error notice, or null.</returns>
    public string StartHosting()
    {
        try
        {
            listener = new TcpListener(IPAddress.Any, ProgramConstants.LAN_GAME_LOBBY_PORT);
            listener.Start();

            client = new TcpClient();
            client.Connect("127.0.0.1", ProgramConstants.LAN_GAME_LOBBY_PORT);
            return null;
        }
        catch (SocketException ex)
        {
            Logger.Log("Failed to start hosting the LAN game lobby: " + ex);
            listener?.Stop();
            client?.Close();
            return string.Format("Unable to host the game because TCP port {0} could not be opened. It may already be in use by another program.".L10N("Client:Main:LANListenerStartFailed"),
                ProgramConstants.LAN_GAME_LOBBY_PORT);
        }
    }

    /// <summary>
    /// Starts the session: the host accepts players and joins itself; a player uses the connection it made to the host.
    /// </summary>
    public void Start(bool isHost, IPEndPoint hostEndPoint, TcpClient playerClient)
    {
        leaving = false;
        sessionId++;
        overMessage = string.Empty;
        this.hostEndPoint = hostEndPoint;

        if (isHost)
        {
            new Thread(ListenForClients).Start();

            string join = PLAYER_JOIN_COMMAND + ProgramConstants.LAN_DATA_SEPARATOR + ProgramConstants.PLAYERNAME;
            if (JoinGameId != null)
                join += ProgramConstants.LAN_DATA_SEPARATOR + JoinGameId;

            byte[] buffer = encoding.GetBytes(join);
            client.GetStream().Write(buffer, 0, buffer.Length);
            client.GetStream().Flush();
        }
        else
        {
            client = playerClient;
        }

        new Thread(HandleServerCommunication).Start();
    }

    private void ListenForClients()
    {
        while (true)
        {
            TcpClient newClient;

            try
            {
                newClient = listener.AcceptTcpClient();
            }
            catch (Exception ex)
            {
                Logger.Log("Listener error: " + ex);
                break;
            }

            Logger.Log("New client connected from " + ((IPEndPoint)newClient.Client.RemoteEndPoint).Address);

            if (!CanAcceptPlayer())
            {
                Logger.Log("Dropping client because of the player limit or a locked game room.");
                newClient.Close();
                continue;
            }

            var lpInfo = new LANPlayerInfo(encoding);
            lpInfo.SetClient(newClient);
            new Thread(HandleClientConnection).Start(lpInfo);
        }
    }

    private void HandleClientConnection(object clientInfo)
    {
        var lpInfo = (LANPlayerInfo)clientInfo;
        byte[] message = new byte[1024];

        while (true)
        {
            int bytesRead;

            try
            {
                bytesRead = lpInfo.TcpClient.GetStream().Read(message, 0, message.Length);
            }
            catch (Exception ex)
            {
                Logger.Log("Socket error with client " + lpInfo.IPAddress + "; removing. Message: " + ex);
                break;
            }

            if (bytesRead == 0)
            {
                Logger.Log("Connect attempt from " + lpInfo.IPAddress + " failed! (0 bytes read)");
                break;
            }

            string msg = encoding.GetString(message, 0, bytesRead);
            string[] command = msg.Split(ProgramConstants.LAN_MESSAGE_SEPARATOR);
            string[] parts = command[0].Split(ProgramConstants.LAN_DATA_SEPARATOR);

            if (parts.Length != (JoinGameId == null ? 2 : 3))
                break;

            string name = parts[1].Trim();

            if (JoinGameId != null && Conversions.IntFromString(parts[2], -1) != Conversions.IntFromString(JoinGameId, -1))
                break;

            if (parts[0] == PLAYER_JOIN_COMMAND && !string.IsNullOrEmpty(name))
            {
                lpInfo.Name = name;
                uiDispatcher.Post(() => PlayerJoined?.Invoke(this, lpInfo));
                return;
            }

            break;
        }

        if (lpInfo.TcpClient.Connected)
            lpInfo.TcpClient.Close();
    }

    /// <summary>Host: starts receiving from a player that was added to the room.</summary>
    public void StartReceiving(LANPlayerInfo lpInfo)
    {
        lpInfo.MessageReceived += LpInfo_MessageReceived;
        lpInfo.ConnectionLost += LpInfo_ConnectionLost;
        lpInfo.StartReceiveLoop();
    }

    private void LpInfo_MessageReceived(object sender, NetworkMessageEventArgs e)
    {
        var lpInfo = (LANPlayerInfo)sender;
        uiDispatcher.Post(() =>
        {
            lpInfo.TimeSinceLastReceivedMessage = TimeSpan.Zero;
            PlayerMessage?.Invoke(this, (lpInfo, e.Message));
        });
    }

    private void LpInfo_ConnectionLost(object sender, EventArgs e)
    {
        var lpInfo = (LANPlayerInfo)sender;
        uiDispatcher.Post(() => PlayerConnectionLost?.Invoke(this, lpInfo));
    }

    /// <summary>Host: stops listening to a player and closes its connection.</summary>
    public void CleanUpPlayer(LANPlayerInfo lpInfo)
    {
        lpInfo.MessageReceived -= LpInfo_MessageReceived;
        lpInfo.ConnectionLost -= LpInfo_ConnectionLost;
        lpInfo.TcpClient?.Close();
    }

    private void HandleServerCommunication()
    {
        byte[] message = new byte[1024];
        int mySessionId = sessionId;

        if (!client.Connected)
            return;

        var stream = client.GetStream();

        while (true)
        {
            int bytesRead;

            try
            {
                bytesRead = stream.Read(message, 0, message.Length);
            }
            catch (Exception ex)
            {
                if (leaving)
                    break;

                Logger.Log(string.Format("Reading data from the server failed! Server address: {0}. Exception: {1}",
                    hostEndPoint.Address, ex));

                string localizedMessage = string.Format(
                    "Reading data from the server failed! Server address: {0}. Exception: {1}".L10N("Client:Main:LanServerReadError"),
                    hostEndPoint.Address, ex.Message);

                uiDispatcher.Post(() =>
                {
                    if (sessionId == mySessionId)
                        HostConnectionFailed?.Invoke(this, localizedMessage);
                });
                break;
            }

            if (bytesRead > 0)
            {
                string msg = overMessage + encoding.GetString(message, 0, bytesRead);
                var commands = new List<string>();

                while (true)
                {
                    int index = msg.IndexOf(ProgramConstants.LAN_MESSAGE_SEPARATOR);

                    if (index == -1)
                    {
                        overMessage = msg;
                        break;
                    }

                    commands.Add(msg.Substring(0, index));
                    msg = msg.Substring(index + 1);
                }

                foreach (string cmd in commands)
                {
                    string capturedCmd = cmd;
                    uiDispatcher.Post(() =>
                    {
                        if (sessionId == mySessionId)
                            HostMessage?.Invoke(this, capturedCmd);
                    });
                }

                continue;
            }

            if (leaving)
                break;

            Logger.Log(string.Format("Reading data from the server failed (0 bytes received)! Server address: {0}", hostEndPoint.Address));

            string zeroMessage = string.Format(
                "Reading data from the server failed (0 bytes received)! Server address: {0}".L10N("Client:Main:LanServerReadZero"),
                hostEndPoint.Address);

            uiDispatcher.Post(() =>
            {
                if (sessionId == mySessionId)
                    HostConnectionFailed?.Invoke(this, zeroMessage);
            });

            break;
        }
    }

    /// <summary>Sends a message to the host (the host sends to itself too).</summary>
    public void SendToHost(string message)
    {
        if (client == null || !client.Connected)
            return;

        byte[] buffer = encoding.GetBytes(message + ProgramConstants.LAN_MESSAGE_SEPARATOR);

        try
        {
            NetworkStream ns = client.GetStream();
            ns.Write(buffer, 0, buffer.Length);
            ns.Flush();
        }
        catch
        {
            Logger.Log("Sending message to game host failed!");
        }
    }

    /// <summary>Host: sends a message to the players (optionally all but the local player).</summary>
    public static void Broadcast(IEnumerable<PlayerInfo> players, string message, bool otherPlayersOnly = false)
    {
        foreach (PlayerInfo pInfo in players.Where(p => !otherPlayersOnly || p.Name != ProgramConstants.PLAYERNAME))
            ((LANPlayerInfo)pInfo).SendMessage(message);
    }

    /// <summary>Stops the session: the host stops listening; the connection to the host is closed.</summary>
    public void Stop()
    {
        leaving = true;
        listener?.Stop();

        if (client != null && client.Connected)
            client.Close();
    }
}
