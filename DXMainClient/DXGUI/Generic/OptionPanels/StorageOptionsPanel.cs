using ClientCore;
using ClientCore.Extensions;
using ClientGUI;
using DTAClient.Domain;
using Microsoft.Xna.Framework;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using System;

namespace DTAClient.DXGUI.Generic.OptionPanels
{
    /// <summary>
    /// Limits on what the client keeps on disk. Currently only replays, but the panel is
    /// deliberately generic - anything else that accumulates files belongs here too.
    /// </summary>
    class StorageOptionsPanel : XNAOptionsPanel
    {
        private const int TEXT_BOX_WIDTH = 70;
        private const int TEXT_BOX_HEIGHT = 21;
        private const int ROW_SPACING = 30;

        /// <summary>
        /// Generous rather than meaningful - the point is to stop a stray keystroke turning into
        /// an absurd value, not to express a real limit.
        /// </summary>
        private const int MAX_KEPT_REPLAYS_LIMIT = 100000;
        private const int MAX_FOLDER_SIZE_LIMIT_MB = 1024 * 1024;

        public StorageOptionsPanel(WindowManager windowManager, UserINISettings iniSettings)
            : base(windowManager, iniSettings)
        {
        }

        private XNATextBox tbMaxKeptReplays;
        private XNATextBox tbMaxReplayFolderSize;
        private XNALabel lblReplayUsage;

        public override void Initialize()
        {
            base.Initialize();

            Name = "StorageOptionsPanel";

            if (!ReplayManager.IsSupported)
                return;

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
                170, lblKeptReplays.Y - 4, TEXT_BOX_WIDTH, TEXT_BOX_HEIGHT);

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
                170, lblFolderSize.Y - 4, TEXT_BOX_WIDTH, TEXT_BOX_HEIGHT);

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
            lblExplanation.Text = ("The oldest replays are deleted once either limit is exceeded.")
                .L10N("Client:DTAConfig:StorageReplayExplanation");
            lblExplanation.ClientRectangle = new Rectangle(12, lblReplayUsage.Y + ROW_SPACING, 0, 0);

            AddChild(lblReplaysHeader);
            AddChild(lblKeptReplays);
            AddChild(tbMaxKeptReplays);
            AddChild(lblKeptReplaysSuffix);
            AddChild(lblFolderSize);
            AddChild(tbMaxReplayFolderSize);
            AddChild(lblFolderSizeSuffix);
            AddChild(lblReplayUsage);
            AddChild(lblExplanation);
        }

        public override void Load()
        {
            base.Load();

            if (!ReplayManager.IsSupported)
                return;

            tbMaxKeptReplays.Text = IniSettings.MaxKeptReplays.Value.ToString();
            tbMaxReplayFolderSize.Text = IniSettings.MaxReplayFolderSizeMB.Value.ToString();

            RefreshUsageLabel();
        }

        public override bool Save()
        {
            bool restartRequired = base.Save();

            if (!ReplayManager.IsSupported)
                return restartRequired;

            IniSettings.MaxKeptReplays.Value =
                ParseLimit(tbMaxKeptReplays.Text, IniSettings.MaxKeptReplays.Value, MAX_KEPT_REPLAYS_LIMIT);
            IniSettings.MaxReplayFolderSizeMB.Value =
                ParseLimit(tbMaxReplayFolderSize.Text, IniSettings.MaxReplayFolderSizeMB.Value, MAX_FOLDER_SIZE_LIMIT_MB);

            // Apply the new limits straight away rather than waiting for the next game to end.
            ReplayManager.Prune();

            return restartRequired;
        }

        public override bool RefreshPanel()
        {
            bool valuesChanged = base.RefreshPanel();

            if (ReplayManager.IsSupported)
                RefreshUsageLabel();

            return valuesChanged;
        }

        private void RefreshUsageLabel()
        {
            int count = 0;
            long bytes = 0;

            try
            {
                System.IO.DirectoryInfo directory = ReplayManager.GetDirectory();
                if (directory.Exists)
                {
                    foreach (System.IO.FileInfo file in directory.EnumerateFiles("*." + ReplayManager.FileExtension))
                    {
                        count++;
                        bytes += file.Length;
                    }
                }
            }
            catch (Exception)
            {
                // Nothing useful to say to the user about a directory we only wanted to measure.
            }

            lblReplayUsage.Text = string.Format(
                "Currently stored: {0} replays, {1:0.#} MB".L10N("Client:DTAConfig:StorageReplayUsage"),
                count, bytes / (1024.0 * 1024.0));
        }

        /// <summary>
        /// Keeps the previous value when the box holds something that is not a number, so a typo
        /// cannot silently turn a limit off.
        /// </summary>
        private static int ParseLimit(string text, int previousValue, int maximum)
        {
            if (!int.TryParse(text?.Trim(), out int value) || value < 0)
                return previousValue;

            return Math.Min(value, maximum);
        }
    }
}
