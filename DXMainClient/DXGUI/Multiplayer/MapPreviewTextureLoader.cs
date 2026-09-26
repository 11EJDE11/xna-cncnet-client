using ClientCore.Caching;
using DTAClient.Domain.Multiplayer;
using Microsoft.Xna.Framework.Graphics;
using Rampastring.XNAUI;
using SixLabors.ImageSharp;

namespace DTAClient.DXGUI.Multiplayer
{
    /// <summary>
    /// Creates XNA textures of map previews.
    /// </summary>
    public static class MapPreviewTextureLoader
    {
        public static Texture2D GetPreviewTextureFromMap(this MapLoader mapLoader, Map map, bool syncLoadOnCacheMiss = false)
        {
            if (map?.IsImmediatePreviewImageAvailable() ?? false)
                return AssetLoader.LoadTextureUncached(map.PreviewPath);

            using CacheLease<Image> cacheLease = mapLoader.GetCachedPreviewImageFromMap(map, syncLoadOnCacheMiss);

            if (cacheLease != null)
                return AssetLoader.TextureFromImage(cacheLease.Value);
            else
                return null;
        }
    }
}
