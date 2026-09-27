using Rampastring.XNAUI.XNAControls;
using System;
using ClientCore;
using Rampastring.XNAUI;
using ClientGUI;
using ClientLogic.Launch;
using ClientCore.Extensions;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace DTAClient.DXGUI
{
    /// <summary>
    /// Displays a dialog in the client when a game is in progress.
    /// Also enables power-saving (lowers FPS) while a game is in progress,
    /// and performs various operations on game start and exit.
    /// </summary>
    public class GameInProgressWindow : XNAPanel
    {
        private const double POWER_SAVING_FPS = 5.0;

        public GameInProgressWindow(WindowManager windowManager, GameInProgressTracker tracker) : base(windowManager)
        {
            this.tracker = tracker;
        }

        private readonly GameInProgressTracker tracker;

        private bool initialized = false;
        private bool nativeCursorUsed = false;
        private bool savedIsFixedTimeStep;

        public override void Initialize()
        {
            if (initialized)
                throw new InvalidOperationException("GameInProgressWindow cannot be initialized twice!");

            initialized = true;
            savedIsFixedTimeStep = Game.IsFixedTimeStep;

            BackgroundTexture = AssetLoader.CreateTexture(new Color(0, 0, 0, 128), 1, 1);
            PanelBackgroundDrawMode = PanelBackgroundImageDrawMode.STRETCHED;
            DrawBorders = false;
            ClientRectangle = new Rectangle(0, 0, WindowManager.RenderResolutionX, WindowManager.RenderResolutionY);

            XNAWindow window = new XNAWindow(WindowManager);

            window.Name = "GameInProgressWindow";
            window.BackgroundTexture = AssetLoader.LoadTexture("gameinprogresswindowbg.png");
            window.ClientRectangle = new Rectangle(0, 0, 200, 100);

            XNALabel explanation = new XNALabel(WindowManager);
            explanation.Text = "A game is in progress.".L10N("Client:Main:GameInProgress");

            AddChild(window);

            window.AddChild(explanation);

            base.Initialize();

            tracker.GameStarted += Tracker_GameStarted;
            tracker.GameExited += Tracker_GameExited;

            explanation.CenterOnParent();

            window.CenterOnParent();

            if (Game.IsFixedTimeStep)
                Game.TargetElapsedTime = TimeSpan.FromMilliseconds(1000.0 / UserINISettings.Instance.ClientFPS);

            Visible = false;
            Enabled = false;
        }

        private void Tracker_GameStarted(object sender, EventArgs e)
        {
            Visible = true;
            Enabled = true;
            WindowManager.Cursor.Visible = false;
            nativeCursorUsed = Game.IsMouseVisible;
            Game.IsMouseVisible = false;
            savedIsFixedTimeStep = Game.IsFixedTimeStep;
            Game.IsFixedTimeStep = true;
            Game.TargetElapsedTime = TimeSpan.FromMilliseconds(1000.0 / POWER_SAVING_FPS);
#if WINFORMS

            if (UserINISettings.Instance.MinimizeWindowsOnGameStart)
                WindowManager.MinimizeWindow();
#endif
        }

        private void Tracker_GameExited(object sender, EventArgs e)
        {
            Visible = false;
            Enabled = false;
            if (nativeCursorUsed)
                Game.IsMouseVisible = true;
            else
                WindowManager.Cursor.Visible = true;
            Game.IsFixedTimeStep = savedIsFixedTimeStep;
            if (Game.IsFixedTimeStep)
                Game.TargetElapsedTime = TimeSpan.FromMilliseconds(1000.0 / UserINISettings.Instance.ClientFPS);

#if WINFORMS
            if (UserINISettings.Instance.MinimizeWindowsOnGameStart)
                WindowManager.MaximizeWindow();

#endif
            if (UserINISettings.Instance.BorderlessWindowedClient)
            {
                // Hack: Re-set graphics mode
                // Windows resizes our window if we're in fullscreen mode and
                // the in-game resolution is lower than the user's desktop resolution.
                // After the game exits, Windows doesn't properly re-size our window
                // back to cover the entire screen, which causes graphics to get
                // stretched and also messes up input handling since the window manager
                // still thinks it's using the original resolution.
                // Re-setting the graphics mode fixes it.
                GameClass.SetGraphicsMode(WindowManager);
            }
        }
    }
}