using System.ComponentModel;
using System.Runtime.InteropServices;
using Boxboard.Models;

namespace Boxboard.Services;

/// <summary>
/// Enumerates the physical monitors Windows currently reports and numbers them left to
/// right, so the numbers stay predictable. Adapter device names such as
/// <c>\\.\DISPLAY577</c> in a remote session carry no usable number, which is why the
/// position decides it and "Identify monitors" shows the result on screen.
/// </summary>
public sealed partial class MonitorShell : IMonitors
{
    public static MonitorShell Instance { get; } = new();

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var handles = new List<nint>();
        MonitorEnumCallback callback = (monitor, _, _, _) =>
        {
            handles.Add(monitor);
            return 1;
        };
        if (EnumDisplayMonitors(0, 0, callback, 0) == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        GC.KeepAlive(callback);

        var found = new List<(string Id, PixelRect Bounds, PixelRect WorkArea, bool Primary)>();
        foreach (var handle in handles)
        {
            var info = NewMonitorInfo();
            if (GetMonitorInfoW(handle, ref info) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            found.Add((ReadDevice(info), info.Monitor.ToPixels(), info.Work.ToPixels(), (info.Flags & 1) != 0));
        }
        if (found.Count == 0)
            throw new InvalidOperationException("Windows reported no monitors.");
        if (found.Select(monitor => monitor.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != found.Count)
            throw new InvalidOperationException("Windows reported duplicate monitor device names.");
        return Number(found);
    }

    /// <summary>Numbers monitors from 1, left to right and then top to bottom.</summary>
    internal static IReadOnlyList<MonitorInfo> Number(
        IEnumerable<(string Id, PixelRect Bounds, PixelRect WorkArea, bool Primary)> monitors) =>
        [.. monitors
            .OrderBy(monitor => monitor.Bounds.X)
            .ThenBy(monitor => monitor.Bounds.Y)
            .Select((monitor, index) =>
                new MonitorInfo(monitor.Id, index + 1, monitor.Bounds, monitor.WorkArea, monitor.Primary))];

    private static MonitorInfoEx NewMonitorInfo()
    {
        var info = default(MonitorInfoEx);
        info.Size = Marshal.SizeOf<MonitorInfoEx>();
        return info;
    }

    private static unsafe string ReadDevice(MonitorInfoEx info)
    {
        var span = new ReadOnlySpan<char>(info.Device, 32);
        var end = span.IndexOf('\0');
        var device = (end < 0 ? span : span[..end]).ToString();
        return device.Length == 0
            ? throw new InvalidOperationException("Windows reported a monitor without a device name.")
            : device;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int MonitorEnumCallback(nint monitor, nint deviceContext, nint rect, nint data);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int EnumDisplayMonitors(nint deviceContext, nint clip,
        MonitorEnumCallback callback, nint data);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    private static partial int GetMonitorInfoW(nint monitor, ref MonitorInfoEx info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly PixelRect ToPixels() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
        public fixed char Device[32];
    }
}
