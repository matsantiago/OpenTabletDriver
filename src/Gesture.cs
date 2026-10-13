using System.Numerics;

namespace PenPanScroll;

internal readonly record struct GestureResult(
    Vector2 Position, bool SuppressTip, bool SuppressButton, int Horizontal, int Vertical);

internal sealed class Gesture
{
    private bool held;
    private bool suppressUntilLift;
    private Vector2 anchor;
    private Vector2? previousContact;
    private Vector2? lastPointer;
    private double remainderX, remainderY;
    private Vector2 relativeOffset;
    private bool rebase;

    public GestureResult Process(Vector2 raw, bool contact, bool button,
        double unitsToMmX, double unitsToMmY, double sensitivityX, double sensitivityY,
        bool reverseX, bool reverseY, bool relative = false)
    {
        if (relative && rebase)
        {
            relativeOffset = (lastPointer ?? raw) - raw;
            rebase = false;
        }
        if (!button)
        {
            if (held && contact)
                suppressUntilLift = true;
            if (!contact)
                suppressUntilLift = false;
            // Rebase the virtual input at release, so RelativeOutputMode sees
            // no accumulated motion from the entire scrolling stroke.
            if (relative && held)
                relativeOffset = anchor - raw;
            held = false;
            ClearStroke();
            lastPointer = relative ? raw + relativeOffset : raw;
            return new(lastPointer.Value, suppressUntilLift, false, 0, 0);
        }

        if (!held)
        {
            anchor = lastPointer ?? (relative ? raw + relativeOffset : raw);
            ClearStroke();
            held = true;
        }

        int x = 0, y = 0;
        if (contact)
        {
            if (previousContact is { } previous)
            {
                // OTD's macOS scroll handler negates amounts before posting them.
                // Positive handler values therefore scroll down/right. Natural
                // scrolling maps upward/leftward pen motion to those values.
                x = Accumulate(ref remainderX,
                    ((double)previous.X - raw.X) * unitsToMmX * sensitivityX * (reverseX ? -1 : 1));
                y = Accumulate(ref remainderY,
                    ((double)previous.Y - raw.Y) * unitsToMmY * sensitivityY * (reverseY ? -1 : 1));
            }
            previousContact = raw;
        }
        else
        {
            suppressUntilLift = false;
            ClearStroke();
        }

        lastPointer = anchor;
        return new(anchor, true, true, x, y);
    }

    private static int Accumulate(ref double remainder, double delta)
    {
        remainder += delta;
        int whole = (int)Math.Clamp(Math.Truncate(remainder + Math.CopySign(1e-9, remainder)), int.MinValue, int.MaxValue);
        remainder -= whole;
        if (Math.Abs(remainder) < 1e-9) remainder = 0;
        return whole;
    }

    private void ClearStroke()
    {
        previousContact = null;
        remainderX = remainderY = 0;
    }

    public void Reset(bool relative = false)
    {
        held = suppressUntilLift = false;
        if (relative)
            rebase = true;
        else
        {
            lastPointer = null;
            relativeOffset = Vector2.Zero;
            rebase = false;
        }
        ClearStroke();
    }
}
