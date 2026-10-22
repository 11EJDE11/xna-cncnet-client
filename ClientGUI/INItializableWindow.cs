using ClientCore;
using ClientCore.I18N;
using ClientCore.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClientGUI
{
    public class INItializableWindow : XNAPanel
    {
        public INItializableWindow(WindowManager windowManager) : base(windowManager)
        {
        }

        /// <summary>
        /// The INI file this window was configured from, or null if it has none.
        /// </summary>
        public CCIniFile ConfigIni { get; private set; }

        /// <summary>
        /// The path of <see cref="ConfigIni"/>, or null if this window has no INI file.
        /// </summary>
        public string ConfigIniPath { get; private set; }

        /// <summary>
        /// The name of the INI file (without extension) this window primarily looks for.
        /// </summary>
        public string IniName => string.IsNullOrWhiteSpace(IniNameOverride) ? Name : IniNameOverride;

        private bool hasCloseButton = false;
        private bool _initialized = false;

        // Set while ReloadIni re-applies the INI to the existing controls
        private bool _reloading = false;
        private readonly HashSet<XNAControl> _reloadedControls = new HashSet<XNAControl>();

        // Designer mode only: each child's rectangle from before its INI section was first read.
        // Expressions such as getY($Self) depend on it, so a reload restores it to give the same result.
        private readonly Dictionary<XNAControl, Rectangle> _preIniRectangles = new Dictionary<XNAControl, Rectangle>();

        /// <summary>
        /// If not null, the client will read an INI file with this name
        /// instead of the window's name.
        /// </summary>
        protected string IniNameOverride { get; set; }

        public T FindChild<T>(string childName, StringComparison comparisonType = StringComparison.Ordinal, bool optional = false, bool recursive = true) where T : XNAControl
            => ClientGUI.Extensions.XNAControlExtensions.FindChild<T>(this, childName, comparisonType, optional, recursive);

        public List<T> FindChildrenStartWith<T>(string prefix, StringComparison comparisonType = StringComparison.Ordinal, bool recursive = true) where T : XNAControl
            => ClientGUI.Extensions.XNAControlExtensions.FindChildrenStartWith<T>(this, prefix, comparisonType, recursive);

        /// <summary>
        /// Attempts to locate the ini config file for the current control.
        /// Only return a config path if it exists.
        /// </summary>
        /// <returns>The ini config file path</returns>
        protected string GetConfigPath()
        {
            string iniFileName = IniName;

            // get theme specific path
            FileInfo configIniPath = SafePath.GetFile(ProgramConstants.GetResourcePath(), FormattableString.Invariant($"{iniFileName}.ini"));
            if (IniOverlay.FileExists(configIniPath.FullName))
                return configIniPath.FullName;

            // get base path
            configIniPath = SafePath.GetFile(ProgramConstants.GetBaseResourcePath(), FormattableString.Invariant($"{iniFileName}.ini"));
            if (configIniPath.Exists)
                return configIniPath.FullName;

            if (iniFileName == Name)
                return null; // IniNameOverride must be null, no need to continue

            iniFileName = Name;

            // get theme specific path
            configIniPath = SafePath.GetFile(ProgramConstants.GetResourcePath(), FormattableString.Invariant($"{iniFileName}.ini"));
            if (IniOverlay.FileExists(configIniPath.FullName))
                return configIniPath.FullName;

            // get base path
            configIniPath = SafePath.GetFile(ProgramConstants.GetBaseResourcePath(), FormattableString.Invariant($"{iniFileName}.ini"));
            return configIniPath.Exists ? configIniPath.FullName : null;
        }

        public override void Initialize()
        {
            if (_initialized)
                throw new InvalidOperationException("INItializableWindow cannot be initialized twice.");

            string configIniPath = GetConfigPath();

            if (string.IsNullOrEmpty(configIniPath))
            {
                base.Initialize();
                return;
            }

            ConfigIniPath = configIniPath;
            ConfigIni = new CCIniFile(configIniPath);

            if (Parser.Instance == null)
                _ = new Parser(WindowManager); // Note: Parser.Instance will be set by calling new Parser()

            Parser.Instance.SetPrimaryControl(this);
            ReadINIForControl(this);
            ReadLateAttributesForControl(this);

            ParseExtraControls();

            base.Initialize();

            _initialized = true;
        }

        /// <summary>
        /// Re-reads this window's INI file and re-applies it to the existing controls, creating
        /// any child controls that were added to it. Keys that would duplicate list items or reset
        /// player choices are skipped; see <see cref="IniReloadPolicy"/>.
        /// </summary>
        public void ReloadIni()
        {
            string configIniPath = GetConfigPath();
            if (string.IsNullOrEmpty(configIniPath))
                return;

            ConfigIniPath = configIniPath;
            ConfigIni = new CCIniFile(configIniPath);

            if (Parser.Instance == null)
                _ = new Parser(WindowManager);

            _reloading = true;
            _reloadedControls.Clear();

            try
            {
                Parser.Instance.SetPrimaryControl(this);
                ReadINIForControl(this);

                // Controls created in code read their sections from code during initialization
                ReadINIForUnreadDescendants(this);
                ReadLateAttributesForControl(this);
                ParseExtraControls();
            }
            finally
            {
                _reloading = false;
                _reloadedControls.Clear();
            }

            OnIniReloaded();
        }

        /// <summary>
        /// Re-reads this window's INI file and re-applies only the sections of the given controls.
        /// Quicker than <see cref="ReloadIni()"/> for previewing a change while other changes depend on it.
        /// </summary>
        public void ReloadIni(IEnumerable<XNAControl> controls)
        {
            string configIniPath = GetConfigPath();
            if (string.IsNullOrEmpty(configIniPath))
                return;

            ConfigIniPath = configIniPath;
            ConfigIni = new CCIniFile(configIniPath);

            if (Parser.Instance == null)
                _ = new Parser(WindowManager);

            _reloading = true;
            _reloadedControls.Clear();

            try
            {
                Parser.Instance.SetPrimaryControl(this);

                foreach (XNAControl control in controls)
                    ReadINIForControl(control);
            }
            finally
            {
                _reloading = false;
                _reloadedControls.Clear();
            }
        }

        /// <summary>
        /// Called after <see cref="ReloadIni()"/> has re-applied the INI file, for windows that
        /// position controls in code based on INI values.
        /// </summary>
        protected virtual void OnIniReloaded()
        {
        }

        private void ReadINIForUnreadDescendants(XNAControl control)
        {
            foreach (XNAControl child in control.Children.ToList())
            {
                // Nested windows reload their own INI files
                if (child is INItializableWindow || child is XNAWindowBase)
                    continue;

                if (!_reloadedControls.Contains(child) && !string.IsNullOrEmpty(child.Name))
                    ReadINIForControl(child);

                ReadINIForUnreadDescendants(child);
            }
        }

        private void ParseExtraControls()
        {
            var section = ConfigIni.GetSection("$ExtraControls");

            if (section == null)
                return;

            foreach (var kvp in section.Keys)
            {
                if (!kvp.Key.StartsWith("$CC"))
                    continue;

                string[] parts = kvp.Value.Split(':');
                if (parts.Length != 2)
                    throw new ClientConfigurationException("Invalid $ExtraControl specified in " + Name + ": " + kvp.Value);

                if (!Children.Any(child => child.Name == parts[0]))
                {
                    var control = CreateChildControl(this, kvp.Value);
                    control.Name = parts[0];
                    control.DrawOrder = -Children.Count;
                    ReadINIForControl(control);
                }
            }
        }

        protected override void ParseControlINIAttribute(IniFile iniFile, string key, string value)
        {
            if (key == "HasCloseButton")
                hasCloseButton = iniFile.GetBooleanValue(Name, key, hasCloseButton);

            base.ParseControlINIAttribute(iniFile, key, value);
        }

        protected void ReadINIForControl(XNAControl control)
        {
            var section = ConfigIni.GetSection(control.Name);
            if (section == null)
                return;

            if (_reloading)
            {
                _reloadedControls.Add(control);

                if (control != this && _preIniRectangles.TryGetValue(control, out Rectangle preIniRectangle))
                    control.ClientRectangle = preIniRectangle;
            }
            else if (ProgramConstants.DesignerMode && !_preIniRectangles.ContainsKey(control))
            {
                _preIniRectangles.Add(control, control.ClientRectangle);
            }

            Parser.Instance.SetPrimaryControl(this);

            // shorthand for localization function
            static string Localize(XNAControl control, string attributeName, string defaultValue, TranslationNotificationLevel notificationLevel = TranslationNotificationLevel.Default)
                => Translation.Instance.LookUp(control, attributeName, defaultValue, notificationLevel);

            foreach (var kvp in section.Keys)
            {
                if (_reloading && IniReloadPolicy.IsUnsafeToReapply(kvp.Key))
                    continue;

                if (kvp.Key.StartsWith("$CC"))
                {
                    if (_reloading)
                    {
                        XNAControl existingChild = FindChildControl(control, kvp.Value);
                        if (existingChild != null)
                        {
                            ReadINIForControl(existingChild);
                            continue;
                        }
                    }

                    var child = CreateChildControl(control, kvp.Value);

                    // A scroll panel only attaches its content panel when initialized, and its children
                    // may look up their ancestors (e.g. the game lobby) in Initialize, so it is initialized first
                    if (child is XNAClientScrollPanel)
                    {
                        child.Initialize();
                        ReadINIForControl(child);
                    }
                    else
                    {
                        ReadINIForControl(child);
                        child.Initialize();
                    }

                    if (child is ICompositeControl composite)
                    {
                        foreach (var sc in composite.SubControls)
                        {
                            ReadINIForControl(sc);
                            sc.Initialize();
                        }
                    }
                }
                else if (kvp.Key == "$X")
                {
                    control.X = Parser.Instance.GetExprValue(
                        Localize(control, kvp.Key, kvp.Value, TranslationNotificationLevel.Verbose), control);
                }
                else if (kvp.Key == "$Y")
                {
                    control.Y = Parser.Instance.GetExprValue(
                        Localize(control, kvp.Key, kvp.Value, TranslationNotificationLevel.Verbose), control);
                }
                else if (kvp.Key == "$Width")
                {
                    control.Width = Parser.Instance.GetExprValue(
                        Localize(control, kvp.Key, kvp.Value, TranslationNotificationLevel.Verbose), control);
                }
                else if (kvp.Key == "$Height")
                {
                    control.Height = Parser.Instance.GetExprValue(
                        Localize(control, kvp.Key, kvp.Value, TranslationNotificationLevel.Verbose), control);
                }
                else if (kvp.Key == "$TextAnchor" && control is XNALabel)
                {
                    // TODO refactor these to be more object-oriented
                    ((XNALabel)control).TextAnchor = (LabelTextAnchorInfo)Enum.Parse(typeof(LabelTextAnchorInfo), kvp.Value);
                }
                else if (kvp.Key == "$AnchorPoint" && control is XNALabel)
                {
                    string[] parts = kvp.Value.Split(',');
                    if (parts.Length != 2)
                        throw new FormatException("Invalid format for AnchorPoint: " + kvp.Value);
                    ((XNALabel)control).AnchorPoint = new Vector2(Parser.Instance.GetExprValue(parts[0], control), Parser.Instance.GetExprValue(parts[1], control));
                }
                else if (kvp.Key == "$LeftClickAction")
                {
                    if (kvp.Value == "Disable")
                        control.LeftClick += (s, e) => Disable();
                }
                else
                {
                    control.ParseINIAttribute(ConfigIni, kvp.Key, kvp.Value);
                }
            }
        }

        /// <summary>
        /// Reads a second set of attributes for a control's child controls.
        /// Enables linking controls to controls that are defined after them.
        /// </summary>
        private void ReadLateAttributesForControl(XNAControl control)
        {
            var section = ConfigIni.GetSection(control.Name);
            if (section == null)
                return;

            var children = Children.ToList();
            foreach (var child in children)
            {
                // This logic should also be enabled for other types in the future,
                // but it requires changes in XNAUI
                if (!(child is XNATextBox))
                    continue;

                var childSection = ConfigIni.GetSection(child.Name);
                if (childSection == null)
                    continue;

                string nextControl = childSection.GetStringValue("NextControl", null);
                if (!string.IsNullOrWhiteSpace(nextControl))
                {
                    var otherChild = children.Find(c => c.Name == nextControl);
                    if (otherChild != null)
                        ((XNATextBox)child).NextControl = otherChild;
                }

                string previousControl = childSection.GetStringValue("PreviousControl", null);
                if (!string.IsNullOrWhiteSpace(previousControl))
                {
                    var otherChild = children.Find(c => c.Name == previousControl);
                    if (otherChild != null)
                        ((XNATextBox)child).PreviousControl = otherChild;
                }
            }
        }

        private static XNAControl FindChildControl(XNAControl parent, string keyValue)
        {
            string childName = keyValue.Split(':')[0];
            XNAControl container = parent is XNAClientScrollPanel scrollPanel ? scrollPanel.Content : parent;
            return container.Children.FirstOrDefault(c => c.Name == childName);
        }

        private XNAControl CreateChildControl(XNAControl parent, string keyValue)
        {
            string[] parts = keyValue.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
                throw new INIConfigException("Invalid child control definition " + keyValue);

            string childName = parts[0];
            if (string.IsNullOrEmpty(childName))
                throw new INIConfigException("Empty name in child control definition for " + parent.Name);

            XNAControl childControl = ClientGUICreator.GetXnaControl(parts[1]);

            if (Array.Exists(childName.ToCharArray(), c => !char.IsLetterOrDigit(c) && c != '_'))
                throw new INIConfigException("Names of INItializableWindow child controls must consist of letters, digits and underscores only. Offending name: " + parts[0]);

            childControl.Name = childName;

            if (parent is XNAClientScrollPanel scrollPanel)
                scrollPanel.Content.AddChildWithoutInitialize(childControl);
            else
                parent.AddChildWithoutInitialize(childControl);

            return childControl;
        }
    }
}
