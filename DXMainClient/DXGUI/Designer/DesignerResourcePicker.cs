#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClientCore;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// Lists the images or sounds the client can load, for the designer's edit box: the files in the asset
    /// search paths (translation, theme, Resources, game folder), each name once, from the folder the client
    /// would load it from. Shows a preview of the selected image; selecting a sound plays it.
    /// </summary>
    internal sealed class DesignerResourcePicker : XNAControl
    {
        private const int PREVIEW_SIZE = 120;
        private const int MAX_ITEMS = 400;

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };
        private static readonly string[] SoundExtensions = { ".wav" };

        private DesignerListBox lbFiles = null!;
        private List<(string Name, string Folder)> files = new List<(string Name, string Folder)>();
        private DesignerValueKind kind;
        private string? filter;
        private Texture2D? preview;
        private string? previewName;
        private bool silent;

        public DesignerResourcePicker(WindowManager windowManager) : base(windowManager)
        {
        }

        /// <summary>
        /// Raised with the file name when one is selected; applied is true for a click, which should apply it.
        /// </summary>
        public event EventHandler<ResourceChosenEventArgs>? ResourceChosen;

        public static int PreferredHeight => 196;

        public override void Initialize()
        {
            Name = "designerResourcePicker";

            lbFiles = new DesignerListBox(WindowManager);
            lbFiles.Name = "designerResourceFiles";
            lbFiles.ValueColumn = 0.6f;
            lbFiles.SelectedIndexChanged += (s, e) => OnSelectionChanged(false);
            lbFiles.LeftClick += (s, e) => OnSelectionChanged(true);
            AddChild(lbFiles);

            base.Initialize();
            ClientRectangleUpdated += (s, e) => LayoutChildren();
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            int previewWidth = kind == DesignerValueKind.Image ? PREVIEW_SIZE + 8 : 0;
            lbFiles.ClientRectangle = new Rectangle(0, 0, Math.Max(80, Width - previewWidth), Height);
        }

        public DesignerValueKind Kind => kind;

        /// <summary>
        /// Starts showing files of a kind, reading the folders again.
        /// </summary>
        public void Open(DesignerValueKind newKind)
        {
            kind = newKind;
            files = FindFiles(kind == DesignerValueKind.Image ? ImageExtensions : SoundExtensions);
            filter = null;
            LayoutChildren();
        }

        /// <summary>
        /// Shows the files whose name contains the text, and selects the one named exactly that, if any.
        /// </summary>
        public void SetFilter(string text)
        {
            text = Normalize(text);
            if (text == filter)
                return;

            filter = text;
            silent = true;
            lbFiles.Clear();

            // The exact name shouldn't hide the others, so typing a full name still lists everything
            bool exact = files.Any(f => string.Equals(f.Name, text, StringComparison.OrdinalIgnoreCase));
            IEnumerable<(string Name, string Folder)> matches = exact || text.Length == 0 ? files :
                files.Where(f => f.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) > -1);

            foreach (var file in matches.Take(MAX_ITEMS))
            {
                var item = new DesignerListItem(file.Name, DesignerStyle.Text, file.Folder) { Tag = file.Name };
                lbFiles.AddItem(item);

                if (string.Equals(file.Name, text, StringComparison.OrdinalIgnoreCase))
                    lbFiles.SelectedIndex = lbFiles.Items.Count - 1;
            }

            if (lbFiles.Items.Count == 0)
                lbFiles.AddItem(new DesignerListItem(files.Count == 0 ? "No files found" : "No file names contain \"" + text + "\"", DesignerStyle.DimText) { Selectable = false });

            lbFiles.TopIndex = Math.Max(0, lbFiles.SelectedIndex - 3);
            silent = false;
            UpdatePreview();
        }

        /// <summary>
        /// Selects the file with the name, if it is listed, without raising <see cref="ResourceChosen"/>.
        /// </summary>
        public void SelectName(string name)
        {
            name = Normalize(name);
            int index = lbFiles.Items.FindIndex(i => i.Tag is string tag && string.Equals(tag, name, StringComparison.OrdinalIgnoreCase));

            silent = true;
            lbFiles.SelectedIndex = index;
            lbFiles.TopIndex = Math.Max(0, index - 3);
            silent = false;
            UpdatePreview();
        }

        // INI files name files in folders with either slash
        private static string Normalize(string name) => (name ?? string.Empty).Trim().Replace('\\', '/');

        /// <summary>
        /// Moves the selection, for Up and Down in the edit box.
        /// </summary>
        public void MoveSelection(int delta)
        {
            int count = lbFiles.Items.Count(i => i.Selectable);
            if (count == 0)
                return;

            int index = Math.Max(0, Math.Min(count - 1, lbFiles.SelectedIndex + delta));
            lbFiles.SelectedIndex = index;

            int visibleLines = lbFiles.Height / lbFiles.LineHeight;
            if (index < lbFiles.TopIndex)
                lbFiles.TopIndex = index;
            else if (index >= lbFiles.TopIndex + visibleLines)
                lbFiles.TopIndex = index - visibleLines + 1;
        }

        private void OnSelectionChanged(bool clicked)
        {
            if (silent || lbFiles.SelectedItem?.Tag is not string name)
                return;

            UpdatePreview();

            // Selecting plays it; clicking the selected file again applies it without playing it twice
            if (kind == DesignerValueKind.Sound && !clicked)
                new EnhancedSoundEffect(name).Play();

            ResourceChosen?.Invoke(this, new ResourceChosenEventArgs(name, clicked));
        }

        private void UpdatePreview()
        {
            string? name = kind == DesignerValueKind.Image ? lbFiles.SelectedItem?.Tag as string : null;
            if (name == previewName)
                return;

            preview?.Dispose();
            preview = null;
            previewName = name;

            if (name != null && AssetLoader.AssetExists(name))
                preview = AssetLoader.LoadTextureUncached(name);
        }

        public override void Kill()
        {
            preview?.Dispose();
            preview = null;
            base.Kill();
        }

        /// <summary>
        /// Lists the files with the extensions in the asset search paths, each name once, from the first
        /// folder that has it, as the client loads them. Subfolders of the theme folders are included.
        /// </summary>
        private static List<(string Name, string Folder)> FindFiles(string[] extensions)
        {
            var result = new List<(string Name, string Folder)>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var searched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var themePaths = new[] { ProgramConstants.GetResourcePath(), UserINISettings.Instance.TranslationThemeFolderPath }
                .Select(p => Path.GetFullPath(p).TrimEnd('\\', '/')).ToList();

            foreach (string searchPath in AssetLoader.AssetSearchPaths)
            {
                if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath))
                    continue;

                string fullPath = Path.GetFullPath(searchPath);
                if (!searched.Add(fullPath))
                    continue;

                // Resources, the translation folder and the game folder contain the other themes, so only their own files count there
                bool includeSubfolders = themePaths.Contains(fullPath.TrimEnd('\\', '/'), StringComparer.OrdinalIgnoreCase);

                string trimmedPath = fullPath.TrimEnd('\\', '/');
                string folder = string.Equals(trimmedPath, Path.GetFullPath(ProgramConstants.GamePath).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
                    ? "game folder" : Path.GetFileName(trimmedPath);

                try
                {
                    foreach (string file in Directory.EnumerateFiles(fullPath, "*", includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                    {
                        if (!extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                            continue;

                        string name = file.Substring(fullPath.TrimEnd('\\', '/').Length + 1).Replace('\\', '/');
                        if (names.Add(name))
                            result.Add((name, folder));
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Rampastring.Tools.Logger.Log("Designer: listing " + fullPath + " failed: " + ex.Message);
                }
            }

            return result.OrderBy(f => f.Name.Contains("/") ? 1 : 0).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public override void Draw(GameTime gameTime)
        {
            FillControlArea(DesignerStyle.PanelBackground);

            base.Draw(gameTime);

            if (kind != DesignerValueKind.Image)
                return;

            var box = new Rectangle(Width - PREVIEW_SIZE, 0, PREVIEW_SIZE, PREVIEW_SIZE);

            // A checkerboard, so transparent parts show
            const int cell = 10;
            for (int y = 0; y < PREVIEW_SIZE; y += cell)
            {
                for (int x = 0; x < PREVIEW_SIZE; x += cell)
                {
                    bool dark = (x / cell + y / cell) % 2 == 0;
                    FillRectangle(new Rectangle(box.X + x, box.Y + y, Math.Min(cell, PREVIEW_SIZE - x), Math.Min(cell, PREVIEW_SIZE - y)),
                        dark ? new Color(40, 44, 50) : new Color(64, 70, 78));
                }
            }

            if (preview != null)
            {
                float scale = Math.Min(1f, Math.Min(PREVIEW_SIZE / (float)preview.Width, PREVIEW_SIZE / (float)preview.Height));
                int width = Math.Max(1, (int)(preview.Width * scale));
                int height = Math.Max(1, (int)(preview.Height * scale));
                DrawTexture(preview, new Rectangle(box.X + (PREVIEW_SIZE - width) / 2, box.Y + (PREVIEW_SIZE - height) / 2, width, height), Color.White);

                string size = preview.Width.ToString(CultureInfo.InvariantCulture) + " x " + preview.Height.ToString(CultureInfo.InvariantCulture);
                DrawString(size, DesignerStyle.FONT, new Vector2(box.X, box.Bottom + 4), DesignerStyle.DimText);
            }
            else
            {
                DrawString("No preview", DesignerStyle.FONT, new Vector2(box.X + 8, box.Y + PREVIEW_SIZE / 2 - 7), DesignerStyle.DimText);
            }

            DrawRectangle(box, DesignerStyle.Border);
        }
    }

    internal sealed class ResourceChosenEventArgs : EventArgs
    {
        public ResourceChosenEventArgs(string name, bool clicked)
        {
            Name = name;
            Clicked = clicked;
        }

        public string Name { get; }

        /// <summary>
        /// Whether it was clicked, rather than selected with the keyboard.
        /// </summary>
        public bool Clicked { get; }
    }
}
