using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Overlay;

/// <summary>
/// A non-activating AppKit border around an externally tracked rectangle. Every member must
/// be called on the macOS main thread. Rectangle coordinates use AppKit's global screen space.
/// </summary>
internal sealed class MacBorderOverlay : IDisposable
{
    private const nuint BorderlessStyleMask = 0;
    private const nuint BufferedBackingStore = 2;
    private const nint FloatingWindowLevel = 3;
    private const nuint CanJoinAllSpaces = 1 << 0;
    private const nuint Stationary = 1 << 4;
    private const nuint IgnoresCycle = 1 << 6;
    private const nuint FullScreenAuxiliary = 1 << 8;
    private const nuint CanJoinAllApplications = 1 << 18;

    private readonly List<IntPtr> _windows = [];
    private MacBorderOverlayConfiguration _configuration;
    private CGRect _targetRectangle;
    private bool _hasTarget;
    private bool _visible;
    private bool _disposed;

    public MacBorderOverlay(MacBorderOverlayConfiguration configuration)
    {
        MainThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    public void Show(CGRect targetRectangle)
    {
        VerifyUsable();
        Validate(targetRectangle);
        _targetRectangle = targetRectangle;
        _hasTarget = true;
        _visible = true;
        Render();
    }

    public void Update(CGRect targetRectangle)
    {
        VerifyUsable();
        Validate(targetRectangle);
        _targetRectangle = targetRectangle;
        _hasTarget = true;
        if (_visible) Render();
    }

    public void Configure(MacBorderOverlayConfiguration configuration)
    {
        VerifyUsable();
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
        if (_visible && _hasTarget) Render();
    }

    public void Hide()
    {
        VerifyUsable();
        _visible = false;
        OrderOutFrom(0);
    }

    public void Dispose()
    {
        MainThread.VerifyAccess();
        if (_disposed) return;

        _disposed = true;
        foreach (IntPtr window in _windows)
        {
            ObjC.SendVoid(window, ObjCSelectors.OrderOut, IntPtr.Zero);
            ObjC.SendVoid(window, ObjCSelectors.Close);
            ObjC.SendVoid(window, ObjCSelectors.Release);
        }
        _windows.Clear();
    }

    private void Render()
    {
        List<CGRect> segments = GetVisibleSegments(_targetRectangle, _configuration.Thickness);
        IntPtr color = ObjC.SendColor(
            ObjCClasses.NSColor,
            ObjCSelectors.ColorWithSrgb,
            _configuration.Red,
            _configuration.Green,
            _configuration.Blue,
            _configuration.Opacity);
        if (color == IntPtr.Zero)
            throw new InvalidOperationException("NSColor failed to create the overlay color.");

        for (int index = 0; index < segments.Count; index++)
        {
            IntPtr window = index < _windows.Count ? _windows[index] : CreateWindow();
            ObjC.SendVoid(window, ObjCSelectors.SetBackgroundColor, color);
            ObjC.SendVoidRectBool(window, ObjCSelectors.SetFrameDisplay, segments[index], true);
            ObjC.SendVoid(window, ObjCSelectors.OrderFrontRegardless);
        }
        OrderOutFrom(segments.Count);
    }

    private IntPtr CreateWindow()
    {
        IntPtr allocated = ObjC.Send(MacOverlayWindowClass.Handle, ObjCSelectors.Alloc);
        IntPtr window = ObjC.SendInitWindow(
            allocated,
            ObjCSelectors.InitWithContentRect,
            new CGRect(0, 0, 1, 1),
            BorderlessStyleMask,
            BufferedBackingStore,
            false);
        if (window == IntPtr.Zero)
            throw new InvalidOperationException("NSWindow failed to create an overlay segment.");

        try
        {
            ObjC.SendVoidBool(window, ObjCSelectors.SetOpaque, false);
            ObjC.SendVoidBool(window, ObjCSelectors.SetHasShadow, false);
            ObjC.SendVoidNInt(window, ObjCSelectors.SetLevel, FloatingWindowLevel);
            ObjC.SendVoidBool(window, ObjCSelectors.SetIgnoresMouseEvents, true);
            ObjC.SendVoidBool(window, ObjCSelectors.SetReleasedWhenClosed, false);
            ObjC.SendVoidNUInt(
                window,
                ObjCSelectors.SetCollectionBehavior,
                CanJoinAllSpaces
                    | Stationary
                    | IgnoresCycle
                    | FullScreenAuxiliary
                    | CanJoinAllApplications);
            _windows.Add(window);
            return window;
        }
        catch
        {
            ObjC.SendVoid(window, ObjCSelectors.Release);
            throw;
        }
    }

    private static List<CGRect> GetVisibleSegments(CGRect target, double configuredThickness)
    {
        double thickness = Math.Min(
            configuredThickness,
            Math.Min(target.Size.Width, target.Size.Height) / 2);
        double sideHeight = Math.Max(0, target.Size.Height - (2 * thickness));
        CGRect[] borders =
        [
            new(target.Origin.X, target.Origin.Y, target.Size.Width, thickness),
            new(target.Origin.X, target.Origin.Y + target.Size.Height - thickness,
                target.Size.Width, thickness),
            new(target.Origin.X, target.Origin.Y + thickness, thickness, sideHeight),
            new(target.Origin.X + target.Size.Width - thickness, target.Origin.Y + thickness,
                thickness, sideHeight),
        ];

        var result = new List<CGRect>();
        IntPtr screens = ObjC.Send(ObjCClasses.NSScreen, ObjCSelectors.Screens);
        nuint screenCount = screens == IntPtr.Zero
            ? 0
            : ObjC.SendNUInt(screens, ObjCSelectors.Count);
        for (nuint screenIndex = 0; screenIndex < screenCount; screenIndex++)
        {
            IntPtr screen = ObjC.SendNUInt(screens, ObjCSelectors.ObjectAtIndex, screenIndex);
            CGRect screenFrame = ObjC.SendRect(screen, ObjCSelectors.Frame);
            foreach (CGRect border in borders)
            {
                CGRect? intersection = Intersect(border, screenFrame);
                if (intersection is CGRect segment && !Contains(result, segment))
                    result.Add(segment);
            }
        }
        return result;
    }

    private static CGRect? Intersect(CGRect first, CGRect second)
    {
        double x = Math.Max(first.Origin.X, second.Origin.X);
        double y = Math.Max(first.Origin.Y, second.Origin.Y);
        double maxX = Math.Min(
            first.Origin.X + first.Size.Width,
            second.Origin.X + second.Size.Width);
        double maxY = Math.Min(
            first.Origin.Y + first.Size.Height,
            second.Origin.Y + second.Size.Height);
        return maxX > x && maxY > y ? new CGRect(x, y, maxX - x, maxY - y) : null;
    }

    private static bool Contains(List<CGRect> rectangles, CGRect candidate)
        => rectangles.Any(rectangle =>
            rectangle.Origin.X == candidate.Origin.X
            && rectangle.Origin.Y == candidate.Origin.Y
            && rectangle.Size.Width == candidate.Size.Width
            && rectangle.Size.Height == candidate.Size.Height);

    private void OrderOutFrom(int firstIndex)
    {
        for (int index = firstIndex; index < _windows.Count; index++)
            ObjC.SendVoid(_windows[index], ObjCSelectors.OrderOut, IntPtr.Zero);
    }

    private void VerifyUsable()
    {
        MainThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void Validate(CGRect rectangle)
    {
        if (!double.IsFinite(rectangle.Origin.X)
            || !double.IsFinite(rectangle.Origin.Y)
            || !double.IsFinite(rectangle.Size.Width)
            || !double.IsFinite(rectangle.Size.Height)
            || rectangle.Size.Width <= 0
            || rectangle.Size.Height <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(rectangle),
                "Overlay rectangle must be finite and have positive dimensions.");
    }
}
