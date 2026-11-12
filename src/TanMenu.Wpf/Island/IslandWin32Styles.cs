namespace TanMenu.Wpf.Island;

/// <summary>
/// Extended styles for the preview host. The whole-window transparent extended style is intentionally
/// absent: it would make the capsule itself unhoverable.
/// </summary>
internal static class IslandWin32Styles
{
    public const uint WsExNoActivate = 0x08000000;
    public const uint WsExToolWindow = 0x00000080;
    public const uint WsExTopmost = 0x00000008;
    public const uint WmNcHitTest = 0x0084;
    public const uint WmMouseActivate = 0x0021;
    public static readonly nint HtTransparent = -1;
    public static readonly nint MaNoActivate = 3;

    public static uint PreviewExtendedStyle => WsExNoActivate | WsExToolWindow | WsExTopmost;
}
