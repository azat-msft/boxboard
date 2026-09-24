using System.Collections.Specialized;
using System.Windows;
using Boxboard.Services;

namespace Boxboard;

public partial class LogWindow : Window
{
    private readonly ActivityLog _log;
    public LogWindow(ActivityLog log)
    {
        InitializeComponent();
        _log = log;
        DataContext = log;
        _log.Entries.CollectionChanged += OnEntry;
        Closed += (_, _) => _log.Entries.CollectionChanged -= OnEntry;
    }
    private void OnEntry(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is { Count: > 0 })
            LogList.ScrollIntoView(e.NewItems[^1]);
    }
}
