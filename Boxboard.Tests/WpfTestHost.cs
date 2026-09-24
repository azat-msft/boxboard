using System.Windows;
using System.Windows.Threading;

namespace Boxboard.Tests;

internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> Dispatcher = new(CreateDispatcher);

    public static async Task RunAsync(Func<Task> action, TimeSpan timeout)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Dispatcher.Value.InvokeAsync(new Action(async () =>
        {
            try
            {
                await action();
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        }));
        await done.Task.WaitAsync(timeout);
    }

    private static Dispatcher CreateDispatcher()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                var app = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                    Resources = new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Boxboard;component/Styles.xaml")
                    }
                };
                ready.SetResult(app.Dispatcher);
                app.Run();
            }
            catch (Exception ex)
            {
                ready.TrySetException(ex);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }
}
