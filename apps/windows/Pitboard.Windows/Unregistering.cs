using System.Runtime.InteropServices;
using System.Security;

namespace Pitboard.Windows;

// What the MSI's full uninstall asks of Pitboard.exe --unregister: the two registrations Pitboard made itself, which no
// table of the MSI removes. Each is tried whatever became of the other.
public static class Unregistering
{
    public static void Run(Build build, string exe, IRunKey run, IAppNotifications notifications, Func<bool> registered)
    {
        if (build is Build.Debug)
        {
            return;
        }

        try
        {
            if (RunValue.Names(run.Value(RunValue.Name), exe))
            {
                run.Remove(RunValue.Name);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
        }

        try
        {
            // UnregisterAll makes the AppUserModelId key as it looks for the class the key names.
            if (registered())
            {
                notifications.UnregisterAll();
            }
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or SecurityException)
        {
        }
    }
}
