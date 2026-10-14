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
    private readonly ReportDiagnostics diagnostics = new();
    private readonly MissionGesture missionGesture = new();
    internal Func<KeyboardModifiers> ReadModifiers { get; set; } = MacMissionControl.ReadModifiers;
    private int keyboardGesture;
    internal Action<int, int, bool> SendKeyboardScroll { get; set; } = MacMissionControl.SendScroll;
    internal Action<MissionAction> SendMissionAction { get; set; } = MacMissionControl.Send;
    private double missionThreshold = 5;
    private IMouseScrollHandler? scroll;
    private IMouseButtonHandler? mouse;
    private MouseButton? clickDown;
    private bool suppressClickUntilLift;
    private int clickButtonNumber;
    private double scaleX, scaleY;
    private bool disposed;
    private bool relative;
    private int buttonNumber = 1;
    private double sensitivityX = 10, sensitivityY = 10;

    [Resolved]
    public IDriver? Driver { get; set; }

    [TabletReference]
    public TabletReference? Tablet { get; set; }

    [Property("Scroll trigger"), DefaultPropertyValue("Pen button"), PropertyValidated(nameof(ScrollTriggers))]
    public string ScrollTrigger { get; set; } = "Pen button";
    public static string[] ScrollTriggers => ["Pen button", "Control"];

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

    [Property("Contact click button"), DefaultPropertyValue(0),
     ToolTip("One-based pen button selecting a contact click. Zero disables it. Must differ from Scroll button.")]
    public int ClickButtonNumber { get => clickButtonNumber; set => clickButtonNumber = Math.Clamp(value, 0, 32); }

    [Property("Contact click type"), DefaultPropertyValue("Right"), PropertyValidated(nameof(ClickTypes))]
    public string ClickType { get; set; } = "Right";
    public static string[] ClickTypes => ["Right", "Middle"];

    [Property("Control + Shift Mission gestures"), DefaultPropertyValue(false),
     ToolTip("Control + Shift + nib drag: up opens Mission Control, right moves to the left Space, left to the right Space. One action per stroke.")]
    public bool MissionGestures { get; set; }

    [Property("Control + Option middle click"), DefaultPropertyValue(false),
     ToolTip("Control + Option + nib contact holds the middle mouse button. Lift to release. Overrides scroll and Mission gestures.")]
    public bool MiddleClickGesture { get; set; }

    [Property("Control + Command zoom"), DefaultPropertyValue(false),
     ToolTip("Control + Command + nib drag sends vertical scroll with Command for app zoom (e.g. FigJam on Mac).")]
    public bool ZoomGesture { get; set; }

    [Property("Mission gesture distance"), DefaultPropertyValue(5.0), Unit("mm")]
    public double MissionThreshold
    {
        get => missionThreshold;
        set => missionThreshold = double.IsFinite(value) ? Math.Clamp(value, 1, 50) : 5;
    }

    [Property("Diagnostic logging"), DefaultPropertyValue(false),
     ToolTip("Logs numeric pen coordinates, active gesture and cursor state once a second for troubleshooting.")]
    public bool DiagnosticLogging { get; set; }

    public PipelinePosition Position => PipelinePosition.PreTransform;
    public event Action<IDeviceReport?>? Emit;

    [OnDependencyLoad]
    public void Initialize()
    {
        // OTD 0.6.7 supplies pointer services to bindings, but not to filters.
        // Resolve the same pointer from the exact input tree instead of creating
        // another native mouse with independent button and pressure state.
        var tree = (Driver as Driver)?.InputDevices.FirstOrDefault(
            device => ReferenceEquals(device.Properties, Tablet?.Properties));
        if (OperatingSystem.IsMacOS() && tree?.OutputMode is OutputMode output && Tablet != null)
            Configure(Tablet, output);

        if (scroll == null)
            Log.Write("Pen Pan/Scroll", "Inactive: requires macOS, Absolute or Relative Mode, and a scroll-capable pointer. Input is passed through.", LogLevel.Error);
        else
            Log.Write("Pen Pan/Scroll", $"Ready: pen button {ButtonNumber}, X={SensitivityX}, Y={SensitivityY} px/mm.");
    }

    internal void Configure(TabletReference tablet, OutputMode output)
    {
        Tablet = tablet;
        var digitizer = tablet.Properties.Specifications.Digitizer;
        if (digitizer == null || digitizer.MaxX == 0 || digitizer.MaxY == 0 ||
            !float.IsFinite(digitizer.Width) || !float.IsFinite(digitizer.Height) ||
            digitizer.Width <= 0 || digitizer.Height <= 0)
            return;
        relative = output is RelativeOutputMode;
        scroll = output switch
        {
            AbsoluteOutputMode absolute => absolute.Pointer as IMouseScrollHandler,
            RelativeOutputMode rel => rel.Pointer as IMouseScrollHandler,
            _ => null
        };
        mouse = scroll as IMouseButtonHandler;
        scaleX = (double)digitizer.Width / digitizer.MaxX;
        scaleY = (double)digitizer.Height / digitizer.MaxY;
    }

    public void Consume(IDeviceReport? report)
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
                ReleaseContactClick();
                suppressClickUntilLift = false;
                gesture.Reset(relative);
                missionGesture.Reset();
                keyboardGesture = 0;
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
                (ScrollTrigger == "Pen button" && ButtonNumber > pen.PenButtons.Length))
            {
                ReleaseContactClick();
                suppressClickUntilLift = false;
                gesture.Reset(relative);
                missionGesture.Reset();
                keyboardGesture = 0;
                Emit?.Invoke(report);
                return;
            }

            // This Bosto emits a zero-coordinate hover sample when the pen
            // leaves range. Treat it as a proximity boundary, never as movement
            // to the origin. Rebase the next valid sample in Relative Mode.
            if (relative && Tablet?.Properties.Name == "Bosto BT-12HD" &&
                pen.Position == Vector2.Zero && pen.Pressure == 0)
            {
                ReleaseContactClick();
                suppressClickUntilLift = false;
                gesture.Reset(relative);
                missionGesture.Reset();
                keyboardGesture = 0;
                if (DiagnosticLogging)
                    diagnostics.Trace("zero-hover boundary", $"rawBytes={Convert.ToHexString(pen.Raw)} rebasing next position");
                Emit?.Invoke(new OutOfRangeReport(pen.Raw));
                return;
            }

            bool contact = pen.Pressure > 0;
            var modifiers = ReadModifiers();
            bool wantsMiddle = MiddleClickGesture && modifiers.Control && modifiers.Option;
            bool wantsMission = MissionGestures && modifiers.Control && modifiers.Shift && !wantsMiddle;
            bool wantsZoom = ZoomGesture && modifiers.Control && modifiers.Command && !modifiers.Shift && !wantsMiddle;
            bool wantsScroll = ScrollTrigger == "Control" && modifiers.Control && !modifiers.Shift && !wantsMiddle && !wantsZoom;
            if (!contact) keyboardGesture = 0;
            else if (keyboardGesture == 0)
                keyboardGesture = wantsMiddle ? 3 : wantsMission ? 2 : wantsZoom ? 4 : wantsScroll ? 1 : 0;
            // A contact stroke belongs to exactly one mode. Releasing or adding
            // Shift mid-stroke never fires the other mode; lift to switch.
            bool keyboardScroll = wantsScroll && (!contact || keyboardGesture == 1);
            bool zoom = wantsZoom && (!contact || keyboardGesture == 4);
            bool mission = wantsMission && (!contact || keyboardGesture == 2);
            bool middle = wantsMiddle && (!contact || keyboardGesture == 3);
            bool penScroll = !middle && ScrollTrigger == "Pen button" && pen.PenButtons[ButtonNumber - 1];
            var missionAction = missionGesture.Process(pen.Position, contact, mission,
                scaleX, scaleY, MissionThreshold);
            bool clickButton = ClickButtonNumber > 0 && (ScrollTrigger != "Pen button" || ClickButtonNumber != ButtonNumber) &&
                ClickButtonNumber <= pen.PenButtons.Length && pen.PenButtons[ClickButtonNumber - 1];
            var result = gesture.Process(pen.Position, pen.Pressure > 0,
                mission || keyboardScroll || zoom || penScroll, scaleX, scaleY,
                mission ? 0 : SensitivityX, mission ? 0 : SensitivityY, ReverseX, ReverseY, relative);

            // The side button chooses the nib click instead of adding another
            // button to the normal left click. Scroll takes precedence.
            bool clickConfigured = mouse != null && ClickButtonNumber > 0 && (ScrollTrigger != "Pen button" || ClickButtonNumber != ButtonNumber) &&
                ClickButtonNumber <= pen.PenButtons.Length;
            bool wantsClick = contact && (middle || (clickConfigured && clickButton && !result.SuppressTip));
            var desiredClick = middle ? MouseButton.Middle : ClickType == "Middle" ? MouseButton.Middle : MouseButton.Right;
            if (wantsClick && clickDown != null && clickDown != desiredClick) ReleaseContactClick();
            if (!contact)
                suppressClickUntilLift = false;
            if (!wantsClick && clickDown != null)
            {
                ReleaseContactClick();
                suppressClickUntilLift = contact;
            }
            if (wantsClick && clickDown == null)
            {
                clickDown = desiredClick;
                mouse!.MouseDown(clickDown.Value);
            }

            if (DiagnosticLogging)
                diagnostics.Trace("input", $"raw={pen.Position} rawBytes={Convert.ToHexString(pen.Raw)} virtual={result.Position} contact={contact} modifiers={modifiers} stroke={keyboardGesture} scroll={keyboardScroll} zoom={zoom} middle={middle} mission={mission} suppressTip={result.SuppressTip}");
            pen.Position = result.Position;
            if (middle || result.SuppressTip || (clickConfigured && clickButton) || suppressClickUntilLift)
                pen.Pressure = 0;
            if (middle || result.SuppressButton || clickConfigured)
            {
                // Do not mutate arrays shared with the parser/debugger.
                pen.PenButtons = (bool[])pen.PenButtons.Clone();
                if (middle || keyboardScroll || zoom || mission)
                    Array.Fill(pen.PenButtons, false);
                else if (result.SuppressButton && ScrollTrigger == "Pen button")
                    pen.PenButtons[ButtonNumber - 1] = false;
                if (clickConfigured)
                    pen.PenButtons[ClickButtonNumber - 1] = false;
            }

            if (!keyboardScroll && !zoom && result.Horizontal != 0)
                scroll.ScrollHorizontally(result.Horizontal);
            if (!keyboardScroll && !zoom && result.Vertical != 0)
                scroll.ScrollVertically(result.Vertical);

            // The normal output stage releases the nib, positions the pointer,
            // and flushes both scroll axes together in its usual report cycle.
            Emit?.Invoke(report);
            if (keyboardScroll)
                SendKeyboardScroll(result.Horizontal, result.Vertical, false);
            else if (zoom)
                SendKeyboardScroll(0, result.Vertical, true);
            // First flush the report with the nib released, then post the
            // shortcut. This prevents Control-click from opening a menu.
            if (missionAction is { } action)
            {
                SendMissionAction(action);
                Log.Write("Pen Pan/Scroll", $"Control gesture: {action}");
            }
        }
    }

    private void ReleaseContactClick()
    {
        if (clickDown is { } button)
            mouse?.MouseUp(button);
        clickDown = null;
    }

    private static double ValidateSensitivity(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1000) : 10;

    public void Dispose()
    {
        lock (sync)
        {
            // OTD disposes the output pipeline on disconnect and settings apply.
            ReleaseContactClick();
            if (mouse is ISynchronousPointer synchronous)
                synchronous.Flush();
            disposed = true;
            gesture.Reset(relative);
            missionGesture.Reset();
                keyboardGesture = 0;
            scroll = null;
        }
    }
}
