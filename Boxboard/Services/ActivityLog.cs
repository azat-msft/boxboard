using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace Boxboard.Services;

public sealed record ActivityEntry(DateTimeOffset Time, string Context, string Message);

public sealed partial class ActivityLog(Dispatcher dispatcher, int capacity = 300)
{
    public ObservableCollection<ActivityEntry> Entries { get; } = [];

    public void Write(string context, string message)
    {
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => Write(context, message));
            return;
        }
        Entries.Add(new(DateTimeOffset.Now, Sanitize(context), Sanitize(message)));
        while (Entries.Count > Math.Max(1, capacity))
            Entries.RemoveAt(0);
    }

    public static string Sanitize(string text)
    {
        var safe = Secret().Replace(Url().Replace(text, "[URL omitted]"), "$1[redacted]");
        return safe.Length > 1000 ? safe[..1000] + "..." : safe;
    }

    [GeneratedRegex(@"(?i)\b(?:https?://|ms-[a-z]+:)\S+")]
    private static partial Regex Url();
    [GeneratedRegex(@"(?i)(?:\b((?:access_token|refresh_token|client_secret|password|authorization)\s*[:=]\s*)(?:Bearer\s+)?\S+|\b(Bearer\s+)\S+)")]
    private static partial Regex Secret();
}
