#nullable enable

using System;
using System.IO;

using ClientCore;
using ClientCore.Extensions;

using ClientGUI;

using DTAClient.Domain;

using Microsoft.Xna.Framework;

using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Generic.OptionPanels;

/// <summary>Configures replay retention limits.</summary>
class StorageOptionsPanel : XNAOptionsPanel
{
    private const int TEXT_BOX_WIDTH = 70;
    private const int TEXT_BOX_HEIGHT = 21;
    private const int TEXT_BOX_X = 170;
    private const int ROW_SPACING = 30;
    private const int MAX_KEPT_REPLAYS_LIMIT = 100000;
    private const int MAX_FOLDER_SIZE_LIMIT_MB = 1024 * 1024;

    public StorageOptionsPanel(WindowManager windowManager, UserINISettings iniSettings)
        : base(windowManager, iniSettings)
    {
    }

    private XNATextBox tbMaxKeptReplays = null!;
    private XNATextBox tbMaxReplayFolderSize = null!;
    private XNATextBox tbReplayKeyframeStorageLimit = null!;
    private XNALabel lblReplayUsage = null!;

    public override void Initialize()
    {
        base.Initialize();

        Name = "StorageOptionsPanel";

        var lblReplaysHeader = new XNALabel(WindowManager);
        lblReplaysHeader.Name = nameof(lblReplaysHeader);
        lblReplaysHeader.FontIndex = 1;
        lblReplaysHeader.Text = "Replays".L10N("Client:DTAConfig:StorageReplaysHeader");
        lblReplaysHeader.ClientRectangle = new Rectangle(12, 14, 0, 0);

        var lblKeptReplays = new XNALabel(WindowManager);
        lblKeptReplays.Name = nameof(lblKeptReplays);
        lblKeptReplays.Text = "Keep at most:".L10N("Client:DTAConfig:StorageKeepAtMost");
        lblKeptReplays.ClientRectangle = new Rectangle(12, lblReplaysHeader.Bottom + ROW_SPACING - 12, 0, 0);

        tbMaxKeptReplays = new XNATextBox(WindowManager);
        tbMaxKeptReplays.Name = nameof(tbMaxKeptReplays);
        tbMaxKeptReplays.MaximumTextLength = 6;
        tbMaxKeptReplays.ClientRectangle = new Rectangle(
            TEXT_BOX_X, lblKeptReplays.Y - 4, TEXT_BOX_WIDTH, TEXT_BOX_HEIGHT);

        var lblKeptReplaysSuffix = new XNALabel(WindowManager);
        lblKeptReplaysSuffix.Name = nameof(lblKeptReplaysSuffix);
        lblKeptReplaysSuffix.Text = "replays  (0 = no limit)".L10N("Client:DTAConfig:StorageKeepAtMostSuffix");
        lblKeptReplaysSuffix.ClientRectangle = new Rectangle(
            tbMaxKeptReplays.Right + 8, lblKeptReplays.Y, 0, 0);

        var lblFolderSize = new XNALabel(WindowManager);
        lblFolderSize.Name = nameof(lblFolderSize);
        lblFolderSize.Text = "Maximum size:".L10N("Client:DTAConfig:StorageMaxSize");
        lblFolderSize.ClientRectangle = new Rectangle(12, lblKeptReplays.Y + ROW_SPACING, 0, 0);

        tbMaxReplayFolderSize = new XNATextBox(WindowManager);
        tbMaxReplayFolderSize.Name = nameof(tbMaxReplayFolderSize);
        tbMaxReplayFolderSize.MaximumTextLength = 7;
        tbMaxReplayFolderSize.ClientRectangle = new Rectangle(
            TEXT_BOX_X, lblFolderSize.Y - 4, TEXT_BOX_WIDTH, TEXT_BOX_HEIGHT);

        var lblFolderSizeSuffix = new XNALabel(WindowManager);
        lblFolderSizeSuffix.Name = nameof(lblFolderSizeSuffix);
        lblFolderSizeSuffix.Text = "MB  (0 = no limit)".L10N("Client:DTAConfig:StorageMaxSizeSuffix");
        lblFolderSizeSuffix.ClientRectangle = new Rectangle(
            tbMaxReplayFolderSize.Right + 8, lblFolderSize.Y, 0, 0);

        lblReplayUsage = new XNALabel(WindowManager);
        lblReplayUsage.Name = nameof(lblReplayUsage);
        lblReplayUsage.ClientRectangle = new Rectangle(12, lblFolderSize.Y + ROW_SPACING + 6, 0, 0);

        var lblExplanation = new XNALabel(WindowManager);
        lblExplanation.Name = nameof(lblExplanation);
        lblExplanation.Text = "The oldest replays are deleted once either limit is exceeded."
            .L10N("Client:DTAConfig:StorageReplayExplanation");
        lblExplanation.ClientRectangle = new Rectangle(12, lblReplayUsage.Y + ROW_SPACING, 0, 0);

        var lblKeyframesHeader = new XNALabel(WindowManager);
        lblKeyframesHeader.Name = nameof(lblKeyframesHeader);
        lblKeyframesHeader.FontIndex = 1;
        lblKeyframesHeader.Text = "Playback keyframes".L10N("Client:DTAConfig:StorageKeyframesHeader");
        lblKeyframesHeader.ClientRectangle = new Rectangle(12, lblExplanation.Bottom + ROW_SPACING, 0, 0);

        var lblKeyframeSize = new XNALabel(WindowManager);
        lblKeyframeSize.Name = nameof(lblKeyframeSize);
        lblKeyframeSize.Text = "Maximum size:".L10N("Client:DTAConfig:StorageKeyframeMaxSize");
        lblKeyframeSize.ClientRectangle = new Rectangle(12, lblKeyframesHeader.Bottom + ROW_SPACING - 12, 0, 0);

        tbReplayKeyframeStorageLimit = new XNATextBox(WindowManager);
        tbReplayKeyframeStorageLimit.Name = nameof(tbReplayKeyframeStorageLimit);
        tbReplayKeyframeStorageLimit.MaximumTextLength = 7;
        tbReplayKeyframeStorageLimit.ClientRectangle = new Rectangle(
            TEXT_BOX_X, lblKeyframeSize.Y - 4, TEXT_BOX_WIDTH, TEXT_BOX_HEIGHT);

        var lblKeyframeSizeSuffix = new XNALabel(WindowManager);
        lblKeyframeSizeSuffix.Name = nameof(lblKeyframeSizeSuffix);
        lblKeyframeSizeSuffix.Text = "MB  (0 = no limit)".L10N("Client:DTAConfig:StorageKeyframeMaxSizeSuffix");
        lblKeyframeSizeSuffix.ClientRectangle = new Rectangle(
            tbReplayKeyframeStorageLimit.Right + 8, lblKeyframeSize.Y, 0, 0);

        AddChild(lblReplaysHeader);
        AddChild(lblKeptReplays);
        AddChild(tbMaxKeptReplays);
        AddChild(lblKeptReplaysSuffix);
        AddChild(lblFolderSize);
        AddChild(tbMaxReplayFolderSize);
        AddChild(lblFolderSizeSuffix);
        AddChild(lblReplayUsage);
        AddChild(lblExplanation);
        AddChild(lblKeyframesHeader);
        AddChild(lblKeyframeSize);
        AddChild(tbReplayKeyframeStorageLimit);
        AddChild(lblKeyframeSizeSuffix);
    }

    public override void Load()
    {
        base.Load();

        tbMaxKeptReplays.Text = IniSettings.MaxKeptReplays.Value.ToString();
        tbMaxReplayFolderSize.Text = IniSettings.MaxReplayFolderSizeMB.Value.ToString();
        tbReplayKeyframeStorageLimit.Text = IniSettings.ReplayKeyframeStorageLimitMB.Value.ToString();

        RefreshUsageLabel();
    }

    public override bool Save()
    {
        bool restartRequired = base.Save();

        IniSettings.MaxKeptReplays.Value =
            ParseLimit(tbMaxKeptReplays.Text, IniSettings.MaxKeptReplays.Value, MAX_KEPT_REPLAYS_LIMIT);
        IniSettings.MaxReplayFolderSizeMB.Value =
            ParseLimit(tbMaxReplayFolderSize.Text, IniSettings.MaxReplayFolderSizeMB.Value, MAX_FOLDER_SIZE_LIMIT_MB);
        IniSettings.ReplayKeyframeStorageLimitMB.Value =
            ParseLimit(tbReplayKeyframeStorageLimit.Text,
                IniSettings.ReplayKeyframeStorageLimitMB.Value, MAX_FOLDER_SIZE_LIMIT_MB);

        return restartRequired;
    }

    public override bool RefreshPanel()
    {
        bool valuesChanged = base.RefreshPanel();

        RefreshUsageLabel();

        return valuesChanged;
    }

    private void RefreshUsageLabel()
    {
        int count = 0;
        long bytes = 0;

        try
        {
            DirectoryInfo directory = ReplayManager.GetDirectory();
            if (directory.Exists)
            {
                foreach (FileInfo file in directory.EnumerateFiles(ReplayManager.SearchPattern))
                {
                    count++;
                    bytes += file.Length;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log("StorageOptionsPanel: could not measure the replay directory: " + ex.Message);
        }

        lblReplayUsage.Text = string.Format(
            "Currently stored: {0} replays, {1:0.#} MB".L10N("Client:DTAConfig:StorageReplayUsage"),
            count, bytes / (1024.0 * 1024.0));
    }

    private static int ParseLimit(string? text, int previousValue, int maximum)
    {
        if (!int.TryParse(text?.Trim(), out int value) || value < 0)
            return previousValue;

        return Math.Min(value, maximum);
    }
}