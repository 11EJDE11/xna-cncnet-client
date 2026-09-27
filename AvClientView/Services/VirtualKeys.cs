using System.Collections.Generic;

using Avalonia.Input;

namespace AvClientView.Services;

/// <summary>
/// Windows virtual-key codes of Avalonia keys (Avalonia's Key follows WPF's, so this is WPF's KeyInterop table). The XNA
/// client's Keys values are these codes, and the game's hotkeys use them.
/// </summary>
public static class VirtualKeys
{
    private static readonly Dictionary<Key, int> map = Build();

    private static Dictionary<Key, int> Build()
    {
        var m = new Dictionary<Key, int>
        {
            [Key.Cancel] = 0x03, [Key.Back] = 0x08, [Key.Tab] = 0x09, [Key.Clear] = 0x0C, [Key.Return] = 0x0D,
            [Key.Pause] = 0x13, [Key.CapsLock] = 0x14, [Key.KanaMode] = 0x15, [Key.JunjaMode] = 0x17, [Key.FinalMode] = 0x18,
            [Key.KanjiMode] = 0x19, [Key.Escape] = 0x1B, [Key.ImeConvert] = 0x1C, [Key.ImeNonConvert] = 0x1D,
            [Key.ImeAccept] = 0x1E, [Key.ImeModeChange] = 0x1F, [Key.Space] = 0x20, [Key.PageUp] = 0x21, [Key.PageDown] = 0x22,
            [Key.End] = 0x23, [Key.Home] = 0x24, [Key.Left] = 0x25, [Key.Up] = 0x26, [Key.Right] = 0x27, [Key.Down] = 0x28,
            [Key.Select] = 0x29, [Key.Print] = 0x2A, [Key.Execute] = 0x2B, [Key.PrintScreen] = 0x2C, [Key.Insert] = 0x2D,
            [Key.Delete] = 0x2E, [Key.Help] = 0x2F, [Key.LWin] = 0x5B, [Key.RWin] = 0x5C, [Key.Apps] = 0x5D, [Key.Sleep] = 0x5F,
            [Key.Multiply] = 0x6A, [Key.Add] = 0x6B, [Key.Separator] = 0x6C, [Key.Subtract] = 0x6D, [Key.Decimal] = 0x6E,
            [Key.Divide] = 0x6F, [Key.NumLock] = 0x90, [Key.Scroll] = 0x91,
            [Key.LeftShift] = 0xA0, [Key.RightShift] = 0xA1, [Key.LeftCtrl] = 0xA2, [Key.RightCtrl] = 0xA3,
            [Key.LeftAlt] = 0xA4, [Key.RightAlt] = 0xA5,
            [Key.BrowserBack] = 0xA6, [Key.BrowserForward] = 0xA7, [Key.BrowserRefresh] = 0xA8, [Key.BrowserStop] = 0xA9,
            [Key.BrowserSearch] = 0xAA, [Key.BrowserFavorites] = 0xAB, [Key.BrowserHome] = 0xAC, [Key.VolumeMute] = 0xAD,
            [Key.VolumeDown] = 0xAE, [Key.VolumeUp] = 0xAF, [Key.MediaNextTrack] = 0xB0, [Key.MediaPreviousTrack] = 0xB1,
            [Key.MediaStop] = 0xB2, [Key.MediaPlayPause] = 0xB3, [Key.LaunchMail] = 0xB4, [Key.SelectMedia] = 0xB5,
            [Key.LaunchApplication1] = 0xB6, [Key.LaunchApplication2] = 0xB7,
            [Key.OemSemicolon] = 0xBA, [Key.OemPlus] = 0xBB, [Key.OemComma] = 0xBC, [Key.OemMinus] = 0xBD,
            [Key.OemPeriod] = 0xBE, [Key.OemQuestion] = 0xBF, [Key.OemTilde] = 0xC0, [Key.AbntC1] = 0xC1, [Key.AbntC2] = 0xC2,
            [Key.OemOpenBrackets] = 0xDB, [Key.OemPipe] = 0xDC, [Key.OemCloseBrackets] = 0xDD, [Key.OemQuotes] = 0xDE,
            [Key.Oem8] = 0xDF, [Key.OemBackslash] = 0xE2, [Key.ImeProcessed] = 0xE5, [Key.OemAttn] = 0xF0,
            [Key.OemFinish] = 0xF1, [Key.OemCopy] = 0xF2, [Key.OemAuto] = 0xF3, [Key.OemEnlw] = 0xF4, [Key.OemBackTab] = 0xF5,
            [Key.Attn] = 0xF6, [Key.CrSel] = 0xF7, [Key.ExSel] = 0xF8, [Key.EraseEof] = 0xF9, [Key.Play] = 0xFA,
            [Key.Zoom] = 0xFB, [Key.Pa1] = 0xFD, [Key.OemClear] = 0xFE,
        };

        for (int i = 0; i <= 9; i++)
        {
            m[Key.D0 + i] = 0x30 + i;
            m[Key.NumPad0 + i] = 0x60 + i;
        }

        for (int i = 0; i < 26; i++)
            m[Key.A + i] = 0x41 + i;

        for (int i = 0; i < 24; i++)
            m[Key.F1 + i] = 0x70 + i;

        return m;
    }

    /// <summary>The virtual-key code, or 0 if the key has none.</summary>
    public static int Of(Key key) => map.TryGetValue(key, out int code) ? code : 0;
}
