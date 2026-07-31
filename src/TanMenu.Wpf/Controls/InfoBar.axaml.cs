using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Interactivity;
using Avalonia.Metadata;

namespace TanMenu.Wpf.Controls;

/// <summary>An informational bar the page can raise inline instead of showing a modal dialog. The host
/// owns the actions (they go in <see cref="ActionContent"/>, deliberately NOT the control's own
/// Content, which for a UserControl is its XAML root); this control owns the surface, the icon, the
/// message and the close glyph.</summary>
public partial class InfoBar : UserControl
{
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<InfoBar, string?>(nameof(Message));

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<InfoBar, bool>(nameof(IsOpen));

    public static readonly StyledProperty<object?> ActionContentProperty =
        AvaloniaProperty.Register<InfoBar, object?>(nameof(ActionContent));

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>Buttons the host wants on the bar's trailing edge.</summary>
    [Content]
    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    /// <summary>Raised when the user clicks the close glyph. The host decides what dismissal means —
    /// this control never hides itself, so a host that persists the decision stays in control of it.</summary>
    public event EventHandler? Dismissed;

    public InfoBar()
    {
        InitializeComponent();

        // Wired here rather than with RelativeSource bindings in XAML: compiled bindings are on by
        // default and this control has no x:DataType to resolve them against.
        MessageText.Bind(TextBlock.TextProperty, this.GetObservable(MessageProperty));
        Surface.Bind(IsVisibleProperty, this.GetObservable(IsOpenProperty));
        ActionHost.Bind(ContentPresenter.ContentProperty, this.GetObservable(ActionContentProperty));
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Dismissed?.Invoke(this, EventArgs.Empty);
}
