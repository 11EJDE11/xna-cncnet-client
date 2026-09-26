#nullable enable
using System;
using System.Reflection;

using SixLabors.ImageSharp;

namespace DTAClient.Domain.Multiplayer.CnCNet
{
    /// <summary>
    /// A <see cref="CnCNetGame"/> that uses a custom icon file from the theme, or falls back
    /// to the unknown game icon embedded in the assembly.
    /// </summary>
    internal sealed class CustomCnCNetGame : CnCNetGame
    {
        private static readonly Lazy<Image> lazyFallbackImage = new(() =>
            Image.Load(
                Assembly.GetAssembly(typeof(CustomCnCNetGame))!
                .GetManifestResourceStream("DTAClient.Icons.unknownicon.png")));
        private static Image FallbackImage => lazyFallbackImage.Value;

        private readonly string iconFilename;

        public CustomCnCNetGame(string iconFilename)
        {
            this.iconFilename = iconFilename;
        }

        protected override Image? LoadImage() => FallbackImage;

        public override string? IconFilename => iconFilename;
    }
}
