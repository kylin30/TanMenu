using Avalonia;
using Avalonia.Controls;

namespace TanMenu.Wpf.Controls;

/// <summary>
/// The blue TanMenu splash card. Used twice: as the launcher's loading state (spinner + dots) while
/// the first menu build runs on the STA worker, and as the "关于" overlay with
/// <see cref="ShowLoading"/> false, which swaps the spinner for a dismiss hint.
/// </summary>
public partial class SplashPanel : UserControl
{
    /// <summary>True for the startup splash (spinner + blinking dots), false for the About panel.</summary>
    public static readonly StyledProperty<bool> ShowLoadingProperty =
        AvaloniaProperty.Register<SplashPanel, bool>(nameof(ShowLoading), defaultValue: true);

    /// <summary>Mirror of <c>!ShowLoading</c>. A plain property rather than a negated binding so the
    /// XAML stays compiled-binding-simple.</summary>
    public static readonly StyledProperty<bool> ShowHintProperty =
        AvaloniaProperty.Register<SplashPanel, bool>(nameof(ShowHint));

    /// <summary>Footer version line, e.g. "TanMenu v0.9.6".</summary>
    public static readonly StyledProperty<string?> VersionTextProperty =
        AvaloniaProperty.Register<SplashPanel, string?>(nameof(VersionText));

    /// <summary>Footer copyright line. The year is computed at render time, never baked in.</summary>
    public static readonly StyledProperty<string?> CopyrightTextProperty =
        AvaloniaProperty.Register<SplashPanel, string?>(nameof(CopyrightText));

    /// <summary>Localized "click anywhere to close" hint, shown only in the About variant.</summary>
    public static readonly StyledProperty<string?> HintTextProperty =
        AvaloniaProperty.Register<SplashPanel, string?>(nameof(HintText));

    public SplashPanel()
    {
        InitializeComponent();
        ShowHint = !ShowLoading;
    }

    public bool ShowLoading
    {
        get => GetValue(ShowLoadingProperty);
        set => SetValue(ShowLoadingProperty, value);
    }

    public bool ShowHint
    {
        get => GetValue(ShowHintProperty);
        private set => SetValue(ShowHintProperty, value);
    }

    public string? VersionText
    {
        get => GetValue(VersionTextProperty);
        set => SetValue(VersionTextProperty, value);
    }

    public string? CopyrightText
    {
        get => GetValue(CopyrightTextProperty);
        set => SetValue(CopyrightTextProperty, value);
    }

    public string? HintText
    {
        get => GetValue(HintTextProperty);
        set => SetValue(HintTextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowLoadingProperty)
            ShowHint = !ShowLoading;
    }
}
