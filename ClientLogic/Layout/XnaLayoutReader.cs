using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;
using ClientCore.I18N;

using ClientLogic.UI;

using Rampastring.Tools;

namespace ClientLogic.Layout;

/// <summary>
/// Reads an XNA client layout INI into <see cref="LayoutControl"/>s without XNA: the plan's converter from the XNA
/// layout INIs (positions, sizes, textures, text). It follows the XNA client's rules for XNAWindow screens: the
/// window's section (or [GenericWindow] if it has none), then [ExtraControls] ("name:Type" entries add controls),
/// then each child's attributes, children before their parent's own keys, in INI order. Text, positions and sizes
/// go through the translation, as the XNA client's TranslationINIParser does.
/// </summary>
public sealed class XnaLayoutReader
{
    public const string GENERIC_WINDOW_INI = "GenericWindow.ini";
    public const string GENERIC_WINDOW_SECTION = "GenericWindow";
    public const string EXTRA_CONTROLS = "ExtraControls";

    private readonly Func<string, (int Width, int Height)?> textureSize;
    private readonly int renderWidth;
    private readonly int renderHeight;

    /// <param name="textureSize">The size of a texture (an IdleTexture sets a button's size), or null if not found.</param>
    /// <param name="renderWidth">The render resolution, for FillWidth on controls without a parent.</param>
    /// <param name="renderHeight">The render resolution, for FillHeight on controls without a parent.</param>
    public XnaLayoutReader(Func<string, (int Width, int Height)?> textureSize, int renderWidth, int renderHeight)
    {
        this.textureSize = textureSize;
        this.renderWidth = renderWidth;
        this.renderHeight = renderHeight;
    }

    /// <summary>
    /// The layout INI of a window, as XNAWindow finds it: the theme's {name}.ini, the base {name}.ini, then
    /// GenericWindow.ini (theme, then base). Null if none exists.
    /// </summary>
    public static string FindWindowIni(string windowName)
    {
        foreach (string file in new[] { windowName + ".ini", GENERIC_WINDOW_INI })
        {
            FileInfo themeFile = SafePath.GetFile(ProgramConstants.GetResourcePath(), file);
            if (themeFile.Exists)
                return themeFile.FullName;

            FileInfo baseFile = SafePath.GetFile(ProgramConstants.GetBaseResourcePath(), file);
            if (baseFile.Exists)
                return baseFile.FullName;
        }

        return null;
    }

    /// <summary>The kind a control type draws as.</summary>
    public static LayoutControlKind KindOf(string typeName)
    {
        if (typeName.Contains("Button"))
            return LayoutControlKind.Button;

        if (typeName.Contains("Label"))
            return LayoutControlKind.Label;

        if (typeName.Contains("Panel") || typeName.Contains("Window") || typeName.Contains("PictureBox"))
            return LayoutControlKind.Panel;

        return LayoutControlKind.Other;
    }

    /// <summary>
    /// Applies a window's layout INI to the window and the controls the screen creates in code (already added as
    /// its children), and adds the INI's extra controls.
    /// </summary>
    public void ReadWindow(IniFile ini, LayoutControl window)
    {
        List<string> keys = ini.GetSectionKeys(window.Name);
        string section = keys != null ? window.Name : GENERIC_WINDOW_SECTION;
        ApplySection(ini, window, section);

        IniSection extraControls = ini.GetSection(EXTRA_CONTROLS);
        if (extraControls != null)
        {
            foreach (var kvp in extraControls.Keys)
            {
                string[] parts = kvp.Value.Split(':');
                if (parts.Length != 2)
                    throw new ClientConfigurationException("Invalid ExtraControl specified in " + window.Name + ": " + kvp.Value);

                if (!window.Children.Any(c => c.Name == parts[0]))
                {
                    window.AddChild(new LayoutControl(parts[0], parts[1], KindOf(parts[1]))
                    {
                        DrawOrder = -window.Children.Count,
                    });
                }
            }
        }

        foreach (LayoutControl child in window.Children)
            ReadControl(ini, child);
    }

    /// <summary>A control's attributes, as XNAControl.GetAttributes reads them: children first, then its own section.</summary>
    public void ReadControl(IniFile ini, LayoutControl control)
    {
        foreach (LayoutControl child in control.Children)
            ReadControl(ini, child);

        ApplySection(ini, control, control.Name);
    }

    private void ApplySection(IniFile ini, LayoutControl control, string section)
    {
        List<string> keys = ini.GetSectionKeys(section);
        if (keys == null)
            return;

        foreach (string key in keys)
            Apply(control, key, ini.GetStringValue(section, key, string.Empty));
    }

    private static string Localize(LayoutControl control, string attribute, string defaultValue)
    {
        if (Translation.Instance == null)
            return defaultValue;

        string key = $"INI:Controls:{control.Parent?.Name ?? "Global"}:{control.Name}:{attribute}";
        string globalKey = $"INI:Controls:Global:{control.Name}:{attribute}";
        return Translation.Instance.LookUp(key, fallbackKey: globalKey, defaultValue, TranslationNotificationLevel.Verbose);
    }

    private static int ParseInt(string value) => int.Parse(value.Trim(), CultureInfo.InvariantCulture);

    /// <summary>Applies one INI key, with the XNA controls' meaning.</summary>
    public void Apply(LayoutControl control, string key, string value)
    {
        control.Attributes[key] = value;

        switch (key)
        {
            // Common (XNAControl, through the translation parser)
            case "DrawOrder":
                control.DrawOrder = ParseInt(value);
                return;
            case "Text":
                control.Text = Localize(control, key, value.FromIniString());
                return;
            case "ToolTip":
                control.ToolTip = Localize(control, key, value.FromIniString());
                return;
            case "URL":
                control.Url = Localize(control, key, value.FromIniString());
                return;
            case "Size":
                string[] size = Localize(control, key, value).Split(',');
                control.Width = ParseInt(size[0]);
                control.Height = ParseInt(size[1]);
                return;
            case "Width":
                control.Width = ParseInt(Localize(control, key, value));
                return;
            case "Height":
                control.Height = ParseInt(Localize(control, key, value));
                return;
            case "Location":
                string[] location = Localize(control, key, value).Split(',');
                control.X = ParseInt(location[0]);
                control.Y = ParseInt(location[1]);
                return;
            case "X":
                control.X = ParseInt(Localize(control, key, value));
                return;
            case "Y":
                control.Y = ParseInt(Localize(control, key, value));
                return;
            case "DistanceFromRightBorder":
                if (control.Parent != null)
                    control.X = control.Parent.Width - control.Width - Conversions.IntFromString(Localize(control, key, value), 0);
                return;
            case "DistanceFromBottomBorder":
                if (control.Parent != null)
                    control.Y = control.Parent.Height - control.Height - Conversions.IntFromString(Localize(control, key, value), 0);
                return;
            case "FillWidth":
                control.Width = (control.Parent?.Width ?? renderWidth) - control.X - Conversions.IntFromString(value, 0);
                return;
            case "FillHeight":
                control.Height = (control.Parent?.Height ?? renderHeight) - control.Y - Conversions.IntFromString(value, 0);
                return;
            case "Visible":
                control.Visible = Conversions.BooleanFromString(value, true);
                control.Enabled = control.Visible;
                return;
            case "Enabled":
                control.Enabled = Conversions.BooleanFromString(value, true);
                return;
        }

        switch (control.Kind)
        {
            case LayoutControlKind.Panel:
                ApplyPanel(control, key, value);
                return;
            case LayoutControlKind.Button:
                ApplyButton(control, key, value);
                return;
            case LayoutControlKind.Label:
                ApplyLabel(control, key, value);
                return;
        }
    }

    private static void ApplyPanel(LayoutControl control, string key, string value)
    {
        switch (key)
        {
            case "BorderColor":
                control.BorderColor = ChatColor.Parse(value);
                return;
            case "DrawMode":
                control.DrawMode = value is "Tiled" or "Centered" ? value : "Stretched";
                return;
            case "BackgroundTexture":
                control.BackgroundTexture = value;
                return;
            case "SolidColorBackgroundTexture":
                control.SolidBackground = ChatColor.Parse(value);
                control.DrawMode = "Stretched";
                return;
            case "DrawBorders":
                control.DrawBorders = Conversions.BooleanFromString(value, true);
                return;
        }
    }

    private void ApplyButton(LayoutControl control, string key, string value)
    {
        switch (key)
        {
            case "TextColorIdle":
                control.TextColor = ChatColor.Parse(value);
                return;
            case "TextColorHover":
                control.TextColorHover = ChatColor.Parse(value);
                return;
            case "HoverSoundEffect":
                control.HoverSoundEffect = value;
                return;
            case "FontIndex":
                control.FontIndex = Conversions.IntFromString(value, 0);
                return;
            case "IdleTexture":
                control.IdleTexture = value;
                if (textureSize(value) is (int width, int height))
                {
                    control.Width = width;
                    control.Height = height;
                }

                return;
            case "HoverTexture":
                control.HoverTexture = value;
                return;
        }
    }

    private static void ApplyLabel(LayoutControl control, string key, string value)
    {
        switch (key)
        {
            case "RemapColor":
            case "TextColor":
                control.TextColor = ChatColor.Parse(value);
                return;
            case "FontIndex":
                control.FontIndex = Conversions.IntFromString(value, 0);
                return;
            case "AnchorPoint":
                string[] point = value.Split(',');
                if (point.Length == 2)
                    control.AnchorPoint = (Conversions.FloatFromString(point[0], 0f), Conversions.FloatFromString(point[1], 0f));
                return;
            case "TextAnchor":
                control.TextAnchor = value;
                return;
        }
    }
}
