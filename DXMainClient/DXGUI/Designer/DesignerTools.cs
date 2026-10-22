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
        private List<PendingEdit> pendingEdits = new List<PendingEdit>();

        // Each edited control's rectangle from before its first unsaved edit. A reload can only apply what
        // the INI says, so a control positioned by code would keep an undone size or position without this.
        private readonly Dictionary<XNAControl, Rectangle> rectanglesBeforeEdits = new Dictionary<XNAControl, Rectangle>();

        // Controls added with the designer, by name; removed again when their unsaved definition is undone
        private readonly Dictionary<string, XNAControl> addedControls = new Dictionary<string, XNAControl>();
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
            List<string> resizedTextures = AssetLoader.ReloadCachedTextures();
            string status = reason + ": " + ReloadWindows();

            try
            {
                refreshUISettings();
            }
            catch (Exception ex)
            {
                Logger.Log("Designer: reloading the theme's client settings failed: " + ex);
                status += ". Theme colors were not reloaded: " + ex.Message;
            }

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

            RestoreRectanglesBeforeEdits(control => true);

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

            undoStack.Push(new List<PendingEdit>(pendingEdits));

            foreach (ControlEdit controlEdit in controlEdits)
                AddControlEdit(controlEdit);

            IniOverlay.Set(pendingEdits.Select(p => p.Edit));

            List<XNAControl> windows = controlEdits.Select(e => e.Window.Control).Distinct().ToList();

            try
            {
                RestoreRectanglesBeforeEdits(c => windows.Contains(IniWindow.OwnerOf(c)?.Control));

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
            undoStack.Push(new List<PendingEdit>(pendingEdits));
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
                RestoreRectanglesBeforeEdits(c => windows.Contains(IniWindow.OwnerOf(c)?.Control));

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
                RestoreRectanglesBeforeEdits(edited.Contains);
                windowEdits.First().Window.Reload(edited);
            }

            controlEdits.Clear();
        }

        // Re-reads one control's section so its position reflects the edits applied so far
        private void Reread(IniWindow window, XNAControl control)
        {
            if (window == null)
                return;

            RestoreRectanglesBeforeEdits(c => c == control);
            window.Reload(new[] { control });
        }

        private void AddControlEdit(ControlEdit controlEdit)
        {
            if (!rectanglesBeforeEdits.ContainsKey(controlEdit.Control))
                rectanglesBeforeEdits.Add(controlEdit.Control, controlEdit.Control.ClientRectangle);

            foreach (var keyValue in controlEdit.Keys)
            {
                // Without an earlier value the control can't be told to go back after an undo
                bool addsKeyWithoutDefault = !IsGeometryKey(keyValue.Key) &&
                    controlEdit.Window.Ini?.GetSectionKeys(controlEdit.Section)?.Contains(keyValue.Key) != true;

                List<IniOverlayEdit> edits = IniEditTargets.Resolve(controlEdit.Window, controlEdit.Section, keyValue.Key, keyValue.Value);
                for (int i = 0; i < edits.Count; i++)
                    AddPendingEdit(edits[i], controlEdit.Control, addsKeyWithoutDefault && i == edits.Count - 1);
            }
        }

        private string RollBack(Exception ex)
        {
            Logger.Log($"Designer: a window could not read the edited INI: {ex}");

            pendingEdits = undoStack.Pop();
            IniOverlay.Set(pendingEdits.Select(p => p.Edit));
            RemoveUndefinedAddedControls();
            ReloadWindowsAndSettings();

            return "Not applied, the window could not read it: " + (ex.InnerException ?? ex).Message;
        }

        private void AddPendingEdit(IniOverlayEdit edit, XNAControl control, bool addsKeyWithoutDefault)
        {
            int index = pendingEdits.FindIndex(p => p.Edit.Section == edit.Section && p.Edit.Key == edit.Key &&
                string.Equals(Path.GetFullPath(p.Edit.FilePath), Path.GetFullPath(edit.FilePath), StringComparison.OrdinalIgnoreCase));

            if (index > -1)
            {
                PendingEdit previous = pendingEdits[index];
                pendingEdits[index] = new PendingEdit(edit, previous.OldValue, previous.CreatesFile, previous.Control,
                    previous.AddsKeyWithoutDefault);
                return;
            }

            bool exists = File.Exists(edit.FilePath);
            string oldValue = exists ? new IniFile(edit.FilePath, false).GetStringValue(edit.Section, edit.Key, null) : null;
            pendingEdits.Add(new PendingEdit(edit, oldValue, !exists, control, addsKeyWithoutDefault));
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
        }

        /// <summary>
        /// Adds an unsaved edit of a key in a file that isn't a window's own INI, such as the theme's
        /// DTACnCNetClient.ini or GenericWindow.ini, previews it and records an undo step.
        /// </summary>
        /// <returns>Null on success, otherwise why the edit was rolled back.</returns>
        public string ApplyFileEdit(string filePath, string section, string key, string value)
        {
            undoStack.Push(new List<PendingEdit>(pendingEdits));
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
            undoStack.Push(new List<PendingEdit>(pendingEdits));

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

            addedControls[name] = control;

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

        // A reload can't remove controls, so take out added controls whose section is no longer pending
        private void RemoveUndefinedAddedControls()
        {
            foreach (var added in addedControls.ToList())
            {
                if (pendingEdits.Any(p => p.Edit.Section == added.Key))
                    continue;

                added.Value.Parent?.RemoveChild(added.Value);
                rectanglesBeforeEdits.Remove(added.Value);
                addedControls.Remove(added.Key);
            }
        }

        private void RestoreRectanglesBeforeEdits(Func<XNAControl, bool> filter)
        {
            foreach (var entry in rectanglesBeforeEdits)
            {
                if (filter(entry.Key))
                    entry.Key.ClientRectangle = entry.Value;
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
            pendingEdits = undoStack.Pop();
            IniOverlay.Set(pendingEdits.Select(p => p.Edit));
            RemoveUndefinedAddedControls();
            ReloadWindowsAndSettings();
            return $"Undone, {pendingEdits.Count} unsaved change(s) left" + DescribeRestartNeeded(undone);
        }

        public string Discard()
        {
            List<PendingEdit> discarded = pendingEdits.ToList();
            pendingEdits.Clear();
            undoStack.Clear();
            IniOverlay.Clear();
            RemoveUndefinedAddedControls();
            ReloadWindowsAndSettings();
            rectanglesBeforeEdits.Clear();
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
            IniOverlay.Clear();
            ReloadWindowsAndSettings();
            rectanglesBeforeEdits.Clear();
            addedControls.Clear();

            return $"Saved {count} change(s) to {string.Join(", ", files.Select(f => DisplayPath(f.Key)))}";
        }

        /// <summary>
        /// Returns a path relative to the game folder for display.
        /// </summary>
        public static string DisplayPath(string path) =>
            path == null ? null : IniEditTargets.GetRelativePath(ProgramConstants.GamePath, path);
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
