using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Filee.Core.Platform;

namespace Filee.Platform.Windows;

/// <summary>Polls UI Automation for focused input bounds only; it never requests names, values, or text.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsFocusedInputWatcher : IDisposable
{
    private static readonly Guid AutomationClassId = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    private static readonly Guid AutomationInterfaceId = new("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE");
    private const uint InprocServer = 1;
    private const int EditControlType = 50004;
    private const int ComboBoxControlType = 50003;
    private const int DocumentControlType = 50030;
    private const int GetFocusedElementSlot = 8;
    private const int CurrentControlTypeSlot = 21;
    private const int CurrentIsKeyboardFocusableSlot = 27;
    private const int CurrentBoundingRectangleSlot = 43;

    private readonly Func<TimeSpan?> _getSystemIdleTime;
    private readonly Action<PlatformScreenBounds?> _callback;
    private readonly Thread _thread;
    private volatile bool _stopping;

    public WindowsFocusedInputWatcher(Func<TimeSpan?> getSystemIdleTime, Action<PlatformScreenBounds?> callback)
    {
        _getSystemIdleTime = getSystemIdleTime;
        _callback = callback;
        _thread = new Thread(Watch)
        {
            IsBackground = true,
            Name = "HOOT focused input bounds",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Watch()
    {
        IntPtr automation = IntPtr.Zero;
        PlatformScreenBounds? previous = null;
        try
        {
            var classId = AutomationClassId;
            var interfaceId = AutomationInterfaceId;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref classId, IntPtr.Zero, InprocServer, ref interfaceId, out automation));

            while (!_stopping)
            {
                PlatformScreenBounds? current = null;
                try
                {
                    if (_getSystemIdleTime() is { } idle && idle <= TimeSpan.FromSeconds(3))
                        current = ReadFocusedInputBounds(automation);
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException
                    or InvalidCastException or ArgumentException)
                {
                    current = null;
                }

                if (current != previous)
                {
                    previous = current;
                    try { _callback(current); }
                    catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { }
                }

                Thread.Sleep(250);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException
            or InvalidCastException or ArgumentException or TypeLoadException)
        {
            try { _callback(null); }
            catch (Exception callbackException) when (callbackException is InvalidOperationException or ObjectDisposedException) { }
        }
        finally
        {
            if (automation != IntPtr.Zero)
                Marshal.Release(automation);
        }
    }

    private static PlatformScreenBounds? ReadFocusedInputBounds(IntPtr automation)
    {
        IntPtr element = IntPtr.Zero;
        try
        {
            var automationVtable = Marshal.ReadIntPtr(automation);
            var getFocusedElement = Marshal.GetDelegateForFunctionPointer<GetFocusedElementDelegate>(
                Marshal.ReadIntPtr(automationVtable, GetFocusedElementSlot * IntPtr.Size));
            Marshal.ThrowExceptionForHR(getFocusedElement(automation, out element));
            if (element == IntPtr.Zero) return null;

            var elementVtable = Marshal.ReadIntPtr(element);
            var getControlType = Marshal.GetDelegateForFunctionPointer<GetIntPropertyDelegate>(
                Marshal.ReadIntPtr(elementVtable, CurrentControlTypeSlot * IntPtr.Size));
            var getKeyboardFocusable = Marshal.GetDelegateForFunctionPointer<GetBoolPropertyDelegate>(
                Marshal.ReadIntPtr(elementVtable, CurrentIsKeyboardFocusableSlot * IntPtr.Size));
            var getBounds = Marshal.GetDelegateForFunctionPointer<GetBoundsPropertyDelegate>(
                Marshal.ReadIntPtr(elementVtable, CurrentBoundingRectangleSlot * IntPtr.Size));

            Marshal.ThrowExceptionForHR(getControlType(element, out var controlType));
            if (controlType is not (EditControlType or ComboBoxControlType or DocumentControlType))
                return null;

            Marshal.ThrowExceptionForHR(getKeyboardFocusable(element, out var keyboardFocusable));
            if (!keyboardFocusable) return null;

            Marshal.ThrowExceptionForHR(getBounds(element, out var bounds));
            if (bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top)
                return null;

            return new PlatformScreenBounds(bounds.Left, bounds.Top,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        }
        finally
        {
            if (element != IntPtr.Zero)
                Marshal.Release(element);
        }
    }

    public void Dispose()
    {
        _stopping = true;
        if (Thread.CurrentThread != _thread)
            _thread.Join(TimeSpan.FromMilliseconds(300));
    }

    [DllImport("ole32.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int CoCreateInstance(ref Guid classId, IntPtr outer, uint classContext,
        ref Guid interfaceId, out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetFocusedElementDelegate(IntPtr self, out IntPtr element);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetIntPropertyDelegate(IntPtr self, out int value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBoolPropertyDelegate(IntPtr self, [MarshalAs(UnmanagedType.Bool)] out bool value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBoundsPropertyDelegate(IntPtr self, out NativeRect bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
