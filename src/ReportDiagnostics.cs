using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenTabletDriver.Plugin;

namespace PenPanScroll;

internal sealed class ReportDiagnostics
{
    private double lastSample = double.NegativeInfinity;
    private double lastSpike = double.NegativeInfinity;

    internal void Trace(string stage, string details, Vector2? delta = null)
    {
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        bool spike = delta is { } d && (!float.IsFinite(d.X) || !float.IsFinite(d.Y) || d.Length() > 200);
        if (now - lastSample < 1 && (!spike || now - lastSpike < 0.25)) return;
        lastSample = now;
        if (spike) lastSpike = now;
        Log.Write("Cursor diagnostics", $"{stage} {(spike ? "LARGE-DELTA " : "")}{details}; {CursorSnapshot()}");
    }

    private static string CursorSnapshot()
    {
        if (!OperatingSystem.IsMacOS()) return "cursor=unavailable";
        var ev = CGEventCreate(IntPtr.Zero);
        if (ev == IntPtr.Zero) return "cursor=unavailable";
        try
        {
            var point = CGEventGetLocation(ev);
            return $"cursor=({point.X:F1},{point.Y:F1}) visible={CGCursorIsVisible()}";
        }
        finally { CFRelease(ev); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public double X, Y; }
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    [DllImport(CG)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CG)] private static extern Point CGEventGetLocation(IntPtr ev);
    // Deprecated, without a replacement; diagnostic observation only.
    [DllImport(CG)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGCursorIsVisible();
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr ev);
}
