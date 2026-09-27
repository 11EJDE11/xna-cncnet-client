#nullable enable
using System;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;

using Rampastring.Tools;

namespace ClientLogic.UI;

/// <summary>Opens links shared in chat, as the XNA URLHandler: trusted domains at once, others after a warning.</summary>
public static class LinkOpener
{
    /// <summary>
    /// Checks whether a URL is safe before opening it, asking for confirmation otherwise.
    /// </summary>
    public static void OpenLink(string url, IDialogService dialogs)
    {
        // Determine if the links is trusted
        bool isTrusted = false;
        try
        {
            string domain = new Uri(url).Host;
            var trustedDomains = ClientConfiguration.Instance.TrustedDomains.Concat(ClientConfiguration.Instance.AlwaysTrustedDomains);
            isTrusted = trustedDomains.Contains(domain, StringComparer.InvariantCultureIgnoreCase)
                || trustedDomains.Any(trustedDomain => domain.EndsWith("." + trustedDomain, StringComparison.InvariantCultureIgnoreCase));
        }
        catch (Exception ex)
        {
            isTrusted = false;
            Logger.Log($"Error in parsing the URL \"{url}\": {ex.ToString()}");
        }

        if (isTrusted)
        {
            ProcessLauncher.StartShellProcess(url);
            return;
        }

        // Show the warning if the links is not trusted
        dialogs.Confirm(
            "Open Link Confirmation".L10N("Client:Main:OpenLinkConfirmationTitle"),
            """
                You're about to open a link shared in chat.

                Please note that this link hasn't been verified,
                and CnCNet is not responsible for its content.

                Would you like to open the following link in your browser?
                """.L10N("Client:Main:OpenLinkConfirmationText")
            + Environment.NewLine + Environment.NewLine + url,
            () => ProcessLauncher.StartShellProcess(url));
    }
}
