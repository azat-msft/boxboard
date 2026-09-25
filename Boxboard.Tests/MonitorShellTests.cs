using Boxboard.Models;
using Boxboard.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Boxboard.Tests;

[TestClass]
public sealed class MonitorShellTests
{
    [TestMethod]
    [DataRow(@"\\.\DISPLAY1", 1)]
    [DataRow(@"\\.\DISPLAY12", 12)]
    [DataRow(@"\\.\DISPLAY3 ", 3)]
    public void ParseNumber_UsesTheTrailingDisplayNumberWindowsSettingsShows(string device, int expected) =>
        Assert.AreEqual(expected, MonitorShell.ParseNumber(device, fallback: 99));

    [TestMethod]
    [DataRow(@"\\.\DISPLAY")]
    [DataRow("")]
    public void ParseNumber_FallsBackWhenTheDeviceNameHasNoNumber(string device) =>
        Assert.AreEqual(7, MonitorShell.ParseNumber(device, fallback: 7));

    [TestMethod]
    public void GetMonitors_ReportsDistinctNumberedMonitorsWithExactlyOnePrimary()
    {
        var monitors = MonitorShell.Instance.GetMonitors();
        Assert.IsNotEmpty(monitors);
        Assert.AreEqual(1, monitors.Count(monitor => monitor.Primary));
        Assert.HasCount(monitors.Count, monitors.Select(monitor => monitor.Id).Distinct().ToList());
        foreach (var monitor in monitors)
        {
            Assert.IsGreaterThan(0, monitor.Number);
            Assert.IsGreaterThan(0, monitor.Bounds.Width);
            Assert.IsGreaterThan(0, monitor.WorkArea.Height);
            Assert.IsTrue(monitor.Bounds.Contains(monitor.WorkArea),
                $"{monitor.Name} work area {monitor.WorkArea} is outside its bounds {monitor.Bounds}.");
            Assert.AreEqual($"Monitor {monitor.Number}", monitor.Name);
        }
        CollectionAssert.AreEqual(monitors.Select(monitor => monitor.Number).Order().ToArray(),
            monitors.Select(monitor => monitor.Number).ToArray());
    }

    [TestMethod]
    public void Description_ShowsResolutionAndMarksThePrimaryAndDisconnectedMonitors()
    {
        var primary = new MonitorInfo(@"\\.\DISPLAY1", 1, new(0, 0, 2560, 1440), new(0, 0, 2560, 1400), true);
        var second = primary with { Id = @"\\.\DISPLAY2", Number = 2, Primary = false };
        Assert.AreEqual("2560 × 1440 · primary", primary.Description);
        Assert.AreEqual("2560 × 1440", second.Description);
        Assert.AreEqual("Disconnected", (second with { Available = false }).Description);
    }
}
