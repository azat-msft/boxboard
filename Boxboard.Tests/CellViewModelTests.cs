using Boxboard.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Boxboard.Tests;

[TestClass]
public sealed class CellViewModelTests
{
    [TestMethod]
    public void ChangedWindowSnapshot_RefreshesStateAndPreservesSelectionThroughItemsReset()
    {
        var model = new CellViewModel
        {
            Slot = new(Guid.NewGuid(), 1), MachineName = "synthetic", MachineDetails = "synthetic"
        };
        var original = new SessionWindow(new(1, 10, 100), "synthetic", Guid.NewGuid(),
            new(0, 0, 1920, 1080), true, false, true);
        model.Windows = [original];
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(model.Windows))
                model.SelectedWindow = null; // WPF selection reset when ItemsSource changes.
        };
        var updated = original with { Fullscreen = false, Maximized = false, Bounds = new(20, 20, 500, 400) };
        model.Windows = [updated];

        Assert.AreSame(updated, model.Windows.Single());
        Assert.AreSame(updated, model.SelectedWindow);
    }
}
