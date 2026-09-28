using ClientGUI;
using DTAClient.Domain;
using ClientCore.Extensions;
using Microsoft.Xna.Framework;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using System;
#if WINFORMS
using System.Runtime.InteropServices;
#endif
using ClientLogic.Updates;

namespace DTAClient.DXGUI.Generic
{
    /// <summary>
    /// The update window, displaying the update progress to the user.
    /// </summary>
    public class UpdateWindow : XNAWindow
    {
        public delegate void UpdateCancelEventHandler(object sender, EventArgs e);
        public event UpdateCancelEventHandler UpdateCancelled;

        public delegate void UpdateCompletedEventHandler(object sender, EventArgs e);
        public event UpdateCompletedEventHandler UpdateCompleted;

        public delegate void UpdateFailureEventHandler(object sender, UpdateFailureEventArgs e);
        public event UpdateFailureEventHandler UpdateFailed;

        private const double DOT_TIME = 0.66;
        private const int MAX_DOTS = 5;

        private readonly UpdateProgress progress;

        public UpdateWindow(WindowManager windowManager, UpdateProgress progress) : base(windowManager)
        {
            this.progress = progress;
        }

        private XNALabel lblDescription;
        private XNALabel lblCurrentFileProgressPercentageValue;
        private XNALabel lblTotalProgressPercentageValue;
        private XNALabel lblCurrentFile;
        private XNALabel lblUpdaterStatus;

        private XNAProgressBar prgCurrentFile;
        private XNAProgressBar prgTotal;
#if WINFORMS
        private TaskbarProgress tbp;
#endif

        int dotCount = 0;
        double currentDotTime = 0.0;

        public override void Initialize()
        {
            Name = "UpdateWindow";
            ClientRectangle = new Rectangle(0, 0, 446, 270);
            BackgroundTexture = AssetLoader.LoadTexture("updaterbg.png");

            lblDescription = new XNALabel(WindowManager);
            lblDescription.Text = string.Empty;
            lblDescription.ClientRectangle = new Rectangle(12, 9, 0, 0);
            lblDescription.Name = nameof(lblDescription);

            var lblCurrentFileProgressPercentage = new XNALabel(WindowManager);
            lblCurrentFileProgressPercentage.Text = "Progress percentage of current file:".L10N("Client:Main:CurrentFileProgressPercentage");
            lblCurrentFileProgressPercentage.ClientRectangle = new Rectangle(12, 90, 0, 0);
            lblCurrentFileProgressPercentage.Name = nameof(lblCurrentFileProgressPercentage);

            lblCurrentFileProgressPercentageValue = new XNALabel(WindowManager);
            lblCurrentFileProgressPercentageValue.Text = "0%";
            lblCurrentFileProgressPercentageValue.ClientRectangle = new Rectangle(409, lblCurrentFileProgressPercentage.Y, 0, 0);
            lblCurrentFileProgressPercentageValue.Name = nameof(lblCurrentFileProgressPercentageValue);

            prgCurrentFile = new XNAProgressBar(WindowManager);
            prgCurrentFile.Name = nameof(prgCurrentFile);
            prgCurrentFile.Maximum = 100;
            prgCurrentFile.ClientRectangle = new Rectangle(12, 110, 422, 30);
            //prgCurrentFile.BorderColor = UISettings.WindowBorderColor;
            prgCurrentFile.SmoothForwardTransition = true;
            prgCurrentFile.SmoothTransitionRate = 10;

            lblCurrentFile = new XNALabel(WindowManager);
            lblCurrentFile.Name = nameof(lblCurrentFile);
            lblCurrentFile.ClientRectangle = new Rectangle(12, 142, 0, 0);

            var lblTotalProgressPercentage = new XNALabel(WindowManager);
            lblTotalProgressPercentage.Text = "Total progress percentage:".L10N("Client:Main:TotalProgressPercentage");
            lblTotalProgressPercentage.ClientRectangle = new Rectangle(12, 170, 0, 0);
            lblTotalProgressPercentage.Name = nameof(lblTotalProgressPercentage);

            lblTotalProgressPercentageValue = new XNALabel(WindowManager);
            lblTotalProgressPercentageValue.Text = "0%";
            lblTotalProgressPercentageValue.ClientRectangle = new Rectangle(409, lblTotalProgressPercentage.Y, 0, 0);
            lblTotalProgressPercentageValue.Name = nameof(lblTotalProgressPercentageValue);

            prgTotal = new XNAProgressBar(WindowManager);
            prgTotal.Name = nameof(prgTotal);
            prgTotal.Maximum = 100;
            prgTotal.ClientRectangle = new Rectangle(12, 190, prgCurrentFile.Width, prgCurrentFile.Height);
            //prgTotal.BorderColor = UISettings.WindowBorderColor;

            lblUpdaterStatus = new XNALabel(WindowManager);
            lblUpdaterStatus.Name = nameof(lblUpdaterStatus);
            lblUpdaterStatus.Text = "Preparing".L10N("Client:Main:StatusPreparing");
            lblUpdaterStatus.ClientRectangle = new Rectangle(12, 240, 0, 0);

            var btnCancel = new XNAClientButton(WindowManager);
            btnCancel.ClientRectangle = new Rectangle(301, 240, UIDesignConstants.BUTTON_WIDTH_133, UIDesignConstants.BUTTON_HEIGHT);
            btnCancel.Text = "Cancel".L10N("Client:Main:ButtonCancel");
            btnCancel.LeftClick += BtnCancel_LeftClick;

            AddChild(lblDescription);
            AddChild(lblCurrentFileProgressPercentage);
            AddChild(lblCurrentFileProgressPercentageValue);
            AddChild(prgCurrentFile);
            AddChild(lblCurrentFile);
            AddChild(lblTotalProgressPercentage);
            AddChild(lblTotalProgressPercentageValue);
            AddChild(prgTotal);
            AddChild(lblUpdaterStatus);
            AddChild(btnCancel);

            base.Initialize(); // Read theme settings from INI

            CenterOnParent();

            progress.Changed += (_, _) => RefreshProgress();
            progress.Completed += Progress_Completed;
            progress.Failed += Progress_Failed;
            progress.Cancelled += Progress_Cancelled;
            progress.ForceUpdateCheckFailed += (_, _) => XNAMessageBox.Show(WindowManager,
                "Force Update Failure".L10N("Client:Main:ForceUpdateFailureTitle"), "Checking for updates failed.".L10N("Client:Main:ForceUpdateFailureText"));
#if WINFORMS

            tbp = new TaskbarProgress();
#endif
        }

        private void RefreshProgress()
        {
            lblDescription.Text = progress.Description;
            lblUpdaterStatus.Text = progress.Status;
            lblCurrentFile.Text = progress.CurrentFile;
            prgCurrentFile.Value = progress.FilePercentage;
            prgTotal.Value = progress.TotalPercentage;
            lblCurrentFileProgressPercentageValue.Text = progress.FilePercentage + "%";
            lblTotalProgressPercentageValue.Text = progress.TotalPercentage + "%";
#if WINFORMS

            /*/ TODO Improve the updater
             * When the updater thread in DTAUpdater.dll has completed the update, it will
             * restart the client right away without giving the UI thread a chance to
             * finish its tasks and free resources in a proper way.
             * Because of that, this function is sometimes executed when
             * the game window has already been hidden / removed, and the code below
             * will then crash the client, causing the user to see a KABOOM message
             * along with the successful update, which is likely quite confusing for the user.
             * The try-catch is a dirty temporary workaround.
             * /*/
            try
            {
                if (progress.TotalPercentage > 0)
                {
                    tbp.SetState(WindowManager.GetWindowHandle(), TaskbarProgress.TaskbarStates.Normal);
                    tbp.SetValue(WindowManager.GetWindowHandle(), prgTotal.Value, prgTotal.Maximum);
                }
            }
            catch
            {
            }
#endif
        }

        private void Progress_Completed(object sender, EventArgs e)
        {
            ClearTaskbarProgress();
            UpdateCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void Progress_Failed(object sender, string reason)
        {
            ClearTaskbarProgress();
            UpdateFailed?.Invoke(this, new UpdateFailureEventArgs(reason));
        }

        private void Progress_Cancelled(object sender, EventArgs e)
        {
            ClearTaskbarProgress();
            UpdateCancelled?.Invoke(this, EventArgs.Empty);
        }

        private void ClearTaskbarProgress()
        {
#if WINFORMS
            tbp.SetState(WindowManager.GetWindowHandle(), TaskbarProgress.TaskbarStates.NoProgress);
#endif
        }

        private void BtnCancel_LeftClick(object sender, EventArgs e) => progress.Cancel();

        public void SetData(string newGameVersion) => progress.Start(newGameVersion);

        public void ForceUpdate() => progress.ForceUpdate();

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            currentDotTime += gameTime.ElapsedGameTime.TotalSeconds;
            if (currentDotTime > DOT_TIME)
            {
                currentDotTime = 0.0;
                dotCount++;
                if (dotCount > MAX_DOTS)
                    dotCount = 0;
            }
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);

            float xOffset = 3.0f;

            for (int i = 0; i < dotCount; i++)
            {
                var wrect = lblUpdaterStatus.RenderRectangle();
                Renderer.DrawStringWithShadow(".", lblUpdaterStatus.FontIndex,
                    new Vector2(wrect.Right + xOffset, wrect.Bottom - 15.0f), lblUpdaterStatus.TextColor);
                xOffset += 3.0f;
            }
        }
    }

    public class UpdateFailureEventArgs : EventArgs
    {
        public UpdateFailureEventArgs(string reason)
        {
            this.reason = reason;
        }

        string reason = String.Empty;

        /// <summary>
        /// The returned error message from the update failure.
        /// </summary>
        public string Reason
        {
            get { return reason; }
        }
    }
#if WINFORMS

    /// <summary>
    /// For utilizing the taskbar progress bar introduced in Windows 7:
    /// http://stackoverflow.com/questions/1295890/windows-7-progress-bar-in-taskbar-in-c
    /// </summary>
    public class TaskbarProgress
    {
        public enum TaskbarStates
        {
            NoProgress = 0,
            Indeterminate = 0x1,
            Normal = 0x2,
            Error = 0x4,
            Paused = 0x8
        }

        [ComImportAttribute()]
        [GuidAttribute("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
        [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            // ITaskbarList
            [PreserveSig]
            void HrInit();
            [PreserveSig]
            void AddTab(IntPtr hwnd);
            [PreserveSig]
            void DeleteTab(IntPtr hwnd);
            [PreserveSig]
            void ActivateTab(IntPtr hwnd);
            [PreserveSig]
            void SetActiveAlt(IntPtr hwnd);

            // ITaskbarList2
            [PreserveSig]
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

            // ITaskbarList3
            [PreserveSig]
            void SetProgressValue(IntPtr hwnd, UInt64 ullCompleted, UInt64 ullTotal);
            [PreserveSig]
            void SetProgressState(IntPtr hwnd, TaskbarStates state);
        }

        [GuidAttribute("56FDF344-FD6D-11d0-958A-006097C9A090")]
        [ClassInterfaceAttribute(ClassInterfaceType.None)]
        [ComImportAttribute()]
        private class TaskbarInstance
        {
        }

        private ITaskbarList3 taskbarInstance = (ITaskbarList3)new TaskbarInstance();

        public void SetState(IntPtr windowHandle, TaskbarStates taskbarState)
        {
            taskbarInstance.SetProgressState(windowHandle, taskbarState);
        }

        public void SetValue(IntPtr windowHandle, double progressValue, double progressMax)
        {
            taskbarInstance.SetProgressValue(windowHandle, (ulong)progressValue, (ulong)progressMax);
        }
    }
#endif
}
