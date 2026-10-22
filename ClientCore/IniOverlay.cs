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

    public sealed class IniOverlayEdit
    {
        public IniOverlayEdit(string filePath, string section, string key, string value)
        {
            FilePath = filePath;
            Section = section;
            Key = key;
            Value = value;
        }

        public string FilePath { get; }
        public string Section { get; }
        public string Key { get; }
        public string Value { get; }
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
