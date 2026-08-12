using Avalonia;
using Avalonia.Styling;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Services;

/// <summary>Maps TanMenu's appearance setting directly onto Avalonia's official FluentTheme.</summary>
public static class AppTheme
{
    /// <summary><see cref="ThemeVariant.Default"/> follows the operating-system setting.</summary>
    public static ThemeVariant ToVariant(string? appearance) =>
        AppAppearance.NormalizeSetting(appearance) switch
        {
            AppAppearance.Light => ThemeVariant.Light,
            AppAppearance.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

    /// <summary>Apply the configured variant without overriding any resources owned by FluentTheme.</summary>
    public static void Apply(Application app, string? appearance) =>
        app.RequestedThemeVariant = ToVariant(appearance);
}
