using Boxboard.Models;

namespace Boxboard.Services;

public sealed record DesktopFocusLayout(Guid DesktopId, DesktopFocusController Controller,
    IReadOnlyList<SessionWindow?> Slots);
public sealed record RoutedFocusTransition(Guid DesktopId, FocusTransition Transition);

public static class DesktopFocusRouter
{
    public static async Task<IReadOnlyList<RoutedFocusTransition>> RouteAsync(nint hwnd,
        IReadOnlyList<DesktopFocusLayout> layouts, CancellationToken ct = default)
    {
        if (layouts.Select(layout => layout.DesktopId).Distinct().Count() != layouts.Count ||
            layouts.Count(layout => layout.Slots.Any(window => window?.Identity.Handle == hwnd)) > 1)
            throw new InvalidOperationException("The foreground window or desktop layout has an ambiguous identity.");
        var results = new List<RoutedFocusTransition>();
        foreach (var layout in layouts)
        {
            ct.ThrowIfCancellationRequested();
            var transition = await layout.Controller.HandleForegroundAsync(hwnd, layout.Slots, ct);
            if (transition is not null)
                results.Add(new(layout.DesktopId, transition));
        }
        return results;
    }
}
