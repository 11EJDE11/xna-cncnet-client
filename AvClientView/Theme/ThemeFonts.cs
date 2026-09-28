using System;
using System.Collections.Generic;
using System.IO;

using Avalonia.Media;
using Avalonia.Media.Fonts;

using ClientCore;

using Rampastring.Tools;

namespace AvClientView.Theme;

/// <summary>
/// One of the theme's TrueType fonts, loaded from disk. Each font index has its own collection: with several faces of
/// one family in a collection (Roboto SemiCondensed Medium and Roboto SemiBold), Avalonia's matching returned the
/// SemiBold face for the Medium font.
/// </summary>
public sealed class ThemeFontCollection(Uri key) : FontCollectionBase
{
    public override Uri Key { get; } = key;

    /// <summary>Adds a font file; returns its family name, or null.</summary>
    public string AddFile(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            var memory = new MemoryStream();
            stream.CopyTo(memory);
            memory.Position = 0;

            return TryAddGlyphTypeface(memory, out GlyphTypeface typeface) ? typeface.FamilyName : null;
        }
        catch (Exception ex)
        {
            Logger.Log($"ThemeFonts: could not load {path}: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// The XNA client's fonts by index (Fonts.ini: [Fonts] Count, [FontN] Type, Path, Size), as Avalonia fonts. Sprite
/// fonts (.xnb) fall back to the default font at the XNA default size.
/// </summary>
public static class ThemeFonts
{
    private const double DEFAULT_SIZE = 12;

    private static readonly List<(FontFamily Family, double Size)> fonts = [];
    private static readonly Dictionary<int, double> centeringOffsets = [];

    /// <summary>
    /// XNA centres the visible capital H, not the font's line box (TTFFontWrapper). Apply this offset to text
    /// that Avalonia has centred by line height, so check boxes and drop-downs use XNA's vertical centering.
    /// </summary>
    public static double CenteringOffset(int fontIndex)
    {
        if (centeringOffsets.TryGetValue(fontIndex, out double offset))
            return offset;

        (FontFamily family, double size) = Get(fontIndex);
        var text = new FormattedText("H", System.Globalization.CultureInfo.InvariantCulture,
            Avalonia.Media.FlowDirection.LeftToRight, new Typeface(family), size, Brushes.White);
        Geometry geometry = text.BuildGeometry(default);
        if (geometry != null && geometry.Bounds.Height > 0)
        {
            Avalonia.Rect bounds = geometry.Bounds;
            offset = text.Height / 2 - (bounds.Top + bounds.Bottom) / 2;
        }

        centeringOffsets[fontIndex] = offset;
        return offset;
    }

    public static void Initialize()
    {
        string iniPath = ThemeAssets.FindFile("Fonts.ini");
        if (iniPath == null)
        {
            Logger.Log("ThemeFonts: no Fonts.ini; using the default font.");
            return;
        }

        var ini = new IniFile(iniPath);

        int count = ini.GetIntValue("Fonts", "Count", 0);
        for (int i = 0; i < count; i++)
        {
            string section = "Font" + i;
            double size = ini.GetIntValue(section, "Size", (int)DEFAULT_SIZE);
            string type = ini.GetStringValue(section, "Type", string.Empty);
            string file = ThemeAssets.FindFile(ini.GetStringValue(section, "Path", string.Empty));

            string familyName = null;
            var collectionKey = new Uri("fonts:ClientTheme" + i);
            if (type.Equals("TrueType", StringComparison.OrdinalIgnoreCase) && file != null)
            {
                var collection = new ThemeFontCollection(collectionKey);
                familyName = collection.AddFile(file);
                if (familyName != null)
                    FontManager.Current.AddFontCollection(collection);
            }

            fonts.Add(familyName == null
                ? (FontFamily.Default, size)
                : (new FontFamily(collectionKey + "#" + familyName), size));

            Logger.Log($"ThemeFonts: font {i} = {familyName ?? "default"} {size}");
        }
    }

    /// <summary>A text's size in an XNA font index, in pixels (rounded up).</summary>
    public static (int Width, int Height) Measure(string text, int fontIndex)
    {
        if (string.IsNullOrEmpty(text))
            return (0, 0);

        (FontFamily family, double size) = Get(fontIndex);
        using var layout = new Avalonia.Media.TextFormatting.TextLayout(text, new Typeface(family), size, Brushes.White);
        return ((int)Math.Ceiling(layout.WidthIncludingTrailingWhitespace), (int)Math.Ceiling(layout.Height));
    }

    /// <summary>The font of an XNA font index.</summary>
    public static (FontFamily Family, double Size) Get(int index) =>
        index >= 0 && index < fonts.Count ? fonts[index] : (FontFamily.Default, DEFAULT_SIZE);
}
