using Avalonia.Input;

namespace TanMenu.Wpf.Services;

/// <summary>Parses/formats global-hotkey combo strings like "Ctrl+Alt+Space" — the bridge between
/// the human-readable config value, the settings capture UI, and Win32 RegisterHotKey.</summary>
/// <remarks>
/// The combo string is a stored config value, so the vocabulary below ("Ctrl", "Esc", "Num3", "`", …)
/// is a compatibility contract: an existing config must keep binding to the same physical key.
///
/// Under WPF this mapped name → <c>System.Windows.Input.Key</c> → VK via <c>KeyInterop</c>. Avalonia
/// has no equivalent (and its <see cref="Key"/> values are not virtual-key codes), so parsing now maps
/// the name straight to the Win32 VK that RegisterHotKey wants, and <see cref="Key"/> is used only on
/// the capture side where a real key event supplies it.
/// </remarks>
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
    public static string? FromKeyEvent(Key key, KeyModifiers modifiers)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None)
            return null;

        if (modifiers == KeyModifiers.None)
            return null;

        var parts = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        // Avalonia reports the Windows key as Meta; the stored vocabulary calls it "Win".
        if (modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Win");

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

        return keyName != null && TryResolveVirtualKey(keyName, out virtualKey);
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

    // Win32 virtual-key codes (winuser.h). Named rather than inlined so the table below reads as a
    // mapping and not as magic numbers.
    private const uint VkBack = 0x08, VkTab = 0x09, VkReturn = 0x0D, VkEscape = 0x1B, VkSpace = 0x20;
    private const uint VkPageUp = 0x21, VkPageDown = 0x22, VkEnd = 0x23, VkHome = 0x24;
    private const uint VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28;
    private const uint VkInsert = 0x2D, VkDelete = 0x2E;
    private const uint VkD0 = 0x30, VkA = 0x41, VkNumPad0 = 0x60, VkF1 = 0x70;
    private const uint VkOem1 = 0xBA;      // ;
    private const uint VkOemPlus = 0xBB;   // =
    private const uint VkOemComma = 0xBC;  // ,
    private const uint VkOemMinus = 0xBD;  // -
    private const uint VkOemPeriod = 0xBE; // .
    private const uint VkOem2 = 0xBF;      // /
    private const uint VkOem3 = 0xC0;      // `
    private const uint VkOem4 = 0xDB;      // [
    private const uint VkOem5 = 0xDC;      // \
    private const uint VkOem6 = 0xDD;      // ]
    private const uint VkOem7 = 0xDE;      // '

    private static bool TryResolveVirtualKey(string name, out uint virtualKey)
    {
        virtualKey = 0;

        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            virtualKey = VkD0 + (uint)(name[0] - '0');
            return true;
        }

        if (name.Length == 4 && name.StartsWith("Num", StringComparison.OrdinalIgnoreCase)
            && char.IsAsciiDigit(name[3]))
        {
            virtualKey = VkNumPad0 + (uint)(name[3] - '0');
            return true;
        }

        if (name.Length == 1 && char.IsAsciiLetter(name[0]))
        {
            virtualKey = VkA + (uint)(char.ToUpperInvariant(name[0]) - 'A');
            return true;
        }

        // F1–F24
        if (name.Length >= 2 && (name[0] is 'F' or 'f') && int.TryParse(name.AsSpan(1), out var fn)
            && fn is >= 1 and <= 24)
        {
            virtualKey = VkF1 + (uint)(fn - 1);
            return true;
        }

        virtualKey = name switch
        {
            "Space" => VkSpace,
            "Enter" => VkReturn,
            "Tab" => VkTab,
            "Backspace" => VkBack,
            "Esc" => VkEscape,
            "`" => VkOem3,
            "-" => VkOemMinus,
            "=" => VkOemPlus,
            "[" => VkOem4,
            "]" => VkOem6,
            ";" => VkOem1,
            "'" => VkOem7,
            "," => VkOemComma,
            "." => VkOemPeriod,
            "/" => VkOem2,
            "\\" => VkOem5,
            "Insert" => VkInsert,
            "Delete" => VkDelete,
            "Home" => VkHome,
            "End" => VkEnd,
            "PageUp" => VkPageUp,
            "PageDown" => VkPageDown,
            "Up" => VkUp,
            "Down" => VkDown,
            "Left" => VkLeft,
            "Right" => VkRight,
            _ => 0,
        };
        return virtualKey != 0;
    }
}
