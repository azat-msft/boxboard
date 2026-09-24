namespace Boxboard.Models;

public sealed record WindowLayoutChoice(WindowLayoutMode Mode, string Name);

public sealed class DesktopCardViewModel
{
    public static IReadOnlyList<WindowLayoutChoice> LayoutChoices { get; } =
    [
        new(WindowLayoutMode.Quadrants, "2 × 2"),
        new(WindowLayoutMode.SideBySide, "Side by side"),
        new(WindowLayoutMode.LargeLeftTwoStackedRight, "Large left + 2"),
        new(WindowLayoutMode.SingleWindow, "One window")
    ];

    public required Guid DesktopId { get; init; }
    public required string Name { get; init; }
    public required WindowLayoutMode Mode { get; init; }
    public required bool KeepConnected { get; init; }
    public required bool CanEdit { get; init; }
    public required bool PendingApply { get; init; }
    public required IReadOnlyList<CellViewModel> Cells { get; init; }
    public required string HiddenAssignments { get; init; }
    public bool HasHiddenAssignments => HiddenAssignments.Length > 0;
    public IReadOnlyList<WindowLayoutChoice> Modes => LayoutChoices;
    public WindowLayoutChoice SelectedMode => LayoutChoices.Single(choice => choice.Mode == Mode);
}
