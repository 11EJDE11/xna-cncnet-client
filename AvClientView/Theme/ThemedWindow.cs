using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

using AvClientView.ViewModels;
using AvClientView.Views;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Layout;

namespace AvClientView.Theme;

/// <summary>
/// Builds an XNAWindow screen (the LAN and CnCNet lobbies): the controls the XNA screen creates in code, the theme's
/// {window}.ini (or GenericWindow) applied by name, the static parts drawn by <see cref="LayoutView"/>, and
/// interactive Avalonia controls placed where the layout puts the named controls.
/// </summary>
public static class ThemedWindow
{
    public static int RenderWidth => ThemeAssets.RenderWidth;

    public static int RenderHeight => ThemeAssets.RenderHeight;

    public static XnaLayoutReader CreateReader() =>
        new(ThemeAssets.TextureSize, RenderWidth, RenderHeight) { MeasureText = ThemeFonts.Measure };

    /// <summary>A code-created control.</summary>
    public static LayoutControl Add(LayoutControl parent, string name, string type, int x, int y, int width, int height, string text = null)
    {
        var control = new LayoutControl(name, type, XnaLayoutReader.KindOf(type))
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Text = text ?? string.Empty,
        };

        parent.AddChild(control);
        return control;
    }

    /// <summary>
    /// Applies the window's INI and builds it; <paramref name="overlays"/> make the interactive controls by name.
    /// </summary>
    /// <param name="readIni">False when the screen has read the window's INI itself (and initialized the controls).</param>
    public static Canvas Build(LayoutControl window, Action<string, LayoutControl> onButton,
        IReadOnlyDictionary<string, Func<LayoutControl, Control>> overlays, bool readIni = true)
    {
        if (readIni)
        {
            XnaLayoutReader reader = CreateReader();

            string iniPath = XnaLayoutReader.FindWindowIni(window.Name);
            if (iniPath != null)
                reader.ReadWindow(new CCIniFile(iniPath), window);

            foreach (LayoutControl control in All(window))
                reader.Initialize(control);
        }

        Canvas root = LayoutView.Build(window, name => onButton(name, window.Find(name)),
            control => overlays.ContainsKey(control.Name));

        foreach (LayoutControl layout in All(window).Where(c => overlays.ContainsKey(c.Name) && c.Visible))
        {
            Control control = overlays[layout.Name](layout);
            if (control == null)
                continue;

            Canvas.SetLeft(control, layout.X);
            Canvas.SetTop(control, layout.Y);
            control.ZIndex = 1000;

            Canvas parent = layout.Parent?.Parent == null ? root : LayoutView.FindNamed<Canvas>(root, layout.Parent.Name) ?? root;
            parent.Children.Add(control);
        }

        return root;
    }

    public static IEnumerable<LayoutControl> All(LayoutControl control) => control.Children.SelectMany(c => All(c).Prepend(c));

    /// <summary>A chat list (ChatListBox) bound to a view model's Messages.</summary>
    public static ListBox ChatList(LayoutControl layout, object dataContext)
    {
        ListBox list = ThemedStyle.List(layout.Width, layout.Height);
        list.DataContext = dataContext;
        list.Background = layout.SolidBackground is { } background ? new SolidColorBrush(ThemeAssets.ToColor(background)) : ThemedStyle.PanelBackground;
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding("Messages"));
        list.ItemTemplate = new FuncDataTemplate<ChatLineViewModel>((line, _) =>
        {
            var text = new TextBlock
            {
                Text = line?.Text,
                Foreground = line?.Brush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 0),
            };
            if (dataContext is PrivateMessagesViewModel or CnCNetLobbyViewModel)
                text.PointerPressed += (_, e) =>
                {
                    if (line?.Message != null && e.GetCurrentPoint(text).Properties.IsRightButtonPressed)
                    {
                        list.SelectedItem = line;
                        PlayerMessageContextMenu.Open(text, e.GetPosition(text), line.Message);
                        e.Handled = true;
                    }
                };
            return text;
        });
        list.Resources["ListBoxItemPadding"] = new Thickness(0);
        ChatScroll.SetFollowNewItems(list, true);
        OpenLinkOnDoubleClick(list);
        return list;
    }

    /// <summary>
    /// ChatListBox_DoubleLeftClick: double-clicking a chat line with one link opens it (trusted domains at once, others
    /// after a warning).
    /// </summary>
    public static void OpenLinkOnDoubleClick(ListBox list) => list.DoubleTapped += (_, _) =>
    {
        if (list.SelectedItem is not ChatLineViewModel line)
            return;

        string[] links = line.Text?.GetLinks();
        if (links == null || links.Length != 1)
            return;

        ClientLogic.UI.LinkOpener.OpenLink(links[0],
            (ClientLogic.UI.IDialogService)App.Services.GetService(typeof(ClientLogic.UI.IDialogService)));
    };

    /// <summary>A chat input bound to a view model's ChatInput, sending with Enter.</summary>
    public static TextBox ChatInput(LayoutControl layout, object dataContext, System.Windows.Input.ICommand send, string suggestion = "Type here to chat...")
    {
        TextBox textBox = ThemedStyle.TextBox(layout.Width, layout.Height, suggestion);
        textBox.DataContext = dataContext;
        textBox.Bind(TextBox.TextProperty, new Binding("ChatInput") { Mode = BindingMode.TwoWay });
        textBox.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Enter), Command = send });
        return textBox;
    }

    /// <summary>A plain list (players, games) with the layout's background.</summary>
    public static ListBox List(LayoutControl layout, object dataContext, string itemsPath, IDataTemplate template = null)
    {
        ListBox list = ThemedStyle.List(layout.Width, layout.Height);
        list.DataContext = dataContext;
        list.Background = layout.SolidBackground is { } background ? new SolidColorBrush(ThemeAssets.ToColor(background)) : ThemedStyle.PanelBackground;
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(itemsPath));
        list.ItemTemplate = template ?? new FuncDataTemplate<object>((item, _) => new TextBlock { Text = item?.ToString(), Margin = new Thickness(4, 1) });
        list.Resources["ListBoxItemPadding"] = new Thickness(0);

        // XNAListBox: the focus colour behind the selected item, nothing on hover
        var focus = new SolidColorBrush(ThemeAssets.ListFocusColor);
        list.Resources["ListBoxItemBackgroundSelected"] = focus;
        list.Resources["ListBoxItemBackgroundSelectedPointerOver"] = focus;
        list.Resources["ListBoxItemBackgroundSelectedPressed"] = focus;
        list.Resources["ListBoxItemBackgroundPointerOver"] = Brushes.Transparent;
        list.Resources["ListBoxItemBackgroundPressed"] = Brushes.Transparent;
        return list;
    }

    /// <summary>A game list (CnCNet or LAN): as <see cref="List"/>, with the HoverOnGameColor behind the hovered game.</summary>
    public static ListBox GameList(LayoutControl layout, object dataContext, string itemsPath, IDataTemplate template)
    {
        ListBox list = List(layout, dataContext, itemsPath, template);
        var hover = new SolidColorBrush(ThemeAssets.HoverOnGameColor);
        list.Resources["ListBoxItemBackgroundPointerOver"] = hover;
        list.Resources["ListBoxItemBackgroundPressed"] = hover;
        return list;
    }
}
