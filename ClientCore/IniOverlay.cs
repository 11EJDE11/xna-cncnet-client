using System;
using System.Collections.Generic;
using System.IO;

namespace ClientCore
{
    /// <summary>
    /// Unsaved INI edits made with the in-client designer tools (the -designer startup parameter).
    /// <see cref="CCIniFile"/> layers them over the file they target before resolving its BasedOn chain,
    /// so a window reloaded through the normal INI pipeline shows exactly what saving the edits would do.
    /// An edit may target a file that does not exist yet; <see cref="FileExists"/> then reports it as existing.
    /// </summary>
    public static class IniOverlay
    {
        private static readonly Dictionary<string, List<IniOverlayEdit>> editsByFile =
            new Dictionary<string, List<IniOverlayEdit>>(StringComparer.OrdinalIgnoreCase);

        public static bool IsEmpty => editsByFile.Count == 0;

        /// <summary>
        /// Replaces all overlay edits. A later edit of the same section and key replaces an earlier one.
        /// </summary>
        public static void Set(IEnumerable<IniOverlayEdit> edits)
        {
            editsByFile.Clear();

            foreach (IniOverlayEdit edit in edits)
            {
                string path = NormalizePath(edit.FilePath);

                if (!editsByFile.TryGetValue(path, out List<IniOverlayEdit> fileEdits))
                {
                    fileEdits = new List<IniOverlayEdit>();
                    editsByFile.Add(path, fileEdits);
                }

                int index = fileEdits.FindIndex(e => e.Section == edit.Section && e.Key == edit.Key);
                if (index > -1)
                    fileEdits[index] = edit;
                else
                    fileEdits.Add(edit);
            }
        }

        public static void Clear() => editsByFile.Clear();

        private static readonly string[] AnchorKeys = { "DistanceFromRightBorder", "DistanceFromBottomBorder", "FillWidth", "FillHeight" };
        private static readonly string[] AnchoredKeys = { "Location", "Size", "X", "Y", "Width", "Height" };

        /// <summary>
        /// Whether a key is calculated from the control's position and size when it is read, like FillWidth.
        /// </summary>
        public static bool IsAnchorKey(string key) => Array.IndexOf(AnchorKeys, key) > -1;

        /// <summary>
        /// Whether a new key goes before the section's first anchor key rather than to the end of the section,
        /// because anchors are calculated from it.
        /// </summary>
        public static bool GoesBeforeAnchors(string key) => Array.IndexOf(AnchoredKeys, key) > -1;

        public static IReadOnlyList<IniOverlayEdit> GetEdits(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !editsByFile.TryGetValue(NormalizePath(filePath), out List<IniOverlayEdit> fileEdits))
                return Array.Empty<IniOverlayEdit>();

            return fileEdits;
        }

        /// <summary>
        /// Whether an INI file exists on disk or only as overlay edits.
        /// </summary>
        public static bool FileExists(string filePath) =>
            File.Exists(filePath) || (!IsEmpty && editsByFile.ContainsKey(NormalizePath(filePath)));

        public static string NormalizePath(string filePath) => Path.GetFullPath(filePath);
    }

    /// <summary>
    /// Sets a key, removes a key (a null <see cref="Value"/>) or removes a whole section (a null <see cref="Key"/>).
    /// </summary>
    public sealed class IniOverlayEdit
    {
        public IniOverlayEdit(string filePath, string section, string key, string value)
        {
            FilePath = filePath;
            Section = section;
            Key = key;
            Value = value;
        }

        public static IniOverlayEdit RemoveKey(string filePath, string section, string key) =>
            new IniOverlayEdit(filePath, section, key, null);

        public static IniOverlayEdit RemoveSection(string filePath, string section) =>
            new IniOverlayEdit(filePath, section, null, null);

        public string FilePath { get; }
        public string Section { get; }
        public string Key { get; }
        public string Value { get; }

        public bool RemovesSection => Key == null;

        public bool RemovesKey => Key != null && Value == null;
    }

    /// <summary>
    /// Where a key of a consolidated <see cref="CCIniFile"/> was defined: the file and the section within it.
    /// The section differs from the key's own section when the key was inherited through $BaseSection.
    /// </summary>
    public sealed class IniKeySource
    {
        public IniKeySource(string filePath, string section)
        {
            FilePath = filePath;
            Section = section;
        }

        public string FilePath { get; }
        public string Section { get; }
    }
}
