namespace TanMenu.Core.Services;

public enum IslandRegionApplyKind
{
    Applied,
    Unchanged,
    RejectedGeometry,
    NativeFailed,
}

/// <summary>
/// Decides when a capsule-shaped device region must be sent to Win32. Identical pixels are skipped.
/// Invalid geometry never means "clear the region". A native failure is reported so the overlay can close.
/// </summary>
public sealed class IslandRegionCoordinator
{
    public IslandDeviceRoundRegion? Applied { get; private set; }

    public IslandRegionApplyKind Apply(
        double hostWidth,
        double hostHeight,
        double capsuleWidth,
        double capsuleHeight,
        double radius,
        double scale,
        Func<IslandDeviceRoundRegion, bool> nativeApply)
    {
        ArgumentNullException.ThrowIfNull(nativeApply);
        var safeScale = scale <= 0 || double.IsNaN(scale) ? 1 : scale;
        var capsule = IslandCapsuleGeometry.Layout(hostWidth, hostHeight, capsuleWidth, capsuleHeight, radius);
        var hostW = Round(hostWidth * safeScale);
        var hostH = Round(hostHeight * safeScale);
        if (!IslandCapsuleGeometry.TryGetDeviceRoundRegion(capsule, safeScale, hostW, hostH, out var region))
            return IslandRegionApplyKind.RejectedGeometry;

        if (Applied is { } current && current.Equals(region))
            return IslandRegionApplyKind.Unchanged;

        if (!nativeApply(region))
            return IslandRegionApplyKind.NativeFailed;

        Applied = region;
        return IslandRegionApplyKind.Applied;
    }

    /// <summary>Initial failure must not show the overlay. A later native failure must remove it.
    /// A later non-capsule sample keeps the last good region instead of clearing it.</summary>
    public static bool ShouldCloseOverlay(IslandRegionApplyKind kind, bool overlayVisible) =>
        kind == IslandRegionApplyKind.NativeFailed ||
        (!overlayVisible && kind == IslandRegionApplyKind.RejectedGeometry);

    private static int Round(double value)
    {
        var rounded = Math.Round(value);
        if (rounded >= int.MaxValue)
            return int.MaxValue;
        if (rounded <= int.MinValue)
            return int.MinValue;
        return (int)rounded;
    }
}
