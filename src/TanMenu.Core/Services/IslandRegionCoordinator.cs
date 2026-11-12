namespace TanMenu.Core.Services;

public enum IslandRegionApplyKind
{
    Applied,
    Unchanged,
    RejectedGeometry,
    NativeFailed,
}

/// <summary>
/// Sends the concave device polygon to Win32 only when its pixels change.
/// Invalid geometry keeps the last good region. A native failure closes the overlay.
/// </summary>
public sealed class IslandRegionCoordinator
{
    public IslandDevicePoint[]? Applied { get; private set; }

    public IslandRegionApplyKind Apply(
        double progress,
        double hostWidth,
        double hostHeight,
        double scale,
        Func<IslandDevicePoint[], bool> nativeApply)
    {
        ArgumentNullException.ThrowIfNull(nativeApply);
        if (hostWidth <= 0 || hostHeight <= 0)
            return IslandRegionApplyKind.RejectedGeometry;

        var outline = IslandTrapezoidGeometry.Layout(progress, hostWidth, hostHeight);
        if (!IslandTrapezoidGeometry.TryGetDevicePoints(outline, scale, out var region))
            return IslandRegionApplyKind.RejectedGeometry;

        if (Applied is { } current && current.AsSpan().SequenceEqual(region))
            return IslandRegionApplyKind.Unchanged;

        if (!nativeApply(region))
            return IslandRegionApplyKind.NativeFailed;

        Applied = region;
        return IslandRegionApplyKind.Applied;
    }

    public static bool ShouldCloseOverlay(IslandRegionApplyKind kind, bool overlayVisible) =>
        kind == IslandRegionApplyKind.NativeFailed ||
        (!overlayVisible && kind == IslandRegionApplyKind.RejectedGeometry);
}
