using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.UI;
using ClientLogic.Updates;

using ClientUpdater;

using CommunityToolkit.Mvvm.ComponentModel;

using Rampastring.Tools;

namespace ClientLogic.Settings;

/// <summary>A custom component's row: its name and its install / update / uninstall button.</summary>
public sealed partial class ComponentRow(CustomComponent component) : ObservableObject
{
    public CustomComponent Component { get; } = component;

    /// <summary>The component's INI name (the XNA controls are "btn" / "lbl" + it).</summary>
    public string IniName => Component.ININame;

    public string Name => Component.GUIName;

    [ObservableProperty]
    private string buttonText = string.Empty;

    [ObservableProperty]
    private bool canClick;
}

/// <summary>
/// The options window's Components tab (DXMainClient's ComponentsPanel): each custom component with a button that
/// installs, updates or uninstalls it, downloads with progress, and cancelling them when the window closes.
/// </summary>
public sealed class ComponentsOptionsModel : OptionsPanelModel
{
    private readonly IDialogService dialogs;
    private readonly IUiDispatcher uiDispatcher;
    private bool downloadCancelled;

    public ComponentsOptionsModel(IUpdater updater, IDialogService dialogs, IUiDispatcher uiDispatcher) : base("ComponentsPanel")
    {
        this.dialogs = dialogs;
        this.uiDispatcher = uiDispatcher;

        foreach (CustomComponent component in Updater.CustomComponents ?? Enumerable.Empty<CustomComponent>())
            Components.Add(new ComponentRow(component) { ButtonText = InitialText(component) });

        updater.FileIdentifiersUpdated += () => uiDispatcher.Post(UpdateInstallationButtons);
    }

    public ObservableCollection<ComponentRow> Components { get; } = [];

    /// <summary>A component is being downloaded (closing the window cancels it after asking).</summary>
    public static bool IsDownloadInProgress => Updater.IsComponentDownloadInProgress();

    public override void Load()
    {
        base.Load();
        downloadCancelled = false;
        UpdateInstallationButtons();
    }

    private static bool IsInstalled(CustomComponent component) => SafePath.GetFile(ProgramConstants.GamePath, component.LocalPath).Exists;

    private static string InitialText(CustomComponent component)
    {
        if (IsInstalled(component))
        {
            return component.LocalIdentifier != component.RemoteIdentifier
                ? "Update".L10N("Client:DTAConfig:Update")
                : "Uninstall".L10N("Client:DTAConfig:Uninstall");
        }

        return !string.IsNullOrEmpty(component.RemoteIdentifier)
            ? "Install".L10N("Client:DTAConfig:Install")
            : "Not Available".L10N("Client:DTAConfig:NotAvailable");
    }

    private void UpdateInstallationButtons()
    {
        foreach (ComponentRow row in Components)
        {
            CustomComponent c = row.Component;
            if (!c.Initialized || c.IsBeingDownloaded)
            {
                row.CanClick = false;
                continue;
            }

            string buttonText = "Not Available".L10N("Client:DTAConfig:NotAvailable");
            bool buttonEnabled = false;

            if (IsInstalled(c))
            {
                buttonText = "Uninstall".L10N("Client:DTAConfig:Uninstall");
                buttonEnabled = true;

                if (c.LocalIdentifier != c.RemoteIdentifier)
                    buttonText = "Update".L10N("Client:DTAConfig:Update") + $" ({GetSizeString(c.RemoteSize)})";
            }
            else if (!string.IsNullOrEmpty(c.RemoteIdentifier))
            {
                buttonText = "Install".L10N("Client:DTAConfig:Install") + $" ({GetSizeString(c.RemoteSize)})";
                buttonEnabled = true;
            }

            row.ButtonText = buttonText;
            row.CanClick = buttonEnabled;
        }
    }

    /// <summary>A component's button: uninstall an up-to-date one, update an outdated one, or install after asking.</summary>
    public void Click(ComponentRow row)
    {
        if (!row.CanClick)
            return;

        CustomComponent cc = row.Component;
        if (cc.IsBeingDownloaded)
            return;

        FileInfo localFileInfo = SafePath.GetFile(ProgramConstants.GamePath, cc.LocalPath);

        if (localFileInfo.Exists)
        {
            if (cc.LocalIdentifier == cc.RemoteIdentifier)
            {
                localFileInfo.IsReadOnly = false;
                localFileInfo.Delete();
                row.ButtonText = "Install".L10N("Client:DTAConfig:Install") + $" ({GetSizeString(cc.RemoteSize)})";
                return;
            }

            Download(row);
            return;
        }

        dialogs.Confirm("Confirmation Required".L10N("Client:DTAConfig:UpdateConfirmRequiredTitle"),
            string.Format(("To enable {0} the Client will need to download the necessary files to your game directory.\n\n" +
                "This will take an additional {1} of disk space, and the download may take some time\n" +
                "depending on your Internet connection speed. The size of the download is {2}.\n\n" +
                "You will not be able to play during the download. Do you wish to continue?").L10N("Client:DTAConfig:UpdateConfirmRequiredText"),
                cc.GUIName, GetSizeString(cc.RemoteSize), GetSizeString(cc.Archived ? cc.RemoteArchiveSize : cc.RemoteSize)),
            () => Download(row));
    }

    private void Download(ComponentRow row)
    {
        row.CanClick = false;
        CustomComponent cc = row.Component;
        cc.DownloadFinished += OnDownloadFinished;
        cc.DownloadProgressChanged += OnDownloadProgressChanged;
        cc.DownloadComponent();
    }

    private ComponentRow RowOf(CustomComponent cc) => Components.FirstOrDefault(r => ReferenceEquals(r.Component, cc));

    private void OnDownloadProgressChanged(CustomComponent cc, int percentage) => uiDispatcher.Post(() =>
    {
        if (RowOf(cc) is not { } row)
            return;

        percentage = Math.Min(percentage, 100);
        row.ButtonText = cc.Archived && percentage == 100
            ? "Unpacking...".L10N("Client:DTAConfig:Unpacking")
            : "Downloading...".L10N("Client:DTAConfig:Downloading") + " " + percentage + "%";
    });

    private void OnDownloadFinished(CustomComponent cc, bool success) => uiDispatcher.Post(() =>
    {
        cc.DownloadFinished -= OnDownloadFinished;
        cc.DownloadProgressChanged -= OnDownloadProgressChanged;

        if (RowOf(cc) is not { } row)
            return;

        row.CanClick = true;

        if (!success)
        {
            if (!downloadCancelled)
            {
                dialogs.ShowMessage("Optional Component Download Failed".L10N("Client:DTAConfig:OptionalComponentDownloadFailedTitle"),
                    string.Format(("Download of optional component {0} failed.\n" +
                    "See client.log for details.\n\n" +
                    "If this problem continues, please contact your mod's authors for support.").L10N("Client:DTAConfig:OptionalComponentDownloadFailedText"),
                    cc.GUIName));
            }

            row.ButtonText = (IsInstalled(cc) ? "Update".L10N("Client:DTAConfig:Update") : "Install".L10N("Client:DTAConfig:Install")) +
                $" ({GetSizeString(cc.RemoteSize)})";
        }
        else
        {
            dialogs.ShowMessage("Download Completed".L10N("Client:DTAConfig:DownloadCompleteTitle"),
                string.Format("Download of optional component {0} completed succesfully.".L10N("Client:DTAConfig:DownloadCompleteText"), cc.GUIName));
            row.ButtonText = "Uninstall".L10N("Client:DTAConfig:Uninstall");
        }
    });

    public void CancelAllDownloads()
    {
        Logger.Log("Cancelling all custom component downloads.");
        downloadCancelled = true;

        foreach (ComponentRow row in Components)
        {
            if (row.Component.IsBeingDownloaded)
                row.Component.StopDownload();
        }
    }

    private static string GetSizeString(long size) => size < 1048576 ? (size / 1024) + " KB" : (size / 1048576) + " MB";

    /// <summary>The question before closing the window during downloads (OptionsWindow).</summary>
    public static (string Title, string Text) DownloadsInProgressQuestion =>
        ("Downloads in progress".L10N("Client:DTAConfig:DownloadingTitle"),
        ("Optional component downloads are in progress. The downloads will be cancelled if you exit the Options menu.\n\n" +
        "Are you sure you want to continue?").L10N("Client:DTAConfig:DownloadingText"));
}
