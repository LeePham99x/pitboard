using System.Diagnostics;
using System.Globalization;

namespace Pitboard.Windows.Tests;

// Pitboard.StandInApp.exe beside the tests, in place of a Store app: the process list puts it and its console child in
// the package's family, with the id the core asks for, as ChatGPT and its codex would be.
internal sealed class StandInApp : IDisposable
{
    public const string Family = "Vendor.App_0000000000000";

    public const string Id = $"{Family}!App";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private StandInApp(Process app, Process child)
    {
        App = app;
        Child = child;
    }

    public Process App { get; }

    public Process Child { get; }

    public static StandInApp Start(bool honours)
    {
        var app = Process.Start(new ProcessStartInfo(
            Path.Combine(AppContext.BaseDirectory, "Pitboard.StandInApp.exe"), honours ? "honours" : "refuses")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        var said = app.StandardOutput.ReadLineAsync();
        if (!said.Wait(Patience) || said.Result?.Split(' ') is not ["ready", var child])
        {
            app.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"the stand-in app never said it was ready: '{(said.IsCompleted ? said.Result : null)}'");
        }

        return new StandInApp(app, Process.GetProcessById(int.Parse(child, CultureInfo.InvariantCulture)));
    }

    public WindowsApps Apps() => new(Listed, _ => { }, () => { });

    public void Dispose()
    {
        foreach (var process in new[] { Child, App })
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.Dispose();
        }
    }

    // This PC's processes as Windows lists them, these two in the package.
    private IReadOnlyList<AppProcess> Listed() =>
        [.. PackagedApps.Listed().Select(listed =>
            listed.Id == App.Id ? listed with { Family = Family, Aumid = Id }
            : listed.Id == Child.Id ? listed with { Family = Family }
            : listed)];
}
