using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Filee.Platform.Windows;

/// <summary>Windows shell-switcher behavior for utility windows that should not appear as app cards.</summary>
public static class WindowTaskSwitching
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_APPWINDOW = 0x00040000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    /// <summary>Excludes a window from the taskbar, Alt+Tab, and Task View without changing its z-order.</summary>
    [SupportedOSPlatform("windows")]
    public static bool ExcludeFromTaskSwitchers(nint hwnd)
    {
        if (hwnd == 0)
            return false;

        var style = IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64()
            : GetWindowLong32(hwnd, GWL_EXSTYLE);
        var updated = (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        if (updated == style)
            return true;

        Marshal.SetLastPInvokeError(0);
        var previous = IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, GWL_EXSTYLE, (nint)updated).ToInt64()
            : SetWindowLong32(hwnd, GWL_EXSTYLE, (int)updated);
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0)
            return false;

        return SetWindowPos(hwnd, 0, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hwnd, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hwnd, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
}
