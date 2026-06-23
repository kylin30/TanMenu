using System.Windows.Input;

namespace TanMenu.Wpf.Services;

/// <summary>Parses/formats global-hotkey combo strings like "Ctrl+Alt+Space" — the bridge between
/// the human-readable config value, the settings capture UI, and Win32 RegisterHotKey.</summary>
public static class HotkeyParser
{
    // RegisterHotKey modifier flags.
    public const uint MOD_ALT = 0x1;
    public const uint MOD_CONTROL = 0x2;
    public const uint MOD_SHIFT = 0x4;
    public const uint MOD_WIN = 0x8;
    public const uint MOD_NOREPEAT = 0x4000;

    /// <summary>Build a combo string from a captured key + modifiers. Returns null when the key is
    /// itself just a modifier (user still composing) or there is no modifier held (we require at
    /// least one modifier so a bare key can't hijack the keyboard globally).</summary>
    public static string? FromKeyEvent(Key key, ModifierKeys modifiers)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None)
            return null;

        if (modifiers == ModifierKeys.None)
            return null;

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        var friendly = FriendlyKeyName(key);
        if (friendly == null) return null;
        parts.Add(friendly);
        return string.Join("+", parts);
    }

    /// <summary>Parse a combo string into RegisterHotKey (modifiers, virtualKey). False if invalid.</summary>
    public static bool TryParse(string? combo, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(combo))
            return false;

        string? keyName = null;
        foreach (var raw in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win": case "windows": case "meta": modifiers |= MOD_WIN; break;
                default: keyName = raw; break;
            }
        }
        if (keyName == null || !TryResolveKey(keyName, out var key))
            return false;

        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }

    private static string? FriendlyKeyName(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9) return ((int)(key - Key.D0)).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return "Num" + (int)(key - Key.NumPad0);
        if (key >= Key.A && key <= Key.Z) return key.ToString();
        if (key >= Key.F1 && key <= Key.F24) return key.ToString();
        return key switch
        {
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.Back => "Backspace",
            Key.Escape => "Esc",
            Key.OemTilde => "`",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemBackslash or Key.OemPipe => "\\",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            _ => null,
        };
    }

    private static bool TryResolveKey(string name, out Key key)
    {
        key = Key.None;
        if (name.Length == 1 && char.IsDigit(name[0])) { key = Key.D0 + (name[0] - '0'); return true; }
        if (name.Length == 4 && name.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && char.IsDigit(name[3]))
        { key = Key.NumPad0 + (name[3] - '0'); return true; }
        switch (name)
        {
            case "Space": key = Key.Space; return true;
            case "Enter": key = Key.Enter; return true;
            case "Tab": key = Key.Tab; return true;
            case "Backspace": key = Key.Back; return true;
            case "Esc": key = Key.Escape; return true;
            case "`": key = Key.OemTilde; return true;
            case "-": key = Key.OemMinus; return true;
            case "=": key = Key.OemPlus; return true;
            case "[": key = Key.OemOpenBrackets; return true;
            case "]": key = Key.OemCloseBrackets; return true;
            case ";": key = Key.OemSemicolon; return true;
            case "'": key = Key.OemQuotes; return true;
            case ",": key = Key.OemComma; return true;
            case ".": key = Key.OemPeriod; return true;
            case "/": key = Key.OemQuestion; return true;
            case "\\": key = Key.OemBackslash; return true;
            case "Insert": key = Key.Insert; return true;
            case "Delete": key = Key.Delete; return true;
            case "Home": key = Key.Home; return true;
            case "End": key = Key.End; return true;
            case "PageUp": key = Key.PageUp; return true;
            case "PageDown": key = Key.PageDown; return true;
            case "Up": key = Key.Up; return true;
            case "Down": key = Key.Down; return true;
            case "Left": key = Key.Left; return true;
            case "Right": key = Key.Right; return true;
        }
        return Enum.TryParse(name, ignoreCase: true, out key) && key != Key.None; // A-Z, F1-F24
    }
}
