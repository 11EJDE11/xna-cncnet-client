#nullable enable
using System.Collections.Generic;

using DTAClient.Domain.Multiplayer.CnCNet;

using Microsoft.Xna.Framework.Graphics;

using Rampastring.XNAUI;

namespace DTAClient.DXGUI.Multiplayer
{
    /// <summary>
    /// The XNA textures of the CnCNet games' icons.
    /// </summary>
    public static class CnCNetGameTextures
    {
        private static readonly Dictionary<CnCNetGame, Texture2D?> textures = new();

        /// <summary>
        /// Gets the texture for a game's icon: the theme's icon file if the game has one and the theme contains it,
        /// otherwise the game's image. Created on first use; must be called only from the main (graphics) thread.
        /// </summary>
        public static Texture2D? GetTexture(this CnCNetGame game)
        {
            if (!textures.TryGetValue(game, out Texture2D? texture))
            {
                texture = LoadTexture(game);
                textures[game] = texture;
            }

            return texture;
        }

        private static Texture2D? LoadTexture(CnCNetGame game)
        {
            if (game.IconFilename != null && AssetLoader.AssetExists(game.IconFilename))
                return AssetLoader.LoadTexture(game.IconFilename);

            return game.Image == null ? null : AssetLoader.TextureFromImage(game.Image);
        }
    }
}
