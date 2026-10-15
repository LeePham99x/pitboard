using System.Collections;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Pitboard.Core;
using Pitboard.Windows;

namespace Pitboard.App;

public static class Program
{
#if DEBUG
    private static readonly Identity Running = new(Build.Debug);
#else
    private static readonly Identity Running = new(Build.Release);
#endif

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Shell.Name(Running);
        var environment = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(variable => (string)variable.Key, variable => variable.Value as string ?? "");
#if DEBUG
        var fixture = environment.GetValueOrDefault("PITBOARD_FIXTURE");
#else
        string? fixture = null;
#endif
        // Where the core makes it: Rust's temp_dir and .NET's GetTempPath both ask GetTempPath2W.
        var fixtureFolder = fixture is null ? null : Path.Combine(Path.GetTempPath(), "pitboard-fixture");
        ReadFailure? refusal = null;
        AppInstance? running = null;
        var start = Launching.Decide(
            args,
            () => refusal = PitboardFfiMethods.LaunchRefusal(environment),
            () =>
            {
                var holder = AppInstance.FindOrRegisterForKey(Running.InstanceKey(fixtureFolder));
                running = holder.IsCurrent ? null : holder;
                return holder.IsCurrent;
            });
        switch (start)
        {
            case Start.Refused:
                Shell.Refuse(refusal!.Message);
                return 1;
            case Start.Redirect:
                Redirection.To(running!);
                return 0;
            case Start.Unregister or Start.Dropped:
                return 0;
        }

        var started = new AppStart(
            Running, environment, fixture, Running.DataFolder(Shell.LocalAppData(), fixtureFolder));
        App? app = null;
        DispatcherQueue? queue = null;
        // Raised on a thread of its own.
        AppInstance.GetCurrent().Activated += (_, _) => queue?.TryEnqueue(() => app?.Reopened());
        Application.Start(_ =>
        {
            queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            app = new App(started);
        });
        return 0;
    }
}
