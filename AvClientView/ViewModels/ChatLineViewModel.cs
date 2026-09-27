using Avalonia.Media;

using DTAClient.Online;

namespace AvClientView.ViewModels;

/// <summary>A line of a chat list.</summary>
public sealed record ChatLineViewModel(string Text, IBrush Brush, ChatMessage Message = null)
{
    public static ChatLineViewModel From(ChatMessage message)
    {
        string text = "[" + message.DateTime.ToString("HH:mm") + "] " +
            (string.IsNullOrEmpty(message.SenderName) ? message.Message : message.SenderName + ": " + message.Message);

        var color = Color.FromArgb(message.Color.A, message.Color.R, message.Color.G, message.Color.B);
        return new ChatLineViewModel(text, new SolidColorBrush(color), message);
    }
}
