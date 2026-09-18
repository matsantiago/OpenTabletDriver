using System.Numerics;
using OpenTabletDriver;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace PenPanScroll;

[PluginName("Pen Pan/Scroll")]
public sealed class PenPanScrollFilter : IPositionedPipelineElement<IDeviceReport>, IDisposable
{
    private readonly object sync = new();
    private readonly Gesture gesture = new();
    private IMouseScrollHandler? scroll;
    private double scaleX, scaleY;
    private bool disposed;
    private int buttonNumber = 1;
    private double sensitivityX = 10, sensitivityY = 10;

    [Resolved]
    public IDriver? Driver { get; set; }

    [TabletReference]
    public TabletReference? Tablet { get; set; }

    [Property("Scroll button"), DefaultPropertyValue(1),
     ToolTip("One-based pen button number shown by the tablet debugger. This button is consumed while the filter is enabled.")]
    public int ButtonNumber { get => buttonNumber; set => buttonNumber = Math.Clamp(value, 1, 32); }

    [Property("Horizontal sensitivity"), DefaultPropertyValue(10.0), Unit("px/mm"),
     ToolTip("Scroll pixels per millimetre of pen movement. Set to zero to disable horizontal scrolling.")]
    public double SensitivityX { get => sensitivityX; set => sensitivityX = ValidateSensitivity(value); }

    [Property("Vertical sensitivity"), DefaultPropertyValue(10.0), Unit("px/mm")]
    public double SensitivityY { get => sensitivityY; set => sensitivityY = ValidateSensitivity(value); }

    [Property("Reverse horizontal"), DefaultPropertyValue(false)]
    public bool ReverseX { get; set; }

    [Property("Reverse vertical"), DefaultPropertyValue(false)]
    public bool ReverseY { get; set; }

    public PipelinePosition Position => PipelinePosition.PreTransform;
    public event Action<IDeviceReport>? Emit;

    [OnDependencyLoad]
    public void Initialize()
    {
        // OTD 0.6.7 supplies pointer services to bindings, but not to filters.
        // Resolve the same pointer from the exact input tree instead of creating
        // another native mouse with independent button and pressure state.
        var tree = (Driver as Driver)?.InputDevices.FirstOrDefault(
            device => ReferenceEquals(device.Properties, Tablet?.Properties));
        if (OperatingSystem.IsMacOS() && tree?.OutputMode is AbsoluteOutputMode output && Tablet != null)
            Configure(Tablet, output);

        if (scroll == null)
            Log.Write("Pen Pan/Scroll", "Inactive: requires macOS, Absolute Mode, and a scroll-capable pointer. Input is passed through.", LogLevel.Error);
        else
            Log.Write("Pen Pan/Scroll", $"Ready: pen button {ButtonNumber}, X={SensitivityX}, Y={SensitivityY} px/mm.");
    }

    internal void Configure(TabletReference tablet, AbsoluteOutputMode output)
    {
        var digitizer = tablet.Properties.Specifications.Digitizer;
        if (digitizer == null || digitizer.MaxX == 0 || digitizer.MaxY == 0 ||
            !float.IsFinite(digitizer.Width) || !float.IsFinite(digitizer.Height) ||
            digitizer.Width <= 0 || digitizer.Height <= 0)
            return;
        scroll = output.Pointer as IMouseScrollHandler;
        scaleX = (double)digitizer.Width / digitizer.MaxX;
        scaleY = (double)digitizer.Height / digitizer.MaxY;
    }

    public void Consume(IDeviceReport report)
    {
        lock (sync)
        {
            if (disposed || scroll == null)
            {
                Emit?.Invoke(report);
                return;
            }

            if (report is OutOfRangeReport || report is IProximityReport { NearProximity: false })
            {
                gesture.Reset();
                Emit?.Invoke(report);
                return;
            }

            if (report is not ITabletReport pen)
            {
                Emit?.Invoke(report);
                return;
            }

            if (report is IEraserReport { Eraser: true } ||
                !float.IsFinite(pen.Position.X) || !float.IsFinite(pen.Position.Y) ||
                ButtonNumber > pen.PenButtons.Length)
            {
                gesture.Reset();
                Emit?.Invoke(report);
                return;
            }

            var result = gesture.Process(pen.Position, pen.Pressure > 0,
                pen.PenButtons[ButtonNumber - 1], scaleX, scaleY,
                SensitivityX, SensitivityY, ReverseX, ReverseY);

            pen.Position = result.Position;
            if (result.SuppressTip)
                pen.Pressure = 0;
            if (result.SuppressButton)
            {
                // Do not mutate arrays shared with the parser/debugger.
                pen.PenButtons = (bool[])pen.PenButtons.Clone();
                pen.PenButtons[ButtonNumber - 1] = false;
            }

            if (result.Horizontal != 0)
                scroll.ScrollHorizontally(result.Horizontal);
            if (result.Vertical != 0)
                scroll.ScrollVertically(result.Vertical);

            // The normal output stage releases the nib, positions the pointer,
            // and flushes both scroll axes together in its usual report cycle.
            Emit?.Invoke(report);
        }
    }

    private static double ValidateSensitivity(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1000) : 10;

    public void Dispose()
    {
        lock (sync)
        {
            // OTD disposes the output pipeline on disconnect and settings apply.
            disposed = true;
            gesture.Reset();
            scroll = null;
        }
    }
}
