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

        // Designer mode only: controls created by $CC or $ExtraControls entries. While ReloadIni runs, those
        // whose entry hasn't been reached yet are pending, as they wouldn't exist yet when loading the client.
        private readonly HashSet<XNAControl> _iniCreatedControls = new HashSet<XNAControl>();
        private readonly HashSet<XNAControl> _pendingIniControls = new HashSet<XNAControl>();

        // Designer mode only: a step that advances whenever a section is read outside a reload, the step each control
        // (by name) was first seen to exist at, and the step its section was first read at. With them a reload checks
        // that expressions only refer to controls that existed when loading the client read them, and reads the
        // sections that code reads, such as the game lobby's player rows, in the same order.
        private int _readStep;
        private int _iniFileEndStep;
        private readonly Dictionary<string, int> _createdSteps = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _firstReadSteps = new Dictionary<string, int>();
        private bool _readingCodeReadSections;

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
                // The designer may create the INI file later
                if (ProgramConstants.DesignerMode)
                {
                    RecordReadStep(null);
                    _iniFileEndStep = _readStep;
                }

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

            if (ProgramConstants.DesignerMode)
                _iniFileEndStep = _readStep;

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

            // Expressions must see what they would see when loading the client: controls that haven't been
            // read yet at their pre-INI rectangles, and controls that haven't been created yet not at all
            foreach (var entry in _preIniRectangles)
            {
                if (entry.Key != this)
                    entry.Key.ClientRectangle = entry.Value;
            }

            _pendingIniControls.UnionWith(_iniCreatedControls);
            Parser.Instance.IsControlCreated = IsControlCreated;

            try
            {
                Parser.Instance.SetPrimaryControl(this);
                ReadINIForControl(this);
                ReadLateAttributesForControl(this);
                ParseExtraControls();

                // Controls created in code read their sections from code after the INI file
                _readingCodeReadSections = true;
                ReadCodeReadSections();
            }
            finally
            {
                _reloading = false;
                _readingCodeReadSections = false;
                _reloadedControls.Clear();
                _pendingIniControls.Clear();
                Parser.Instance.IsControlCreated = null;
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

        // Designer mode: records that a section is read now, outside a reload, and which controls exist by then
        private void RecordReadStep(XNAControl control)
        {
            _readStep++;
            RecordCreatedSteps(this, _readStep);

            if (control != null && !string.IsNullOrEmpty(control.Name) && !_firstReadSteps.ContainsKey(control.Name))
                _firstReadSteps.Add(control.Name, _readStep);
        }

        private void RecordCreatedSteps(XNAControl control, int step)
        {
            foreach (XNAControl child in control.Children)
            {
                if (!string.IsNullOrEmpty(child.Name) && !_createdSteps.ContainsKey(child.Name))
                    _createdSteps.Add(child.Name, step);

                RecordCreatedSteps(child, step);
            }
        }

        // Whether a control exists yet when loading the client reads the given control's expressions
        private bool IsControlCreated(XNAControl parsingControl, XNAControl control)
        {
            if (!_createdSteps.TryGetValue(control.Name, out int createdStep))
                return false;

            if (_readingCodeReadSections)
                return !_firstReadSteps.TryGetValue(parsingControl.Name, out int readStep) || createdStep <= readStep;

            // In the INI file: not created later in code, and neither it nor a container of it
            // is created by an entry that hasn't been reached yet
            if (createdStep > _iniFileEndStep)
                return false;

            for (XNAControl c = control; c != null && c != this; c = c.Parent)
            {
                if (_pendingIniControls.Contains(c))
                    return false;
            }

            return true;
        }

        // Re-reads the sections that code reads, in the order loading the client first read them. Sections of
        // controls that code never reads have no effect when loading the client, so they aren't read either.
        private void ReadCodeReadSections()
        {
            var controls = new List<XNAControl>();
            FindCodeReadDescendants(this, controls);

            foreach (XNAControl control in controls.OrderBy(c => _firstReadSteps[c.Name]))
            {
                if (!_reloadedControls.Contains(control))
                    ReadINIForControl(control);
            }
        }

        private void FindCodeReadDescendants(XNAControl control, List<XNAControl> controls)
        {
            foreach (XNAControl child in control.Children)
            {
                // Nested windows reload their own INI files
                if (child is INItializableWindow || child is XNAWindowBase)
                    continue;

                if (!_reloadedControls.Contains(child) && !string.IsNullOrEmpty(child.Name) && _firstReadSteps.ContainsKey(child.Name))
                    controls.Add(child);

                FindCodeReadDescendants(child, controls);
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

                XNAControl existingControl = Children.FirstOrDefault(child => child.Name == parts[0]);

                if (existingControl == null)
                {
                    var control = CreateChildControl(this, kvp.Value);
                    control.Name = parts[0];
                    control.DrawOrder = -Children.Count;
                    ReadINIForControl(control);
                }
                else if (_reloading && _iniCreatedControls.Contains(existingControl))
                {
                    // Read where loading the client would create it, so the controls after it can refer to it
                    _pendingIniControls.Remove(existingControl);
                    ReadINIForControl(existingControl);
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
            // Recorded even without a section, as one may be added with the designer
            if (ProgramConstants.DesignerMode && !_reloading)
                RecordReadStep(control);

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
                            _pendingIniControls.Remove(existingChild);
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

                    // Controls it created when initialized also exist from here on
                    if (ProgramConstants.DesignerMode && !_reloading)
                        RecordCreatedSteps(this, _readStep);
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

            if (ProgramConstants.DesignerMode)
            {
                _iniCreatedControls.Add(childControl);

                // A control added with the designer exists from its entry on, like the ones created when loading the client
                if (_reloading && !_createdSteps.ContainsKey(childName))
                {
                    _createdSteps.Add(childName, 0);
                    RecordCreatedSteps(childControl, 0);
                }
            }

            if (parent is XNAClientScrollPanel scrollPanel)
                scrollPanel.Content.AddChildWithoutInitialize(childControl);
            else
                parent.AddChildWithoutInitialize(childControl);

            return childControl;
        }
    }
}
