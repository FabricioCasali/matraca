using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal static class MacWindowVisibility
{
    private const double BoundsTolerance = 3;

    public static bool TryGetOnScreenBounds(MacTargetLease target, out CGRect bounds)
    {
        ArgumentNullException.ThrowIfNull(target);
        bounds = default;
        if (!Accessibility.TryGetWindowBounds(target.Window, out CGRect accessibilityBounds))
            return false;

        string accessibilityTitle = Accessibility.GetStringAttribute(
            target.Window,
            Accessibility.TitleAttribute);
        IntPtr windows = CoreGraphics.CGWindowListCopyWindowInfo(
            CoreGraphics.WindowListOptionOnScreenOnly
                | CoreGraphics.WindowListExcludeDesktopElements,
            CoreGraphics.NullWindow);
        if (windows == IntPtr.Zero) return false;

        try
        {
            var candidates = new List<(CGRect Bounds, string Title)>();
            IntPtr ownerProcessIdKey = CoreGraphics.WindowOwnerProcessIdKey;
            IntPtr isOnscreenKey = CoreGraphics.WindowIsOnscreenKey;
            IntPtr boundsKey = CoreGraphics.WindowBoundsKey;
            IntPtr nameKey = CoreGraphics.WindowNameKey;
            nint count = CoreFoundation.GetArrayCount(windows);
            for (nint index = 0; index < count; index++)
            {
                IntPtr window = CoreFoundation.GetArrayValue(windows, index);
                if (!TryGetInt(window, ownerProcessIdKey, out int processId)
                    || processId != target.ProcessId
                    || !CoreFoundation.GetBoolean(CoreFoundation.GetDictionaryValue(
                        window,
                        isOnscreenKey)))
                    continue;

                IntPtr boundsDictionary = CoreFoundation.GetDictionaryValue(
                    window,
                    boundsKey);
                if (!CoreGraphics.CGRectMakeWithDictionaryRepresentation(
                        boundsDictionary,
                        out CGRect candidateBounds)
                    || !Matches(accessibilityBounds, candidateBounds))
                    continue;

                string title = CoreFoundation.GetString(CoreFoundation.GetDictionaryValue(
                    window,
                    nameKey)) ?? string.Empty;
                candidates.Add((candidateBounds, title));
            }

            if (candidates.Count == 1)
            {
                bounds = accessibilityBounds;
                return true;
            }

            if (!string.IsNullOrEmpty(accessibilityTitle))
            {
                var titleMatches = candidates
                    .Where(candidate => string.Equals(
                        candidate.Title,
                        accessibilityTitle,
                        StringComparison.Ordinal))
                    .ToArray();
                if (titleMatches.Length == 1)
                {
                    bounds = accessibilityBounds;
                    return true;
                }
            }

            return false;
        }
        finally { CoreFoundation.Release(windows); }
    }

    private static bool TryGetInt(IntPtr dictionary, IntPtr key, out int value)
        => CoreFoundation.TryGetInt32(
            CoreFoundation.GetDictionaryValue(dictionary, key),
            out value);

    private static bool Matches(CGRect first, CGRect second)
        => Math.Abs(first.Origin.X - second.Origin.X) <= BoundsTolerance
            && Math.Abs(first.Origin.Y - second.Origin.Y) <= BoundsTolerance
            && Math.Abs(first.Size.Width - second.Size.Width) <= BoundsTolerance
            && Math.Abs(first.Size.Height - second.Size.Height) <= BoundsTolerance;
}
