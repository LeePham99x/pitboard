using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Pitboard.Core;
using Pitboard.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Shell;

namespace Pitboard.Probe;

// The app's own adapters, measured by hand on the session machine, under an AppUserModelID of the probe's own so
// an installed Pitboard keeps its notification registration.
public static class Program
{
    private const string Aumid = "Pitboard.Probe";

    // pitboard-probe's, which the owner puts in each throwaway account's profile folder.
    private const string Marker = "pitboard-probe-throwaway.marker";

    private const string Usage = "Usage: Pitboard.Probe notify | unregister | package-of --pid <process id>";

    private const string AumidKey = $@"Software\Classes\AppUserModelId\{Aumid}";

    private static readonly RunOutNotice RunOut = new(
        Id: "pitboard-probe-run-out", Title: "work has no 5-hour limit left", Subtitle: null,
        Body: "spare has 80% of its own left.", SwitchTo: "claude/spare");

    private static int Main(string[] args)
    {
        if (!File.Exists(Path.Combine(Profile(), Marker)))
        {
            Console.Error.WriteLine(
                $"This account carries no {Marker} in its profile folder: Pitboard.Probe runs only in a throwaway account the owner marked.");
            return 2;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        PInvoke.SetCurrentProcessExplicitAppUserModelID(Aumid).ThrowOnFailure();
        return args switch
        {
            ["notify"] => Notify(),
            [Launching.Notified, ..] => StartedByAClick(),
            ["unregister"] => Unregister(),
            ["package-of", "--pid", var given] when uint.TryParse(given, CultureInfo.InvariantCulture, out var pid) => PackageOf(pid),
            _ => Refuse(Usage),
        };
    }

    private static WindowsNotifications Notifications() =>
        new(Build.Release, () => new SystemAppNotifications(), Path.Combine(AppContext.BaseDirectory, WindowsNotifications.IconFile));

    // Posts one notification and waits for its Switch, or for Enter to quit before it.
    private static int Notify()
    {
        var notifications = Notifications();
        var clicked = new TaskCompletionSource<Intent>(TaskCreationOptions.RunContinuationsAsynchronously);
        notifications.Start(intent => clicked.TrySetResult(intent));
        if (!Registered(notifications))
        {
            return 1;
        }

        try
        {
            notifications.Post(RunOut);
            Console.WriteLine($"Posted, tagged {WindowsNotifications.Tag(RunOut.Id)}. Choose Switch, or press Enter to quit first.");
            var entered = Task.Run(Console.ReadLine);
            Console.WriteLine(Task.WaitAny(clicked.Task, entered) == 0 ? $"Clicked: {Said(clicked.Task.Result)}." : "Quit before a click.");
            return 0;
        }
        finally
        {
            notifications.Stop();
        }
    }

    // COM starts the probe this way for a click on a notification it posted before it quit.
    private static int StartedByAClick()
    {
        var notifications = Notifications();
        var launchedBy = notifications.Start(intent => Console.WriteLine($"Clicked: {Said(intent)}."));
        if (!Registered(notifications))
        {
            return 1;
        }

        Console.WriteLine($"Started by a click: {Said(launchedBy)}. Press Enter to close.");
        Console.ReadLine();
        notifications.Stop();
        return 0;
    }

    private static bool Registered(WindowsNotifications notifications)
    {
        Console.WriteLine(notifications.Off is { } off ? $"Not registered: {off}" : "Registered.");
        return notifications.Off is null;
    }

    private static string Said(Intent? intent) =>
        intent switch
        {
            Intent.SwitchTo switched => $"Switch to {switched.Qualified}",
            Intent.ShowWindow => "the notification itself",
            _ => "nothing",
        };

    // UnregisterAll makes the AppUserModelId key as it looks for the class the key names, so it runs only after a Register.
    private static int Unregister()
    {
        string? activator;
        using (var key = Registry.CurrentUser.OpenSubKey(AumidKey))
        {
            activator = key?.GetValue("CustomActivator") as string;
        }

        if (activator is null)
        {
            Console.WriteLine($"Nothing to unregister: HKCU\\{AumidKey} names no class.");
            return 0;
        }

        Left("Before", activator);
        new SystemAppNotifications().UnregisterAll();
        Left("After", activator);
        return 0;
    }

    // What Register wrote and what Windows keeps for the probe's notifications, by name only.
    private static void Left(string when, string activator)
    {
        string[] keys =
        [
            AumidKey,
            $@"Software\Classes\CLSID\{activator}",
            $@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\{Aumid}",
        ];
        foreach (var name in keys)
        {
            using var key = Registry.CurrentUser.OpenSubKey(name);
            Console.WriteLine($"{when}: HKCU\\{name} {(key is null ? "is gone" : "is there")}.");
        }
    }

    private static int PackageOf(uint pid)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process.IsInvalid)
        {
            return Refuse($"Process {pid} could not be opened: {Marshal.GetLastPInvokeErrorMessage()}");
        }

        var length = 0u;
        var error = PInvoke.GetPackageFullName(process, ref length, default);
        if (error == WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE)
        {
            Console.WriteLine($"Process {pid} has no package identity.");
            return 0;
        }

        if (error == WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
        {
            var name = new char[length];
            error = PInvoke.GetPackageFullName(process, ref length, name);
            if (error == WIN32_ERROR.ERROR_SUCCESS)
            {
                Console.WriteLine($"Process {pid} runs as {new string(name, 0, (int)length - 1)}.");
                return 0;
            }
        }

        return Refuse($"GetPackageFullName failed for process {pid}: {error}");
    }

    private static int Refuse(string said)
    {
        Console.Error.WriteLine(said);
        return 2;
    }

    // The known folder, which no variable moves.
    private static unsafe string Profile()
    {
        PInvoke.SHGetKnownFolderPath(PInvoke.FOLDERID_Profile, KNOWN_FOLDER_FLAG.KF_FLAG_DONT_VERIFY, null, out var path).ThrowOnFailure();
        try
        {
            return path.ToString();
        }
        finally
        {
            PInvoke.CoTaskMemFree(path);
        }
    }
}
