using System.Collections.Generic;

using ClientCore.Extensions;

using ClientLogic.UI;

namespace ClientLogic.Lan;

/// <summary>A LAN chat colour: its display name and colour. The index in <see cref="LanChatColors.All"/> is sent.</summary>
public sealed record LanChatColor(string Name, ChatColor Color);

/// <summary>The LAN chat colours, in the order the XNA LAN lobby lists (and sends) them.</summary>
public static class LanChatColors
{
    public static IReadOnlyList<LanChatColor> All { get; } =
    [
        new("Gray".L10N("Client:Main:ColorGray"), new ChatColor(128, 128, 128)),
        new("Metallic".L10N("Client:Main:ColorLightGrayMetallic"), new ChatColor(211, 211, 211)),
        new("Green".L10N("Client:Main:ColorGreen"), new ChatColor(34, 139, 34)),
        new("Lime Green".L10N("Client:Main:ColorLimeGreen"), new ChatColor(50, 205, 50)),
        new("Green Yellow".L10N("Client:Main:ColorGreenYellow"), new ChatColor(173, 255, 47)),
        new("Goldenrod".L10N("Client:Main:ColorGoldenrod"), new ChatColor(218, 165, 32)),
        new("Yellow".L10N("Client:Main:ColorYellow"), new ChatColor(255, 255, 0)),
        new("Orange".L10N("Client:Main:ColorOrange"), new ChatColor(255, 165, 0)),
        new("Red".L10N("Client:Main:ColorRed"), new ChatColor(255, 0, 0)),
        new("Pink".L10N("Client:Main:ColorPink"), new ChatColor(255, 20, 147)),
        new("Purple".L10N("Client:Main:ColorPurple"), new ChatColor(147, 112, 219)),
        new("Sky Blue".L10N("Client:Main:ColorSkyBlue"), new ChatColor(135, 206, 250)),
        new("Blue".L10N("Client:Main:ColorBlue"), new ChatColor(65, 105, 225)),
        new("Brown".L10N("Client:Main:ColorBrown"), new ChatColor(139, 69, 19)),
        new("Teal".L10N("Client:Main:ColorTeal"), new ChatColor(0, 128, 128)),
    ];

    public static bool IsValidIndex(int index) => index >= 0 && index < All.Count;
}
