using System.Collections.Generic;
using System.IO;
using ClientCore;
using ClientGUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// A window that reads its layout from an INI file, either an <see cref="XNAWindow"/> or an
    /// <see cref="INItializableWindow"/>, seen the same way by the designer tools.
    /// </summary>
    internal sealed class IniWindow
    {
        private const string GENERIC_WINDOW_INI = "GenericWindow.ini";
        private const string GENERIC_WINDOW_SECTION = "GenericWindow";

        private IniWindow(XNAControl control)
        {
            Control = control;
        }

        public XNAControl Control { get; }

        /// <summary>
        /// The consolidated INI the window was last configured from, or null if it has none.
        /// </summary>
        public CCIniFile Ini => Control switch
        {
            XNAWindow window => window.ThemeIni as CCIniFile,
            INItializableWindow window => window.ConfigIni,
            _ => null
        };

        /// <summary>
        /// The path of the INI file the window loaded, or null if it has none.
        /// </summary>
        public string IniPath => Control switch
        {
            XNAWindow window => window.ThemeIniPath,
            INItializableWindow window => window.ConfigIniPath,
            _ => null
        };

        /// <summary>
        /// The file name of the window's own INI file, whether or not it exists yet.
        /// </summary>
        public string OwnIniFileName => Control is INItializableWindow window ? window.IniName + ".ini" : Control.Name + ".ini";

        /// <summary>
        /// Whether the window is configured from the shared GenericWindow.ini rather than its own file.
        /// </summary>
        public bool UsesGenericWindowIni => IniPath != null && IsGenericWindowIni(IniPath);

        public static bool IsGenericWindowIni(string path) =>
            string.Equals(Path.GetFileName(path), GENERIC_WINDOW_INI, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the window's own attributes come from the shared [GenericWindow] section.
        /// </summary>
        public bool UsesGenericWindowSection => SectionFor(Control) == GENERIC_WINDOW_SECTION;

        public static string GenericWindowSection => GENERIC_WINDOW_SECTION;

        public static IniWindow From(XNAControl control) =>
            control is XNAWindow || control is INItializableWindow ? new IniWindow(control) : null;

        /// <summary>
        /// Returns the nearest INI-configured window containing the control, including the control itself.
        /// </summary>
        public static IniWindow OwnerOf(XNAControl control)
        {
            for (XNAControl current = control; current != null; current = current.Parent)
            {
                IniWindow window = From(current);
                if (window != null)
                    return window;
            }

            return null;
        }

        /// <summary>
        /// Returns the INI section the window reads the control's attributes from,
        /// or null if the control has no name to look a section up by.
        /// </summary>
        public string SectionFor(XNAControl control)
        {
            if (control == Control && Control is XNAWindow window)
                return window.WindowSectionName;

            return string.IsNullOrEmpty(control.Name) ? null : control.Name;
        }

        /// <summary>
        /// Re-applies the window's INI to the given controls only.
        /// </summary>
        public void Reload(IEnumerable<XNAControl> controls)
        {
            switch (Control)
            {
                case XNAWindow window:
                    window.ReloadIni(controls);
                    break;
                case INItializableWindow window:
                    window.ReloadIni(controls);
                    break;
            }
        }

        public void Reload()
        {
            switch (Control)
            {
                case XNAWindow window:
                    window.ReloadIni();
                    break;
                case INItializableWindow window:
                    window.ReloadIni();
                    break;
            }
        }
    }
}
