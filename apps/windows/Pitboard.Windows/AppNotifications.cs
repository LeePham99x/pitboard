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

// What Register writes under HKCU for an unpackaged app: its AppUserModelId's key, naming the class a click starts.
public interface INotificationRegistration
{
    string? Activator();

    void Delete(string activator);
}

public sealed class SystemAppNotifications : IAppNotifications
{
    private static AppNotificationManager Manager => AppNotificationManager.Default;

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

public sealed class RegistryNotificationRegistration(string aumid) : INotificationRegistration
{
    private string Key => $@"Software\Classes\AppUserModelId\{aumid}";

    // Register names the class a click starts under the AppUserModelId's key, which it alone writes.
    public string? Activator()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return key?.GetValue("CustomActivator") as string;
    }

    // The class's key only by a GUID, so no other name reaches CLSID itself.
    public void Delete(string activator)
    {
        if (Guid.TryParse(activator, out var named))
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\CLSID\{named:B}", throwOnMissingSubKey: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree(Key, throwOnMissingSubKey: false);
    }
}
