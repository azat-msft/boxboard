namespace Boxboard.Models;

public static class WindowLayoutGeometry
{
    public static IReadOnlyList<PixelRect> Divide(PixelRect area, WindowLayoutMode mode) => mode switch
    {
        WindowLayoutMode.Quadrants => FourCellGeometry.Divide(area, gap: 0),
        WindowLayoutMode.SideBySide when area.Width >= 200 && area.Height >= 100 =>
        [
            new(area.X, area.Y, area.Width / 2, area.Height),
            new(area.X + area.Width / 2, area.Y, area.Width - area.Width / 2, area.Height)
        ],
        WindowLayoutMode.SideBySide => throw new ArgumentOutOfRangeException(nameof(area),
            "The monitor is too small for two full-height windows."),
        WindowLayoutMode.LargeLeftTwoStackedRight when area.Width >= 200 && area.Height >= 200 =>
        [
            new(area.X, area.Y, area.Width / 2, area.Height),
            new(area.X + area.Width / 2, area.Y, area.Width - area.Width / 2, area.Height / 2),
            new(area.X + area.Width / 2, area.Y + area.Height / 2,
                area.Width - area.Width / 2, area.Height - area.Height / 2)
        ],
        WindowLayoutMode.LargeLeftTwoStackedRight => throw new ArgumentOutOfRangeException(nameof(area),
            "The monitor is too small for one large and two stacked windows."),
        WindowLayoutMode.SingleWindow when area.Width >= 100 && area.Height >= 100 => [area],
        WindowLayoutMode.SingleWindow => throw new ArgumentOutOfRangeException(nameof(area),
            "The monitor is too small for a single window."),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
