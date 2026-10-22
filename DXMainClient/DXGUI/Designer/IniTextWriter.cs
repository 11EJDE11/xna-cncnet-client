using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ClientCore;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// Writes designer edits into an INI file as text, so comments, blank lines, key order and
    /// formatting the author chose are kept. Only the lines of changed keys are touched; new keys go
    /// to the end of their section, except position and size keys, which go before the section's first
    /// anchor (DistanceFrom*, Fill*) because those are calculated from them when the INI is read.
    /// </summary>
    internal static class IniTextWriter
    {
        private static readonly string[] AnchorKeys = { "DistanceFromRightBorder", "DistanceFromBottomBorder", "FillWidth", "FillHeight" };
        private static readonly string[] AnchoredKeys = { "Location", "Size", "X", "Y", "Width", "Height" };

        public static void Apply(string path, IEnumerable<IniOverlayEdit> edits)
        {
            bool exists = File.Exists(path);
            bool hasBom = false;
            string newLine = "\r\n";
            var lines = new List<string>();

            if (exists)
            {
                byte[] bytes = File.ReadAllBytes(path);
                hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                string text = new UTF8Encoding(false).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));

                if (!text.Contains("\r\n") && text.Contains("\n"))
                    newLine = "\n";

                lines.AddRange(text.Replace("\r\n", "\n").Split('\n'));

                // A trailing newline leaves an empty last element; it is written back below
                if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                    lines.RemoveAt(lines.Count - 1);
            }
            else
            {
                lines.Add("; Created with the client's designer tools (-designer).");
            }

            foreach (IniOverlayEdit edit in edits)
                SetKey(lines, edit.Section, edit.Key, edit.Value);

            string output = string.Join(newLine, lines) + newLine;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, (hasBom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>())
                .Concat(new UTF8Encoding(false).GetBytes(output)).ToArray());
        }

        private static void SetKey(List<string> lines, string section, string key, string value)
        {
            List<int> headers = FindSectionHeaders(lines, section);

            if (headers.Count == 0)
            {
                while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                    lines.RemoveAt(lines.Count - 1);

                if (lines.Count > 0)
                    lines.Add(string.Empty);

                lines.Add("[" + section + "]");
                lines.Add(key + "=" + value);
                return;
            }

            // A section may be split over several headers; the INI parser merges them
            foreach (int header in headers)
            {
                int end = FindSectionEnd(lines, header);
                for (int i = header + 1; i < end; i++)
                {
                    if (GetKeyName(lines[i]) == key)
                    {
                        lines[i] = ReplaceValue(lines[i], value);
                        return;
                    }
                }
            }

            int lastHeader = headers[headers.Count - 1];
            int sectionEnd = FindSectionEnd(lines, lastHeader);
            int insertAt = lastHeader + 1;

            for (int i = lastHeader + 1; i < sectionEnd; i++)
            {
                if (GetKeyName(lines[i]) != null)
                    insertAt = i + 1;
            }

            if (AnchoredKeys.Contains(key))
            {
                for (int i = lastHeader + 1; i < sectionEnd; i++)
                {
                    if (AnchorKeys.Contains(GetKeyName(lines[i])))
                    {
                        insertAt = i;
                        break;
                    }
                }
            }

            lines.Insert(insertAt, key + "=" + value);
        }

        private static List<int> FindSectionHeaders(List<string> lines, string section)
        {
            var headers = new List<int>();

            for (int i = 0; i < lines.Count; i++)
            {
                string line = StripComment(lines[i]);
                if (line.Length == 0 || line[0] != '[')
                    continue;

                int close = line.IndexOf(']');
                if (close > 0 && line.Substring(1, close - 1) == section)
                    headers.Add(i);
            }

            return headers;
        }

        private static int FindSectionEnd(List<string> lines, int header)
        {
            for (int i = header + 1; i < lines.Count; i++)
            {
                string line = StripComment(lines[i]);
                if (line.Length > 0 && line[0] == '[')
                    return i;
            }

            return lines.Count;
        }

        /// <summary>
        /// Returns the key name of a key line the way the INI parser reads it, or null for other lines.
        /// </summary>
        private static string GetKeyName(string line)
        {
            line = StripComment(line);
            if (string.IsNullOrWhiteSpace(line) || line[0] == '[')
                return null;

            int equals = line.IndexOf('=');
            return equals == -1 ? null : line.Substring(0, equals).Trim();
        }

        private static string StripComment(string line)
        {
            int comment = line.IndexOf(';');
            return comment == -1 ? line : line.Substring(0, comment);
        }

        /// <summary>
        /// Replaces the value of a key line, keeping the spacing around it and any trailing comment.
        /// </summary>
        private static string ReplaceValue(string line, string value)
        {
            int equals = line.IndexOf('=');
            int comment = line.IndexOf(';', equals + 1);
            string oldValue = comment == -1 ? line.Substring(equals + 1) : line.Substring(equals + 1, comment - equals - 1);

            string leading = oldValue.Substring(0, oldValue.Length - oldValue.TrimStart().Length);
            string trailing = oldValue.Substring(oldValue.TrimEnd().Length);

            if (comment == -1)
                return line.Substring(0, equals + 1) + leading + value;

            return line.Substring(0, equals + 1) + leading + value + (trailing.Length > 0 ? trailing : " ") + line.Substring(comment);
        }
    }
}
