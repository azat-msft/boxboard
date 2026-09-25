using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Boxboard.Models;

namespace Boxboard.Services;

/// <summary>
/// Enumerates the physical monitors Windows currently reports. The numbers match the
/// ones the Windows display settings page shows, because both come from the adapter
/// device name (<c>\\.\DISPLAY1</c>).
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

        var monitors = new List<MonitorInfo>();
        foreach (var handle in handles)
        {
            var info = NewMonitorInfo();
            if (GetMonitorInfoW(handle, ref info) == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            var device = ReadDevice(info);
            monitors.Add(new(device, ParseNumber(device, monitors.Count + 1),
                info.Monitor.ToPixels(), info.Work.ToPixels(), (info.Flags & 1) != 0));
        }
        if (monitors.Count == 0)
            throw new InvalidOperationException("Windows reported no monitors.");
        if (monitors.Select(monitor => monitor.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != monitors.Count)
            throw new InvalidOperationException("Windows reported duplicate monitor device names.");
        return [.. monitors.OrderBy(monitor => monitor.Number)];
    }

    /// <summary>
    /// Windows names adapters <c>\\.\DISPLAY1</c>, <c>\\.\DISPLAY2</c> and so on, and the
    /// display settings page shows that same trailing number.
    /// </summary>
    internal static int ParseNumber(string device, int fallback)
    {
        var digits = device.AsSpan().TrimEnd();
        var start = digits.Length;
        while (start > 0 && char.IsAsciiDigit(digits[start - 1]))
            start--;
        return start < digits.Length &&
            int.TryParse(digits[start..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
            number > 0 ? number : fallback;
    }

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
