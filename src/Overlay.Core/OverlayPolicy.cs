namespace Overlay.Core;

public static class OverlayPolicy
{
    public static bool ShouldShow(bool requested, bool hasRegion, bool targetAvailable,
        bool targetMinimized, bool gameForeground, bool controlForeground, bool editing)
        => requested && hasRegion && targetAvailable && !targetMinimized &&
           (gameForeground || (editing && controlForeground));
}
