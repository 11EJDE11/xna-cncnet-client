using ClientCore;
using Rampastring.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace ClientGUI
{
    /// <summary>
    /// A sub-window to be displayed inside the game window.
    /// Supports easy reading of child controls' attributes from an INI file.
    /// </summary>
    public class XNAWindow : XNAWindowBase
    {
        private const string GENERIC_WINDOW_INI = "GenericWindow.ini";
        private const string GENERIC_WINDOW_SECTION = "GenericWindow";
        private const string EXTRA_CONTROLS = "ExtraControls";

        public XNAWindow(WindowManager windowManager) : base(windowManager)
        {
        }

        /// <summary>
        /// The INI file that was used for theming this window.
        /// </summary>
        public IniFile ThemeIni { get; protected set; }

        /// <summary>
        /// The path of the INI file that was used for theming this window.
        /// </summary>
        public string ThemeIniPath { get; private set; }

        /// <summary>
        /// The section this window's own attributes are read from: its name, or the
        /// generic window section if the INI has no section for it.
        /// </summary>
        public string WindowSectionName =>
            ThemeIni != null && !ThemeIni.SectionExists(Name) ? GENERIC_WINDOW_SECTION : Name;

        public override float Alpha
        {
            get
            {
                return 1.0f;
            }
        }

        protected virtual void SetAttributesFromIni()
        {
            ThemeIniPath = FindIniPath();
            var iniFile = new CCIniFile(ThemeIniPath);

            if (PreIniRectangles != null)
                RecordPreIniRectangles(this, iniFile);

            GetINIAttributes(iniFile);
        }

        private void RecordPreIniRectangles(XNAControl control, IniFile iniFile)
        {
            foreach (XNAControl child in control.Children)
            {
                // Nested windows read their own INI files
                if (child is XNAWindow)
                    continue;

                if (!string.IsNullOrEmpty(child.Name) && iniFile.SectionExists(child.Name) && !PreIniRectangles.ContainsKey(child))
                    PreIniRectangles.Add(child, child.ClientRectangle);

                RecordPreIniRectangles(child, iniFile);
            }
        }

        private string FindIniPath()
        {
            string themeIniPath = SafePath.CombineFilePath(ProgramConstants.GetResourcePath(), FormattableString.Invariant($"{Name}.ini"));
            if (IniOverlay.FileExists(themeIniPath))
                return themeIniPath;

            string baseIniPath = SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), FormattableString.Invariant($"{Name}.ini"));
            if (File.Exists(baseIniPath))
                return baseIniPath;

            string themeGenericIniPath = SafePath.CombineFilePath(ProgramConstants.GetResourcePath(), GENERIC_WINDOW_INI);
            if (File.Exists(themeGenericIniPath))
                return themeGenericIniPath;

            return SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), GENERIC_WINDOW_INI);
        }

        /// <summary>
        /// Re-reads this window's INI file and re-applies it to the existing controls,
        /// creating any extra controls that were added to it. Keys that would duplicate
        /// list items or reset player choices are skipped; see <see cref="IniReloadPolicy"/>.
        /// </summary>
        public void ReloadIni()
        {
            string path = FindIniPath();
            CCIniFile applyIni = LoadIniForReapplying(path);

            // Expressions and anchors depend on a control's current size, so start from the
            // rectangles the controls had when the INI was first applied
            if (PreIniRectangles != null)
            {
                foreach (var entry in PreIniRectangles)
                {
                    if (entry.Key.Parent != null)
                        entry.Key.ClientRectangle = entry.Value;
                }
            }

            GetINIAttributes(applyIni);

            ThemeIniPath = path;
            ThemeIni = new CCIniFile(path);
            OnIniReloaded();
        }

        /// <summary>
        /// Re-reads this window's INI file and re-applies only the sections of the given controls
        /// (and their children). Quicker than <see cref="ReloadIni()"/> for previewing a change while
        /// other changes depend on it.
        /// </summary>
        public void ReloadIni(IEnumerable<XNAControl> controls)
        {
            string path = FindIniPath();
            CCIniFile applyIni = LoadIniForReapplying(path);

            foreach (XNAControl control in controls)
            {
                if (control == this)
                    continue;

                if (PreIniRectangles != null && PreIniRectangles.TryGetValue(control, out Rectangle preIniRectangle))
                    control.ClientRectangle = preIniRectangle;

                control.GetAttributes(applyIni);
            }

            ThemeIniPath = path;
            ThemeIni = new CCIniFile(path);
        }

        private static CCIniFile LoadIniForReapplying(string path)
        {
            var iniFile = new CCIniFile(path);

            foreach (string sectionName in iniFile.GetSections())
            {
                foreach (string key in iniFile.GetSectionKeys(sectionName))
                {
                    if (IniReloadPolicy.IsUnsafeToReapply(key))
                        iniFile.RemoveKey(sectionName, key);
                }
            }

            return iniFile;
        }

        /// <summary>
        /// Called after <see cref="ReloadIni()"/> has re-applied the INI file, for windows that
        /// position controls in code based on INI values.
        /// </summary>
        protected virtual void OnIniReloaded()
        {
        }

        /// <summary>
        /// Reads this window's attributes from an INI file.
        /// </summary>
        protected virtual void GetINIAttributes(IniFile iniFile)
        {
            ThemeIni = iniFile;

            List<string> keys = iniFile.GetSectionKeys(Name);

            if (keys != null)
            {
                foreach (string key in keys)
                    ParseINIAttribute(iniFile, key, iniFile.GetStringValue(Name, key, String.Empty));
            }
            else
            {
                keys = iniFile.GetSectionKeys(GENERIC_WINDOW_SECTION);

                if (keys != null)
                {
                    foreach (string key in keys)
                        ParseINIAttribute(iniFile, key, iniFile.GetStringValue(GENERIC_WINDOW_SECTION, key, String.Empty));
                }
            }

            ParseExtraControls(iniFile, EXTRA_CONTROLS);
            ReadChildControlAttributes(iniFile);
        }

        public override void Initialize()
        {
            base.Initialize();

            SetAttributesFromIni();
        }
    }
}
