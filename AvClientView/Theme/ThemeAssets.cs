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

    /// <summary>A texture's size in pixels, or null.</summary>
    public static (int Width, int Height)? TextureSize(string name) =>
        LoadBitmap(name) is Bitmap bitmap ? (bitmap.PixelSize.Width, bitmap.PixelSize.Height) : null;

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
