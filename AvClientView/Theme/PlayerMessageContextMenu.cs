using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using AvClientView.ViewModels;
using ClientCore.Extensions;
using ClientLogic.UI;
using DTAClient.Online;
using Microsoft.Extensions.DependencyInjection;

namespace AvClientView.Theme;

/// <summary>GlobalContextMenu's message actions, shared by lobby and private chat.</summary>
public static class PlayerMessageContextMenu
{
    public static void Open(Control target, Point position, ChatMessage message)
    {
        var connection = App.Services.GetRequiredService<CnCNetManager>();
        var model = App.Services.GetRequiredService<PrivateMessagesViewModel>();
        var items = new List<ThemedMenuItem>();
        if (!string.IsNullOrEmpty(message.SenderName) && connection.UserList.Any(u => u.Name == message.SenderName))
            items.AddRange(model.PlayerMenu(message.SenderName));
        // GetLinks returns null when the message has no links
        foreach (string link in (message.Message.GetLinks() ?? []).Distinct())
        {
            string display = link.Length > 40 ? link[..30] + "..." + link[^5..] : link;
            items.Add(new("Open Link".L10N("Client:Main:OpenLink") + " " + display, () => LinkOpener.OpenLink(link, App.Services.GetRequiredService<IDialogService>())));
            items.Add(new("Copy Link".L10N("Client:Main:CopyLink") + " " + display, async () =>
            {
                try
                {
                    var clipboard = TopLevel.GetTopLevel(target)?.Clipboard;
                    if (clipboard != null)
                        await clipboard.SetTextAsync(link);
                }
                catch (Exception)
                {
                    App.Services.GetRequiredService<IDialogService>().ShowMessage("Error".L10N("Client:Main:Error"),
                        "Unable to copy link".L10N("Client:Main:ClipboardCopyLinkFailed"));
                }
            }));
        }
        ThemedContextMenu.Open(target, position, items, items.Count > 5 ? 300 : 150);
    }
}
