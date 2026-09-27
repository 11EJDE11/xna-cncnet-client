using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Avalonia.Media;
using Avalonia.Media.Imaging;

using ClientCore;

using ClientLogic.UI;

using Rampastring.Tools;

namespace AvClientView.Theme;

/// <summary>
/// The theme's files, found as the XNA client's AssetLoader finds them: the translation's theme folder, the theme
/// folder, the translation folder, the base resource folder, then the game folder.
/// </summary>
public static class ThemeAssets
{
    private static readonly Dictionary<string, Bitmap> bitmaps = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> SearchPaths { get; private set; } = [];

    /// <summary>The client's render resolution (ClientResolutionX/Y), as the XNA client lays its screens out for it.</summary>
    /// <remarks>
    /// The XNA client creates these settings at start-up with a default from the screen; here they're read from the
    /// settings INI (default 1280x768).
    /// </remarks>
    public static int RenderWidth => Math.Max(800, UserINISettings.Instance.SettingsIni.GetIntValue(UserINISettings.VIDEO, "ClientResolutionX", 1280));

    public static int RenderHeight => Math.Max(600, UserINISettings.Instance.SettingsIni.GetIntValue(UserINISettings.VIDEO, "ClientResolutionY", 768));

    /// <summary>Sets the search paths; call after the settings and theme are known.</summary>
    public static void Initialize()
    {
        SearchPaths = new[]
        {
            UserINISettings.Instance.TranslationThemeFolderPath,
            ProgramConstants.GetResourcePath(),
            UserINISettings.Instance.TranslationFolderPath,
            ProgramConstants.GetBaseResourcePath(),
            ProgramConstants.GamePath,
        }.Where(p => !string.IsNullOrEmpty(p)).ToList();
    }

    /// <summary>The full path of a theme file, or null.</summary>
    public static string FindFile(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        string relative = name.Replace('\\', '/').TrimStart('/');

        foreach (string searchPath in SearchPaths)
        {
            FileInfo file = SafePath.GetFile(searchPath, relative);
            if (file.Exists)
                return file.FullName;
        }

        return null;
    }

    /// <summary>A texture as a bitmap (cached), or null if it doesn't exist.</summary>
    public static Bitmap LoadBitmap(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (bitmaps.TryGetValue(name, out Bitmap bitmap))
            return bitmap;

        string path = FindFile(name);
        if (path != null)
        {
            try
            {
                bitmap = new Bitmap(path);
            }
            catch (Exception ex)
            {
                Logger.Log($"ThemeAssets: could not load {path}: {ex.Message}");
            }
        }
        else
        {
            Logger.Log("ThemeAssets: texture not found: " + name);
        }

        bitmaps[name] = bitmap;
        return bitmap;
    }

    private static readonly Dictionary<object, Bitmap> gameIcons = [];

    /// <summary>A CnCNet game's icon (its embedded image), as the XNA lists draw it; null if it has none.</summary>
    public static Bitmap GameIcon(DTAClient.Domain.Multiplayer.CnCNet.CnCNetGame game)
    {
        if (game == null)
            return null;

        if (gameIcons.TryGetValue(game, out Bitmap icon))
            return icon;

        // The theme's icon file first, as CnCNetGameTextures does
        if (game.IconFilename != null && FindFile(game.IconFilename) != null)
        {
            icon = LoadBitmap(game.IconFilename);
            gameIcons[game] = icon;
            return icon;
        }

        try
        {
            if (game.Image is SixLabors.ImageSharp.Image image)
            {
                using var stream = new MemoryStream();
                SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, stream);
                stream.Position = 0;
                icon = new Bitmap(stream);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"ThemeAssets: could not load the icon of {game.InternalName}: {ex.Message}");
        }

        if (icon == null)
            Logger.Log($"ThemeAssets: no icon for game {game.InternalName} (IconFilename {game.IconFilename ?? "none"})");

        gameIcons[game] = icon;
        return icon;
    }

    /// <summary>A texture's size in pixels, or null.</summary>
    public static (int Width, int Height)? TextureSize(string name) =>
        LoadBitmap(name) is Bitmap bitmap ? (bitmap.PixelSize.Width, bitmap.PixelSize.Height) : null;

    /// <summary>Opens a link button's web address (XNALinkButton); other targets are ignored.</summary>
    public static void OpenUrl(string url)
    {
        if (!string.IsNullOrEmpty(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static Color ToColor(ChatColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>A colour from the client configuration ("R,G,B" or "R,G,B,A").</summary>
    public static Color ParseColor(string value, Color fallback)
    {
        try
        {
            return ToColor(ChatColor.Parse(value));
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    /// <summary>The theme's label colour (UILabelColor).</summary>
    public static Color LabelColor => ParseColor(ClientConfiguration.Instance.UILabelColor, Colors.White);

    /// <summary>The theme's button and input text colour (AltUIColor).</summary>
    public static Color ButtonTextColor => ParseColor(ClientConfiguration.Instance.AltUIColor, Colors.White);

    /// <summary>The theme's button hover text colour (ButtonHoverColor).</summary>
    public static Color ButtonHoverColor => ParseColor(ClientConfiguration.Instance.ButtonHoverColor, Colors.White);

    /// <summary>The theme's panel border colour (PanelBorderColor).</summary>
    public static Color PanelBorderColor => ParseColor(ClientConfiguration.Instance.PanelBorderColor, Colors.Gray);
}
