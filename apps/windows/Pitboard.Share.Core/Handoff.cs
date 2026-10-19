using System.Diagnostics;

namespace Pitboard.Share.Core;

// As the MSI's pitboard command starts it, since LaunchUriAsync from a package identity started no handler (CF8).
public static class Handoff
{
    private const string App = "Pitboard.exe";

    public static ProcessStartInfo StartInfo(string folder, string link)
    {
        var start = new ProcessStartInfo(Path.Combine(folder, App)) { UseShellExecute = false };
        start.ArgumentList.Add(link);
        return start;
    }

    public static void Start(string folder, string link) => Process.Start(StartInfo(folder, link))?.Dispose();
}
