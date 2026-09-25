using Boxboard.Models;
using Boxboard.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Boxboard.Tests;

[TestClass]
public sealed class MonitorShellTests
{
    [TestMethod]
    public void Number_OrdersMonitorsLeftToRightRegardlessOfTheAdapterDeviceName()
    {
        var numbered = MonitorShell.Number(
        [
            (@"\\.\DISPLAY577", new PixelRect(2560, 0, 1920, 1080), new PixelRect(2560, 0, 1920, 1040), false),
            (@"\\.\DISPLAY3", new PixelRect(-1920, 0, 1920, 1080), new PixelRect(-1920, 0, 1920, 1040), false),
            (@"\\.\DISPLAY1", new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1400), true)
        ]);
        CollectionAssert.AreEqual(new[] { @"\\.\DISPLAY3", @"\\.\DISPLAY1", @"\\.\DISPLAY577" },
            numbered.Select(monitor => monitor.Id).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, numbered.Select(monitor => monitor.Number).ToArray());
        Assert.AreEqual("Monitor 2", numbered[1].Name);
    }

    [TestMethod]
    public void Number_BreaksTiesOnTheSameColumnByTheTopEdge()
    {
        var numbered = MonitorShell.Number(
        [
            ("lower", new PixelRect(0, 1080, 1920, 1080), new PixelRect(0, 1080, 1920, 1040), false),
            ("upper", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), true)
        ]);
        CollectionAssert.AreEqual(new[] { "upper", "lower" }, numbered.Select(monitor => monitor.Id).ToArray());
    }

    [TestMethod]
    public void GetMonitors_ReportsDistinctNumberedMonitorsWithExactlyOnePrimary()
    {
        var monitors = MonitorShell.Instance.GetMonitors();
        Assert.IsNotEmpty(monitors);
        Assert.AreEqual(1, monitors.Count(monitor => monitor.Primary));
        Assert.HasCount(monitors.Count, monitors.Select(monitor => monitor.Id).Distinct().ToList());
        CollectionAssert.AreEqual(Enumerable.Range(1, monitors.Count).ToArray(),
            monitors.Select(monitor => monitor.Number).ToArray());
        foreach (var monitor in monitors)
        {
            Assert.IsGreaterThan(0, monitor.Bounds.Width);
            Assert.IsGreaterThan(0, monitor.WorkArea.Height);
            Assert.IsTrue(monitor.Bounds.Contains(monitor.WorkArea),
                $"{monitor.Name} work area {monitor.WorkArea} is outside its bounds {monitor.Bounds}.");
            Assert.AreEqual($"Monitor {monitor.Number}", monitor.Name);
        }
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
