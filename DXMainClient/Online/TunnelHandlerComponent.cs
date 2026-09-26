using System;

using DTAClient.Domain.Multiplayer.CnCNet;

using Microsoft.Xna.Framework;

using Rampastring.XNAUI;

namespace DTAClient.Online
{
    /// <summary>
    /// Drives the <see cref="TunnelHandler"/> from the XNA game loop while the client is connected to CnCNet.
    /// </summary>
    public sealed class TunnelHandlerComponent : GameComponent
    {
        private readonly TunnelHandler tunnelHandler;

        public TunnelHandlerComponent(WindowManager windowManager, CnCNetManager connectionManager, TunnelHandler tunnelHandler)
            : base(windowManager.Game)
        {
            this.tunnelHandler = tunnelHandler;

            Enabled = false;

            connectionManager.Connected += ConnectionManager_Connected;
            connectionManager.Disconnected += ConnectionManager_Disconnected;
            connectionManager.ConnectionLost += ConnectionManager_ConnectionLost;
        }

        private void ConnectionManager_Connected(object sender, EventArgs e)
        {
            tunnelHandler.OnConnected();
            Enabled = true;
        }

        private void ConnectionManager_ConnectionLost(object sender, EventArguments.ConnectionLostEventArgs e)
        {
            Enabled = false;
            tunnelHandler.OnDisconnected();
        }

        private void ConnectionManager_Disconnected(object sender, EventArgs e)
        {
            Enabled = false;
            tunnelHandler.OnDisconnected();
        }

        public override void Update(GameTime gameTime)
        {
            tunnelHandler.Update();

            base.Update(gameTime);
        }
    }
}
