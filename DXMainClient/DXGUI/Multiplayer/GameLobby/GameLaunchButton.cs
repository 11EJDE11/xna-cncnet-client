using ClientCore;
using ClientGUI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using System;

namespace DTAClient.DXGUI.Multiplayer.GameLobby
{
    public class GameLaunchButton : XNAClientButton
    {
        public GameLaunchButton(WindowManager windowManager) : base(windowManager)
        {
        }

        private StarDisplay starDisplay;

        /// <summary>
        /// How long the "get ready" highlight effect plays for, in seconds.
        /// </summary>
        private const double FlashDuration = 6.0;

        /// <summary>
        /// The length of a single pulse of the highlight effect, in seconds.
        /// </summary>
        private const double FlashPulseLength = 0.7;

        private double flashTimeRemaining;

        /// <summary>
        /// The color of the "get ready" highlight effect. Read from the client
        /// configuration INI (GetReadyHighlightColor), defaulting to gold.
        /// </summary>
        private Color flashColor = Color.Gold;

        /// <summary>
        /// Starts a pulsing highlight effect around the button to draw the
        /// player's attention to it (e.g. when the host asks everyone to ready up).
        /// </summary>
        public void StartFlashing() => flashTimeRemaining = FlashDuration;

        /// <summary>
        /// Stops the highlight effect, if it's currently playing.
        /// </summary>
        public void StopFlashing() => flashTimeRemaining = 0.0;

        public void InitStarDisplay(Texture2D[] rankTextures)
        {
            if (starDisplay != null)
                throw new InvalidOperationException("The star display is already initialized!");

            starDisplay = new StarDisplay(WindowManager, rankTextures);
            starDisplay.InputEnabled = false;
            AddChild(starDisplay);
            ClientRectangleUpdated += (e, sender) => UpdateStarPosition();
            UpdateStarPosition();
        }

        public override void Initialize()
        {
            base.Initialize();

            flashColor = AssetLoader.GetColorFromString(ClientConfiguration.Instance.GetReadyHighlightColor);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (flashTimeRemaining > 0.0)
                flashTimeRemaining -= gameTime.ElapsedGameTime.TotalSeconds;
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);

            if (flashTimeRemaining <= 0.0)
                return;

            double pulse = (Math.Sin(flashTimeRemaining / FlashPulseLength * Math.PI * 2.0) + 1.0) / 2.0;
            float alpha = (float)(0.35 + (pulse * 0.65));

            Color highlight = flashColor * alpha;

            for (int i = 0; i < 3; i++)
                DrawRectangle(new Rectangle(i, i, Width - (i * 2), Height - (i * 2)), highlight);
        }

        public override string Text
        {
            get => base.Text;
            set { base.Text = value; UpdateStarPosition(); }
        }

        private void UpdateStarPosition()
        {
            if (starDisplay == null)
                return;

            starDisplay.Y = (Height - starDisplay.Height) / 2;
            starDisplay.X = (Width / 2) + (int)(Renderer.GetTextDimensions(Text, FontIndex).X / 2) + 3;
        }

        public void SetRank(int rank)
        {
            starDisplay.Rank = rank;
            UpdateStarPosition();
        }
    }

    class StarDisplay : XNAControl
    {
        public StarDisplay(WindowManager windowManager, Texture2D[] rankTextures) : base(windowManager)
        {
            Name = "StarDisplay";
            this.rankTextures = rankTextures;
            Width = rankTextures[1].Width;
            Height = rankTextures[1].Height;
        }

        private readonly Texture2D[] rankTextures;

        public int Rank { get; set; }

        public override void Initialize()
        {
            base.Initialize();
        }

        public override void Draw(GameTime gameTime)
        {
            DrawTexture(rankTextures[Rank], Point.Zero, Color.White);
            base.Draw(gameTime);
        }
    }
}
