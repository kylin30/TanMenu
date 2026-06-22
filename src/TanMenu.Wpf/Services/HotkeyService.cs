using System.Runtime.InteropServices;

namespace TanMenu.Wpf.Services;

/// <summary>Registers the global Alt+Space hotkey on a window HWND and identifies WM_HOTKEY.</summary>
public sealed class HotkeyService : IDisposable
{
    public const int WmHotkey = 0x0312;

    private const int Id = 0x9001;
    private const uint ModAlt = 0x0001;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkSpace = 0x20;

    private IntPtr _hwnd;

    public bool Register(IntPtr hwnd)
    {
        _hwnd = hwnd;
        return RegisterHotKey(hwnd, Id, ModAlt | ModNoRepeat, VkSpace);
    }

    public bool IsOurHotkey(IntPtr wParam) => wParam.ToInt32() == Id;

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, Id);
            _hwnd = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
