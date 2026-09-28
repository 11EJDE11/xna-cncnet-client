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

    private const int CHECK_BOX_TEXT_PADDING = 5;
    private const string CHECK_BOX_TEXTURE = "checkBoxChecked.png";

    private readonly Func<string, (int Width, int Height)?> textureSize;
    private readonly int renderWidth;
    private readonly int renderHeight;

    /// <summary>Measures a text in an XNA font index (labels and check boxes take their size from it).</summary>
    public Func<string, int, (int Width, int Height)> MeasureText { get; set; } = (_, _) => (0, 0);

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

        if (typeName.Contains("CheckBox"))
            return LayoutControlKind.CheckBox;

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

    /// <summary>
    /// Reads an INItializableWindow screen (e.g. the game lobbies): the window's section in INI order, where a "$CC"
    /// key adds a child ("name:Type") and reads its section at once, "$X", "$Y", "$Width" and "$Height" are
    /// expressions, and "$TextAnchor"/"$AnchorPoint" place labels; then the [$ExtraControls] "$CC" entries. Controls
    /// the screen creates in code are children of <paramref name="window"/> already and read their sections when an
    /// expression doesn't need them first (call <see cref="ReadInitializableControl"/> for them in the screen's
    /// order).
    /// </summary>
    public LayoutExpressionParser ReadInitializableWindow(IniFile ini, LayoutControl window, Dictionary<string, int> constants)
    {
        var parser = new LayoutExpressionParser(constants, window);
        ReadInitializableControl(ini, window, parser);

        IniSection extraControls = ini.GetSection("$ExtraControls");
        if (extraControls != null)
        {
            foreach (var kvp in extraControls.Keys.Where(k => k.Key.StartsWith("$CC")))
            {
                string[] parts = kvp.Value.Split(':');
                if (parts.Length != 2)
                    throw new ClientConfigurationException("Invalid $ExtraControl specified in " + window.Name + ": " + kvp.Value);

                if (!window.Children.Any(c => c.Name == parts[0]))
                {
                    var control = new LayoutControl(parts[0], parts[1], KindOf(parts[1])) { DrawOrder = -window.Children.Count };
                    window.AddChild(control);
                    ReadInitializableControl(ini, control, parser);
                }
            }
        }

        return parser;
    }

    /// <summary>One control's section, with the INItializableWindow rules (see <see cref="ReadInitializableWindow"/>).</summary>
    /// <param name="sectionName">The section to read, if not the control's name (e.g. lbChatMessages_Host).</param>
    public void ReadInitializableControl(IniFile ini, LayoutControl control, LayoutExpressionParser parser, string sectionName = null)
    {
        IniSection section = ini.GetSection(sectionName ?? control.Name);
        if (section == null)
            return;

        foreach (var kvp in section.Keys)
        {
            string key = kvp.Key;
            string value = kvp.Value;

            if (key.StartsWith("$CC"))
            {
                string[] parts = value.Split([':'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                    throw new ClientConfigurationException("Invalid child control definition " + value);

                LayoutControl child = control.Children.FirstOrDefault(c => c.Name == parts[0]);
                if (child == null)
                {
                    child = new LayoutControl(parts[0], parts[1], KindOf(parts[1]));
                    control.AddChild(child);
                }

                ReadInitializableControl(ini, child, parser);
                continue;
            }

            control.Attributes[key] = value;

            switch (key)
            {
                case "$X":
                    control.X = parser.GetExprValue(Localize(control, key, value), control);
                    break;
                case "$Y":
                    control.Y = parser.GetExprValue(Localize(control, key, value), control);
                    break;
                case "$Width":
                    control.Width = parser.GetExprValue(Localize(control, key, value), control);
                    break;
                case "$Height":
                    control.Height = parser.GetExprValue(Localize(control, key, value), control);
                    break;
                case "$TextAnchor" when control.Kind == LayoutControlKind.Label:
                    control.TextAnchor = value;
                    break;
                case "$AnchorPoint" when control.Kind == LayoutControlKind.Label:
                    string[] point = value.Split(',');
                    if (point.Length != 2)
                        throw new FormatException("Invalid format for AnchorPoint: " + value);
                    control.AnchorPoint = (parser.GetExprValue(point[0], control), parser.GetExprValue(point[1], control));
                    break;
                default:
                    if (!key.StartsWith("$"))
                        Apply(control, key, value);
                    break;
            }

            if (key is "$TextAnchor" or "$AnchorPoint")
                RefreshSize(control);
        }

        Initialize(control);
    }

    /// <summary>
    /// What the XNA controls do in Initialize once their INI is read: a client button without textures uses
    /// "{Width}pxbtn.png" (and takes its width from it), and labels and check boxes take their size from their text.
    /// </summary>
    public void Initialize(LayoutControl control)
    {
        if (control.TypeName is "XNAClientButton" or "GameLaunchButton" && control.IdleTexture == null)
        {
            string idle = control.Width + "pxbtn.png";
            if (textureSize(idle) is (int width, int height))
            {
                control.IdleTexture = idle;
                control.HoverTexture ??= control.Width + "pxbtn_c.png";
                if (control.Width == 0)
                    control.Width = width;
                if (control.Height == 0)
                    control.Height = height;
            }
        }

        // XNALabel sizes itself when its text is set, before subsequent INI Height/Size keys. Initialize must
        // not measure it again: getBottom(label) in later controls needs the explicitly overridden height.
        if (control.Kind != LayoutControlKind.Label || !control.TextSizeInitialized)
            RefreshSize(control);
    }

    /// <summary>
    /// A label's or check box's size from its text (XNALabel.RefreshClientRectangle, XNACheckBox
    /// SetTextPositionAndSize); an anchored label is placed by its anchor point.
    /// </summary>
    public void RefreshSize(LayoutControl control)
    {
        if (control.Kind == LayoutControlKind.Label)
        {
            if (string.IsNullOrEmpty(control.Text))
                return;

            (int width, int height) = MeasureText(control.Text, control.FontIndex);
            control.TextSizeInitialized = true;
            control.Width = width;
            control.Height = height;

            if (control.AnchorPoint is (float ax, float ay) && !string.IsNullOrEmpty(control.TextAnchor))
            {
                string anchor = control.TextAnchor.ToUpperInvariant();
                control.X = (int)ax;
                control.Y = (int)ay;

                if (anchor.Contains("HORIZONTAL_CENTER") || anchor == "CENTER")
                    control.X = (int)(ax - width / 2f);
                else if (anchor.Contains("RIGHT"))
                    control.X = (int)ax;
                else if (anchor.Contains("LEFT"))
                    control.X = (int)(ax - width);

                if (anchor.Contains("VERTICAL_CENTER") || anchor == "CENTER")
                    control.Y = (int)(ay - height / 2f);
                else if (anchor.Contains("TOP"))
                    control.Y = (int)(ay - height);
                else if (anchor.Contains("BOTTOM"))
                    control.Y = (int)ay;
            }
        }
        else if (control.Kind == LayoutControlKind.CheckBox && textureSize(CHECK_BOX_TEXTURE) is (int boxWidth, int boxHeight))
        {
            if (string.IsNullOrEmpty(control.Text))
            {
                control.Width = boxWidth;
                control.Height = boxHeight;
                return;
            }

            (int width, int height) = MeasureText(control.Text, control.FontIndex);
            control.Width = width + CHECK_BOX_TEXT_PADDING + boxWidth;
            control.Height = Math.Max(height, boxHeight);
        }
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
                RefreshSize(control);
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
            case LayoutControlKind.CheckBox when key == "FontIndex":
                control.FontIndex = Conversions.IntFromString(value, 0);
                RefreshSize(control);
                return;
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

    private void ApplyLabel(LayoutControl control, string key, string value)
    {
        switch (key)
        {
            case "RemapColor":
            case "TextColor":
                control.TextColor = ChatColor.Parse(value);
                return;
            case "FontIndex":
                control.FontIndex = Conversions.IntFromString(value, 0);
                RefreshSize(control);
                return;
            case "AnchorPoint":
                string[] point = value.Split(',');
                if (point.Length == 2)
                    control.AnchorPoint = (Conversions.FloatFromString(point[0], 0f), Conversions.FloatFromString(point[1], 0f));
                RefreshSize(control);
                return;
            case "TextAnchor":
                control.TextAnchor = value;
                RefreshSize(control);
                return;
        }
    }
}
