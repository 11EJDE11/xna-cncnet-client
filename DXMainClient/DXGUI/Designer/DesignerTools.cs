using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ClientCore;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.Input;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// In-client tools for theme designers, enabled with the -designer startup parameter.
    /// <list type="bullet">
    /// <item>F5, or saving an INI or PNG file in the resource folders, reloads images, theme colors
    /// and the INI layout of every open window without restarting.</item>
    /// <item>F6 opens the <see cref="DesignerOverlay"/> to inspect controls, see which INI file and
    /// section every key comes from, and move, resize or edit them.</item>
    /// </list>
    /// Edits stay pending (previewed through <see cref="IniOverlay"/>) until they are reviewed and saved.
    /// </summary>
    internal sealed class DesignerTools
    {
        private const int FILE_CHANGE_DELAY_MS = 400;
        private const int OWN_WRITE_IGNORE_MS = 2000;

        private readonly WindowManager windowManager;
        private readonly Action refreshUISettings;
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly Stack<List<PendingEdit>> undoStack = new Stack<List<PendingEdit>>();
        private readonly Stack<List<PendingEdit>> redoStack = new Stack<List<PendingEdit>>();
        private List<PendingEdit> pendingEdits = new List<PendingEdit>();

        // Each edited control's rectangle and visibility from before its first unsaved edit. A reload can only apply
        // what the INI says, so a control positioned or shown by code would keep an undone change without this.
        private readonly Dictionary<XNAControl, ControlState> statesBeforeEdits = new Dictionary<XNAControl, ControlState>();

        // Controls added with the designer, by name; removed again when their unsaved definition is undone
        private readonly Dictionary<string, AddedControl> addedControls = new Dictionary<string, AddedControl>();

        // Controls deleted with the designer. A reload can't remove controls either, so they are hidden while
        // the INI no longer creates them, and shown again when an undo brings their definition back.
        private readonly Dictionary<XNAControl, DeletedControl> deletedControls = new Dictionary<XNAControl, DeletedControl>();
        private DesignerOverlay overlay;
        private Timer fileChangeTimer;
        private long ignoreFileChangesUntilTicks;

        public DesignerTools(WindowManager windowManager, Action refreshUISettings)
        {
            this.windowManager = windowManager;
            this.refreshUISettings = refreshUISettings;
        }

        public IReadOnlyList<PendingEdit> PendingEdits => pendingEdits;

        public bool CanUndo => undoStack.Count > 0;

        public bool CanRedo => redoStack.Count > 0;

        /// <summary>
        /// Whether the control was deleted and the deletion isn't saved yet; it stays hidden until then.
        /// </summary>
        public bool IsDeleted(XNAControl control) => deletedControls.TryGetValue(control, out DeletedControl deleted) && deleted.Hidden;

        /// <summary>
        /// The controls that have unsaved edits.
        /// </summary>
        public IEnumerable<XNAControl> EditedControls => pendingEdits.Where(p => p.Control != null).Select(p => p.Control).Distinct();

        public void Initialize()
        {
            overlay = new DesignerOverlay(windowManager, this);
            windowManager.AddAndInitializeControl(overlay);
            windowManager.Keyboard.OnKeyPressed += Keyboard_OnKeyPressed;

            fileChangeTimer = new Timer(_ => windowManager.AddCallback(new Action(OnFilesChanged)), null, Timeout.Infinite, Timeout.Infinite);

            string basePath = Path.GetFullPath(ProgramConstants.GetBaseResourcePath());
            string themePath = Path.GetFullPath(ProgramConstants.GetResourcePath());
            WatchDirectory(basePath);
            if (!string.Equals(basePath, themePath, StringComparison.OrdinalIgnoreCase))
                WatchDirectory(themePath);

            Logger.Log("Designer tools enabled: F5 reloads the UI, F6 opens the inspector.");
        }

        private void WatchDirectory(string path)
        {
            if (!Directory.Exists(path))
                return;

            foreach (string filter in new[] { "*.ini", "*.png" })
            {
                var watcher = new FileSystemWatcher(path, filter)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
                };

                watcher.Changed += Watcher_Changed;
                watcher.Created += Watcher_Changed;
                watcher.Renamed += Watcher_Changed;
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
        }

        // Runs on a thread pool thread; editors often write a file several times, so wait for quiet first
        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (DateTime.UtcNow.Ticks < Interlocked.Read(ref ignoreFileChangesUntilTicks))
                return;

            fileChangeTimer.Change(FILE_CHANGE_DELAY_MS, Timeout.Infinite);
        }

        private void OnFilesChanged() => overlay.SetStatus(ReloadAll("Files changed, reloaded"));

        // A key saved in another editor since it was edited here keeps the saved value: the unsaved edit
        // would hide it from the reload and overwrite it on saving
        private string DropEditsChangedOnDisk()
        {
            var files = new Dictionary<string, IniFile>(StringComparer.OrdinalIgnoreCase);

            string GetValueOnDisk(IniOverlayEdit edit)
            {
                string path = Path.GetFullPath(edit.FilePath);
                if (!files.TryGetValue(path, out IniFile file))
                {
                    file = File.Exists(path) ? new IniFile(path, false) : null;
                    files.Add(path, file);
                }

                return file?.GetStringValue(edit.Section, edit.Key, null);
            }

            // Removing a whole section doesn't depend on the value of any one key
            List<PendingEdit> changed = pendingEdits.Where(p => !p.Edit.RemovesSection && GetValueOnDisk(p.Edit) != p.OldValue).ToList();
            if (changed.Count == 0)
                return string.Empty;

            bool IsChanged(PendingEdit pending) => !pending.Edit.RemovesSection && changed.Any(c => c.Edit.Section == pending.Edit.Section &&
                c.Edit.Key == pending.Edit.Key && IsSamePath(c.Edit.FilePath, pending.Edit.FilePath));

            pendingEdits = pendingEdits.Where(p => !IsChanged(p)).ToList();

            // Undoing or redoing must not bring the dropped edits back either
            RemoveFromSteps(undoStack, IsChanged);
            RemoveFromSteps(redoStack, IsChanged);

            IniOverlay.Set(pendingEdits.Select(p => p.Edit));

            List<string> keys = changed.Select(p => $"{Path.GetFileName(p.Edit.FilePath)} [{p.Edit.Section}] {p.Edit.Key}").Distinct().ToList();
            return $". {keys.Count} unsaved edit(s) dropped, the file was saved with a different value: " +
                string.Join(", ", keys.Take(3)) + (keys.Count > 3 ? ", ..." : string.Empty);
        }

        private static void RemoveFromSteps(Stack<List<PendingEdit>> steps, Func<PendingEdit, bool> remove)
        {
            List<List<PendingEdit>> kept = steps.Reverse().Select(step => step.Where(p => !remove(p)).ToList()).ToList();
            steps.Clear();

            foreach (List<PendingEdit> step in kept)
                steps.Push(step);
        }

        private void Keyboard_OnKeyPressed(object sender, KeyPressEventArgs e)
        {
            if (ProgramConstants.IsInGame)
                return;

            switch (e.PressedKey)
            {
                case Keys.F5:
                    overlay.SetStatus(ReloadAll("Reloaded"));
                    e.Handled = true;
                    return;
                case Keys.F6:
                    overlay.Toggle();
                    e.Handled = true;
                    return;
            }

            if (overlay.IsOpen)
                e.Handled = overlay.HandleKey(e.PressedKey);
        }

        /// <summary>
        /// Reloads images, theme colors and the INI layout of every window.
        /// </summary>
        /// <returns>A status message.</returns>
        public string ReloadAll(string reason)
        {
            string droppedEdits = DropEditsChangedOnDisk();
            List<string> resizedTextures = AssetLoader.ReloadCachedTextures();
            string status = reason + ": " + ReloadWindows() + droppedEdits;

            try
            {
                refreshUISettings();
            }
            catch (Exception ex)
            {
                Logger.Log("Designer: reloading the theme's client settings failed: " + ex);
                status += ". Theme colors were not reloaded: " + ex.Message;
            }

            UpdateAddedAndDeletedControls();

            if (resizedTextures.Count > 0)
            {
                status += $". {resizedTextures.Count} image(s) changed size and only fully update after a restart: " +
                    string.Join(", ", resizedTextures.Take(3)) + (resizedTextures.Count > 3 ? ", ..." : string.Empty);
            }

            return status;
        }

        /// <summary>
        /// Re-applies the INI file of every INI-configured window, including pending edits.
        /// </summary>
        /// <returns>A short summary.</returns>
        public string ReloadWindows()
        {
            List<IniWindow> windows = FindWindows();
            var failed = new List<string>();

            RestoreStatesBeforeEdits(control => true);

            foreach (IniWindow window in windows)
            {
                try
                {
                    window.Reload();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Designer: reloading {window.Control.Name} failed: {ex}");
                    failed.Add(window.Control.Name);
                }
            }

            string summary = $"{windows.Count} windows";
            if (failed.Count > 0)
                summary += $", failed: {string.Join(", ", failed)} (see client.log)";

            return summary;
        }

        private List<IniWindow> FindWindows()
        {
            var windows = new List<IniWindow>();

            foreach (XNAControl control in windowManager.TopLevelControls.ToList())
            {
                if (control != overlay)
                    FindWindows(control, windows);
            }

            return windows;
        }

        private static void FindWindows(XNAControl control, List<IniWindow> windows)
        {
            IniWindow window = IniWindow.From(control);
            if (window != null)
                windows.Add(window);

            foreach (XNAControl child in control.Children)
                FindWindows(child, windows);
        }

        /// <summary>
        /// Adds pending edits for keys of one control's section, previews them and records an undo step.
        /// If the window can't read the edited INI (for example an invalid color), the edits are rolled back.
        /// </summary>
        /// <returns>Null on success, otherwise why the edits were rolled back.</returns>
        public string ApplyEdits(IniWindow window, XNAControl control, string section, IEnumerable<KeyValuePair<string, string>> keyValues) =>
            ApplyEdits(new[] { new ControlEdit(window, control, section, keyValues.ToList()) });

        /// <summary>
        /// Adds pending edits for several controls as one undo step and previews them.
        /// If a window can't read its edited INI (for example an invalid color), all of them are rolled back.
        /// </summary>
        /// <returns>Null on success, otherwise why the edits were rolled back.</returns>
        public string ApplyEdits(IReadOnlyList<ControlEdit> controlEdits)
        {
            if (controlEdits.Count == 0)
                return null;

            PushUndoStep();

            foreach (ControlEdit controlEdit in controlEdits)
                AddControlEdit(controlEdit);

            IniOverlay.Set(pendingEdits.Select(p => p.Edit));

            List<XNAControl> windows = controlEdits.Select(e => e.Window.Control).Distinct().ToList();

            try
            {
                RestoreStatesBeforeEdits(c => windows.Contains(IniWindow.OwnerOf(c)?.Control));

                foreach (ControlEdit controlEdit in controlEdits.GroupBy(e => e.Window.Control).Select(g => g.First()))
                    controlEdit.Window.Reload();

                return null;
            }
            catch (Exception ex)
            {
                return RollBack(ex);
            }
        }

        /// <summary>
        /// Plans and previews edits of several controls in order, as one undo step. Before planning a control
        /// that depends on controls changed earlier (its INI position refers to them, as in
        /// getBottom(otherControl) + 6, or one of them contains it), the earlier edits are applied and the
        /// control is re-read, so it is planned from where its dependencies moved it.
        /// </summary>
        /// <param name="controls">The controls in the order their INI sections are read.</param>
        /// <param name="planEdit">Returns the edit for a control in its current state, or null for none.</param>
        /// <param name="dependsOn">Whether a control depends on any of the given edited controls.</param>
        /// <param name="appliedEdits">Receives the edits that were made.</param>
        /// <returns>Null on success, otherwise why the edits were rolled back.</returns>
        public string ApplyEditsInOrder(IEnumerable<XNAControl> controls, Func<XNAControl, ControlEdit> planEdit,
            Func<XNAControl, IEnumerable<XNAControl>, bool> dependsOn, List<ControlEdit> appliedEdits)
        {
            PushUndoStep();
            var unpreviewed = new List<ControlEdit>();

            // Controls that were edited or re-read, so their position may have changed
            var touched = new List<XNAControl>();

            try
            {
                foreach (XNAControl control in controls)
                {
                    if (touched.Count > 0 && dependsOn(control, touched))
                    {
                        PreviewControls(unpreviewed);
                        Reread(IniWindow.OwnerOf(control), control);
                        touched.Add(control);
                    }

                    ControlEdit controlEdit = planEdit(control);
                    if (controlEdit == null || controlEdit.Keys.Count == 0)
                        continue;

                    AddControlEdit(controlEdit);
                    unpreviewed.Add(controlEdit);
                    appliedEdits.Add(controlEdit);
                    touched.Add(control);
                }

                // One full reload so everything else that depends on the edited controls follows too
                IniOverlay.Set(pendingEdits.Select(p => p.Edit));
                List<XNAControl> windows = appliedEdits.Select(e => e.Window.Control).Distinct().ToList();
                RestoreStatesBeforeEdits(c => windows.Contains(IniWindow.OwnerOf(c)?.Control));

                foreach (ControlEdit controlEdit in appliedEdits.GroupBy(e => e.Window.Control).Select(g => g.First()))
                    controlEdit.Window.Reload();
            }
            catch (Exception ex)
            {
                appliedEdits.Clear();
                return RollBack(ex);
            }

            if (appliedEdits.Count == 0)
                undoStack.Pop();

            return null;
        }

        // Applies the edits to their own controls only, so later controls can be planned from them
        private void PreviewControls(List<ControlEdit> controlEdits)
        {
            if (controlEdits.Count == 0)
                return;

            IniOverlay.Set(pendingEdits.Select(p => p.Edit));

            foreach (var windowEdits in controlEdits.GroupBy(e => e.Window.Control))
            {
                List<XNAControl> edited = windowEdits.Select(e => e.Control).ToList();
                RestoreStatesBeforeEdits(edited.Contains);
                windowEdits.First().Window.Reload(edited);
            }

            controlEdits.Clear();
        }

        // Re-reads one control's section so its position reflects the edits applied so far
        private void Reread(IniWindow window, XNAControl control)
        {
            if (window == null)
                return;

            RestoreStatesBeforeEdits(c => c == control);
            window.Reload(new[] { control });
        }

        private void AddControlEdit(ControlEdit controlEdit)
        {
            RecordState(controlEdit.Control, controlEdit.Keys.Select(k => k.Key));

            foreach (var keyValue in controlEdit.Keys)
            {
                // Without an earlier value the control can't be told to go back after an undo
                bool addsKeyWithoutDefault = !IsGeometryKey(keyValue.Key) && keyValue.Key != "Visible" && keyValue.Key != "Enabled" &&
                    controlEdit.Window.Ini?.GetSectionKeys(controlEdit.Section)?.Contains(keyValue.Key) != true;

                List<IniOverlayEdit> edits = IniEditTargets.Resolve(controlEdit.Window, controlEdit.Section, keyValue.Key, keyValue.Value);
                for (int i = 0; i < edits.Count; i++)
                    AddPendingEdit(edits[i], controlEdit.Control, addsKeyWithoutDefault && i == edits.Count - 1);
            }
        }

        // Remembers how the control was before its first edit, so undoing can put it back
        private void RecordState(XNAControl control, IEnumerable<string> editedKeys)
        {
            if (!statesBeforeEdits.TryGetValue(control, out ControlState state))
            {
                state = new ControlState(control);
                statesBeforeEdits.Add(control, state);
            }

            // Only for edits of these keys, as code shows and hides many controls itself
            if (editedKeys.Any(k => k == "Visible" || k == "Enabled"))
                state.RecordVisibility(control);
        }

        /// <summary>
        /// Removes a key of a control's section from the theme file that defines it, previews it and records an undo step.
        /// </summary>
        /// <param name="needsRestart">Whether the control keeps showing the old value until a restart: the key was saved
        /// before and nothing else sets it, so the window has no value to go back to.</param>
        /// <returns>Null on success, otherwise why the key wasn't removed.</returns>
        public string RemoveKey(IniWindow window, XNAControl control, string section, string key, out bool needsRestart)
        {
            needsRestart = false;

            string filePath = IniEditTargets.ResolveRemoval(window, section, key);
            if (filePath == null)
                return $"{key} comes from a file outside the theme folder, so it can't be removed here; set another value instead.";

            PushUndoStep();
            RecordState(control, new[] { key });
            AddPendingEdit(IniOverlayEdit.RemoveKey(filePath, section, key), control, false);
            bool wasSaved = pendingEdits.Any(p => p.Edit.RemovesKey && p.Edit.Section == section && p.Edit.Key == key &&
                IsSamePath(p.Edit.FilePath, filePath));
            DropFilesWithNothingButBasedOn();
            IniOverlay.Set(pendingEdits.Select(p => p.Edit));

            try
            {
                RestoreStatesBeforeEdits(c => IniWindow.OwnerOf(c)?.Control == window.Control);
                window.Reload();
            }
            catch (Exception ex)
            {
                return RollBack(ex);
            }

            needsRestart = wasSaved && window.Ini?.GetKeySource(section, key) == null;
            return null;
        }

        private string RollBack(Exception ex)
        {
            Logger.Log($"Designer: a window could not read the edited INI: {ex}");

            pendingEdits = undoStack.Pop();
            IniOverlay.Set(pendingEdits.Select(p => p.Edit));
            ReloadWindowsAndSettings();

            return "Not applied, the window could not read it: " + (ex.InnerException ?? ex).Message;
        }

        private void AddPendingEdit(IniOverlayEdit edit, XNAControl control, bool addsKeyWithoutDefault)
        {
            if (edit.RemovesSection)
            {
                AddSectionRemoval(edit);
                return;
            }

            int index = pendingEdits.FindIndex(p => p.Edit.Section == edit.Section && p.Edit.Key == edit.Key &&
                IsSamePath(p.Edit.FilePath, edit.FilePath));

            if (index > -1)
            {
                PendingEdit previous = pendingEdits[index];

                // Removing a key that only exists as an unsaved edit leaves nothing to save
                if (edit.RemovesKey && previous.OldValue == null)
                {
                    pendingEdits.RemoveAt(index);
                    return;
                }

                pendingEdits[index] = new PendingEdit(edit, previous.OldValue, previous.CreatesFile, previous.Control,
                    previous.AddsKeyWithoutDefault);
                return;
            }

            bool exists = File.Exists(edit.FilePath);
            string oldValue = exists ? new IniFile(edit.FilePath, false).GetStringValue(edit.Section, edit.Key, null) : null;
            if (edit.RemovesKey && oldValue == null)
                return;

            pendingEdits.Add(new PendingEdit(edit, oldValue, !exists, control, addsKeyWithoutDefault));
        }

        // Removing a section replaces the unsaved edits of its keys; a section that only exists as unsaved edits just goes
        private void AddSectionRemoval(IniOverlayEdit edit)
        {
            pendingEdits.RemoveAll(p => p.Edit.Section == edit.Section && IsSamePath(p.Edit.FilePath, edit.FilePath));

            if (!File.Exists(edit.FilePath) || !new IniFile(edit.FilePath, false).SectionExists(edit.Section))
                return;

            pendingEdits.Add(new PendingEdit(edit, string.Empty, false, null, false));
        }

        private static bool IsSamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        private void PushUndoStep()
        {
            undoStack.Push(new List<PendingEdit>(pendingEdits));
            redoStack.Clear();
        }

        private void ReloadWindowsAndSettings()
        {
            ReloadWindows();

            try
            {
                refreshUISettings();
            }
            catch (Exception ex)
            {
                Logger.Log("Designer: reloading the theme's client settings failed: " + ex);
            }

            UpdateAddedAndDeletedControls();
        }

        /// <summary>
        /// Adds an unsaved edit of a key in a file that isn't a window's own INI, such as the theme's
        /// DTACnCNetClient.ini or GenericWindow.ini, previews it and records an undo step.
        /// </summary>
        /// <returns>Null on success, otherwise why the edit was rolled back.</returns>
        public string ApplyFileEdit(string filePath, string section, string key, string value)
        {
            PushUndoStep();
            AddPendingEdit(new IniOverlayEdit(filePath, section, key, value), null, false);
            IniOverlay.Set(pendingEdits.Select(p => p.Edit));

            try
            {
                ReloadWindows();
                refreshUISettings();
                return null;
            }
            catch (Exception ex)
            {
                return RollBack(ex);
            }
        }

        /// <summary>
        /// Adds a new control to a window: the edits that declare it (an ExtraControls or $CC entry) and
        /// its own section. Previews it, records an undo step and returns the created control.
        /// </summary>
        /// <param name="error">Why the control wasn't added, if it wasn't.</param>
        public XNAControl AddControl(IniWindow window, string name, IEnumerable<IniOverlayEdit> sectionEdits, out string error)
        {
            PushUndoStep();

            foreach (IniOverlayEdit sectionEdit in sectionEdits)
            {
                foreach (IniOverlayEdit edit in IniEditTargets.Resolve(window, sectionEdit.Section, sectionEdit.Key, sectionEdit.Value))
                    AddPendingEdit(edit, null, false);
            }

            IniOverlay.Set(pendingEdits.Select(p => p.Edit));

            try
            {
                window.Reload();
            }
            catch (Exception ex)
            {
                error = RollBack(ex);
                return null;
            }

            XNAControl control = FindDescendant(window.Control, name);
            if (control == null)
            {
                error = $"{name} was not created; the window may not read the section it was added to.";
                pendingEdits = undoStack.Pop();
                IniOverlay.Set(pendingEdits.Select(p => p.Edit));
                ReloadWindows();
                return null;
            }

            addedControls[name] = new AddedControl(window, control);

            // Mark the new control's own keys as its edits, for the unsaved changes markers
            for (int i = 0; i < pendingEdits.Count; i++)
            {
                PendingEdit pending = pendingEdits[i];
                if (pending.Control == null && pending.Edit.Section == name)
                    pendingEdits[i] = new PendingEdit(pending.Edit, pending.OldValue, pending.CreatesFile, control, false);
            }

            error = null;
            return control;
        }

        public static XNAControl FindDescendant(XNAControl root, string name)
        {
            foreach (XNAControl child in root.Children)
            {
                if (child.Name == name)
                    return child;

                XNAControl found = FindDescendant(child, name);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// A reload can't remove controls, so this takes out added controls whose definition is no longer
        /// pending and picks up the ones a redo created again. Deleted controls are hidden while the INI
        /// no longer creates them and shown again when it does.
        /// </summary>
        private void UpdateAddedAndDeletedControls()
        {
            foreach (var added in addedControls.ToList())
            {
                XNAControl control = added.Value.Control;
                bool defined = pendingEdits.Any(p => p.Edit.Section == added.Key && !p.Edit.RemovesSection);

                if (!defined && control.Parent != null)
                {
                    control.Parent.RemoveChild(control);
                    statesBeforeEdits.Remove(control);
                }
                else if (defined && control.Parent == null)
                {
                    XNAControl recreated = FindDescendant(added.Value.Window.Control, added.Key);
                    if (recreated != null)
                        addedControls[added.Key] = new AddedControl(added.Value.Window, recreated);
                }
            }

            foreach (var entry in deletedControls)
            {
                XNAControl control = entry.Key;
                DeletedControl deleted = entry.Value;
                bool isCreated = deleted.Window.FindDeclaration(control.Name) != null;

                if (!isCreated)
                {
                    if (!deleted.Hidden)
                    {
                        deleted.Visible = control.Visible;
                        deleted.Enabled = control.Enabled;
                        deleted.Hidden = true;
                    }

                    // Also after a reload restored how it was before its edits
                    control.Visible = false;
                    control.Enabled = false;
                }
                else if (deleted.Hidden)
                {
                    deleted.Hidden = false;
                    control.Visible = deleted.Visible;
                    control.Enabled = deleted.Enabled;
                }
            }
        }

        /// <summary>
        /// Deletes controls as one undo step. A control the INI creates in the current theme (an ExtraControls
        /// entry or a $CC key) loses that entry, its own section and those of the controls inside it. A control
        /// created by code or by a file outside the theme can't be removed from an INI file, so it gets
        /// Visible=false and Enabled=false instead.
        /// </summary>
        /// <returns>A status message.</returns>
        public string DeleteControls(IReadOnlyList<XNAControl> controls) => DeleteControls(controls, new HashSet<XNAControl>());

        private string DeleteControls(IReadOnlyList<XNAControl> controls, HashSet<XNAControl> hideOnly)
        {
            var removals = new List<IniOverlayEdit>();
            var removed = new List<XNAControl>();
            var hidden = new List<ControlEdit>();
            var reasons = new List<string>();
            var refused = new List<string>();

            foreach (XNAControl control in controls)
            {
                IniWindow window = IniWindow.OwnerOf(control);
                if (window == null || window.Control == control || string.IsNullOrEmpty(control.Name))
                {
                    refused.Add(string.IsNullOrEmpty(control.Name) ? "(unnamed)" : control.Name);
                    continue;
                }

                var controlRemovals = new List<IniOverlayEdit>();
                string reason = hideOnly.Contains(control) ? "a file outside the theme creates it too" : PlanRemoval(window, control, controlRemovals);

                if (reason == null)
                {
                    removals.AddRange(controlRemovals);
                    removed.Add(control);
                    continue;
                }

                reasons.Add($"{control.Name} ({reason})");
                hidden.Add(new ControlEdit(window, control, window.SectionFor(control), new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("Visible", "false"),
                    new KeyValuePair<string, string>("Enabled", "false"),
                }));
            }

            if (removals.Count == 0 && hidden.Count == 0)
                return refused.Count == 0 ? "Nothing to delete." : $"Windows and unnamed controls can't be deleted: {string.Join(", ", refused)}.";

            PushUndoStep();

            foreach (IniOverlayEdit removal in removals)
                AddPendingEdit(removal, null, false);

            foreach (ControlEdit controlEdit in hidden)
                AddControlEdit(controlEdit);

            DropFilesWithNothingButBasedOn();
            IniOverlay.Set(pendingEdits.Select(p => p.Edit));

            try
            {
                ReloadWindows();
            }
            catch (Exception ex)
            {
                return RollBack(ex);
            }

            // A stock file the theme's INI is based on may create the control too
            List<XNAControl> stillCreated = removed.Where(c => IniWindow.OwnerOf(c)?.FindDeclaration(c.Name) != null).ToList();
            if (stillCreated.Count > 0)
            {
                pendingEdits = undoStack.Pop();
                IniOverlay.Set(pendingEdits.Select(p => p.Edit));
                ReloadWindows();
                hideOnly.UnionWith(stillCreated);
                return DeleteControls(controls, hideOnly);
            }

            foreach (XNAControl control in removed)
            {
                if (!addedControls.ContainsKey(control.Name) && !deletedControls.ContainsKey(control))
                    deletedControls.Add(control, new DeletedControl(IniWindow.OwnerOf(control)));
            }

            UpdateAddedAndDeletedControls();

            string message = removed.Count == 0 ? string.Empty : $"Deleted {string.Join(", ", removed.Select(c => c.Name))}. ";

            if (hidden.Count > 0)
                message += $"Hidden with Visible=false, as the theme can't remove them: {string.Join(", ", reasons)}. ";

            if (refused.Count > 0)
                message += $"Windows and unnamed controls can't be deleted: {string.Join(", ", refused)}. ";

            return message + "Ctrl+Z undoes this.";
        }

        // A new file that would only say what it is based on, as its other edits were removed, isn't needed
        private void DropFilesWithNothingButBasedOn()
        {
            foreach (var file in pendingEdits.GroupBy(p => Path.GetFullPath(p.Edit.FilePath), StringComparer.OrdinalIgnoreCase).ToList())
            {
                if (file.All(p => p.CreatesFile && p.Edit.Section == "INISystem" && p.Edit.Key == "BasedOn"))
                    pendingEdits.RemoveAll(p => file.Contains(p));
            }
        }

        /// <summary>
        /// Plans removing the entry that creates a control and the sections of it and the controls inside it.
        /// </summary>
        /// <returns>Null if the control can be removed, otherwise why not.</returns>
        private static string PlanRemoval(IniWindow window, XNAControl control, List<IniOverlayEdit> removals)
        {
            string themeDirectory = Path.GetFullPath(ProgramConstants.GetResourcePath());

            string reason = PlanDeclarationRemoval(window, control.Name, themeDirectory, removals);
            if (reason != null)
                return reason;

            foreach (XNAControl part in GetNamedDescendants(control).Prepend(control))
            {
                // A control inside it with an entry of its own, an ExtraControls entry naming its parent,
                // would make the window fail to load once its parent is gone
                if (part != control && window.FindDeclaration(part.Name) is ControlDeclaration declaration &&
                    declaration.ParentName != null)
                {
                    reason = PlanDeclarationRemoval(window, part.Name, themeDirectory, removals);
                    if (reason != null)
                        return $"{part.Name} inside it {reason}";
                }

                foreach (string file in GetFilesDefiningSection(window, part.Name, themeDirectory))
                    removals.Add(IniOverlayEdit.RemoveSection(file, part.Name));
            }

            return null;
        }

        private static string PlanDeclarationRemoval(IniWindow window, string name, string themeDirectory, List<IniOverlayEdit> removals)
        {
            ControlDeclaration declaration = window.FindDeclaration(name);
            if (declaration == null)
                return "created by code";

            IniKeySource source = window.Ini.GetKeySource(declaration.Section, declaration.Key);
            if (source == null || source.Section != declaration.Section || !IsInDirectory(source.FilePath, themeDirectory) ||
                IniWindow.IsGenericWindowIni(source.FilePath))
            {
                return $"created by {DisplayPath(source?.FilePath) ?? "another file"}";
            }

            removals.Add(IniOverlayEdit.RemoveKey(source.FilePath, declaration.Section, declaration.Key));
            return null;
        }

        private static IEnumerable<XNAControl> GetNamedDescendants(XNAControl control)
        {
            foreach (XNAControl child in control.Children)
            {
                if (!string.IsNullOrEmpty(child.Name))
                    yield return child;

                foreach (XNAControl descendant in GetNamedDescendants(child))
                    yield return descendant;
            }
        }

        // The theme files that define keys of the section itself, rather than inheriting them
        private static List<string> GetFilesDefiningSection(IniWindow window, string section, string themeDirectory)
        {
            List<string> keys = window.Ini?.GetSectionKeys(section);
            if (keys == null)
                return new List<string>();

            return keys.Select(k => window.Ini.GetKeySource(section, k))
                .Where(s => s != null && s.Section == section && IsInDirectory(s.FilePath, themeDirectory))
                .Select(s => s.FilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsInDirectory(string filePath, string directory) =>
            string.Equals(Path.GetDirectoryName(Path.GetFullPath(filePath)).TrimEnd(Path.DirectorySeparatorChar),
                directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Joins the last two undo steps, so a change made in two steps is undone at once.
        /// </summary>
        public void MergeLastUndoSteps()
        {
            if (undoStack.Count >= 2)
                undoStack.Pop();
        }

        /// <summary>
        /// Takes back the last undo step without keeping it for redo, for a change that turned out not to help.
        /// </summary>
        public void RevertLastStep()
        {
            if (undoStack.Count == 0)
                return;

            pendingEdits = undoStack.Pop();
            IniOverlay.Set(pendingEdits.Select(p => p.Edit));
            ReloadWindowsAndSettings();
        }

        private void RestoreStatesBeforeEdits(Func<XNAControl, bool> filter)
        {
            foreach (var entry in statesBeforeEdits)
            {
                if (filter(entry.Key))
                    entry.Value.Restore(entry.Key);
            }
        }

        private static bool IsGeometryKey(string key)
        {
            switch (key)
            {
                case "Location":
                case "Size":
                case "X":
                case "Y":
                case "Width":
                case "Height":
                case "$X":
                case "$Y":
                case "$Width":
                case "$Height":
                case "DistanceFromRightBorder":
                case "DistanceFromBottomBorder":
                case "FillWidth":
                case "FillHeight":
                    return true;
            }

            return false;
        }

        public string Undo()
        {
            if (undoStack.Count == 0)
                return "Nothing to undo";

            List<PendingEdit> undone = pendingEdits.Where(p => !undoStack.Peek().Contains(p)).ToList();
            redoStack.Push(pendingEdits);
            pendingEdits = undoStack.Pop();
            IniOverlay.Set(pendingEdits.Select(p => p.Edit));
            ReloadWindowsAndSettings();
            return $"Undone, {pendingEdits.Count} unsaved change(s) left" + DescribeRestartNeeded(undone);
        }

        public string Redo()
        {
            if (redoStack.Count == 0)
                return "Nothing to redo";

            undoStack.Push(pendingEdits);
            pendingEdits = redoStack.Pop();
            IniOverlay.Set(pendingEdits.Select(p => p.Edit));

            try
            {
                ReloadWindowsAndSettings();
            }
            catch (Exception ex)
            {
                return RollBack(ex);
            }

            return $"Redone, {pendingEdits.Count} unsaved change(s)";
        }

        public string Discard()
        {
            List<PendingEdit> discarded = pendingEdits.ToList();
            pendingEdits.Clear();
            undoStack.Clear();
            redoStack.Clear();
            IniOverlay.Clear();
            ReloadWindowsAndSettings();
            statesBeforeEdits.Clear();
            deletedControls.Clear();
            return $"Discarded {discarded.Count} unsaved change(s)" + DescribeRestartNeeded(discarded);
        }

        // Keys that were added rather than changed can't be unset on a live control
        private static string DescribeRestartNeeded(IEnumerable<PendingEdit> removed)
        {
            List<string> keys = removed.Where(p => p.AddsKeyWithoutDefault)
                .Select(p => $"{p.Control?.Name} {p.Edit.Key}").Distinct().ToList();

            return keys.Count == 0 ? string.Empty :
                $". Restart to see {string.Join(", ", keys)} as before; the window had no earlier value to go back to";
        }

        /// <summary>
        /// Writes all pending edits to their INI files and reloads from disk.
        /// </summary>
        public string Save()
        {
            if (pendingEdits.Count == 0)
                return "Nothing to save";

            Interlocked.Exchange(ref ignoreFileChangesUntilTicks, DateTime.UtcNow.AddMilliseconds(OWN_WRITE_IGNORE_MS).Ticks);

            var files = pendingEdits.GroupBy(p => Path.GetFullPath(p.Edit.FilePath), StringComparer.OrdinalIgnoreCase).ToList();

            try
            {
                foreach (var file in files)
                    IniTextWriter.Apply(file.Key, file.Select(p => p.Edit));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Logger.Log("Designer: saving failed: " + ex);
                return "Saving failed, nothing was lost: " + ex.Message;
            }

            int count = pendingEdits.Count;
            pendingEdits.Clear();
            undoStack.Clear();
            redoStack.Clear();
            addedControls.Clear();
            IniOverlay.Clear();
            ReloadWindowsAndSettings();
            statesBeforeEdits.Clear();

            // Deleted controls stay hidden; they are gone once the client restarts
            deletedControls.Clear();

            return $"Saved {count} change(s) to {string.Join(", ", files.Select(f => DisplayPath(f.Key)))}";
        }

        /// <summary>
        /// Returns a path relative to the game folder for display.
        /// </summary>
        public static string DisplayPath(string path) =>
            path == null ? null : IniEditTargets.GetRelativePath(ProgramConstants.GamePath, path);
    }

    /// <summary>
    /// How a control was placed, and shown if its visibility was edited, before the designer edited it.
    /// </summary>
    internal sealed class ControlState
    {
        private bool? visible;
        private bool? enabled;

        public ControlState(XNAControl control)
        {
            Rectangle = control.ClientRectangle;
        }

        public Rectangle Rectangle { get; }

        public void RecordVisibility(XNAControl control)
        {
            visible ??= control.Visible;
            enabled ??= control.Enabled;
        }

        public void Restore(XNAControl control)
        {
            control.ClientRectangle = Rectangle;

            if (visible.HasValue)
                control.Visible = visible.Value;

            if (enabled.HasValue)
                control.Enabled = enabled.Value;
        }
    }

    internal sealed class AddedControl
    {
        public AddedControl(IniWindow window, XNAControl control)
        {
            Window = window;
            Control = control;
        }

        public IniWindow Window { get; }
        public XNAControl Control { get; }
    }

    internal sealed class DeletedControl
    {
        public DeletedControl(IniWindow window)
        {
            Window = window;
        }

        public IniWindow Window { get; }

        /// <summary>
        /// Whether the designer hid the control because the INI no longer creates it.
        /// </summary>
        public bool Hidden { get; set; }

        public bool Visible { get; set; }
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// INI key changes for one control's section.
    /// </summary>
    internal sealed class ControlEdit
    {
        public ControlEdit(IniWindow window, XNAControl control, string section, List<KeyValuePair<string, string>> keys)
        {
            Window = window;
            Control = control;
            Section = section;
            Keys = keys;
        }

        public IniWindow Window { get; }
        public XNAControl Control { get; }
        public string Section { get; }
        public List<KeyValuePair<string, string>> Keys { get; }
    }

    internal sealed class PendingEdit
    {
        public PendingEdit(IniOverlayEdit edit, string oldValue, bool createsFile, XNAControl control, bool addsKeyWithoutDefault)
        {
            Edit = edit;
            OldValue = oldValue;
            CreatesFile = createsFile;
            Control = control;
            AddsKeyWithoutDefault = addsKeyWithoutDefault;
        }

        /// <summary>
        /// The control the edit was made for.
        /// </summary>
        public XNAControl Control { get; }

        /// <summary>
        /// Whether the edit adds a non-position key the control's section did not have, so undoing it
        /// can't restore the control's previous value until the client restarts.
        /// </summary>
        public bool AddsKeyWithoutDefault { get; }

        public IniOverlayEdit Edit { get; }

        /// <summary>
        /// The key's value in the target file before any pending edit, or null if the file does not set it.
        /// </summary>
        public string OldValue { get; }

        public bool CreatesFile { get; }
    }
}
