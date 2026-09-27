using System;
using System.Collections.Generic;
using System.IO;

using Avalonia.Media;
using Avalonia.Media.Fonts;

using ClientCore;

using Rampastring.Tools;

namespace AvClientView.Theme;

/// <summary>The theme's TrueType fonts, loaded from disk.</summary>
public sealed class ThemeFontCollection : FontCollectionBase
{
    public static readonly Uri CollectionKey = new("fonts:ClientTheme");

    public override Uri Key => CollectionKey;

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

    public static void Initialize()
    {
        string iniPath = ThemeAssets.FindFile("Fonts.ini");
        if (iniPath == null)
        {
            Logger.Log("ThemeFonts: no Fonts.ini; using the default font.");
            return;
        }

        var ini = new IniFile(iniPath);
        var collection = new ThemeFontCollection();
        FontManager.Current.AddFontCollection(collection);

        int count = ini.GetIntValue("Fonts", "Count", 0);
        for (int i = 0; i < count; i++)
        {
            string section = "Font" + i;
            double size = ini.GetIntValue(section, "Size", (int)DEFAULT_SIZE);
            string type = ini.GetStringValue(section, "Type", string.Empty);
            string file = ThemeAssets.FindFile(ini.GetStringValue(section, "Path", string.Empty));

            string familyName = type.Equals("TrueType", StringComparison.OrdinalIgnoreCase) && file != null
                ? collection.AddFile(file)
                : null;

            fonts.Add(familyName == null
                ? (FontFamily.Default, size)
                : (new FontFamily(ThemeFontCollection.CollectionKey + "#" + familyName), size));

            Logger.Log($"ThemeFonts: font {i} = {familyName ?? "default"} {size}");
        }
    }

    /// <summary>The font of an XNA font index.</summary>
    public static (FontFamily Family, double Size) Get(int index) =>
        index >= 0 && index < fonts.Count ? fonts[index] : (FontFamily.Default, DEFAULT_SIZE);
}
