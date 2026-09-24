using Bevdox.Auth;
using Bevdox.Models;
using Bevdox.Services;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var auth = new AuthService();
var configStore = new ConfigStore();
var discovery = new DevBoxDiscovery(auth);
var launcher = new DevBoxLauncher(auth);

// Load cached config
var config = await configStore.LoadAsync(cts.Token);

// Background refresh with progress reporting
string refreshStatus = "Starting...";
bool refreshDone = false;
// Signal when refresh status changes so the UI can update immediately
var statusChanged = new SemaphoreSlim(0);
var progress = new Progress<string>(s =>
{
    refreshStatus = s;
    statusChanged.Release();
});

Task<List<DevBoxInstance>>? refreshTask = Task.Run(async () =>
{
    try { return await discovery.DiscoverAsync(progress, cts.Token); }
    finally
    {
        refreshDone = true;
        statusChanged.Release();
    }
}, cts.Token);

if (config.Instances.Count == 0)
{
    // No cache — must wait for discovery
    Console.WriteLine("Bevdox - DevBox RDP Launcher");
    Console.Write($"  {refreshStatus}");
    while (!refreshTask.IsCompleted)
    {
        await Task.Delay(200, cts.Token);
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write($"  {refreshStatus,-60}");
    }
    Console.WriteLine();

    try
    {
        var freshInstances = await refreshTask;
        config = ConfigStore.Merge(config, freshInstances);
        await configStore.SaveAsync(config, cts.Token);
        refreshTask = null;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Failed: {ex.Message}");
        return 1;
    }
}

if (config.Instances.Count == 0)
{
    Console.WriteLine("  No Dev Box instances found.");
    return 0;
}

// Main loop
int selectedIndex = 0;
bool redraw = true;
string lastRenderedStatus = "";
Task<ConsoleKeyInfo>? keyTask = null;

while (!cts.Token.IsCancellationRequested)
{
    var sorted = config.Instances.OrderBy(i => i.EffectiveName, StringComparer.OrdinalIgnoreCase).ToList();
    if (selectedIndex >= sorted.Count) selectedIndex = sorted.Count - 1;

    // Check if background refresh completed since last draw
    if (refreshTask is not null && refreshDone && !redraw)
    {
        try
        {
            var freshInstances = await refreshTask;
            config = ConfigStore.Merge(config, freshInstances);
            await configStore.SaveAsync(config, cts.Token);
        }
        catch (Exception ex)
        {
            refreshStatus = $"Refresh failed: {ex.Message}";
        }
        refreshTask = null;
        refreshDone = false;
        redraw = true;
        continue;
    }

    // Check if status text changed — update just the status line
    if (!redraw && refreshStatus != lastRenderedStatus)
    {
        UpdateStatusLine(config, refreshTask is not null, refreshStatus);
        lastRenderedStatus = refreshStatus;
    }

    if (redraw)
    {
        Console.Clear();
        Console.WriteLine("Bevdox - DevBox RDP Launcher");
        Console.WriteLine();
        DisplayStatusLine(config, refreshTask is not null, refreshStatus);
        lastRenderedStatus = refreshStatus;
        Console.WriteLine();
        DisplayMenu(sorted, selectedIndex);
        Console.WriteLine();
        Console.Write("  ↑↓ Navigate  Enter/1-9 Launch  N Rename  R Reload  Q Quit");
        redraw = false;
    }

    // Await key press or status change — no polling
    keyTask ??= Task.Run(() => Console.ReadKey(intercept: true));
    var statusWait = statusChanged.WaitAsync(cts.Token);

    var completed = await Task.WhenAny(keyTask, statusWait);
    if (completed == statusWait)
    {
        // Status changed — drain any extra signals and loop to update display
        while (statusChanged.CurrentCount > 0)
            await statusChanged.WaitAsync();
        continue;
    }

    // Key was pressed
    var key = await keyTask;
    keyTask = null;

    // Handle digit keys (1-9) for direct selection
    int digitValue = key.Key switch
    {
        >= ConsoleKey.D1 and <= ConsoleKey.D9 => key.Key - ConsoleKey.D0,
        >= ConsoleKey.NumPad1 and <= ConsoleKey.NumPad9 => key.Key - ConsoleKey.NumPad0,
        _ => -1
    };

    if (digitValue >= 1 && digitValue <= sorted.Count)
    {
        selectedIndex = digitValue - 1;
        var selected = sorted[selectedIndex];
        Console.WriteLine();
        try
        {
            await launcher.LaunchAsync(selected, cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Launch failed: {ex.Message}");
            Console.Write("  Press any key to continue...");
            Console.ReadKey(intercept: true);
            redraw = true;
            continue;
        }
        goto exit;
    }

    switch (key.Key)
    {
        case ConsoleKey.UpArrow:
            if (selectedIndex > 0)
            {
                selectedIndex--;
                redraw = true;
            }
            break;

        case ConsoleKey.DownArrow:
            if (selectedIndex < sorted.Count - 1)
            {
                selectedIndex++;
                redraw = true;
            }
            break;

        case ConsoleKey.Enter:
        {
            var selected = sorted[selectedIndex];
            Console.WriteLine();
            try
            {
                await launcher.LaunchAsync(selected, cts.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Launch failed: {ex.Message}");
                Console.Write("  Press any key to continue...");
                Console.ReadKey(intercept: true);
                redraw = true;
                break;
            }
            goto exit;
        }

        case ConsoleKey.Q:
            goto exit;

        case ConsoleKey.R:
        {
            if (refreshTask is not null)
                break;
            refreshDone = false;
            refreshStatus = "Starting...";
            refreshTask = Task.Run(async () =>
            {
                try { return await discovery.DiscoverAsync(progress, cts.Token); }
                finally
                {
                    refreshDone = true;
                    statusChanged.Release();
                }
            }, cts.Token);
            redraw = true;
            break;
        }

        case ConsoleKey.N:
        {
            Console.WriteLine();
            await HandleRenameAsync(sorted, selectedIndex, config, configStore, cts.Token);
            redraw = true;
            break;
        }
    }
}

exit:
// Wait for background refresh before exiting
if (refreshTask is not null)
{
    try
    {
        Console.WriteLine();
        Console.Write($"  ⟳ {refreshStatus}");
        while (!refreshTask.IsCompleted)
        {
            await Task.Delay(200);
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write($"  ⟳ {refreshStatus,-60}");
        }
        var freshInstances = await refreshTask;
        var merged = ConfigStore.Merge(config, freshInstances);
        await configStore.SaveAsync(merged, cts.Token);
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.WriteLine($"  ✓ {refreshStatus,-60}");
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        Console.WriteLine($"\n  ✗ Refresh failed: {ex.Message}");
    }
}
return 0;

// --- Helper methods ---

static void DisplayStatusLine(BevdoxConfig config, bool refreshing, string refreshStatus)
{
    var cacheText = "";
    if (config.LastRefreshUtc is not null)
    {
        var ago = DateTimeOffset.UtcNow - config.LastRefreshUtc.Value;
        cacheText = ago.TotalMinutes < 1 ? "Cache: just now" :
            ago.TotalHours < 1 ? $"Cache: {(int)ago.TotalMinutes} min ago" :
            $"Cache: {(int)ago.TotalHours}h {ago.Minutes}m ago";
    }

    var statusText = refreshing ? $"  │ ⟳ {refreshStatus}" :
        refreshStatus.StartsWith("Refresh failed:", StringComparison.Ordinal) ? $"  │ {refreshStatus}" : "";
    Console.WriteLine($"  {cacheText}{statusText}");
}

static void UpdateStatusLine(BevdoxConfig config, bool refreshing, string refreshStatus)
{
    // Status line is on row 2 (0=title, 1=blank, 2=status)
    var (savedLeft, savedTop) = (Console.CursorLeft, Console.CursorTop);
    Console.SetCursorPosition(0, 2);
    DisplayStatusLine(config, refreshing, refreshStatus);
    Console.SetCursorPosition(savedLeft, savedTop);
}

static void DisplayMenu(List<DevBoxInstance> instances, int selectedIndex)
{
    Console.WriteLine($"  {"#",-4} {"Name",-30} {"Project",-20} {"State",-12} {"Location"}");
    Console.WriteLine($"  {"─",-4} {"─",-30} {"─",-20} {"─",-12} {"─"}");

    for (int i = 0; i < instances.Count; i++)
    {
        var d = instances[i];
        bool isSelected = i == selectedIndex;
        var stateColor = d.State.Equals("Running", StringComparison.OrdinalIgnoreCase)
            ? ConsoleColor.Green : ConsoleColor.DarkGray;

        if (isSelected)
        {
            Console.BackgroundColor = ConsoleColor.DarkCyan;
            Console.ForegroundColor = ConsoleColor.White;
        }

        Console.Write(isSelected ? "> " : "  ");
        Console.Write($"{i + 1,-3} {d.EffectiveName,-30} {d.ProjectName,-20} ");

        if (!isSelected)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = stateColor;
            Console.Write($"{d.State,-12}");
            Console.ForegroundColor = prev;
        }
        else
        {
            Console.Write($"{d.State,-12}");
        }

        Console.Write($" {d.Location}");

        if (isSelected)
            Console.ResetColor();

        Console.WriteLine();
    }
}

static async Task HandleRenameAsync(List<DevBoxInstance> sorted, int selectedIndex, BevdoxConfig config, ConfigStore configStore, CancellationToken ct)
{
    var target = sorted[selectedIndex];
    Console.Write($"  New name for '{target.EffectiveName}' (empty to reset): ");
    var newName = Console.ReadLine()?.Trim();

    target.DisplayName = string.IsNullOrEmpty(newName) ? null : newName;
    await configStore.SaveAsync(config, ct);
    Console.WriteLine("  Renamed.");
}
