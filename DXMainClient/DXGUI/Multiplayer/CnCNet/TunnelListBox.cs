using DTAClient.Domain.Multiplayer.CnCNet;
using ClientCore.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using System;
using System.Collections.Generic;
using System.Linq;
using ClientCore;
using ClientLogic.Tunnels;
using System.IO;
using System.Reflection;

namespace DTAClient.DXGUI.Multiplayer.CnCNet
{
    /// <summary>
    /// A list box for listing CnCNet tunnel servers.
    /// </summary>
    class TunnelListBox : XNAMultiColumnListBox
    {
        private const int FLAG_WIDTH = TunnelFlags.FLAG_WIDTH;
        private const int FLAG_HEIGHT = TunnelFlags.FLAG_HEIGHT;

        public TunnelListBox(WindowManager windowManager, TunnelHandler tunnelHandler) : base(windowManager)
        {
            this.tunnelHandler = tunnelHandler;
            state = new TunnelListState(() => tunnelHandler.Tunnels);

            tunnelHandler.TunnelsRefreshed += TunnelHandler_TunnelsRefreshed;
            tunnelHandler.TunnelPinged += TunnelHandler_TunnelPinged;

            SelectedIndexChanged += TunnelListBox_SelectedIndexChanged;

            int headerHeight = (int)Renderer.GetTextDimensions("Name", HeaderFontIndex).Y;

            Width = 466;
            Height = LineHeight * 12 + headerHeight + 3;
            PanelBackgroundDrawMode = PanelBackgroundImageDrawMode.STRETCHED;
            BackgroundTexture = AssetLoader.CreateTexture(new Color(0, 0, 0, 128), 1, 1);

            using Stream flagsStream = Assembly.GetAssembly(typeof(GameCollection)).GetManifestResourceStream("DTAClient.Icons.flags16.png");
            var flagsPNG = SixLabors.ImageSharp.Image.Load(flagsStream);
            flagsSpriteSheet = AssetLoader.TextureFromImage(flagsPNG);

            var flagListBox = new FlagListBox(windowManager, tunnelHandler, flagsSpriteSheet);
            flagListBox.FontIndex = FontIndex;
            flagListBox.LineHeight = LineHeight;

            var flagHeader = new XNAPanel(windowManager);
            flagHeader.Width = 20;
            flagHeader.Height = headerHeight + 3;

            AddColumn(flagHeader, flagListBox);

            AddColumn("Name".L10N("Client:Main:NameHeader"), 210);
            AddColumn("Official".L10N("Client:Main:OfficialHeader"), 70);
            AddColumn("Ping".L10N("Client:Main:PingHeader"), 76);
            AddColumn("Players".L10N("Client:Main:PlayersHeader"), 90);
            AllowRightClickUnselect = false;
            AllowKeyboardInput = true;
        }

        public int? TargetVersion
        {
            get => state.TargetVersion;
            set
            {
                if (state.TargetVersion != value)
                {
                    state.TargetVersion = value;
                    if (ItemCount > 0)
                        TunnelHandler_TunnelsRefreshed(this, EventArgs.Empty);
                }
            }
        }

        public event EventHandler ListRefreshed;

        private readonly TunnelHandler tunnelHandler;
        private readonly TunnelListState state;
        private Texture2D flagsSpriteSheet;

        /// <summary>
        /// Selects a tunnel from the list with the given address and port.
        /// </summary>
        /// <param name="address">The address of the tunnel server to select.</param>
        /// <param name="port">The port of the tunnel server to select.</param>
        public void SelectTunnel(string address, int port)
        {
            state.SelectTunnel(address, port);
            SelectedIndex = state.SelectedIndex;
        }

        /// <summary>
        /// Gets whether or not a tunnel from the list with the given address and port is selected.
        /// </summary>
        /// <param name="address">The address of the tunnel server</param>
        /// <param name="port">The port of the tunnel server</param>
        /// <returns>True if tunnel with given address is selected, otherwise false.</returns>
        public bool IsTunnelSelected(string address, int port) => state.IsTunnelSelected(address, port);

        private void TunnelHandler_TunnelsRefreshed(object sender, EventArgs e)
        {
            ClearItems();

            state.Refresh();

            int tunnelIndex = 0;
            foreach (CnCNetTunnel tunnel in state.Tunnels)
            {
                List<string> info = new List<string>();

                info.Add(""); // Flag column
                info.Add(tunnel.Name);
                info.Add(Conversions.BooleanToString(tunnel.Official, BooleanStringStyle.YESNO));
                info.Add(tunnel.Ping.ToString());
                info.Add(tunnel.Clients + " / " + tunnel.MaxClients);

                AddItem(info, true);

                XNAListBoxItem flagItem = GetItem(0, tunnelIndex);
                if (flagItem != null)
                    flagItem.Tag = GetFlagRectangle(tunnel.CountryCode);

                tunnelIndex++;
            }

            SelectedIndex = state.SelectedIndex;

            ListRefreshed?.Invoke(this, EventArgs.Empty);
        }

        private void TunnelHandler_TunnelPinged(string address, int port)
        {
            int filteredIndex = state.OnTunnelPinged(address, port);
            if (filteredIndex == -1)
                return;

            CnCNetTunnel tunnel = tunnelHandler.Tunnels.First(t => t.Address == address && t.Port == port);
            XNAListBoxItem lbItem = GetItem(3, filteredIndex);
            lbItem.Text = tunnel.Ping.ToString();

            SelectedIndex = state.SelectedIndex;
        }

        public CnCNetTunnel GetSelectedTunnel() => IsValidIndexSelected() ? state.GetSelectedTunnel() : null;

        private void TunnelListBox_SelectedIndexChanged(object sender, EventArgs e) => state.Select(SelectedIndex);

        private static Rectangle? GetFlagRectangle(string countryCode) =>
            TunnelFlags.GetFlagOffset(countryCode) is int yOffset ? new Rectangle(0, yOffset, FLAG_WIDTH, FLAG_HEIGHT) : null;

        /// <summary>
        /// Custom listbox that draws country flags.
        /// </summary>
        private class FlagListBox : XNAListBox
        {
            private readonly TunnelHandler tunnelHandler;
            private readonly Texture2D flagsSpriteSheet;

            public FlagListBox(WindowManager windowManager, TunnelHandler tunnelHandler, Texture2D flagsSpriteSheet)
                : base(windowManager)
            {
                this.tunnelHandler = tunnelHandler;
                this.flagsSpriteSheet = flagsSpriteSheet;
            }

            public override void Draw(GameTime gameTime)
            {
                DrawPanel();

                int height = 2 - (ViewTop % LineHeight);

                for (int i = TopIndex; i < Items.Count; i++)
                {
                    if (height > Height)
                        break;

                    Rectangle? flagRect = Items[i].Tag as Rectangle?;

                    if (flagRect.HasValue)
                    {
                        int x = (Width - FLAG_WIDTH) / 2;
                        DrawTexture(flagsSpriteSheet,
                            flagRect.Value,
                            new Rectangle(x, height, FLAG_WIDTH, FLAG_HEIGHT),
                            Color.White);
                    }

                    height += LineHeight;
                }

                if (DrawBorders)
                    DrawPanelBorders();

                DrawChildren(gameTime);
            }
        }
    }
}
