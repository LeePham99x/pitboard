namespace Pitboard.Windows;

// The session's end lets go of what Pitboard holds; Exit, Restart Manager and an update's installer also stop the
// downloads, then exit.
public sealed class Lifetime(
    AppModel model, NotifyIcon icon, IUpdates updates, WindowsNotifications notifications, Action stopDownloads, Action exit)
{
    public void EndSession()
    {
        icon.Remove();
        updates.Shutdown();
        model.Shutdown();
        notifications.Stop();
    }

    public void End()
    {
        stopDownloads();
        EndSession();
        exit();
    }
}
