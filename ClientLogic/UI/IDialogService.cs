using System;

namespace ClientLogic.UI;

/// <summary>Shows message boxes on the front end.</summary>
public interface IDialogService
{
    /// <summary>Shows a message with an OK button.</summary>
    void ShowMessage(string title, string text);

    /// <summary>Asks a yes/no question and runs <paramref name="onYes"/> if the user answers yes.</summary>
    void Confirm(string title, string text, Action onYes);
}
