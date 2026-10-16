using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Pitboard.Core;

namespace Pitboard.Windows;

public sealed class WindowsNotifications(Build build, Func<IAppNotifications> system, string icon) : Notifications
{
    public const string DisplayName = "Pitboard";

    public const string IconFile = "Pitboard.ico";

    public const string SwitchArgument = "switch";

    private readonly Lock _lock = new();
    private IAppNotifications? _registered;

    // Why nothing is posted, once Start has asked.
    public string? Off { get; private set; }

    // Register takes the icon as a file, which Pitboard.exe carries beside it.
    public static WindowsNotifications For(Build build, string folder) =>
        new(build, () => new SystemAppNotifications(), Path.Combine(folder, IconFile));

    // In the quickstart's order: listen, register, then read the click that started this process.
    public Intent? Start(Action<Intent> clicked)
    {
        if (build is Build.Debug)
        {
            Off = "A Debug build posts no notifications.";
            return null;
        }

        var notifications = system();
        try
        {
            if (!notifications.IsSupported())
            {
                Off = "Windows offers this process no app notifications.";
                return null;
            }

            notifications.Listen(arguments => clicked(Read(arguments)));
            notifications.Register(DisplayName, new Uri(icon));
        }
        catch (Exception error) when (Refused(error))
        {
            Off = error.Message;
            return null;
        }

        lock (_lock)
        {
            _registered = notifications;
        }

        try
        {
            return notifications.LaunchedBy() is { } launched ? Read(launched) : null;
        }
        catch (Exception error) when (Refused(error))
        {
            return null;
        }
    }

    // On the model's thread.
    public void Post(RunOutNotice notice)
    {
        IAppNotifications? registered;
        lock (_lock)
        {
            registered = _registered;
        }

        registered?.Show(Notification(notice));
    }

    // Before Pitboard exits, so a later click starts it again.
    public void Stop()
    {
        lock (_lock)
        {
            try
            {
                _registered?.Unregister();
            }
            catch (Exception error) when (Refused(error))
            {
                // COM revokes the process's registration as it exits anyway.
            }

            _registered = null;
        }
    }

    // A tag holds 64 characters, and a warm-up's notice is told by its message.
    public static string Tag(string id) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)));

    // Title, tool and body, in the macOS notification's order.
    public static AppNotification Notification(RunOutNotice notice)
    {
        var builder = new AppNotificationBuilder().AddText(notice.Title);
        if (notice.Subtitle is { } subtitle)
        {
            builder.AddText(subtitle);
        }

        builder.AddText(notice.Body).SetTag(Tag(notice.Id));
        if (notice.SwitchTo is { } account)
        {
            builder.AddButton(new AppNotificationButton("Switch").AddArgument(SwitchArgument, account));
        }

        return builder.BuildNotification();
    }

    // A click on the notification itself opens the window on its accounts, the notification's context.
    public static Intent Read(IDictionary<string, string> arguments) =>
        arguments.TryGetValue(SwitchArgument, out var account)
            ? new Intent.SwitchTo(account)
            : new Intent.ShowWindow(Pane.Accounts);

    private static bool Refused(Exception error) =>
        error is COMException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or IOException;
}
