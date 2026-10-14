using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace PenPanScroll;

[PluginName("Relative Pointer Acceleration")]
public sealed class PointerAccelerationFilter : IPositionedPipelineElement<IDeviceReport>, IDisposable
{
    private RelativeOutputMode? output;
    private readonly AccelerationCurve curve = new();
    private readonly ReportDiagnostics diagnostics = new();
    private double startSpeed = 20, fullSpeed = 150, maxGain = 2, smoothing = 20;
    private bool disposed;
    private double travelDistance = 3, travelWindow = 100;
    internal Func<double> Clock { get; set; } = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    [Resolved] public IDriver? Driver { get; set; }
    [TabletReference] public TabletReference? Tablet { get; set; }

    [Property("Acceleration starts at"), DefaultPropertyValue(20.0), Unit("mm/s")]
    public double StartSpeed { get => startSpeed; set => startSpeed = Valid(value, 0, 5000, 20); }

    [Property("Maximum gain at"), DefaultPropertyValue(150.0), Unit("mm/s"),
     ToolTip("Must exceed Acceleration starts at. The curve transitions smoothly between these speeds.")]
    public double FullSpeed { get => fullSpeed; set => fullSpeed = Valid(value, 1, 10000, 150); }

    [Property("Maximum gain"), DefaultPropertyValue(2.0), Unit("×"),
     ToolTip("Multiplier applied to Relative Mode sensitivity at high speed. 1 disables acceleration.")]
    public double MaxGain { get => maxGain; set => maxGain = Valid(value, 1, 10, 2); }

    [Property("Speed smoothing"), DefaultPropertyValue(20.0), Unit("ms"),
     ToolTip("Smooths the speed estimate, without delaying cursor samples. Zero uses instantaneous speed.")]
    public double Smoothing { get => smoothing; set => smoothing = Valid(value, 0, 200, 20); }

    [Property("Travel before acceleration"), DefaultPropertyValue(3.0), Unit("mm"),
     ToolTip("Short movements stay at base sensitivity. Gain ramps between this distance and twice it, using net movement in the motion window. Zero uses speed alone.")]
    public double TravelDistance { get => travelDistance; set => travelDistance = Valid(value, 0, 50, 3); }

    [Property("Motion window"), DefaultPropertyValue(100.0), Unit("ms"),
     ToolTip("Window for net physical displacement. Back-and-forth motion cancels instead of accumulating distance.")]
    public double TravelWindow { get => travelWindow; set => travelWindow = Valid(value, 10, 500, 100); }

    [Property("Diagnostic logging"), DefaultPropertyValue(false),
     ToolTip("Logs relative delta before/after acceleration and cursor state, including unusually large deltas.")]
    public bool DiagnosticLogging { get; set; }

    public PipelinePosition Position => PipelinePosition.PostTransform;
    public event Action<IDeviceReport?>? Emit;

    [OnDependencyLoad]
    public void Initialize()
    {
        var tree = (Driver as Driver)?.InputDevices.FirstOrDefault(
            device => ReferenceEquals(device.Properties, Tablet?.Properties));
        Configure(tree?.OutputMode as RelativeOutputMode);
        Log.Write("Pointer Acceleration", output == null
            ? "Inactive: requires Relative Mode. Input passed through."
            : $"Ready: {StartSpeed}–{FullSpeed} mm/s, max {MaxGain}x, smoothing {Smoothing} ms.");
    }

    internal void Configure(RelativeOutputMode? mode) { output = mode; curve.Reset(); }

    public void Consume(IDeviceReport? report)
    {
        if (!disposed && output != null)
        {
            if (report is OutOfRangeReport || report is IProximityReport { NearProximity: false })
                curve.Reset();
            else if (report is IAbsolutePositionReport position)
            {
                var inputDelta = position.Position;
                // Undo OTD's scale/rotation only to measure physical speed. Apply
                // one isotropic gain to the original delta, preserving direction.
                var digitizer = output.Tablet?.Properties.Specifications.Digitizer;
                if (digitizer != null && Matrix3x2.Invert(output.TransformationMatrix, out var inverse))
                {
                    var raw = Vector2.TransformNormal(position.Position, inverse);
                    var mm = new Vector2(raw.X * digitizer.Width / digitizer.MaxX,
                        raw.Y * digitizer.Height / digitizer.MaxY);
                    position.Position = curve.Apply(position.Position, mm, Clock(),
                        StartSpeed, FullSpeed, MaxGain, Smoothing, output.ResetTime.TotalSeconds, TravelDistance, TravelWindow / 1000);
                }
                else curve.Reset();
                if (DiagnosticLogging)
                    diagnostics.Trace("output", $"deltaIn={inputDelta} deltaOut={position.Position}", position.Position);
            }
        }
        Emit?.Invoke(report);
    }

    private static double Valid(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public void Dispose() { disposed = true; curve.Reset(); output = null; }
}

internal sealed class AccelerationCurve
{
    private double? lastTime;
    private double filteredSpeed;
    private readonly Queue<(double Time, Vector2 Movement)> history = new();
    private Vector2 travel;

    public Vector2 Apply(Vector2 delta, Vector2 mm, double time, double start,
        double full, double maxGain, double smoothingMs, double resetSeconds,
        double travelDistance = 0, double windowSeconds = 0.1)
    {
        var elapsed = time - lastTime;
        lastTime = time;
        if (elapsed == null || elapsed <= 0 || elapsed > resetSeconds ||
            !double.IsFinite(time) || !float.IsFinite(mm.X) || !float.IsFinite(mm.Y))
        {
            filteredSpeed = 0;
            history.Clear();
            travel = Vector2.Zero;
            return delta;
        }
        double speed = mm.Length() / elapsed.Value;
        double weight = smoothingMs <= 0 ? 1 : 1 - Math.Exp(-elapsed.Value / (smoothingMs / 1000));
        filteredSpeed += weight * (speed - filteredSpeed);
        double t = Math.Clamp((filteredSpeed - start) / Math.Max(1, full - start), 0, 1);
        // Smoothstep has zero slope at both ends, avoiding a hard speed threshold.
        history.Enqueue((time, mm));
        travel += mm;
        while (history.TryPeek(out var oldest) && oldest.Time <= time - windowSeconds)
        {
            history.Dequeue();
            travel -= oldest.Movement;
        }
        double distanceFactor = travelDistance <= 0 ? 1
            : Math.Clamp((travel.Length() - travelDistance) / travelDistance, 0, 1);
        distanceFactor = distanceFactor * distanceFactor * (3 - 2 * distanceFactor);
        double gain = 1 + (maxGain - 1) * t * t * (3 - 2 * t) * distanceFactor;
        return delta * (float)gain;
    }

    public void Reset() { lastTime = null; filteredSpeed = 0; history.Clear(); travel = Vector2.Zero; }
}
