using Boxboard.Models;

namespace Boxboard.Services;

public sealed class DesktopWindowLayout(ISessionWindows windows)
{
    private Guid? _overlayDesktop;
    private PixelRect? _overlayArea;
    private readonly WindowIdentity?[] _overlayIdentities = new WindowIdentity?[4];
    private readonly PixelRect?[] _overlayBaseBounds = new PixelRect?[4];
    private int? _overlayFocusedIndex;
    public bool HasOverlay => _overlayFocusedIndex is not null;
    public int? FocusedIndex => _overlayFocusedIndex;

    public async Task<IReadOnlyList<PixelRect>> ArrangeFocusAsync(
        IReadOnlyList<SessionWindow?> slots, int focusedIndex, int focusedPercent = 70,
        CancellationToken ct = default)
    {
        var (environment, observed) = Validate(slots, focusedIndex);
        var bounds = FourCellGeometry.Focus(environment.WorkArea, focusedIndex, focusedPercent);
        for (int index = 0; index < slots.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            if (slots[index] is { } selected)
            {
                var current = observed.Single(w => w.Identity == selected.Identity);
                await windows.PlaceAsync(current, bounds[index], ct);
            }
        }
        _overlayDesktop = null;
        _overlayArea = null;
        _overlayFocusedIndex = null;
        Array.Fill(_overlayIdentities, null);
        Array.Fill(_overlayBaseBounds, null);
        return bounds;
    }

    public async Task<PixelRect> ArrangeOverlayAsync(
        IReadOnlyList<SessionWindow?> slots, int focusedIndex, int focusedPercent = 70,
        CancellationToken ct = default)
    {
        var (environment, observed) = Validate(slots, focusedIndex);
        var expanded = FourCellGeometry.Overlay(environment.WorkArea, focusedIndex, focusedPercent);
        var sameLayout = _overlayDesktop == environment.DesktopId && _overlayArea == environment.WorkArea &&
            _overlayIdentities.SequenceEqual(slots.Select(slot => slot?.Identity));
        if (!sameLayout)
        {
            if (_overlayFocusedIndex is not null)
                throw new InvalidOperationException("Restore the focused window before changing the desktop or slot assignments.");
            for (int index = 0; index < slots.Count; index++)
            {
                if (slots[index] is not { } selected)
                {
                    _overlayBaseBounds[index] = null;
                    continue;
                }
                var original = windows.GetVisibleBounds(observed.Single(w => w.Identity == selected.Identity));
                if (!environment.WorkArea.Contains(original))
                    throw new InvalidOperationException($"{selected.Title} is outside the target monitor's work area.");
                _overlayBaseBounds[index] = original;
            }
        }
        for (int index = 0; index < slots.Count; index++)
        {
            if (index != focusedIndex && slots[index] is { } other &&
                _overlayBaseBounds[index] is { } original && !HasVisibleStrip(original, expanded))
                throw new InvalidOperationException(
                    $"Focusing this slot would hide {other.Title} completely; adjust the layout first.");
        }
        if (sameLayout && _overlayFocusedIndex is { } previous && previous != focusedIndex &&
            slots[previous] is { } previousWindow)
        {
            ct.ThrowIfCancellationRequested();
            await windows.PlaceAsync(observed.Single(w => w.Identity == previousWindow.Identity),
                _overlayBaseBounds[previous]!.Value, ct);
        }

        if (!sameLayout || _overlayFocusedIndex != focusedIndex)
        {
            ct.ThrowIfCancellationRequested();
            var focused = slots[focusedIndex]!;
            await windows.PlaceAsync(observed.Single(w => w.Identity == focused.Identity), expanded, ct);
        }
        _overlayDesktop = environment.DesktopId;
        _overlayArea = environment.WorkArea;
        for (int index = 0; index < slots.Count; index++)
            _overlayIdentities[index] = slots[index]?.Identity;
        _overlayFocusedIndex = focusedIndex;
        return expanded;
    }

    public async Task RestoreGridAsync(IReadOnlyList<SessionWindow?> slots, CancellationToken ct = default)
    {
        if (_overlayFocusedIndex is not { } focusedIndex)
            return;
        var (environment, observed) = Validate(slots, focusedIndex);
        if (_overlayDesktop != environment.DesktopId || _overlayArea != environment.WorkArea ||
            !_overlayIdentities.SequenceEqual(slots.Select(slot => slot?.Identity)))
            throw new InvalidOperationException("The desktop or slot assignments changed before focus could be restored.");
        var bounds = _overlayBaseBounds[focusedIndex] ??
            throw new InvalidOperationException("The original focused-window rectangle is unavailable.");
        ct.ThrowIfCancellationRequested();
        await windows.PlaceAsync(observed.Single(w => w.Identity == slots[focusedIndex]!.Identity), bounds, ct);
        _overlayFocusedIndex = null;
    }

    private (BoardEnvironment Environment, IReadOnlyList<SessionWindow> Observed) Validate(
        IReadOnlyList<SessionWindow?> slots, int focusedIndex)
    {
        if (slots.Count != 4)
            throw new ArgumentException("Exactly four slot positions are required.", nameof(slots));
        if (focusedIndex is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(focusedIndex));
        var environment = windows.GetEnvironment();
        if (!environment.CanInteract)
            throw new InvalidOperationException("Windows is locked or showing a secure desktop; no windows were moved.");
        if (slots[focusedIndex] is null)
            throw new InvalidOperationException("The focused slot has no bound client window.");

        var observed = windows.Enumerate();
        var identities = new HashSet<WindowIdentity>();
        foreach (var selected in slots.OfType<SessionWindow>())
        {
            if (!identities.Add(selected.Identity))
                throw new InvalidOperationException("The same client is assigned to multiple slots.");
            var current = observed.SingleOrDefault(candidate => candidate.Identity == selected.Identity &&
                string.Equals(candidate.Title, selected.Title, StringComparison.Ordinal));
            if (current is null || current.DesktopId != environment.DesktopId)
                throw new InvalidOperationException("A selected client changed identity or virtual desktop; no windows were moved.");
            if (current.Fullscreen)
                throw new InvalidOperationException("A selected client is fullscreen; no windows were moved.");
        }
        return (environment, observed);
    }

    private static bool HasVisibleStrip(PixelRect original, PixelRect overlay)
    {
        const int minimum = 100;
        return ((overlay.X - original.X >= minimum || original.Right - overlay.Right >= minimum)
            && original.Height >= minimum) ||
            ((overlay.Y - original.Y >= minimum || original.Bottom - overlay.Bottom >= minimum)
            && original.Width >= minimum);
    }
}
