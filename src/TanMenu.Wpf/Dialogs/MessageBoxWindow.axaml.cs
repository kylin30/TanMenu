using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using TanMenu.Core.Services;

namespace TanMenu.Wpf.Dialogs;

/// <summary>The modal alert/confirmation window behind <see cref="MessageBox"/>. Avalonia ships no
/// MessageBox, and a <c>MessageBoxW</c> P/Invoke would not be modal to an Avalonia owner, so this is
/// a real owned window: sized to content, Esc = Cancel/No, Enter = the default button.</summary>
public partial class MessageBoxWindow : Window
{
    /// <summary>Result reported when the window is closed by the title-bar X rather than a button.</summary>
    private MessageBoxResult _escapeResult = MessageBoxResult.Cancel;
    private MessageBoxResult _defaultResult = MessageBoxResult.Ok;
    private bool _closedWithResult;

    public MessageBoxWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    internal void Configure(string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon,
        string? language)
    {
        Title = title;
        this.FindControl<TextBlock>("MessageText")!.Text = message;
        ApplyIcon(icon);
        BuildButtons(buttons, language);
    }

    private void ApplyIcon(MessageBoxIcon icon)
    {
        if (icon == MessageBoxIcon.None)
            return;

        // A glyph on a coloured plate rather than a bitmap: it scales to any DPI and needs no asset.
        // The plate is a SQUARE, not a disc — Round 8, Task 8 deleted the CornerRadius="15" that made
        // it one; MessageBoxWindow.axaml's IconHost remark carries the reason. Contrast does not
        // depend on the app's light/dark variant either way: the fill is one of the four fixed colours
        // below and the glyph is a literal White, neither of them a themed brush.
        var (glyph, color) = icon switch
        {
            MessageBoxIcon.Info => ("i", Color.FromRgb(0x2D, 0x7D, 0xD2)),
            MessageBoxIcon.Question => ("?", Color.FromRgb(0x2D, 0x7D, 0xD2)),
            MessageBoxIcon.Warning => ("!", Color.FromRgb(0xE8, 0x9C, 0x25)),
            MessageBoxIcon.Error => ("✕", Color.FromRgb(0xD6, 0x45, 0x45)),
            _ => (string.Empty, Colors.Transparent),
        };

        var host = this.FindControl<Border>("IconHost")!;
        host.Background = new SolidColorBrush(color);
        host.IsVisible = true;
        this.FindControl<TextBlock>("IconGlyph")!.Text = glyph;
    }

    private void BuildButtons(MessageBoxButtons buttons, string? language)
    {
        var row = this.FindControl<StackPanel>("ButtonRow")!;

        // Order and roles mirror the Win32 message box the app used to show.
        var results = buttons switch
        {
            MessageBoxButtons.OkCancel => new[] { MessageBoxResult.Ok, MessageBoxResult.Cancel },
            MessageBoxButtons.YesNo => new[] { MessageBoxResult.Yes, MessageBoxResult.No },
            MessageBoxButtons.YesNoCancel =>
                new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel },
            _ => new[] { MessageBoxResult.Ok },
        };

        _defaultResult = results[0];
        _escapeResult = buttons switch
        {
            MessageBoxButtons.OkCancel => MessageBoxResult.Cancel,
            MessageBoxButtons.YesNo => MessageBoxResult.No,
            MessageBoxButtons.YesNoCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.Ok, // an OK-only box has nothing to cancel to
        };

        foreach (var result in results)
        {
            // No Theme is set, deliberately: that is what puts these on whatever ControlTheme the
            // application publishes for {x:Type Button}, which since Round 7 Task 3 is the vendored
            // Aero one. MessageBoxWindow.axaml's remark carries the measurement and the reason.
            var button = new Button
            {
                Content = Label(result, language),
                MinWidth = 84,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                IsDefault = result == _defaultResult,
                IsCancel = result == _escapeResult && results.Length > 1,
            };
            var captured = result;
            button.Click += (_, _) => CloseWith(captured);
            row.Children.Add(button);
        }
    }

    /// <summary>Localized button captions. <c>Ok</c>/<c>Cancel</c> come from the shared table;
    /// <c>Yes</c>/<c>No</c> have no keys there (the WPF build got them from the OS message box) and
    /// TanMenu.Core is out of scope for this change, so they are resolved locally against the same
    /// language rule.</summary>
    private static string Label(MessageBoxResult result, string? language) => result switch
    {
        MessageBoxResult.Ok => AppLanguage.Text("Ok", language),
        MessageBoxResult.Cancel => AppLanguage.Text("Cancel", language),
        MessageBoxResult.Yes => AppLanguage.Resolve(language) == AppLanguage.ZhHans ? "是" : "Yes",
        _ => AppLanguage.Resolve(language) == AppLanguage.ZhHans ? "否" : "No",
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // IsDefault/IsCancel already cover Enter and Esc when a cancel-role button exists; this also
        // gives an OK-only box a working Esc, which is what the Win32 box did.
        if (e.Key == Key.Escape)
        {
            CloseWith(_escapeResult);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            CloseWith(_defaultResult);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Closed by the title-bar X: report the same answer Esc would have given, so a caller can
        // never see a "confirmed" result it did not get.
        if (!_closedWithResult)
            Result = _escapeResult;
        base.OnClosing(e);
    }

    /// <summary>The answer, also used by the ownerless code path where there is no ShowDialog task.</summary>
    internal MessageBoxResult Result { get; private set; } = MessageBoxResult.Cancel;

    private void CloseWith(MessageBoxResult result)
    {
        _closedWithResult = true;
        Result = result;
        Close(result);
    }
}
