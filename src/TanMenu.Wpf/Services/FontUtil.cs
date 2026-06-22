using System.Linq;

namespace TanMenu.Wpf.Services;

/// <summary>Helpers for the configured UI font.</summary>
public static class FontUtil
{
    /// <summary>
    /// Sanitize a configured font-family name so it is safe to inline into a CSS
    /// <c>&lt;style&gt;</c> block. The settings dropdown is a closed list (built-in + enumerated
    /// system families), so this is a defensive guard — chiefly against a hand-edited config —
    /// before the value is inlined. Keeps only letters (incl. CJK), digits, spaces, hyphen and
    /// underscore, dropping CSS-significant characters (<c>" ; { } &lt; &gt; \ ( )</c> etc.) that
    /// could otherwise break or hijack the stylesheet. Returns "" for blank input.
    /// </summary>
    public static string Sanitize(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
            return string.Empty;
        var cleaned = new string(family.Where(c =>
            char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_').ToArray());
        return cleaned.Trim();
    }
}
