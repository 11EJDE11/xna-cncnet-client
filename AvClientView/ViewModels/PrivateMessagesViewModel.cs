using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using AvClientView.Theme;
using ClientCore;
using ClientCore.Enums;
using ClientCore.Extensions;
using ClientLogic.CnCNet;
using ClientLogic.Launch;
using ClientLogic.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DTAClient.Online;
using DTAClient.Online.EventArguments;

namespace AvClientView.ViewModels;

/// <summary>The XNA private messaging window's conversations, lists and notification rules.</summary>
public sealed partial class PrivateMessagesViewModel : ObservableObject
{
    private readonly CnCNetManager connection;
    private readonly CnCNetUserData userData;
    private readonly PrivateMessageHandler handler;
    private readonly CnCNetGameRoom room;
    private readonly Dictionary<string, List<ChatMessage>> conversations = [];
    private readonly ThemeSound messageSound = ThemeSound.Load("message.wav", (float)ClientConfiguration.Instance.SoundMessageCooldown);
    private readonly ThemeSound privateSound = ThemeSound.Load("pm.wav", (float)ClientConfiguration.Instance.SoundPrivateMessageCooldown);
    private string lastPartner;
    private string lastSender;
    private (string Name, string Message)? deferredNotification;
    private readonly DispatcherTimer notificationTimer;

    public PrivateMessagesViewModel(CnCNetManager connection, CnCNetUserData userData,
        PrivateMessageHandler handler, CnCNetGameRoom room, GameProcessService process)
    {
        this.connection = connection;
        this.userData = userData;
        this.handler = handler;
        this.room = room;
        handler.PrivateMessageReceived += Receive;
        handler.UnreadMessageCountUpdated += (_, e) => UnreadCount = e.UnreadMessageCount;
        connection.UserAdded += (_, e) => Presence(e.User.Name, true);
        connection.UserRemoved += (_, e) => Presence(e.UserName, false);
        connection.UserGameIndexUpdated += (_, _) => RefreshUsers();
        connection.Disconnected += (_, _) => RefreshUsers();
        connection.ConnectionLost += (_, _) => RefreshUsers();
        userData.UserFriendToggled += (_, _) => RefreshUsers();
        process.GameProcessExited += () => Dispatcher.UIThread.Post(() =>
        {
            if (deferredNotification is { } pending)
                Notify(pending.Name, pending.Message);
            deferredNotification = null;
        });
        notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        notificationTimer.Tick += (_, _) => { Notification = null; notificationTimer.Stop(); };
    }

    public ObservableCollection<PrivateMessagePlayer> Users { get; } = [];
    public ObservableCollection<ChatLineViewModel> Messages { get; } = [];
    public IEnumerable<RecentPlayer> RecentPlayers => userData.RecentList.OrderByDescending(p => p.GameTime);
    public bool ShowRecentPlayers => SelectedTab == 3;
    public bool CanSend => SelectedUser != null && IsOnline(SelectedUser.Name);
    public bool IsPlayerOnline(string name) => IsOnline(name);
    public event EventHandler OpenRequested;
    public event EventHandler<string> JoinRequested;

    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private int selectedTab;
    [ObservableProperty] private PrivateMessagePlayer selectedUser;
    [ObservableProperty] private string chatInput = string.Empty;
    [ObservableProperty] private int unreadCount;
    [ObservableProperty] private string notification;
    public string NotificationSender { get; private set; }
    public int NotificationGameId => connection.UserList.Find(u => u.Name == NotificationSender)?.GameID ?? -1;

    public void KeepNotificationVisible()
    {
        notificationTimer.Stop();
        notificationTimer.Start();
    }

    partial void OnSelectedTabChanged(int value)
    {
        SelectedUser = null;
        ChatInput = string.Empty;
        Messages.Clear();
        RefreshUsers();
        OnPropertyChanged(nameof(ShowRecentPlayers));
        OnPropertyChanged(nameof(RecentPlayers));
    }

    partial void OnSelectedUserChanged(PrivateMessagePlayer value)
    {
        ChatInput = string.Empty;
        RefreshMessages();
        OnPropertyChanged(nameof(CanSend));
    }

    public void Open()
    {
        bool wasOpen = IsOpen;
        SelectedTab = 0;
        RefreshUsers();
        Select(wasOpen ? lastSender : lastPartner);
        IsOpen = true;
        Notification = null;
        notificationTimer.Stop();
        handler.ResetUnreadMessageCount();
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public void OpenConversation(string name)
    {
        Open();
        SelectedTab = conversations.ContainsKey(name) ? 0 : userData.IsFriend(name) ? 1 : 2;
        RefreshUsers();
        Select(name);
    }

    public void Close() => IsOpen = false;

    private bool IsOnline(string name) => connection.UserList.Any(u => u.Name == name);
    private void Select(string name) => SelectedUser = Users.FirstOrDefault(u => u.Name == name);

    private void RefreshUsers()
    {
        string selected = SelectedUser?.Name;
        string draft = ChatInput;
        IEnumerable<string> names = SelectedTab switch
        {
            0 => conversations.Keys.OrderBy(n => !IsOnline(n)).ThenBy(n => !userData.IsFriend(n)).ThenBy(n => n),
            1 => userData.FriendList.OrderBy(n => !IsOnline(n)).ThenBy(n => n),
            2 => connection.UserList.Select(u => u.Name),
            _ => [],
        };
        var players = names.Select(n => new PrivateMessagePlayer(n, IsOnline(n),
            connection.UserList.Find(u => u.Name == n)?.GameID ?? -1)).ToList();
        Users.Clear();
        foreach (var player in players)
            Users.Add(player);
        Select(selected);
        if (SelectedUser?.Name == selected && selected != null)
            ChatInput = draft;
        OnPropertyChanged(nameof(CanSend));
    }

    private List<ChatMessage> Conversation(string name)
    {
        if (!conversations.TryGetValue(name, out var messages))
            conversations.Add(name, messages = []);
        return messages;
    }

    private void RefreshMessages()
    {
        Messages.Clear();
        if (SelectedUser != null && conversations.TryGetValue(SelectedUser.Name, out var messages))
            foreach (var message in messages)
                Messages.Add(ChatLineViewModel.From(message));
    }

    private static ChatColor MessageColor(string configured)
    {
        var color = ThemeAssets.ParseColor(configured, Avalonia.Media.Colors.White);
        return new ChatColor(color.R, color.G, color.B, color.A);
    }

    private void PlayMessageSound()
    {
        if (UserINISettings.Instance.MessageSound)
            messageSound.Play();
    }

    [RelayCommand]
    private void Send()
    {
        if (!CanSend || string.IsNullOrEmpty(ChatInput))
            return;
        string name = SelectedUser.Name;
        connection.SendCustomMessage(new QueuedMessage("PRIVMSG " + name + " :" + ChatInput, QueuedMessageType.CHAT_MESSAGE, 0));
        Conversation(name).Add(new ChatMessage(ProgramConstants.PLAYERNAME,
            MessageColor(ClientConfiguration.Instance.SentPMColor), DateTime.Now, ChatInput));
        lastPartner = name;
        SelectedTab = 0;
        RefreshUsers();
        Select(name);
        RefreshMessages();
        ChatInput = string.Empty;
        PlayMessageSound();
    }

    private void Receive(object sender, PrivateMessageEventArgs e)
    {
        int policy = UserINISettings.Instance.AllowPrivateMessagesFromState;
        if (policy == (int)AllowPrivateMessagesFromEnum.None)
            return;
        // XNA creates the conversation before applying the friends/current-channel filter.
        Conversation(e.Sender);
        RefreshUsers();
        bool friend = userData.IsFriend(e.Sender);
        if (policy == (int)AllowPrivateMessagesFromEnum.Friends && !friend)
            return;
        if (!friend && policy != (int)AllowPrivateMessagesFromEnum.All && connection.MainChannel.Users.Find(e.Sender) == null)
            return;
        Conversation(e.Sender).Add(new ChatMessage(e.Sender,
            MessageColor(ClientConfiguration.Instance.ReceivedPMColor), DateTime.Now, e.Message));
        lastSender = lastPartner = e.Sender;
        if (!IsOpen || SelectedUser?.Name != e.Sender)
        {
            if (ProgramConstants.IsInGame)
                deferredNotification = (e.Sender, e.Message);
            else
                Notify(e.Sender, e.Message);
            if (SelectedUser?.Name != e.Sender)
                return;
        }
        RefreshMessages();
        PlayMessageSound();
    }

    private void Notify(string name, string message)
    {
        if (UserINISettings.Instance.DisablePrivateMessagePopups)
            handler.IncrementUnreadMessageCount();
        else
        {
            NotificationSender = name;
            Notification = message;
            OnPropertyChanged(nameof(Notification));
            notificationTimer.Stop();
            notificationTimer.Start();
        }
        privateSound.Play();
    }

    private void Presence(string name, bool online)
    {
        if (conversations.TryGetValue(name, out var messages))
            messages.Add(new ChatMessage(string.Format(online
                ? "{0} is now online.".L10N("Client:Main:PlayerOnline")
                : "{0} is now offline.".L10N("Client:Main:PlayerOffline"), name)));
        RefreshUsers();
        RefreshMessages();
    }

    /// <summary>The same player actions as GlobalContextMenu; the lobby passes the channel admin flag.</summary>
    public IReadOnlyList<ThemedMenuItem> PlayerMenu(string name, bool isAdmin = false, bool allowInvite = false)
    {
        IRCUser user = connection.UserList.Find(u => u.Name == name) ?? new IRCUser(name);
        bool online = IsOnline(name);
        var items = new List<ThemedMenuItem>();
        if (online)
            items.Add(new("Private Message".L10N("Client:Main:PrivateMessage"), () => OpenConversation(name)));
        items.Add(new(userData.IsFriend(name) ? "Remove Friend".L10N("Client:Main:RemoveFriend") : "Add Friend".L10N("Client:Main:AddFriend"), () => userData.ToggleFriend(name)));
        items.Add(new(userData.IsIgnored(user.Ident) ? "Unblock".L10N("Client:Main:Unblock") : "Block".L10N("Client:Main:Block"),
            () => ToggleIgnore(user), Selectable: !isAdmin));
        if (online && allowInvite && !string.IsNullOrEmpty(room.ChannelName))
            items.Add(new("Invite".L10N("Client:Main:Invite"), () =>
            {
                if (ProgramConstants.IsInGame || string.IsNullOrEmpty(room.ChannelName))
                    return;
                string body = ProgramConstants.GAME_INVITE_CTCP_COMMAND + " " + room.ChannelName + ";" + room.RoomSettings.RoomName;
                if (!string.IsNullOrEmpty(room.CustomPassword))
                    body += ";" + room.CustomPassword;
                connection.SendCustomMessage(new QueuedMessage("PRIVMSG " + name + " :\u0001" + body + "\u0001", QueuedMessageType.CHAT_MESSAGE, 0));
            }));
        if (online)
            items.Add(new("Join".L10N("Client:Main:Join"), () => JoinRequested?.Invoke(this, name)));
        return items;
    }

    private void ToggleIgnore(IRCUser user)
    {
        if (!string.IsNullOrEmpty(user.Ident))
        {
            userData.ToggleIgnoreUser(user.Ident);
            return;
        }
        void Reply(object sender, WhoEventArgs e)
        {
            if (e.UserName != user.Name)
                return;
            connection.WhoReplyReceived -= Reply;
            user.Ident = e.Ident;
            userData.ToggleIgnoreUser(e.Ident);
        }
        connection.WhoReplyReceived += Reply;
        connection.SendWhoIsMessage(user.Name);
    }
}

public sealed record PrivateMessagePlayer(string Name, bool Online, int GameId);
