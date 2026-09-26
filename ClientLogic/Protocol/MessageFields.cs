using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ClientLogic.Protocol;

/// <summary>
/// The field format shared by all game lobby messages, on CnCNet and LAN: fields are separated by ';'.
/// Text fields are escaped so that they can contain any character: '%', ';' and the control
/// characters (which IRC and the LAN protocol use as delimiters) are written as %XX.
/// </summary>
public static class MessageFields
{
    public const char SEPARATOR = ';';

    public static string Join(IEnumerable<string> fields) => string.Join(SEPARATOR.ToString(), fields);

    public static string[] Split(string payload) => (payload ?? string.Empty).Split(SEPARATOR);

    public static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        StringBuilder sb = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '%' || c == SEPARATOR || c < 0x20)
            {
                sb ??= new StringBuilder(text, 0, i, text.Length + 8);
                sb.Append('%').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                sb?.Append(c);
            }
        }

        return sb?.ToString() ?? text;
    }

    /// <returns>False if the text contains an invalid escape sequence.</returns>
    public static bool TryUnescape(string text, out string result)
    {
        if (text.IndexOf('%') < 0)
        {
            result = text;
            return true;
        }

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '%')
            {
                sb.Append(c);
                continue;
            }

            if (i + 2 >= text.Length ||
                !int.TryParse(text.Substring(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
            {
                result = null;
                return false;
            }

            sb.Append((char)code);
            i += 2;
        }

        result = sb.ToString();
        return true;
    }

    public static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Format(bool value) => value ? "1" : "0";
}

/// <summary>
/// Reads the fields of a message in order. Every read fails (returns false) instead of throwing,
/// so decoders can reject a malformed message before changing any state.
/// </summary>
public sealed class MessageFieldReader
{
    private readonly string[] fields;
    private int position;

    public MessageFieldReader(string payload)
    {
        fields = MessageFields.Split(payload);
    }

    public int Remaining => fields.Length - position;

    public bool IsAtEnd => position >= fields.Length;

    public bool TryReadInt(out int value)
    {
        value = 0;
        return position < fields.Length &&
            int.TryParse(fields[position++], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    public bool TryReadInt(int min, int max, out int value) =>
        TryReadInt(out value) && value >= min && value <= max;

    public bool TryReadUInt(out uint value)
    {
        value = 0;
        return position < fields.Length &&
            uint.TryParse(fields[position++], NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    public bool TryReadBool(out bool value)
    {
        value = false;
        if (position >= fields.Length)
            return false;

        string field = fields[position++];
        if (field != "0" && field != "1")
            return false;

        value = field == "1";
        return true;
    }

    public bool TryReadText(out string value)
    {
        value = null;
        return position < fields.Length && MessageFields.TryUnescape(fields[position++], out value);
    }
}

/// <summary>Builds a message payload field by field.</summary>
public sealed class MessageFieldWriter
{
    private readonly List<string> fields = [];

    public MessageFieldWriter Add(int value)
    {
        fields.Add(MessageFields.Format(value));
        return this;
    }

    public MessageFieldWriter Add(uint value)
    {
        fields.Add(value.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    public MessageFieldWriter Add(bool value)
    {
        fields.Add(MessageFields.Format(value));
        return this;
    }

    public MessageFieldWriter AddText(string value)
    {
        fields.Add(MessageFields.Escape(value));
        return this;
    }

    public override string ToString() => MessageFields.Join(fields);
}
