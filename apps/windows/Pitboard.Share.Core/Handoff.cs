using System.ComponentModel;
using System.Diagnostics;

namespace Pitboard.Share.Core;

// As the MSI's pitboard command starts it, since LaunchUriAsync from a package identity started no handler (CF8).
public static class Handoff
{
    public const string App = "Pitboard.exe";

    public static ProcessStartInfo StartInfo(string folder, string link)
    {
        var start = new ProcessStartInfo(Path.Combine(folder, App)) { UseShellExecute = false };
        start.ArgumentList.Add(link);
        return start;
    }

    public static bool Start(string folder, string link)
    {
        try
        {
            using var started = Process.Start(StartInfo(folder, link));
            return started is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
