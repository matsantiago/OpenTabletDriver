using System.Numerics;
using System.Runtime.InteropServices;

namespace PenPanScroll;

internal readonly record struct KeyboardModifiers(bool Control, bool Shift, bool Option = false);

internal enum MissionAction { Overview, Dismiss, SpaceLeft, SpaceRight }

internal sealed class MissionGesture
{
    private Vector2? start;
    private bool fired;

    public MissionAction? Process(Vector2 raw, bool contact, bool control,
        double scaleX, double scaleY, double threshold)
    {
        if (!contact || !control)
        {
            Reset();
            return null;
        }
        start ??= raw;
        if (fired) return null;
        double x = (raw.X - start.Value.X) * scaleX;
        double y = (raw.Y - start.Value.Y) * scaleY;
        // Dominant axis prevents diagonal noise from selecting two actions.
        MissionAction? action = Math.Abs(x) > Math.Abs(y) && Math.Abs(x) >= threshold
            ? (x > 0 ? MissionAction.SpaceLeft : MissionAction.SpaceRight)
            : y <= -threshold ? MissionAction.Overview
            : y >= threshold ? MissionAction.Dismiss : null;
        if (action != null) fired = true;
        return action;
    }

    public void Reset() { start = null; fired = false; }
}

internal static class MacMissionControl
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const ulong Control = 1UL << 18;

    internal static KeyboardModifiers ReadModifiers()
    {
        if (!OperatingSystem.IsMacOS()) return default;
        // Read physical HID key state. Our synthetic scroll/shortcut events
        // deliberately clear modifier flags and must not affect this decision.
        return new(CGEventSourceKeyState(1, 59) || CGEventSourceKeyState(1, 62),
            CGEventSourceKeyState(1, 56) || CGEventSourceKeyState(1, 60),
            CGEventSourceKeyState(1, 58) || CGEventSourceKeyState(1, 61));
    }

    internal static void Send(MissionAction action)
    {
        if (!OperatingSystem.IsMacOS()) return;
        ushort key = action switch { MissionAction.Dismiss => 53, MissionAction.Overview => 126, MissionAction.SpaceLeft => 123, _ => 124 };
        // Arrow keys carry the numeric-pad and secondary-Fn flags on macOS.
        // Do not synthesize Control up/down: the user is physically holding it.
        ulong flags = action == MissionAction.Dismiss ? 0 : Control | (1UL << 21) | (1UL << 23);
        for (int i = 0; i < 2; i++)
        {
            var ev = CGEventCreateKeyboardEvent(IntPtr.Zero, key, i == 0);
            if (ev == IntPtr.Zero) return;
            try { CGEventSetFlags(ev, flags); CGEventPost(0, ev); }
            finally { CFRelease(ev); }
        }
    }

    internal static void SendScroll(int horizontal, int vertical)
    {
        if (!OperatingSystem.IsMacOS() || (horizontal == 0 && vertical == 0)) return;
        var ev = CGEventCreateScrollWheelEvent2(IntPtr.Zero, 0, 2, -vertical, -horizontal, 0);
        if (ev == IntPtr.Zero) return;
        try
        {
            // The modifier chord selects our gesture, not app zoom/Shift-scroll.
            CGEventSetFlags(ev, 0);
            CGEventPost(0, ev);
        }
        finally { CFRelease(ev); }
    }

    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateScrollWheelEvent2(IntPtr source, int unit, uint axes, int vertical, int horizontal, int third);
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGEventSourceKeyState(int state, ushort key);
    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort key, [MarshalAs(UnmanagedType.I1)] bool down);
    [DllImport(CoreGraphics)] private static extern void CGEventSetFlags(IntPtr ev, ulong flags);
    [DllImport(CoreGraphics)] private static extern void CGEventPost(int location, IntPtr ev);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr value);
}
