namespace Boxboard.Models;

/// <summary>
/// A physical display. <see cref="Id"/> is the Windows adapter device name
/// (<c>\\.\DISPLAY1</c>), which is also what the Windows display settings number.
/// </summary>
public sealed record MonitorInfo(string Id, int Number, PixelRect Bounds, PixelRect WorkArea,
    bool Primary, bool Available = true)
{
    public string Name => $"Monitor {Number}";
    public string Description => Available
        ? $"{Bounds.Width} × {Bounds.Height}{(Primary ? " · primary" : "")}"
        : "Disconnected";
}

/// <summary>
/// Identifies one slot layout: a virtual desktop plus the monitor it is pinned to.
/// A null <see cref="MonitorId"/> is an unpinned layout saved before monitor pinning existed.
/// </summary>
public readonly record struct LayoutKey(Guid DesktopId, string? MonitorId = null)
{
    public static implicit operator LayoutKey(Guid desktopId) => new(desktopId);
}

public interface IMonitors
{
    IReadOnlyList<MonitorInfo> GetMonitors();
}
