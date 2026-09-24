using System.Runtime.InteropServices;
using Bevdox.Models;
using Boxboard.Models;

namespace Boxboard.Services;

public sealed record VirtualDesktopInfo(int Number, Guid Id, string Name, bool Available = true);

public static class VirtualDesktopShell
{
    private static readonly Guid ShellClsid = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid ManagerService = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
    private static readonly Guid ManagerIid = new("53F5CA0B-158F-4124-900C-057158060B27");
    private static readonly Guid ViewsIid = new("1841C6D7-4F9D-42C0-AF41-8747538F10E5");
    private static readonly Guid DesktopIid = new("3F07F4BE-B107-441A-AF0F-39D82529072C");

    public static IReadOnlyList<VirtualDesktopInfo> GetDesktops()
    {
        RequireSupportedWindows();
        var shell = CreateShell();
        try
        {
            var manager = GetService<IInternalDesktopManager>(shell, ManagerService, ManagerIid);
            try
            {
                Marshal.ThrowExceptionForHR(manager.GetDesktopCount(out var count));
                Marshal.ThrowExceptionForHR(manager.GetDesktops(out var pointer));
                if (pointer == 0)
                    throw new InvalidOperationException("Shell returned no virtual desktop list.");
                var desktops = GetObject<IObjectArray>(pointer);
                try
                {
                    Marshal.ThrowExceptionForHR(desktops.GetCount(out var actual));
                    if (actual != count || actual is < 1 or > 32)
                        throw new InvalidOperationException($"Invalid virtual desktop count: manager {count}, list {actual}.");
                    var result = new List<VirtualDesktopInfo>();
                    for (uint index = 0; index < count; index++)
                    {
                        Marshal.ThrowExceptionForHR(desktops.GetAt(index, in DesktopIid, out pointer));
                        if (pointer == 0)
                            throw new InvalidOperationException($"Desktop {index + 1} returned no interface.");
                        var desktop = GetObject<IVirtualDesktop>(pointer);
                        try
                        {
                            Marshal.ThrowExceptionForHR(desktop.GetId(out var id));
                            result.Add(new((int)index + 1, id, $"Desktop {index + 1}"));
                        }
                        finally { Marshal.ReleaseComObject(desktop); }
                    }
                    if (result.Any(desktop => desktop.Id == Guid.Empty) ||
                        result.Select(desktop => desktop.Id).Distinct().Count() != result.Count)
                        throw new InvalidOperationException("Shell returned duplicate or empty virtual desktop IDs.");
                    return result;
                }
                finally { Marshal.ReleaseComObject(desktops); }
            }
            finally { Marshal.ReleaseComObject(manager); }
        }
        finally { Marshal.ReleaseComObject(shell); }
    }

    public static Guid GetCurrentDesktopId()
    {
        RequireSupportedWindows();
        var shell = CreateShell();
        try
        {
            var manager = GetService<IInternalDesktopManager>(shell, ManagerService, ManagerIid);
            try
            {
                Marshal.ThrowExceptionForHR(manager.GetCurrentDesktop(out var pointer));
                if (pointer == 0)
                    throw new InvalidOperationException("Shell returned no current virtual desktop.");
                var desktop = GetObject<IVirtualDesktop>(pointer);
                try
                {
                    Marshal.ThrowExceptionForHR(desktop.GetId(out var id));
                    if (id == Guid.Empty)
                        throw new InvalidOperationException("Shell returned an empty current virtual desktop ID.");
                    return id;
                }
                finally { Marshal.ReleaseComObject(desktop); }
            }
            finally { Marshal.ReleaseComObject(manager); }
        }
        finally { Marshal.ReleaseComObject(shell); }
    }

    public static void MoveAssignedWindow(SessionWindow window, DevBoxInstance machine, Guid targetDesktop)
    {
        if (!string.Equals(window.Title, machine.OriginalName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected Windows App window does not match the assigned Dev Box.");
        using var windows = new NativeSessionWindows(window.Identity.Handle);
        if (!windows.Enumerate().Any(candidate => candidate.Identity == window.Identity &&
            candidate.DesktopId == window.DesktopId &&
            string.Equals(candidate.Title, window.Title, StringComparison.Ordinal)))
            throw new InvalidOperationException("The selected client identity changed before the desktop move.");
        var desktops = GetDesktops();
        var source = desktops.SingleOrDefault(desktop => desktop.Id == window.DesktopId)
            ?? throw new InvalidOperationException("The client's source virtual desktop no longer exists.");
        var target = desktops.SingleOrDefault(desktop => desktop.Id == targetDesktop)
            ?? throw new InvalidOperationException("The selected target virtual desktop no longer exists.");
        if (source.Id == target.Id)
            return;
        RunCore(window.Identity.Handle, source.Id, move: true, machine.OriginalName,
            source.Number, target.Number, report: null);
    }

    public static void Run(nint hwnd, Guid sourceDesktop, bool move, string expectedName = "007playground",
        int sourceDesktopNumber = 3, int targetDesktopNumber = 2)
    {
        if (!string.Equals(expectedName, "007playground", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(expectedName, "Azdo1", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Desktop movement is restricted to the two approved Dev Boxes.");
        if ((sourceDesktopNumber, targetDesktopNumber) is not ((3, 2) or (2, 3)))
            throw new InvalidOperationException("The experiment only permits movement between desktops 2 and 3.");
        RunCore(hwnd, sourceDesktop, move, expectedName, sourceDesktopNumber, targetDesktopNumber,
            Console.WriteLine);
    }

    private static void RunCore(nint hwnd, Guid sourceDesktop, bool move, string expectedName,
        int sourceDesktopNumber, int targetDesktopNumber, Action<string>? report)
    {
        RequireSupportedWindows();
        var shell = CreateShell();
        try
        {
            var manager = GetService<IInternalDesktopManager>(shell, ManagerService, ManagerIid);
            var views = GetService<IViewCollection>(shell, ViewsIid, ViewsIid);
            try
            {
                Marshal.ThrowExceptionForHR(manager.GetDesktopCount(out var count));
                Marshal.ThrowExceptionForHR(manager.GetDesktops(out var arrayPointer));
                if (arrayPointer == 0) throw new InvalidOperationException("Shell returned a null desktop array.");
                var array = GetObject<IObjectArray>(arrayPointer);
                var desktops = new List<nint>();
                try
                {
                    Marshal.ThrowExceptionForHR(array.GetCount(out var arrayCount));
                    if (count != arrayCount || count < 2 || count > 32 ||
                        sourceDesktopNumber < 1 || targetDesktopNumber < 1 ||
                        sourceDesktopNumber > count || targetDesktopNumber > count)
                        throw new InvalidOperationException($"Unexpected virtual desktop count: manager {count}, array {arrayCount}.");
                    var ids = new List<Guid>();
                    for (uint index = 0; index < count; index++)
                    {
                        Marshal.ThrowExceptionForHR(array.GetAt(index, in DesktopIid, out var pointer));
                        if (pointer == 0) throw new InvalidOperationException($"Desktop {index + 1} returned a null interface.");
                        desktops.Add(pointer);
                        var desktop = (IVirtualDesktop)Marshal.GetTypedObjectForIUnknown(pointer, typeof(IVirtualDesktop));
                        try
                        {
                            Marshal.ThrowExceptionForHR(desktop.GetId(out var id));
                            ids.Add(id);
                            report?.Invoke($"Desktop {index + 1}: {id}");
                        }
                        finally { Marshal.ReleaseComObject(desktop); }
                    }
                    if (ids.Distinct().Count() != ids.Count ||
                        ids[sourceDesktopNumber - 1] != sourceDesktop ||
                        ids[targetDesktopNumber - 1] == sourceDesktop)
                        throw new InvalidOperationException("The selected client is not on the expected source desktop, or the desktop list is inconsistent.");

                    Marshal.ThrowExceptionForHR(views.GetViewForHwnd(hwnd, out var view));
                    if (view == 0) throw new InvalidOperationException($"Shell returned no application view for {expectedName}.");
                    try
                    {
                        Marshal.ThrowExceptionForHR(manager.CanMoveViewBetweenDesktops(view, out var canMove));
                        report?.Invoke($"Shell reports {expectedName} can move: {canMove != 0}");
                        if (!move) return;
                        if (canMove == 0)
                            throw new InvalidOperationException($"Shell refused to move the {expectedName} application view.");
                        Marshal.ThrowExceptionForHR(manager.MoveViewToDesktop(view, desktops[targetDesktopNumber - 1]));
                        VerifyDesktop(hwnd, ids[targetDesktopNumber - 1], expectedName);
                        report?.Invoke($"{expectedName} moved to desktop {targetDesktopNumber}; the active desktop was not switched.");
                    }
                    finally { Marshal.Release(view); }
                }
                finally
                {
                    foreach (var pointer in desktops) Marshal.Release(pointer);
                    Marshal.ReleaseComObject(array);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(views);
                Marshal.ReleaseComObject(manager);
            }
        }
        finally { Marshal.ReleaseComObject(shell); }
    }

    private static void RequireSupportedWindows()
    {
        if (Environment.OSVersion.Version.Build < 26100)
            throw new PlatformNotSupportedException("Virtual desktop management requires Windows 11 build 26100 or later.");
    }

    private static IShellServices CreateShell() =>
        (IShellServices)Activator.CreateInstance(Type.GetTypeFromCLSID(ShellClsid, throwOnError: true)!)!;

    private static T GetService<T>(IShellServices shell, Guid serviceId, Guid interfaceId) where T : class
    {
        Marshal.ThrowExceptionForHR(shell.QueryService(in serviceId, in interfaceId, out var pointer));
        if (pointer == 0) throw new InvalidOperationException($"Shell returned no {typeof(T).Name} service.");
        return GetObject<T>(pointer);
    }

    private static T GetObject<T>(nint pointer) where T : class
    {
        try { return (T)Marshal.GetTypedObjectForIUnknown(pointer, typeof(T)); }
        finally { Marshal.Release(pointer); }
    }

    private static void VerifyDesktop(nint hwnd, Guid expected, string expectedName)
    {
        var manager = (IPublicDesktopManager)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), throwOnError: true)!)!;
        try
        {
            Marshal.ThrowExceptionForHR(manager.GetWindowDesktopId(hwnd, out var actual));
            if (actual != expected)
                throw new InvalidOperationException($"Shell returned success but {expectedName} is on {actual}, not {expected}.");
        }
        finally { Marshal.ReleaseComObject(manager); }
    }
}

[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellServices
{
    [PreserveSig] int QueryService(in Guid service, in Guid iid, out nint result);
}

[ComImport, Guid("53F5CA0B-158F-4124-900C-057158060B27"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInternalDesktopManager
{
    [PreserveSig] int GetDesktopCount(out uint count);
    [PreserveSig] int MoveViewToDesktop(nint view, nint desktop);
    [PreserveSig] int CanMoveViewBetweenDesktops(nint view, out int canMove);
    [PreserveSig] int GetCurrentDesktop(out nint desktop);
    [PreserveSig] int GetDesktops(out nint desktops);
}

[ComImport, Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IViewCollection
{
    [PreserveSig] int GetViews(out nint views);
    [PreserveSig] int GetViewsByZOrder(out nint views);
    [PreserveSig] int GetViewsByAppUserModelId(nint appId, out nint views);
    [PreserveSig] int GetViewForHwnd(nint hwnd, out nint view);
}

[ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectArray
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, in Guid iid, out nint result);
}

[ComImport, Guid("3F07F4BE-B107-441A-AF0F-39D82529072C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktop
{
    [PreserveSig] int IsViewVisible(nint view, out int visible);
    [PreserveSig] int GetId(out Guid id);
}

[ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPublicDesktopManager
{
    [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint hwnd, out int onCurrentDesktop);
    [PreserveSig] int GetWindowDesktopId(nint hwnd, out Guid desktop);
    [PreserveSig] int MoveWindowToDesktop(nint hwnd, in Guid desktop);
}
