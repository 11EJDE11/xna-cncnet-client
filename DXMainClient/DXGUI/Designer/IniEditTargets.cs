using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClientCore;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// Decides which INI file and section a designer edit is saved to.
    /// <list type="bullet">
    /// <item>A key defined in the current theme folder is changed where it is defined,
    /// unless that is GenericWindow.ini, which every window without its own INI shares.</item>
    /// <item>Anything else goes to the window's own INI in the theme folder, so stock files and the
    /// files other themes share are never changed. If that file does not exist yet it is created,
    /// based on whatever the window loaded until now (another INI, GenericWindow.ini or nothing).</item>
    /// </list>
    /// </summary>
    internal static class IniEditTargets
    {
        public static List<IniOverlayEdit> Resolve(IniWindow window, string section, string key, string value)
        {
            var edits = new List<IniOverlayEdit>();
            string themeDirectory = NormalizeDirectory(ProgramConstants.GetResourcePath());

            IniKeySource source = window.Ini?.GetKeySource(section, key);
            if (source != null && source.Section == section && IsInDirectory(source.FilePath, themeDirectory) &&
                !IniWindow.IsGenericWindowIni(source.FilePath))
            {
                edits.Add(new IniOverlayEdit(source.FilePath, section, key, value));
                return edits;
            }

            string ownIniPath = Path.Combine(themeDirectory, window.OwnIniFileName);
            string targetSection = section;

            // A window reading the shared [GenericWindow] section gets a section of its own that inherits it
            if (section == IniWindow.GenericWindowSection && window.UsesGenericWindowSection)
            {
                targetSection = window.Control.Name;

                if (window.Ini?.GetKeySource(targetSection, "$BaseSection") == null &&
                    !OverlayHasKey(ownIniPath, targetSection, "$BaseSection"))
                {
                    edits.Add(new IniOverlayEdit(ownIniPath, targetSection, "$BaseSection", IniWindow.GenericWindowSection));
                }
            }

            if (!IniOverlay.FileExists(ownIniPath) && window.IniPath != null &&
                !string.Equals(Path.GetFullPath(window.IniPath), Path.GetFullPath(ownIniPath), StringComparison.OrdinalIgnoreCase))
            {
                edits.Insert(0, new IniOverlayEdit(ownIniPath, "INISystem", "BasedOn",
                    GetRelativePath(themeDirectory, window.IniPath)));
            }

            edits.Add(new IniOverlayEdit(ownIniPath, targetSection, key, value));
            return edits;
        }

        /// <summary>
        /// Returns the file a key of the section can be removed from: the file that defines it, when that is in the
        /// current theme folder and isn't GenericWindow.ini. Null when the key comes from anywhere else, such as a
        /// stock file or another section through $BaseSection.
        /// </summary>
        public static string ResolveRemoval(IniWindow window, string section, string key)
        {
            IniKeySource source = window.Ini?.GetKeySource(section, key);
            bool removable = source != null && source.Section == section &&
                IsInDirectory(source.FilePath, NormalizeDirectory(ProgramConstants.GetResourcePath())) &&
                !IniWindow.IsGenericWindowIni(source.FilePath);

            return removable ? source.FilePath : null;
        }

        private static bool OverlayHasKey(string path, string section, string key) =>
            IniOverlay.GetEdits(path).Any(e => e.Section == section && e.Key == key);

        private static string NormalizeDirectory(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        private static bool IsInDirectory(string filePath, string normalizedDirectory) =>
            string.Equals(Path.GetDirectoryName(Path.GetFullPath(filePath)) + Path.DirectorySeparatorChar,
                normalizedDirectory, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Returns the path of a file relative to a directory, in the form BasedOn expects.
        /// </summary>
        public static string GetRelativePath(string fromDirectory, string toFile)
        {
            string[] from = Path.GetFullPath(fromDirectory).TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar);
            string[] to = Path.GetFullPath(toFile).Split(Path.DirectorySeparatorChar);

            int common = 0;
            while (common < from.Length && common < to.Length - 1 &&
                string.Equals(from[common], to[common], StringComparison.OrdinalIgnoreCase))
            {
                common++;
            }

            if (common == 0)
                return Path.GetFullPath(toFile);

            var parts = new List<string>();
            parts.AddRange(Enumerable.Repeat("..", from.Length - common));
            parts.AddRange(to.Skip(common));
            return string.Join("\\", parts);
        }
    }
}
