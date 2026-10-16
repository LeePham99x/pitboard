using Microsoft.Win32;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace Pitboard.Windows;

public interface IAppNotifications
{
    bool IsSupported();

    // A click while this process runs, which it gets only when it listens before it registers.
    void Listen(Action<IDictionary<string, string>> invoked);

    void Register(string displayName, Uri icon);

    // The arguments of the click that started this process, which come only after Register.
    IDictionary<string, string>? LaunchedBy();

    void Show(AppNotification notification);

    void Unregister();

    void UnregisterAll();
}

public sealed class SystemAppNotifications : IAppNotifications
{
    private static AppNotificationManager Manager => AppNotificationManager.Default;

    // Register names the class a click starts under the AppUserModelId's key, which it alone writes.
    public static bool Registered(string aumid)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\AppUserModelId\{aumid}");
        return key?.GetValue("CustomActivator") is string;
    }

    public bool IsSupported() => AppNotificationManager.IsSupported();

    public void Listen(Action<IDictionary<string, string>> invoked) =>
        Manager.NotificationInvoked += (_, args) => invoked(args.Arguments);

    public void Register(string displayName, Uri icon) => Manager.Register(displayName, icon);

    public IDictionary<string, string>? LaunchedBy() =>
        AppInstance.GetCurrent().GetActivatedEventArgs() is { Kind: ExtendedActivationKind.AppNotification } activated
            && activated.Data is AppNotificationActivatedEventArgs clicked
            ? clicked.Arguments
            : null;

    public void Show(AppNotification notification) => Manager.Show(notification);

    public void Unregister() => Manager.Unregister();

    public void UnregisterAll() => Manager.UnregisterAll();
}
