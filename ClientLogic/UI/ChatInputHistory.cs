using System.Collections.Generic;

namespace ClientLogic.UI;

/// <summary>The navigation rules of XNAChatTextBox, including stopping at the newest message on Down.</summary>
public sealed class ChatInputHistory
{
    private readonly LinkedList<string> messages = new();
    private LinkedListNode<string> current;

    public void ResetNavigation() => current = null;

    public void Record(string text)
    {
        if (!string.IsNullOrEmpty(text))
            messages.AddFirst(text);
    }

    public string Older()
    {
        current = current == null ? messages.First : current.Next ?? current;
        return current?.Value;
    }

    public string Newer()
    {
        if (current?.Previous != null)
            current = current.Previous;
        return current?.Value;
    }
}
