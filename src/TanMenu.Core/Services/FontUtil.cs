using System.Linq;

namespace TanMenu.Core.Services;

/// <summary>Helpers for the configured UI font.</summary>
public static class FontUtil
{
    /// <summary>
    /// Sanitize a configured font-family name before it is handed to Avalonia's
    /// <c>FontFamily</c>. The settings dropdown is a closed list (built-in + enumerated system
    /// families), so this is a defensive guard — chiefly against a hand-edited
    /// <c>general.fontFamily</c> in config.json. Keeps only letters (incl. CJK), digits, spaces,
    /// hyphen and underscore; every other character (quotes, punctuation, brackets, <c>#</c>,
    /// <c>:</c>, <c>/</c>, …) is dropped, so a junk value degrades to a harmless family token
    /// rather than a name Avalonia would have to interpret. Returns "" for blank input.
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
