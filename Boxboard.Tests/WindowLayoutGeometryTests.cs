using Boxboard.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Boxboard.Tests;

[TestClass]
public sealed class WindowLayoutGeometryTests
{
    [TestMethod]
    [DataRow(3840, 2088)]
    [DataRow(1919, 1079)]
    public void SingleWindow_FillsWorkAreaWithOneFramedWindow(int width, int height)
    {
        var area = new PixelRect(100, -200, width, height);
        var windows = WindowLayoutGeometry.Divide(area, WindowLayoutMode.SingleWindow);
        Assert.HasCount(1, windows);
        Assert.AreEqual(area, windows[0]);
    }

    [TestMethod]
    [DataRow(3840, 2088)]
    [DataRow(1919, 1079)]
    public void SideBySide_UsesTwoFullHeightColumns(int width, int height)
    {
        var area = new PixelRect(100, 200, width, height);
        var windows = WindowLayoutGeometry.Divide(area, WindowLayoutMode.SideBySide);
        Assert.HasCount(2, windows);
        Assert.AreEqual(new PixelRect(area.X, area.Y, width / 2, height), windows[0]);
        Assert.AreEqual(new PixelRect(area.X + width / 2, area.Y, width - width / 2, height), windows[1]);
        Assert.AreEqual(area.Right, windows[1].Right);
    }

    [TestMethod]
    [DataRow(3840, 2088)]
    [DataRow(1919, 1079)]
    public void LargeLeftTwoStackedRight_CoversOneWorkAreaWithoutOverlap(int width, int height)
    {
        var area = new PixelRect(-500, 100, width, height);
        var windows = WindowLayoutGeometry.Divide(area, WindowLayoutMode.LargeLeftTwoStackedRight);
        Assert.HasCount(3, windows);
        Assert.AreEqual(new PixelRect(area.X, area.Y, width / 2, height), windows[0]);
        Assert.AreEqual(new PixelRect(area.X + width / 2, area.Y,
            width - width / 2, height / 2), windows[1]);
        Assert.AreEqual(new PixelRect(area.X + width / 2, area.Y + height / 2,
            width - width / 2, height - height / 2), windows[2]);
        Assert.IsTrue(windows.All(area.Contains));
        Assert.AreEqual((long)width * height, windows.Sum(window => (long)window.Width * window.Height));
    }

    [TestMethod]
    public void InvalidModeAndWorkArea_AreRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            WindowLayoutGeometry.Divide(new(0, 0, 100, 100), WindowLayoutMode.SideBySide));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            WindowLayoutGeometry.Divide(new(0, 0, 100, 100), WindowLayoutMode.LargeLeftTwoStackedRight));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            WindowLayoutGeometry.Divide(new(0, 0, 99, 100), WindowLayoutMode.SingleWindow));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            WindowLayoutGeometry.Divide(new(0, 0, 3840, 2088), (WindowLayoutMode)99));
    }
}
