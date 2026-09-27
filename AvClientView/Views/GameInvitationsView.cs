using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientCore.Extensions;

using ClientLogic.Layout;

namespace AvClientView.Views;

/// <summary>
/// The open game invitations at the top left, one XNA ChoiceNotificationBox each: "GAME INVITATION", the sender's
/// game icon and name, "Join {0}?", Yes and No.
/// </summary>
public sealed class GameInvitationsView : StackPanel
{
    private const int BOX_WIDTH = 300;
    private const int BOX_HEIGHT = 101;
    private const int BUTTON_WIDTH = 75;
    private const int BUTTON_HEIGHT = 23;

    private readonly GameInvitationsViewModel viewModel;

    public GameInvitationsView(GameInvitationsViewModel viewModel)
    {
        this.viewModel = viewModel;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;

        viewModel.Items.CollectionChanged += (_, _) => Rebuild();
        Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        foreach (GameInvitationItemViewModel item in viewModel.Items)
            Children.Add(Box(item));

        IsVisible = Children.Count > 0;
    }

    private Control Box(GameInvitationItemViewModel item)
    {
        var canvas = new Canvas
        {
            Width = BOX_WIDTH,
            Height = BOX_HEIGHT,
            ClipToBounds = true,
        };

        var box = new Border
        {
            Width = BOX_WIDTH,
            Height = BOX_HEIGHT,
            Background = new SolidColorBrush(Color.FromArgb(196, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor),
            BorderThickness = new Thickness(1),
            Child = canvas,
        };

        int fontHeight = ThemeFonts.Measure("H", 1).Height;

        TextBlock Label(string text, double x, double y)
        {
            (FontFamily family, double size) = ThemeFonts.Get(1);
            var label = new TextBlock { Text = text, FontFamily = family, FontSize = size, Foreground = new SolidColorBrush(ThemeAssets.LabelColor) };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, y);
            canvas.Children.Add(label);
            return label;
        }

        // lblHeader is centred on (Width / 2, 12)
        string header = "GAME INVITATION".L10N("Client:Main:GameInviteTitle");
        int headerTop = 12 - fontHeight / 2;
        Label(header, (BOX_WIDTH - ThemeFonts.Measure(header, 1).Width) / 2, headerTop);
        int senderTop = headerTop + fontHeight + 6;

        var icon = new Image
        {
            Width = 16,
            Height = 16,
            Source = item.SenderGame != null ? ThemeAssets.GameIcon(item.SenderGame) : ThemeAssets.EmbeddedIcon("unknownicon.png"),
        };
        Canvas.SetLeft(icon, 12);
        Canvas.SetTop(icon, senderTop);
        canvas.Children.Add(icon);

        Label(item.Invitation.Sender, 12 + 16 + 3, senderTop);
        int choiceTop = senderTop + fontHeight + 6;

        // the text is clipped at the box's edge, as the XNA box removes characters until it fits
        TextBlock choice = Label(string.Format("Join {0}?".L10N("Client:Main:GameInviteText"), item.Invitation.GameName), 12, choiceTop);
        choice.MaxWidth = BOX_WIDTH - 12;
        choice.TextWrapping = TextWrapping.NoWrap;
        int buttonTop = choiceTop + fontHeight + 6;

        ThemedButton yes = Button("affirmativeButton", "Yes".L10N("Client:Main:ButtonYes"));
        yes.Click += (_, _) => viewModel.Accept(item);
        Canvas.SetLeft(yes, 8);
        Canvas.SetTop(yes, buttonTop);
        canvas.Children.Add(yes);

        ThemedButton no = Button("negativeButton", "No".L10N("Client:Main:ButtonNo"));
        no.Click += (_, _) => viewModel.Decline(item);
        Canvas.SetLeft(no, BOX_WIDTH - (BUTTON_WIDTH + 8));
        Canvas.SetTop(no, buttonTop);
        canvas.Children.Add(no);

        return box;
    }

    private static ThemedButton Button(string name, string text)
    {
        var layout = new LayoutControl(name, "XNAClientButton", LayoutControlKind.Button) { Width = BUTTON_WIDTH, Height = BUTTON_HEIGHT, Text = text };
        ThemedWindow.CreateReader().Initialize(layout);
        return new ThemedButton(layout);
    }
}
