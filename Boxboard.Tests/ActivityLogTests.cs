using System.Windows.Threading;
using Boxboard.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Boxboard.Tests;

[TestClass]
public sealed class ActivityLogTests
{
    [TestMethod]
    public void Retention_KeepsOnlyLatestEntries()
    {
        var log = new ActivityLog(Dispatcher.CurrentDispatcher, capacity: 3);
        for (int i = 0; i < 10; i++) log.Write("Slot 4", $"Phase {i}");
        Assert.HasCount(3, log.Entries);
        Assert.AreEqual("Phase 7", log.Entries[0].Message);
        Assert.AreEqual("Phase 9", log.Entries[^1].Message);
    }
    [TestMethod]
    [DataRow("https://example.invalid/path?secret=abc")]
    [DataRow("ms-avd:connect?resourceid=abc&user=private")]
    [DataRow("client_secret=abc")]
    [DataRow("Authorization: Bearer abc")]
    public void Sanitization_DoesNotKeepRawConnectionUrlsOrSecrets(string sensitive)
    {
        var result = ActivityLog.Sanitize($"Failed: {sensitive}");
        Assert.DoesNotContain("abc", result);
        Assert.DoesNotContain("private", result);
        Assert.DoesNotContain("example.invalid", result);
    }
}
