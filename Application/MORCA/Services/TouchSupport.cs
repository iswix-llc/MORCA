using System.Runtime.InteropServices;

namespace MORCA.Services;

/// <summary>Whether this device has a touch screen, asked of the operating system rather than the web view.</summary>
public static class TouchSupport
{
    public static bool HasTouchScreen { get; } = Detect();

    private static bool Detect()
    {
#if WINDOWS
        try
        {
            // SM_DIGITIZER: NID_READY (0x80) plus an integrated (0x01) or external (0x02) touch digitizer.
            // Pen-only digitizers and drivers without a screen do not set those bits.
            var digitizer = GetSystemMetrics(94);
            return (digitizer & 0x80) != 0 && (digitizer & 0x03) != 0;
        }
        catch
        {
            return false;
        }
#elif ANDROID || IOS
        return true;
#else
        return false;
#endif
    }

#if WINDOWS
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
#endif
}
