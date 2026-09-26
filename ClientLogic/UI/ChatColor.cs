using System;
using System.Globalization;

namespace ClientLogic.UI;

/// <summary>A colour of chat text, independent of any UI framework.</summary>
public readonly record struct ChatColor(byte R, byte G, byte B, byte A = 255)
{
    public static ChatColor White => new(255, 255, 255);
    public static ChatColor Red => new(255, 0, 0);
    public static ChatColor Gray => new(128, 128, 128);

    /// <summary>Parses "R,G,B" or "R,G,B,A" (0-255 each), as colours are written in the client's INI files.</summary>
    public static ChatColor Parse(string colorString)
    {
        try
        {
            string[] parts = colorString.Split(',');
            byte alpha = parts.Length == 4 ? Convert.ToByte(parts[3], CultureInfo.InvariantCulture) : (byte)255;

            return new ChatColor(
                Convert.ToByte(parts[0], CultureInfo.InvariantCulture),
                Convert.ToByte(parts[1], CultureInfo.InvariantCulture),
                Convert.ToByte(parts[2], CultureInfo.InvariantCulture),
                alpha);
        }
        catch
        {
            throw new FormatException("ChatColor.Parse: Failed to convert " + colorString + " to a valid color!");
        }
    }
}
