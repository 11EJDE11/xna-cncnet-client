using System.Linq;

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
        return new ChatLineViewModel(WithoutControlCharacters(text), new SolidColorBrush(color), message);
    }

    /// <summary>
    /// IRC formatting codes (colour 0x03, bold 0x02, reset 0x0F...) and other control characters, which the XNA font
    /// renderer draws nothing for and Avalonia would draw as boxes; the text around them stays.
    /// </summary>
    private static string WithoutControlCharacters(string text)
    {
        if (text == null || !text.Any(IsHidden))
            return text;

        return new string(text.Where(c => !IsHidden(c)).ToArray());
    }

    private static bool IsHidden(char c) => char.IsControl(c) && c != '\n';
}
