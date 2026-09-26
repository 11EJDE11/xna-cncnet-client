using System;
using System.Collections.Generic;
using System.Linq;

using DTAClient.Domain.Multiplayer.CnCNet;

namespace ClientLogic.CnCNet;

/// <summary>A game invitation: "channel;game name[;password]" sent as a private CTCP message.</summary>
public sealed record GameInvitation(string Sender, string ChannelName, string GameName, string Password)
{
    /// <summary>Reads the invitation's arguments; returns false if there are not 2 or 3 of them.</summary>
    public static bool TryParse(string sender, string arguments, out GameInvitation invitation)
    {
        string[] parts = arguments.Split(';');
        invitation = parts.Length is 2 or 3
            ? new GameInvitation(sender, parts[0], parts[1], parts.Length == 3 ? parts[2] : string.Empty)
            : null;

        return invitation != null;
    }
}

/// <summary>What to do with a received invitation.</summary>
public enum InvitationResponse
{
    /// <summary>From someone we share no channel with, or ignore: drop it silently.</summary>
    Ignore,

    /// <summary>We can't or won't join: tell the sender it failed.</summary>
    Decline,

    /// <summary>The same sender already invited us to the same game and that invitation is still shown.</summary>
    AlreadyShown,

    Show,
}

/// <summary>
/// The CnCNet game invitations the user has open, one per sender and game channel, and the rules for new ones.
/// </summary>
/// <typeparam name="TNotification">The front end's notification for an open invitation.</typeparam>
public sealed class GameInvitations<TNotification> where TNotification : class
{
    private readonly Dictionary<(string Sender, string Channel), WeakReference<TNotification>> open = [];

    /// <param name="senderIsKnown">We share a channel with the sender.</param>
    /// <param name="senderIsIgnored">The sender is on the ignore list.</param>
    /// <param name="joinError">Why the invited game can't be joined, or null.</param>
    /// <param name="friendsOnly">The user only accepts invitations from friends.</param>
    public InvitationResponse Decide(GameInvitation invitation, bool senderIsKnown, bool senderIsIgnored,
        string joinError, bool friendsOnly, bool senderIsFriend)
    {
        if (!senderIsKnown || senderIsIgnored)
            return InvitationResponse.Ignore;

        if (!string.IsNullOrEmpty(joinError) || (friendsOnly && !senderIsFriend))
            return InvitationResponse.Decline;

        return open.ContainsKey((invitation.Sender, invitation.ChannelName))
            ? InvitationResponse.AlreadyShown
            : InvitationResponse.Show;
    }

    /// <summary>Records the notification shown for an invitation.</summary>
    public void Add(GameInvitation invitation, TNotification notification) =>
        open[(invitation.Sender, invitation.ChannelName)] = new WeakReference<TNotification>(notification);

    /// <summary>Forgets an invitation (answered or dismissed); returns its notification if it's still alive.</summary>
    public TNotification Remove(string sender, string channelName)
    {
        if (!open.TryGetValue((sender, channelName), out WeakReference<TNotification> reference))
            return null;

        open.Remove((sender, channelName));
        return reference.TryGetTarget(out TNotification notification) ? notification : null;
    }

    /// <summary>
    /// The open invitations whose game is no longer listed. An invitation matches a listed game hosted by its sender
    /// on its channel.
    /// </summary>
    public IReadOnlyList<(string Sender, string Channel)> FindInvalid(IEnumerable<HostedCnCNetGame> listedGames) =>
        open.Keys.Where(key => !listedGames.Any(hg => hg.HostName == key.Sender && hg.ChannelName == key.Channel)).ToList();
}
