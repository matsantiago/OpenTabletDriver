using System.Numerics;
using PenPanScroll;
using OpenTabletDriver.Configurations.Parsers.Wacom.IntuosV3;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

var tests = new (string, Action)[]
{
    ("Control gestures: threshold, inverse horizontal direction and one action per touch", () =>
    {
        using var h = new Harness(relative: true);
        bool ctrl = true;
        var actions = new List<MissionAction>();
        h.Filter.MissionGestures = true;
        h.Filter.ControlPressed = () => ctrl;
        h.Filter.SendMissionAction = actions.Add;
        h.Send(10000, 10000, true);
        h.Send(10900, 10000, true);
        Check(actions.Count == 0 && !h.Pointer.Left && !h.Pointer.Right);
        h.Send(11000, 10000, true);
        h.Send(15000, 10000, true);
        Check(actions.SequenceEqual(new[] { MissionAction.SpaceLeft }));
        Check(h.Pointer.Position == Vector2.Zero && h.Pointer.Scroll == Vector2.Zero);
        h.Send(15000, 10000);
        h.Send(15000, 10000, true);
        h.Send(14000, 10000, true);
        Check(actions.Last() == MissionAction.SpaceRight && actions.Count == 2);
        ctrl = false;
        h.Send(12000, 10000, true);
        Check(!h.Pointer.Left && h.Pointer.Position == Vector2.Zero);
        h.Send(12200, 10000);
        Check(Vector2.Distance(h.Pointer.Position, new(40, 0)) < 0.001f);
        h.Send(12200, 10000, true);
        Check(h.Pointer.Left);
    }),
    ("Control gestures: up overview, hover/down ignored, normal scroll resumes", () =>
    {
        using var h = new Harness(relative: true);
        bool ctrl = true;
        var actions = new List<MissionAction>();
        h.Filter.MissionGestures = true;
        h.Filter.ControlPressed = () => ctrl;
        h.Filter.SendMissionAction = actions.Add;
        h.Send(10000, 10000);
        h.Send(10000, 5000);
        Check(actions.Count == 0);
        h.Send(10000, 10000, true);
        h.Send(10000, 11000, true);
        Check(actions.Count == 0);
        h.Send(10000, 9000, true);
        Check(actions.SequenceEqual(new[] { MissionAction.Overview }));
        h.Send(10000, 5000, true);
        Check(actions.Count == 1);
        h.Send(10000, 5000);
        ctrl = false;
        h.Send(10000, 5000, true, true);
        h.Send(9800, 4800, true, true);
        Check(h.Pointer.Scroll == new Vector2(10, 10));
    }),
    ("Control gestures: proximity loss resets and disabled mode leaves Control-click alone", () =>
    {
        using var h = new Harness(relative: true);
        var actions = new List<MissionAction>();
        h.Filter.ControlPressed = () => true;
        h.Filter.SendMissionAction = actions.Add;
        h.Send(10000, 10000, true);
        Check(h.Pointer.Left && actions.Count == 0);
        h.Filter.MissionGestures = true;
        h.Send(10000, 10000, true);
        h.Send(11000, 10000, true);
        h.Mode.Read(new OutOfRangeReport([]));
        h.Send(30000, 10000, true);
        Check(actions.Count == 1);
        h.Send(31000, 10000, true);
        Check(actions.Count == 2);
    }),
    ("Relative: scroll anchors, lifting repositions, release does not jump", () =>
    {
        using var h = new Harness(relative: true);
        h.Send(10000, 10000);
        h.Send(10200, 10000);
        Check(Vector2.Distance(h.Pointer.Position, new(40, 0)) < 0.001f);
        h.Send(10200, 10000, true, true);
        h.Send(10000, 9800, true, true);
        Check(h.Pointer.Scroll == new Vector2(10, 10));
        h.Send(30000, 18000, false, true);
        h.Send(30000, 18000, true, true);
        h.Send(29800, 17800, true, true);
        Check(h.Pointer.Scroll == new Vector2(20, 20));
        Check(Vector2.Distance(h.Pointer.Position, new(40, 0)) < 0.001f);
        h.Send(31000, 19000, true, false);
        Check(Vector2.Distance(h.Pointer.Position, new(40, 0)) < 0.001f && !h.Pointer.Left);
        h.Send(31200, 19000);
        Check(Vector2.Distance(h.Pointer.Position, new(80, 0)) < 0.001f);
        h.Send(31200, 19000, true);
        Check(h.Pointer.Left);
    }),
    ("Relative: proximity return rebases and repeated scroll does not drift", () =>
    {
        using var h = new Harness(relative: true);
        h.Send(10000, 10000);
        h.Send(10200, 10000);
        for (int i = 0; i < 5; i++)
        {
            h.Send(20000, 10000, true, true);
            h.Send(18000, 10000, true, true);
            h.Send(30000, 10000);
            Check(Vector2.Distance(h.Pointer.Position, new(40, 0)) < 0.001f);
        }
        h.Mode.Read(new OutOfRangeReport([]));
        h.Send(5000, 5000);
        Check(Vector2.Distance(h.Pointer.Position, new(40, 0)) < 0.001f);
        h.Send(5200, 5000);
        Check(Vector2.Distance(h.Pointer.Position, new(80, 0)) < 0.001f);
    }),
    ("Contact click: hover does not click; touch replaces left; release waits for lift", () =>
    {
        using var h = new Harness(relative: true);
        h.Filter.ClickButtonNumber = 2;
        h.Send(10000, 10000, right: true);
        Check(!h.Pointer.Right && !h.Pointer.Left);
        h.Send(10000, 10000, true, right: true);
        Check(h.Pointer.Right && !h.Pointer.Left);
        h.Send(10200, 10000, true, right: true);
        Check(h.Pointer.Right && !h.Pointer.Left);
        h.Send(10200, 10000, true);
        Check(!h.Pointer.Right && !h.Pointer.Left);
        h.Send(10200, 10000);
        h.Send(10200, 10000, true);
        Check(h.Pointer.Left);
    }),
    ("Contact click: middle option, scroll priority, range loss and disposal release", () =>
    {
        using var h = new Harness(relative: true);
        h.Filter.ClickButtonNumber = 2;
        h.Filter.ClickType = "Middle";
        h.Send(10000, 10000, true, right: true);
        Check(h.Pointer.Events.Contains("down:Middle") && !h.Pointer.Left);
        h.Send(9800, 10000, true, true, true);
        Check(h.Pointer.Events.Contains("up:Middle") && !h.Pointer.Left);
        h.Send(9600, 10000, true, true, true);
        Check(h.Pointer.Scroll.X == 10);
        h.Send(9600, 10000);
        h.Send(9600, 10000, true, right: true);
        h.Mode.Read(new OutOfRangeReport([]));
        Check(h.Pointer.Events.Count(e => e == "up:Middle") == 2);
        h.Send(5000, 5000, true, right: true);
        h.Filter.Dispose();
        Check(h.Pointer.Events.Count(e => e == "up:Middle") == 3);
    }),
    ("Up/left motion scrolls down/right; pointer stays anchored", () =>
    {
        using var h = new Harness();
        h.Send(10000, 10000);
        h.Send(10200, 10200, true, true);
        h.Send(10000, 10000, true, true);
        Check(h.Pointer.Scroll == new Vector2(10, 10));
        Check(Vector2.Distance(h.Pointer.Position, new Vector2(500, 500)) < 0.001f);
        Check(!h.Pointer.Left && !h.Pointer.Right && h.Pointer.Pressure == 0);
    }),
    ("Down/right motion and axis reversal", () =>
    {
        using var h = new Harness();
        h.Filter.ReverseX = h.Filter.ReverseY = true;
        h.Send(10000, 10000, true, true);
        h.Send(10200, 10200, true, true);
        Check(h.Pointer.Scroll == new Vector2(10, 10));
    }),
    ("Hover with button held produces no scrolling", () =>
    {
        using var h = new Harness();
        h.Send(10000, 10000, false, true);
        h.Send(20000, 20000, false, true);
        Check(h.Pointer.Scroll == Vector2.Zero);
        Check(Vector2.Distance(h.Pointer.Position, new Vector2(500, 500)) < 0.001f);
    }),
    ("Lifting and repositioning does not jump or carry fractions", () =>
    {
        using var h = new Harness();
        h.Send(10000, 10000, true, true);
        h.Send(9990, 9990, true, true);
        h.Send(30000, 15000, false, true);
        h.Send(30000, 15000, true, true);
        Check(h.Pointer.Scroll == Vector2.Zero);
        h.Send(29800, 14800, true, true);
        Check(h.Pointer.Scroll == new Vector2(10, 10));
    }),
    ("Fractional movements accumulate; stationary pen emits nothing", () =>
    {
        using var h = new Harness();
        h.Send(10000, 10000, true, true);
        for (int i = 1; i <= 200; i++) h.Send(10000 - i, 10000, true, true);
        Check(h.Pointer.Scroll.X == 10 && h.Pointer.Scroll.Y == 0);
        int count = h.Pointer.ScrollCalls;
        h.Send(9800, 10000, true, true);
        Check(h.Pointer.ScrollCalls == count);
    }),
    ("Button release while touching suppresses nib until lift", () =>
    {
        using var h = new Harness();
        h.Send(10000, 10000, true, true);
        h.Send(12000, 12000, true, false);
        Check(!h.Pointer.Left && Vector2.Distance(h.Pointer.Position, new Vector2(600, 600)) < 0.001f);
        h.Send(13000, 13000, true, false);
        Check(!h.Pointer.Left);
        h.Send(13000, 13000);
        h.Send(13000, 13000, true);
        Check(h.Pointer.Left);
    }),
    ("Pressing scroll during an existing drag releases left button", () =>
    {
        using var h = new Harness();
        h.Send(10000, 10000, true);
        Check(h.Pointer.Left);
        h.Send(10000, 10000, true, true);
        Check(!h.Pointer.Left && h.Pointer.Pressure == 0);
        Check(h.Pointer.Events.Contains("up:Left"));
    }),
    ("Normal nib, right click, and drag survive outside gesture", () =>
    {
        using var h = new Harness();
        h.Send(10000, 10000, true);
        h.Send(10200, 10200, true);
        Check(h.Pointer.Left && Vector2.Distance(h.Pointer.Position, new Vector2(510, 510)) < 0.001f);
        h.Send(10200, 10200);
        h.Send(10200, 10200, right: true);
        Check(!h.Pointer.Left && h.Pointer.Right);
        h.Send(10200, 10200);
        Check(!h.Pointer.Right);
    }),
    ("Other buttons and parser-owned arrays are preserved", () =>
    {
        using var h = new Harness();
        bool[] buttons = [true, true, false];
        IDeviceReport report = new IntuosV3ExtendedReport
        { Position = new(10000, 10000), Pressure = 1000, PenButtons = buttons, NearProximity = true, Raw = [1, 2, 3] };
        h.Mode.Read(report);
        Check(buttons[0] && buttons[1]);
        Check(!((ITabletReport)report).PenButtons[0] && ((ITabletReport)report).PenButtons[1]);
        Check(report.Raw.SequenceEqual(new byte[] { 1, 2, 3 }));
        Check(h.Pointer.Right);
    }),
    ("Proximity loss resets gesture and anchors fresh on return", () =>
    {
        using var h = new Harness();
        h.Send(10000, 10000, true, true);
        h.Mode.Read(new OutOfRangeReport([]));
        h.Send(30000, 10000, true, true);
        Check(h.Pointer.Scroll == Vector2.Zero);
        Check(Vector2.Distance(h.Pointer.Position, new Vector2(1500, 500)) < 0.001f);
        h.Send(29800, 10000, true, true);
        Check(h.Pointer.Scroll.X == 10);
    }),
    ("NearProximity false resets before processing motion", () =>
    {
        using var h = new Harness();
        h.Send(10000, 10000, true, true);
        h.Mode.Read(new IntuosV3ExtendedReport
        { Position = new(30000, 10000), Pressure = 0, PenButtons = [false, false, false], NearProximity = false, Raw = [] });
        h.Send(30000, 10000, true, true);
        Check(h.Pointer.Scroll == Vector2.Zero);
    }),
    ("Unrelated reports pass through", () =>
    {
        var filter = new PenPanScrollFilter();
        IDeviceReport? received = null;
        filter.Emit += r => received = r;
        var report = new AuxReport();
        filter.Consume(report);
        Check(ReferenceEquals(received, report));
    }),
    ("Disposal and disabled filter restore ordinary nib behavior", () =>
    {
        using var h = new Harness();
        h.Send(10000, 10000, true, true);
        h.Filter.Dispose();
        h.Send(12000, 12000, true);
        Check(h.Pointer.Left && Vector2.Distance(h.Pointer.Position, new Vector2(600, 600)) < 0.001f);
        h.Mode.Elements = [h.Bindings];
        h.Send(12000, 12000);
        h.Send(12200, 12200, true);
        Check(h.Pointer.Left && Vector2.Distance(h.Pointer.Position, new Vector2(610, 610)) < 0.001f);
    }),
    ("Sensitivity zero disables one axis; invalid settings normalize", () =>
    {
        using var h = new Harness();
        h.Filter.SensitivityX = 0;
        h.Filter.SensitivityY = double.NaN;
        h.Filter.ButtonNumber = 0;
        h.Send(10000, 10000, true, true);
        h.Send(9800, 9800, true, true);
        Check(h.Pointer.Scroll == new Vector2(0, 10));
        Check(h.Filter.ButtonNumber == 1 && h.Filter.SensitivityY == 10);
    }),
    ("Unconfigured filter fails open", () =>
    {
        var filter = new PenPanScrollFilter();
        IDeviceReport report = new IntuosV3ExtendedReport
        { Position = new(10000, 10000), Pressure = 1000, PenButtons = [true, false, false], NearProximity = true };
        filter.Consume(report);
        Check(((ITabletReport)report).Pressure == 1000 && ((ITabletReport)report).PenButtons[0]);
    }),
    ("Eraser remains usable", () =>
    {
        using var h = new Harness();
        h.Mode.Read(new IntuosV3ExtendedReport
        { Position = new(10000, 10000), Pressure = 1000, PenButtons = [false, false, false], NearProximity = true, Eraser = true, Raw = [] });
        Check(h.Pointer.Left);
    })
};
int failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failures++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static void Check(bool result, [System.Runtime.CompilerServices.CallerArgumentExpression("result")] string expression = "")
{
    if (!result) throw new Exception("Assertion failed: " + expression);
}

sealed class Harness : IDisposable
{
    public FakePointer Pointer { get; } = new();
    public OutputMode Mode { get; }
    public PenPanScrollFilter Filter { get; } = new();
    public BindingHandler Bindings { get; }

    public Harness(bool relative = false)
    {
        var tablet = new TabletReference
        {
            Properties = new TabletConfiguration
            {
                Name = "Wacom PTK-470",
                DigitizerIdentifiers = [],
                Specifications = new TabletSpecifications
                {
                    Digitizer = new DigitizerSpecifications { Width = 187, Height = 105, MaxX = 37400, MaxY = 21000 },
                    Pen = new PenSpecifications { MaxPressure = 8191, ButtonCount = 3 }
                }
            }
        };
        Pointer.Relative = relative;
        if (relative)
            Mode = new FakeRelativeMode { Pointer = Pointer, Tablet = tablet, Sensitivity = new(40, 40), ResetTime = TimeSpan.FromMinutes(1) };
        else
            Mode = new FakeMode
            {
                Pointer = Pointer, Tablet = tablet,
                Input = new Area(187, 105, new(93.5f, 52.5f), 0),
                Output = new Area(1870, 1050, new(935, 525), 0)
            };
        Filter.Configure(tablet, Mode);
        Bindings = new(tablet)
        {
            Tip = new ThresholdBindingState { Binding = new AdaptiveBinding(PenAction.Tip) { MouseButtonHandler = Pointer } },
            Eraser = new ThresholdBindingState { Binding = new AdaptiveBinding(PenAction.Eraser) { MouseButtonHandler = Pointer } }
        };
        Bindings.PenButtons[0] = new BindingState { Binding = new AdaptiveBinding(PenAction.BarrelButton1) { MouseButtonHandler = Pointer } };
        Bindings.PenButtons[1] = new BindingState { Binding = new AdaptiveBinding(PenAction.BarrelButton1) { MouseButtonHandler = Pointer } };
        Mode.Elements = [Filter, Bindings];
    }

    public void Send(float x, float y, bool contact = false, bool button = false, bool right = false) =>
        Mode.Read(new IntuosV3ExtendedReport
        { Position = new(x, y), Pressure = contact ? 1000u : 0u, PenButtons = [button, right, false], NearProximity = true, Raw = [] });

    public void Dispose() => Mode.Dispose();
}

sealed class FakeMode : AbsoluteOutputMode
{
    public override IAbsolutePointer? Pointer { get; set; } = null!;
}

sealed class FakeRelativeMode : RelativeOutputMode
{
    public override IRelativePointer? Pointer { get; set; } = null!;
}

sealed class FakePointer : IRelativePointer, IAbsolutePointer, IMouseScrollHandler, IMouseButtonHandler, IPressureHandler, ISynchronousPointer
{
    public Vector2 Position, Scroll;
    public bool Relative;
    public float Pressure;
    public bool Left, Right;
    public int ScrollCalls;
    public List<string> Events { get; } = [];
    public void SetPosition(Vector2 position) => Position = Relative ? Position + position : position;
    public void ScrollHorizontally(int amount) { Scroll.X += amount; ScrollCalls++; }
    public void ScrollVertically(int amount) { Scroll.Y += amount; ScrollCalls++; }
    public void MouseDown(MouseButton button)
    {
        Events.Add($"down:{button}");
        if (button == MouseButton.Left) Left = true;
        if (button == MouseButton.Right) Right = true;
    }
    public void MouseUp(MouseButton button)
    {
        Events.Add($"up:{button}");
        if (button == MouseButton.Left) Left = false;
        if (button == MouseButton.Right) Right = false;
    }
    public void SetPressure(float pressure) => Pressure = pressure;
    public void Reset() { Left = Right = false; }
    public void Flush() { }
}

sealed class AuxReport : IDeviceReport
{
    public byte[] Raw { get; set; } = [];
}
