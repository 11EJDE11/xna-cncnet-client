using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClientCore;
using Microsoft.Xna.Framework;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// What the designer does to the INI files: key edits, moves and resizes, adding, duplicating
    /// and deleting controls, and saving.
    /// </summary>
    internal sealed partial class DesignerOverlay
    {
        private sealed class NewControlType
        {
            public NewControlType(string typeName, string prefix, string description, params string[] keys)
            {
                TypeName = typeName;
                Prefix = prefix;
                Description = description;
                Keys = keys;
            }

            public string TypeName { get; }
            public string Prefix { get; }
            public string Description { get; }

            /// <summary>
            /// Keys the new control starts with, as Key=Value. Size=$BUTTON is a button size the theme has an image for,
            /// with its images: a button in ExtraControls is initialized before its size is read, so it can't find them itself.
            /// </summary>
            public string[] Keys { get; }
        }

        private static readonly NewControlType[] NewControlTypes =
        {
            new NewControlType("XNALabel", "lbl", "text", "Text=New label"),
            new NewControlType("XNAClientButton", "btn", "button", "Size=$BUTTON", "Text=New button"),
            new NewControlType("XNALinkButton", "btn", "button that opens a web page", "Size=$BUTTON", "Text=Link", "URL=https://cncnet.org"),
            new NewControlType("XNAClientLinkLabel", "lnk", "text that opens a web page", "Text=Link", "URL=https://cncnet.org"),
            new NewControlType("XNAPanel", "pnl", "panel", "Size=160,100", "SolidColorBackgroundTexture=0,0,0,160", "DrawBorders=true"),
            new NewControlType("XNAExtraPanel", "img", "image or decoration, ignores the mouse", "Size=64,64", "SolidColorBackgroundTexture=255,255,255,96"),
            new NewControlType("XNAClientCheckBox", "chk", "check box", "Text=New check box"),
            new NewControlType("XNAClientDropDown", "dd", "drop-down", "Size=140,21"),
            new NewControlType("XNASuggestionTextBox", "tb", "text box", "Size=160,21", "Suggestion=Type here"),
            new NewControlType("XNAListBox", "lb", "list", "Size=200,120", "SolidColorBackgroundTexture=0,0,0,160"),
        };

        private const int DUPLICATE_OFFSET = 12;

        private XNAControl addContainer;
        private string suggestedControlName;

        // Client buttons use the {width}pxbtn.png images, so pick a width the theme has images for
        private static IEnumerable<string> GetButtonKeys()
        {
            int width = new[] { 133, 160, 121, 110, 92, 75 }.FirstOrDefault(w => AssetLoader.AssetExists(w + "pxbtn.png"));
            if (width == 0)
                return new[] { "Size=133,23" };

            return new[] { $"Size={width},23", $"IdleTexture={width}pxbtn.png", $"HoverTexture={width}pxbtn_c.png" };
        }

        #region Geometry

        /// <summary>
        /// Turns new positions and sizes of the selected controls into INI edits, previews them as one
        /// undo step and reports controls that ended up elsewhere or can't be configured from an INI file.
        /// </summary>
        private void CommitGeometry(Dictionary<XNAControl, Rectangle> before, Dictionary<XNAControl, Rectangle> after)
        {
            // Let the INI decide where the controls end up
            foreach (var entry in before)
                entry.Key.ClientRectangle = entry.Value;

            var edits = new List<ControlEdit>();
            var notConfigurable = new List<string>();

            // Parents first, then in the order the INI creates and reads controls, so controls positioned
            // relative to an earlier one are planned from where that one moved them
            List<XNAControl> ordered = selection.OrderBy(GetDepth).ThenBy(c => c.Parent?.Children.IndexOf(c) ?? 0).ToList();

            string error = tools.ApplyEditsInOrder(ordered, control =>
            {
                bool parentResized = control.Parent != null && before.TryGetValue(control.Parent, out Rectangle parentBefore) &&
                    (after[control.Parent].Width != parentBefore.Width || after[control.Parent].Height != parentBefore.Height);

                Rectangle current = control.ClientRectangle;
                if (current == after[control] && !parentResized)
                    return null;

                IniWindow window = IniWindow.OwnerOf(control);
                string section = window?.SectionFor(control);
                if (section == null)
                {
                    notConfigurable.Add(Describe(control));
                    return null;
                }

                Point? parentSize = control.Parent == null ? null : new Point(control.Parent.Width, control.Parent.Height);
                List<KeyValuePair<string, string>> changes = GeometryEdits.Apply(GetOrderedKeys(window, section), current, after[control],
                    parentSize, parentResized);

                return changes.Count == 0 ? null : new ControlEdit(window, control, section, changes);
            }, DependsOn, edits);

            if (error != null)
            {
                SetStatus(error, DesignerStyle.Warning);
                return;
            }

            // Report the values the controls ended up with
            foreach (ControlEdit correction in CorrectGeometry(ordered.Where(c => !notConfigurable.Contains(Describe(c))).ToList(), after))
            {
                ControlEdit edit = edits.FirstOrDefault(e => e.Control == correction.Control);
                if (edit == null)
                {
                    edits.Add(correction);
                    continue;
                }

                foreach (var key in correction.Keys)
                {
                    edit.Keys.RemoveAll(k => k.Key == key.Key);
                    edit.Keys.Add(key);
                }
            }

            // Scaling rounds to whole pixels, so allow a pixel either way
            List<string> misplaced = selection.Where(c => !IsWithinPixel(c.ClientRectangle, after[c]) && !notConfigurable.Contains(Describe(c)))
                .Select(c => c.Name).ToList();
            string message = edits.Count == 0 ? "Nothing changed." :
                edits.Count == 1 ? $"Changed {string.Join(", ", edits[0].Keys.Select(k => k.Key + "=" + k.Value))} in [{edits[0].Section}]." :
                $"Changed {edits.Count} controls: {ListNames(edits.Select(e => e.Control.Name))}.";

            if (notConfigurable.Count > 0)
                message += $" Can't be configured from an INI file: {ListNames(notConfigurable)}.";

            if (misplaced.Count > 0)
                message += $" A later key or code places {ListNames(misplaced)} elsewhere.";

            SetStatus(message, notConfigurable.Count > 0 || misplaced.Count > 0 ? DesignerStyle.Warning : DesignerStyle.ThemeKey);
        }

        /// <summary>
        /// Fixes controls the preview put somewhere other than planned, which happens when a key is calculated from
        /// something the plan can't foresee, such as a $Width that reads the control's own position. The keys that
        /// decide the wrong edges are shifted by what is missing, as part of the same undo step. A correction that
        /// doesn't bring the controls closer is dropped again: then code or a later key decides where they go.
        /// </summary>
        /// <returns>The corrections that were kept.</returns>
        private List<ControlEdit> CorrectGeometry(List<XNAControl> controls, Dictionary<XNAControl, Rectangle> after)
        {
            var kept = new List<ControlEdit>();

            for (int pass = 0; pass < 2; pass++)
            {
                List<XNAControl> misplaced = controls.Where(c => !IsWithinPixel(c.ClientRectangle, after[c])).ToList();
                if (misplaced.Count == 0)
                    break;

                int errorBefore = GetGeometryError(controls, after);
                var corrections = new List<ControlEdit>();

                string error = tools.ApplyEditsInOrder(misplaced, control =>
                {
                    IniWindow window = IniWindow.OwnerOf(control);
                    string section = window?.SectionFor(control);
                    if (section == null || control.ClientRectangle == after[control])
                        return null;

                    List<KeyValuePair<string, string>> changes = GeometryEdits.Correct(GetOrderedKeys(window, section), control.ClientRectangle, after[control]);
                    return changes.Count == 0 ? null : new ControlEdit(window, control, section, changes);
                }, DependsOn, corrections);

                if (error != null || corrections.Count == 0)
                    break;

                if (GetGeometryError(controls, after) >= errorBefore)
                {
                    tools.RevertLastStep();
                    break;
                }

                tools.MergeLastUndoSteps();
                kept.AddRange(corrections);
            }

            return kept;
        }

        private static int GetGeometryError(IEnumerable<XNAControl> controls, Dictionary<XNAControl, Rectangle> after) =>
            controls.Sum(c => Math.Abs(c.X - after[c].X) + Math.Abs(c.Y - after[c].Y) +
                Math.Abs(c.Width - after[c].Width) + Math.Abs(c.Height - after[c].Height));

        /// <summary>
        /// Whether a control's position can change when the given controls change: when one of them
        /// contains it, or when its INI keys refer to one of them by name, as in getBottom(name).
        /// </summary>
        private static bool DependsOn(XNAControl control, IEnumerable<XNAControl> changed)
        {
            List<XNAControl> changedList = changed.ToList();
            if (changedList.Any(c => c.IsParentOf(control)))
                return true;

            IniWindow window = IniWindow.OwnerOf(control);
            string section = window?.SectionFor(control);
            if (section == null)
                return false;

            string values = string.Join(" ", GetKeys(window, section).Values);
            return changedList.Any(c => !string.IsNullOrEmpty(c.Name) && values.Contains("(" + c.Name + ")"));
        }

        private static bool IsWithinPixel(Rectangle a, Rectangle b) =>
            Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1 && Math.Abs(a.Width - b.Width) <= 1 && Math.Abs(a.Height - b.Height) <= 1;

        private static List<KeyValuePair<string, string>> GetOrderedKeys(IniWindow window, string section) =>
            window.Ini?.GetSection(section)?.Keys.ToList() ?? new List<KeyValuePair<string, string>>();

        #endregion

        #region Key edits

        private void TbEdit_EnterPressed(object sender, EventArgs e) => ApplyEditText();

        /// <summary>
        /// Applies what the edit box holds: Key=Value for the selected control or the open file section,
        /// the name of a control to add, or in the Controls tab the first control found.
        /// </summary>
        private void ApplyEditText()
        {
            string text = tbEdit.Text ?? string.Empty;

            if (mode == PanelMode.AddControl)
            {
                AddControl(text.Trim());
                return;
            }

            if (mode == PanelMode.Controls)
            {
                XNAControl found = lbKeys.Items.Select(i => i.Tag).OfType<XNAControl>().FirstOrDefault();
                if (found != null)
                {
                    Select(found);
                    SetMode(PanelMode.Inspector);
                }

                return;
            }

            int equals = text.IndexOf('=');
            if (equals < 1 || (mode == PanelMode.Inspector && selected == null) || (mode == PanelMode.Files && fileLevel != 2))
            {
                SetStatus(mode == PanelMode.Files ? "Open a section and type Key=Value." : "Select a control and type Key=Value.", DesignerStyle.Warning);
                return;
            }

            string key = text.Substring(0, equals).Trim();
            string value = text.Substring(equals + 1).Trim();

            if (value.Contains(";"))
            {
                SetStatus("Values can't contain ';', INI files read it as the start of a comment.", DesignerStyle.Warning);
                return;
            }

            if (mode == PanelMode.Files)
            {
                string fileError = tools.ApplyFileEdit(filePath, fileSection, key, value);
                if (fileError != null)
                    SetStatus(fileError, DesignerStyle.Warning);
                else
                    SetStatus($"Set {key}={value} in [{fileSection}] of {Path.GetFileName(filePath)}.", DesignerStyle.ThemeKey);

                return;
            }

            IniWindow window = IniWindow.OwnerOf(selected);
            string section = window?.SectionFor(selected);
            if (window == null || section == null)
            {
                SetStatus($"{Describe(selected)} can't be configured from an INI file.", DesignerStyle.Warning);
                return;
            }

            string error = tools.ApplyEdits(window, selected, section, new[] { new KeyValuePair<string, string>(key, value) });
            if (error != null)
                SetStatus(error, DesignerStyle.Warning);
            else
                SetStatus($"Set {key}={value} in [{section}].", DesignerStyle.ThemeKey);
        }

        #endregion

        #region Adding, duplicating and deleting

        // Chooses where the Add tab adds to: inside a selected window or plain panel, otherwise next to the selected control
        private bool PrepareAddControl()
        {
            if (selected == null)
            {
                SetStatus("Select the window or panel to add a control to first.", DesignerStyle.Warning);
                return false;
            }

            addContainer = selected is XNAPanel && (IniWindow.From(selected) != null || selected.GetType() == typeof(XNAPanel) ||
                selected is ClientGUI.XNAClientScrollPanel) ? selected : GetContainer(selected);

            if (addContainer == null || IniWindow.OwnerOf(addContainer) == null)
            {
                SetStatus($"{Describe(selected)} isn't inside a window that reads an INI file.", DesignerStyle.Warning);
                return false;
            }

            suggestedControlName = null;
            return true;
        }

        // The control a control is declared in: a scroll panel rather than its content panel
        private static XNAControl GetContainer(XNAControl control)
        {
            XNAControl parent = control.Parent;
            while (parent != null && IsInternalPart(parent))
                parent = parent.Parent;

            return parent;
        }

        private NewControlType GetChosenControlType() =>
            NewControlTypes.FirstOrDefault(t => t.TypeName == lbKeys.SelectedItem?.Tag as string) ?? NewControlTypes[0];

        private string SuggestControlName(NewControlType type)
        {
            IniWindow window = IniWindow.OwnerOf(addContainer);
            return window == null ? null : GetFreeName(window, type.Prefix + "New", 1);
        }

        private string GetFreeName(IniWindow window, string baseName, int first)
        {
            for (int i = first; ; i++)
            {
                string name = baseName + i.ToString(CultureInfo.InvariantCulture);
                if (IsControlNameFree(window, name))
                    return name;
            }
        }

        private bool IsControlNameFree(IniWindow window, string name)
        {
            XNAControl existing = DesignerTools.FindDescendant(window.Control, name);
            return (existing == null || tools.IsDeleted(existing)) && window.Ini?.SectionExists(name) != true;
        }

        // Whether a control is created from the window's INI, so sections of its own are read on every load
        private static bool IsCreatedFromIni(IniWindow window, XNAControl control)
        {
            if (window.Ini == null)
                return false;

            foreach (string section in window.Ini.GetSections())
            {
                foreach (string key in window.Ini.GetSectionKeys(section))
                {
                    if (key.StartsWith("$CC", StringComparison.Ordinal) &&
                        window.Ini.GetStringValue(section, key, string.Empty).StartsWith(control.Name + ":", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Adds a control of the chosen type with the given name to the chosen container, in its middle.
        /// </summary>
        private void AddControl(string name)
        {
            NewControlType type = GetChosenControlType();
            var keys = type.Keys.SelectMany(k => k == "Size=$BUTTON" ? GetButtonKeys() : new[] { k })
                .Select(k => new KeyValuePair<string, string>(k.Substring(0, k.IndexOf('=')), k.Substring(k.IndexOf('=') + 1)))
                .ToList();

            // In the middle of the container, so it isn't hidden in a corner
            string sizeValue = keys.FirstOrDefault(k => k.Key == "Size").Value;
            string[] sizeParts = sizeValue?.Split(',');
            Point size = sizeParts != null ? new Point(int.Parse(sizeParts[0], CultureInfo.InvariantCulture), int.Parse(sizeParts[1], CultureInfo.InvariantCulture)) : new Point(100, 20);
            var rectangle = new Rectangle(Math.Max(0, (addContainer.Width - size.X) / 2), Math.Max(0, (addContainer.Height - size.Y) / 2), size.X, size.Y);
            keys.Insert(0, new KeyValuePair<string, string>("Location", rectangle.X + "," + rectangle.Y));

            XNAControl control = CreateControl(addContainer, name, type.TypeName, keys, rectangle);
            if (control == null)
                return;

            SetStatus($"Added {name} to {addContainer.Name}. Drag it into place and set its keys; Ctrl+Z removes it again.", DesignerStyle.ThemeKey);
        }

        /// <summary>
        /// Copies the selected control with the keys of its section, a little below and right of it.
        /// Controls inside it aren't copied.
        /// </summary>
        private void DuplicateSelected()
        {
            XNAControl source = selection.Count == 1 ? selected : null;
            IniWindow window = source == null ? null : IniWindow.OwnerOf(source);
            string section = window?.SectionFor(source);
            XNAControl container = source == null ? null : GetContainer(source);

            if (window == null || section == null || source == window.Control || container == null)
            {
                SetStatus("Select one named control inside a window to duplicate it.", DesignerStyle.Warning);
                return;
            }

            ControlDeclaration declaration = window.FindDeclaration(source.Name);
            string typeName = declaration?.TypeName ?? source.GetType().Name;

            // Copies of copies count on from the original's number: btnOk2 becomes btnOk3
            Match numbered = Regex.Match(source.Name, @"^(.*?[^0-9])([0-9]+)$");
            string baseName = numbered.Success ? numbered.Groups[1].Value : source.Name;
            int first = numbered.Success ? int.Parse(numbered.Groups[2].Value, CultureInfo.InvariantCulture) + 1 : 2;
            string name = GetFreeName(window, baseName, first);

            List<KeyValuePair<string, string>> keys = GetOrderedKeys(window, section)
                .Where(k => !k.Key.StartsWith("$CC", StringComparison.Ordinal)).ToList();

            Rectangle rectangle = source.ClientRectangle;
            var target = new Rectangle(rectangle.X + DUPLICATE_OFFSET, rectangle.Y + DUPLICATE_OFFSET, rectangle.Width, rectangle.Height);
            Point? parentSize = source.Parent == null ? null : new Point(source.Parent.Width, source.Parent.Height);

            foreach (var change in GeometryEdits.Apply(keys, rectangle, target, parentSize, false))
                SetKey(keys, change.Key, change.Value);

            // Images code gave it, which the section doesn't name
            if (source is XNAButton button)
            {
                CopyTextureKey(keys, "IdleTexture", button.IdleTexture);
                CopyTextureKey(keys, "HoverTexture", button.HoverTexture);
            }
            else if (source is XNAPanel panelSource && !keys.Any(k => k.Key == "SolidColorBackgroundTexture"))
            {
                CopyTextureKey(keys, "BackgroundTexture", panelSource.BackgroundTexture);
            }

            // A control sized by code gets the size it has now
            if (!keys.Any(k => k.Key == "Size" || k.Key == "Width" || k.Key == "$Width" || k.Key == "FillWidth"))
                SetKey(keys, "Width", rectangle.Width.ToString(CultureInfo.InvariantCulture));

            if (!keys.Any(k => k.Key == "Size" || k.Key == "Height" || k.Key == "$Height" || k.Key == "FillHeight"))
                SetKey(keys, "Height", rectangle.Height.ToString(CultureInfo.InvariantCulture));

            XNAControl control = CreateControl(container, name, typeName, keys, target);
            if (control == null)
                return;

            bool hasChildren = GetSelectableChildren(source).Any();
            SetStatus($"Duplicated {source.Name} as {name}." + (hasChildren ? " The controls inside it aren't copied." : string.Empty) +
                " Ctrl+Z removes it again.", DesignerStyle.ThemeKey);
        }

        private static void CopyTextureKey(List<KeyValuePair<string, string>> keys, string key, Microsoft.Xna.Framework.Graphics.Texture2D texture)
        {
            // Textures made in code, such as solid colors, have no file name
            if (texture != null && !string.IsNullOrEmpty(texture.Name) && !keys.Any(k => k.Key == key))
                keys.Add(new KeyValuePair<string, string>(key, texture.Name));
        }

        private static void SetKey(List<KeyValuePair<string, string>> keys, string key, string value)
        {
            int index = keys.FindIndex(k => k.Key == key);
            if (index > -1)
                keys[index] = new KeyValuePair<string, string>(key, value);
            else
                keys.Add(new KeyValuePair<string, string>(key, value));
        }

        /// <summary>
        /// Creates a control with the given keys in a container: an entry that declares it (ExtraControls,
        /// or a $CC key in an INI-configured window) and a section of its own. Selects it if it was created.
        /// </summary>
        /// <param name="rectangle">Where it goes, relative to the container.</param>
        private XNAControl CreateControl(XNAControl container, string name, string typeName, List<KeyValuePair<string, string>> keys, Rectangle rectangle)
        {
            IniWindow window = IniWindow.OwnerOf(container);

            if (window == null)
            {
                SetStatus("The control to add to is no longer part of a window.", DesignerStyle.Warning);
                return null;
            }

            if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                SetStatus("Names can only use letters, digits and underscores, and can't start with a digit.", DesignerStyle.Warning);
                return null;
            }

            if (!IsControlNameFree(window, name))
            {
                SetStatus($"{window.Control.Name} already has a control or section named {name}.", DesignerStyle.Warning);
                return null;
            }

            var edits = new List<IniOverlayEdit>();
            keys = new List<KeyValuePair<string, string>>(keys);

            if (window.Control is ClientGUI.INItializableWindow)
            {
                // A $CC entry is only read when its section is; code-created containers may never be read
                XNAControl parent = container == window.Control || IsCreatedFromIni(window, container) ? container : window.Control;
                if (parent != container)
                {
                    Rectangle containerRectangle = container.GetWindowRectangle();
                    Rectangle windowRectangle = window.Control.GetWindowRectangle();
                    var moved = new Rectangle(containerRectangle.X - windowRectangle.X + rectangle.X, containerRectangle.Y - windowRectangle.Y + rectangle.Y,
                        rectangle.Width, rectangle.Height);

                    foreach (var change in GeometryEdits.Apply(keys, rectangle, moved, new Point(window.Control.Width, window.Control.Height), false))
                        SetKey(keys, change.Key, change.Value);
                }

                edits.Add(new IniOverlayEdit(null, window.SectionFor(parent), "$CC" + name, name + ":" + typeName));
            }
            else
            {
                string parentPart = container == window.Control ? string.Empty : ":" + container.Name;
                edits.Add(new IniOverlayEdit(null, "ExtraControls", name, name + ":" + typeName + parentPart));

                // ExtraControls are drawn behind the window's own controls, which suits decorations but not these
                if (typeName != "XNAPanel" && typeName != "XNAExtraPanel" && !keys.Any(k => k.Key == "DrawOrder"))
                    keys.Add(new KeyValuePair<string, string>("DrawOrder", "1"));
            }

            foreach (var keyValue in keys)
                edits.Add(new IniOverlayEdit(null, name, keyValue.Key, keyValue.Value));

            XNAControl control = tools.AddControl(window, name, edits, out string error);
            if (control == null)
            {
                SetStatus(error, DesignerStyle.Warning);
                return null;
            }

            mode = PanelMode.Inspector;
            tbEdit.Text = string.Empty;
            Select(control);
            return control;
        }

        private void LbKeys_RemoveClicked(object sender, DesignerListItem item)
        {
            if (reviewing || showingHelp || mode != PanelMode.Inspector || selected == null || item.Tag is not string key)
                return;

            IniWindow window = IniWindow.OwnerOf(selected);
            string section = window?.SectionFor(selected);
            if (section == null)
                return;

            string name = selected.Name;
            string error = tools.RemoveKey(window, selected, section, key, out bool needsRestart);
            if (error != null)
            {
                SetStatus(error, DesignerStyle.Warning);
                return;
            }

            if ((tbEdit.Text ?? string.Empty).StartsWith(key + "=", StringComparison.Ordinal))
                tbEdit.Text = string.Empty;

            SetStatus($"Removed {key} from [{section}]." +
                (needsRestart ? $" {name} shows the old value until a restart, as the window has no earlier value to go back to." : string.Empty) +
                " Ctrl+Z undoes this.", DesignerStyle.ThemeKey);
        }

        /// <summary>
        /// Replaces links to other controls with plain numbers: the links of the selected row of the key list, or
        /// every link of the selected control. The controls stay where they are but no longer follow the others.
        /// </summary>
        private void UnlinkSelected()
        {
            List<ControlLink> links = lbKeys.SelectedItem?.Tag is LinkRow row ? row.Links : selectedLinks;
            if (links.Count == 0)
                return;

            List<XNAControl> followers = links.Select(l => l.Follower).Distinct()
                .OrderBy(GetDepth).ThenBy(c => c.Parent?.Children.IndexOf(c) ?? 0).ToList();
            Dictionary<XNAControl, Rectangle> places = followers.ToDictionary(c => c, c => c.ClientRectangle);

            List<ControlEdit> edits = links.GroupBy(l => l.Follower).Select(g => new ControlEdit(IniWindow.OwnerOf(g.Key), g.Key, g.First().Section,
                g.Select(l => l.Key).Distinct().Select(k => new KeyValuePair<string, string>(k, GetGeometryValue(g.Key, k))).ToList())).ToList();

            string error = tools.ApplyEdits(edits);
            if (error != null)
            {
                SetStatus(error, DesignerStyle.Warning);
                return;
            }

            CorrectGeometry(followers, places);

            string sources = ListNames(links.Select(l => l.Source.Name).Distinct());
            string keys = string.Join(", ", edits.SelectMany(e => e.Keys.Select(k => $"{k.Key}={k.Value}")));
            string result = followers.Count == 1 ? "It keeps its place but no longer follows" : "They keep their place but no longer follow";
            SetStatus($"Unlinked {ListNames(followers.Select(c => c.Name))} from {sources}: {keys}. {result} {sources}. Ctrl+Z undoes this.",
                DesignerStyle.ThemeKey);
        }

        private static string GetGeometryValue(XNAControl control, string key)
        {
            int value = key switch
            {
                "$X" => control.X,
                "$Y" => control.Y,
                "$Width" => control.Width,
                _ => control.Height,
            };

            return value.ToString(CultureInfo.InvariantCulture);
        }

        private void DeleteSelection()
        {
            if (selection.Count == 0)
            {
                SetStatus("Select the controls to delete first.", DesignerStyle.Warning);
                return;
            }

            List<XNAControl> targets = GetOutermostSelected();
            XNAControl parent = GetContainer(targets[0]);
            string message = tools.DeleteControls(targets);

            selection.RemoveAll(c => !IsShown(c));
            if (selection.Count == 0 && parent != null && IsShown(parent))
                selection.Add(parent);

            SelectionChanged();
            SetStatus(message, message.StartsWith("Deleted", StringComparison.Ordinal) ? DesignerStyle.ThemeKey : DesignerStyle.Warning);
        }

        #endregion

        /// <summary>
        /// Opens the file the selected key comes from, or the selected control's own INI file, in the program
        /// the system opens INI files with. Saving it there reloads the client.
        /// </summary>
        private void OpenIniFile()
        {
            string path = null;

            if (mode == PanelMode.Files)
            {
                path = filePath;
            }
            else if (selected != null && IniWindow.OwnerOf(selected) is IniWindow window)
            {
                string section = window.SectionFor(selected);
                string key = mode == PanelMode.Inspector ? lbKeys.SelectedItem?.Tag as string : null;
                string themeFile = Path.Combine(ProgramConstants.GetResourcePath(), window.OwnIniFileName);

                if (key != null && section != null)
                    path = window.Ini?.GetKeySource(section, key)?.FilePath;

                path ??= File.Exists(themeFile) ? themeFile : window.IniPath;
            }

            if (path == null || !File.Exists(path))
            {
                SetStatus(path == null ? "Select a control or file first." : $"{DesignerTools.DisplayPath(path)} doesn't exist yet; save a change to create it.",
                    DesignerStyle.Warning);
                return;
            }

            ProcessLauncher.StartShellProcess(path);
            SetStatus($"Opened {DesignerTools.DisplayPath(path)}. The client reloads when you save it.");
        }

        #region Saving

        private void StartReview()
        {
            if (tools.PendingEdits.Count == 0)
            {
                SetStatus("There are no unsaved changes.");
                return;
            }

            reviewing = true;
            showingHelp = false;
            SetStatus("Check the changes, then Write files saves them.");
        }

        private void EndReview()
        {
            reviewing = false;
            RefreshPanel();
        }

        private void SaveReviewed()
        {
            reviewing = false;
            string result = tools.Save();
            SetStatus(result, result.StartsWith("Saved", StringComparison.Ordinal) ? DesignerStyle.ThemeKey : DesignerStyle.Warning);
        }

        #endregion
    }
}
