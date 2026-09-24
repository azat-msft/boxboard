using Boxboard.Models;

namespace Boxboard.Services;

public sealed record FocusTransition(int? PreviousIndex, int? FocusedIndex, PixelRect? ExpandedBounds);

public sealed class DesktopFocusController(DesktopWindowLayout layout)
{
    public async Task<FocusTransition?> HandleForegroundAsync(nint hwnd,
        IReadOnlyList<SessionWindow?> slots, CancellationToken ct = default)
    {
        int focused = -1;
        for (int index = 0; index < slots.Count; index++)
            if (slots[index]?.Identity.Handle == hwnd)
            {
                focused = index;
                break;
            }
        if (focused >= 0)
        {
            if (layout.FocusedIndex == focused)
                return null;
            var previous = layout.FocusedIndex;
            var expanded = await layout.ArrangeOverlayAsync(slots, focused, ct: ct);
            return new(previous, focused, expanded);
        }
        if (!layout.HasOverlay)
            return null;
        var restored = layout.FocusedIndex;
        await layout.RestoreGridAsync(slots, ct);
        return new(restored, null, null);
    }
}
