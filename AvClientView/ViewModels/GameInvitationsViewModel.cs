using System;
using System.Collections.ObjectModel;

using ClientLogic.CnCNet;

using DTAClient.Domain.Multiplayer.CnCNet;

namespace AvClientView.ViewModels;

/// <summary>An invitation's box: the invitation and the sender's game (for its icon; null if unknown).</summary>
public sealed record GameInvitationItemViewModel(GameInvitation Invitation, CnCNetGame SenderGame);

/// <summary>
/// The open CnCNet game invitations, each shown as the XNA ChoiceNotificationBox at the top left until it is answered
/// or its game closes. The CnCNet lobby fills it and handles the answers.
/// </summary>
public sealed class GameInvitationsViewModel
{
    public ObservableCollection<GameInvitationItemViewModel> Items { get; } = [];

    /// <summary>Yes was clicked.</summary>
    public event EventHandler<GameInvitation> Accepted;

    /// <summary>No was clicked.</summary>
    public event EventHandler<GameInvitation> Declined;

    public void Add(GameInvitation invitation, CnCNetGame senderGame) => Items.Add(new(invitation, senderGame));

    public void Remove(GameInvitation invitation)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i].Invitation == invitation)
                Items.RemoveAt(i);
        }
    }

    public void Accept(GameInvitationItemViewModel item)
    {
        Items.Remove(item);
        Accepted?.Invoke(this, item.Invitation);
    }

    public void Decline(GameInvitationItemViewModel item)
    {
        Items.Remove(item);
        Declined?.Invoke(this, item.Invitation);
    }
}
