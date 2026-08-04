using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace TanMenu.Wpf.Dialogs;

/// <summary>
/// Makes Windows draw a real DIALOG caption on an Avalonia window: the Close button alone, and no
/// title-bar icon.
/// </summary>
/// <remarks>
/// <para>
/// <b>Round 8, Task 2 measured all of this on <c>OptionsWindow</c> and Task 4 moved it here</b>, because
/// the About box is the same kind of surface — modal, fixed size, owned by the launcher — and would
/// otherwise have needed the same five P/Invokes copied a second time. The measurement below is that
/// task's, taken on the first build of the Options dialog and re-read on this one; nothing about it is
/// specific to which window calls it.
/// </para>
/// <para>
/// PUBLISHED, <c>win-dialog-box</c>: "Resizable dialogs can have a Maximize button", i.e. a fixed one has
/// none; and a modal dialog with a live Minimize box is a window the user can send to the taskbar while
/// its owner is disabled. <b>MEASURED, and it was real:</b> on the first build of the Options dialog,
/// <c>GetWindowLong(GWL_STYLE)</c> read <c>0x16CA0000</c> — <c>WS_MINIMIZEBOX</c> SET,
/// <c>WS_MAXIMIZEBOX</c> clear, <c>WS_THICKFRAME</c> clear — and UIA reported a caption with an ENABLED
/// "最小化" button and a greyed "最大化" beside it. Windows draws both boxes when either style bit is
/// present and greys the absent one, so clearing <c>CanResize</c> alone gets you a dead maximize box and
/// a live minimize box.
/// </para>
/// <para>
/// <b>Avalonia's <c>SystemDecorations</c> is NOT the tool for it</b>, which is the one place this
/// departs from the design's suggested remedy. It has three values (<c>None</c>, <c>BorderOnly</c>,
/// <c>Full</c>); anything below <c>Full</c> removes the whole title bar, which would take the Close
/// button and the window's title with it and break two PUBLISHED rules (<c>win-dialog-box</c>: "Don't
/// disable the Close button"; <c>win-property-win</c>: "Options dialog box titles — Use 'Options.'") to
/// fix one. Clearing the two style bits is what a real Win32 dialog does, and this app already reaches
/// for the same class of tool one window over — <c>MainWindow.RestoreSystemCloseCommand</c> re-enables
/// <c>SC_CLOSE</c> through <c>GetSystemMenu</c>/<c>EnableMenuItem</c> for a defect Avalonia's own
/// client-area extension creates.
/// </para>
/// <para>
/// <b>THE TITLE-BAR ICON IS THE SAME KIND OF FINDING AND IS REMOVED IN THE SAME BLOCK.</b> PUBLISHED,
/// <c>win-dialog-box</c>: "Dialog boxes don't have title bar icons." Setting no <c>Icon</c> on the window
/// is not enough — MEASURED on a <c>PrintWindow</c> capture of the Options dialog's first build, Windows
/// drew the app's own icon left of 选项 regardless, because an owned window with no icon of its own falls
/// back to its owner's. The Win32 answer is the classic pair: add <c>WS_EX_DLGMODALFRAME</c>, which tells
/// Windows this is a modal dialog frame and suppresses the icon slot, and blank both icon slots with
/// <c>WM_SETICON</c> so nothing can be drawn into them. One <c>SWP_FRAMECHANGED</c> commits all three
/// writes at once.
/// </para>
/// <para>
/// Call it from <c>OnOpened</c>: the HWND does not exist before that. <c>SWP_FRAMECHANGED</c> with no
/// move, no size and no z-order change is what makes Windows recompute and repaint the non-client area
/// after the style writes; without it the old caption stays on screen until something else invalidates
/// it. Failure is logged and ignored — a dialog with a spare minimize box is worse-looking, not broken.
/// </para>
/// </remarks>
internal static partial class DialogCaption
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_EX_DLGMODALFRAME = 0x00000001;
    private const uint WM_SETICON = 0x0080;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;

    /// <summary>Strip the minimize and maximize boxes and the title-bar icon from an open window.</summary>
    public static void ApplyTo(Window window)
    {
        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
                return;

            var style = GetWindowLongW(hwnd, GWL_STYLE);
            var exStyle = GetWindowLongW(hwnd, GWL_EXSTYLE);
            var wantedStyle = style & ~(WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
            var wantedExStyle = exStyle | WS_EX_DLGMODALFRAME;

            if (wantedStyle != style)
                SetWindowLongW(hwnd, GWL_STYLE, wantedStyle);
            if (wantedExStyle != exStyle)
                SetWindowLongW(hwnd, GWL_EXSTYLE, wantedExStyle);

            // ICON_SMALL = 0, ICON_BIG = 1. Blanking both is what leaves the caption with nothing to
            // draw once WS_EX_DLGMODALFRAME has removed the slot.
            SendMessageW(hwnd, WM_SETICON, IntPtr.Zero, IntPtr.Zero);
            SendMessageW(hwnd, WM_SETICON, new IntPtr(1), IntPtr.Zero);

            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);

            Serilog.Log.Debug(
                "{Window} caption: style 0x{Before:X8} -> 0x{After:X8}, exstyle 0x{ExBefore:X8} -> " +
                "0x{ExAfter:X8} (minimize/maximize boxes and the title-bar icon removed)",
                window.GetType().Name, style, GetWindowLongW(hwnd, GWL_STYLE), exStyle,
                GetWindowLongW(hwnd, GWL_EXSTYLE));
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to give {Window} a plain dialog caption", window.GetType().Name);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
