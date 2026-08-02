using Avalonia;
using Avalonia.Controls;

namespace TanMenu.Wpf.Controls;

/// <summary>
/// The blue TanMenu splash card: the launcher's loading state (spinner + dots) while the first menu
/// build runs on the STA worker.
/// <para>
/// It had a second use — the "关于" overlay, the same card with <see cref="ShowLoading"/> false and a
/// "click anywhere to close" hint in place of the spinner — until Round 5, item 2 folded 关于 into the
/// 设置 page as a card and deleted the overlay. <c>ShowHint</c>/<c>HintText</c>, the hint TextBlock and
/// the mirror that drove them went with it; <see cref="ShowLoading"/> stays because it is what gates
/// the two infinite animations (a hidden control stays attached to the visual tree, so an ungated
/// animation would tick for the whole tray-resident session).
/// </para>
/// </summary>
public partial class SplashPanel : UserControl
{
    /// <summary>True while the first menu build is running: the spinner and the blinking dots. False
    /// stops both animations dead — see the class remarks.</summary>
    public static readonly StyledProperty<bool> ShowLoadingProperty =
        AvaloniaProperty.Register<SplashPanel, bool>(nameof(ShowLoading), defaultValue: true);

    /// <summary>Footer version line, e.g. "TanMenu v0.9.6".</summary>
    public static readonly StyledProperty<string?> VersionTextProperty =
        AvaloniaProperty.Register<SplashPanel, string?>(nameof(VersionText));

    /// <summary>Footer copyright line. The year is computed at render time, never baked in.</summary>
    public static readonly StyledProperty<string?> CopyrightTextProperty =
        AvaloniaProperty.Register<SplashPanel, string?>(nameof(CopyrightText));

    public SplashPanel()
    {
        InitializeComponent();
    }

    public bool ShowLoading
    {
        get => GetValue(ShowLoadingProperty);
        set => SetValue(ShowLoadingProperty, value);
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
}
