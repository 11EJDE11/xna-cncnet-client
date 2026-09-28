using System.Collections.Generic;
using System.Linq;

using ClientLogic.UI;

namespace ClientLogic.Layout;

/// <summary>What kind of control a layout entry is, as far as drawing it goes.</summary>
public enum LayoutControlKind
{
    /// <summary>A window or panel: background, borders, children.</summary>
    Panel,

    /// <summary>A button: idle and hover textures and a caption.</summary>
    Button,

    /// <summary>A text label.</summary>
    Label,

    /// <summary>A check box: its texture and text set its size.</summary>
    CheckBox,

    /// <summary>Anything else; the front end decides.</summary>
    Other,
}

/// <summary>
/// One control of an XNA layout INI after the XNA client's attribute rules are applied: position, size,
/// textures, text and fonts. Front ends draw it; the keys they need beyond these are in <see cref="Attributes"/>.
/// </summary>
public sealed class LayoutControl(string name, string typeName, LayoutControlKind kind)
{
    public string Name { get; } = name;

    /// <summary>The XNA control type, e.g. XNAClientButton (from code or an extra-controls entry).</summary>
    public string TypeName { get; } = typeName;

    public LayoutControlKind Kind { get; } = kind;

    public LayoutControl Parent { get; private set; }

    public List<LayoutControl> Children { get; } = [];

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    internal bool TextSizeInitialized { get; set; }

    public string Text { get; set; } = string.Empty;

    public string ToolTip { get; set; }

    /// <summary>Lower draws first (behind); extra controls get negative values, as in XNA.</summary>
    public int DrawOrder { get; set; }

    public bool Visible { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public int FontIndex { get; set; }

    public ChatColor? TextColor { get; set; }

    public ChatColor? TextColorHover { get; set; }

    public string BackgroundTexture { get; set; }

    /// <summary>Stretched (default), Tiled or Centered.</summary>
    public string DrawMode { get; set; } = "Stretched";

    /// <summary>The panel's one-pixel border (XNAPanel draws it unless the control or its INI turns it off).</summary>
    public bool DrawBorders { get; set; } = DrawsBordersByDefault(typeName);

    /// <summary>
    /// Whether an XNA control type draws its border by default: XNAPanel and the windows and panels derived from it do,
    /// except the ones that turn it off in their constructor (XNAExtraPanel, DarkeningPanel, TopBar, ...).
    /// </summary>
    public static bool DrawsBordersByDefault(string typeName) => typeName switch
    {
        "XNAExtraPanel" or "DarkeningPanel" or "TopBar" or "GameInProgressWindow" or "GameInformationIconPanel"
            or "GameInformationIconOnlyPanel" => false,
        "XNAPanel" or "XNAWindow" or "INItializableWindow" or "XNAOptionsPanel" or "MapPreviewBox" or "PlayerExtraOptionsPanel"
            or "TeamStartMappingPanel" or "TeamStartMappingsPanel" or "CoopBriefingBox" or "GameFiltersPanel" or "GameInformationPanel"
            or "MapSharingConfirmationPanel" or "TunnelNegotiationStatusPanel" or "XNAScrollPanel" or "ChoiceNotificationBox"
            or "PrivateMessageNotificationBox" or "MainMenu" => true,
        _ => typeName != null && (typeName.EndsWith("Window") || typeName.EndsWith("Lobby")),
    };

    public ChatColor? BorderColor { get; set; }

    /// <summary>A solid background colour instead of a texture.</summary>
    public ChatColor? SolidBackground { get; set; }

    public string IdleTexture { get; set; }

    public string HoverTexture { get; set; }

    public string HoverSoundEffect { get; set; }

    /// <summary>A label's anchor point; with <see cref="TextAnchor"/> it places the text instead of X and Y.</summary>
    public (float X, float Y)? AnchorPoint { get; set; }

    /// <summary>A label's text anchor, e.g. HORIZONTAL_CENTER, LEFT, RIGHT, TOP, BOTTOM, VERTICAL_CENTER.</summary>
    public string TextAnchor { get; set; }

    public string Url { get; set; }

    /// <summary>Every INI key and value applied to the control, in order (last value wins).</summary>
    public Dictionary<string, string> Attributes { get; } = [];

    public void AddChild(LayoutControl child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public LayoutControl Find(string name)
    {
        if (Name == name)
            return this;

        return Children.Select(c => c.Find(name)).FirstOrDefault(c => c != null);
    }

    public override string ToString() => $"{Name} ({TypeName}) {X},{Y} {Width}x{Height}";
}
