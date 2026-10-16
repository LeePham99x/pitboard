using Microsoft.Windows.AppNotifications;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class UnregisteringTests
{
    private const string Installed = @"C:\Users\Dana Lee\AppData\Local\Programs\Pitboard\Pitboard.exe";

    [TestMethod]
    public void TheUninstallDeletesTheRunValueNamingThisPitboardExeInAnyCase()
    {
        var key = new StandInRunKey();
        key.Values["Pitboard"] = $"\"{Installed.ToUpperInvariant()}\" --sign-in";
        var notifications = new StandInNotifications();

        Unregistering.Run(Build.Release, Installed, key, notifications, registered: () => true);

        Assert.IsFalse(key.Values.ContainsKey("Pitboard"));
        Assert.AreEqual("unregister all", string.Join(", ", notifications.Asked));
    }

    [TestMethod]
    public void ItLeavesARunValueNamingAnotherPitboardExe()
    {
        var key = new StandInRunKey();
        key.Values["Pitboard"] = @"""C:\Users\Dana Lee\AppData\Local\Programs\Other\Pitboard.exe"" --sign-in";

        Unregistering.Run(Build.Release, Installed, key, new StandInNotifications(), registered: () => false);

        Assert.AreEqual(@"""C:\Users\Dana Lee\AppData\Local\Programs\Other\Pitboard.exe"" --sign-in", key.Values["Pitboard"]);
        CollectionAssert.DoesNotContain(key.Asked, "remove Pitboard");
    }

    // UnregisterAll makes the AppUserModelId key as it looks for the class, so notifications never registered are left alone.
    [TestMethod]
    public void NotificationsThatNeverRegisteredAreNotAsked()
    {
        var notifications = new StandInNotifications();

        Unregistering.Run(Build.Release, Installed, new StandInRunKey(), notifications, registered: () => false);

        Assert.IsEmpty(notifications.Asked);
    }

    [TestMethod]
    public void ARunKeyItCannotReadStillLetsTheNotificationsGo()
    {
        var key = new StandInRunKey { ReadRefuses = new UnauthorizedAccessException("Access is denied.") };
        var notifications = new StandInNotifications();

        Unregistering.Run(Build.Release, Installed, key, notifications, registered: () => true);

        Assert.AreEqual("unregister all", string.Join(", ", notifications.Asked));
    }

    [TestMethod]
    public void ADebugBuildRegisteredNothingAndTouchesNothing()
    {
        var key = new StandInRunKey();
        key.Values["Pitboard"] = $"\"{Installed}\" --sign-in";
        var notifications = new StandInNotifications();

        Unregistering.Run(Build.Debug, Installed, key, notifications, registered: () => true);

        Assert.IsEmpty(key.Asked);
        Assert.IsEmpty(notifications.Asked);
    }

    private sealed class StandInNotifications : IAppNotifications
    {
        public List<string> Asked { get; } = [];

        public bool IsSupported() => throw new AssertFailedException("IsSupported was asked");

        public void Listen(Action<IDictionary<string, string>> invoked) => throw new AssertFailedException("Listen was asked");

        public void Register(string displayName, Uri icon) => throw new AssertFailedException("Register was asked");

        public IDictionary<string, string>? LaunchedBy() => throw new AssertFailedException("LaunchedBy was asked");

        public void Show(AppNotification notification) => throw new AssertFailedException("Show was asked");

        public void Unregister() => throw new AssertFailedException("Unregister was asked");

        public void UnregisterAll() => Asked.Add("unregister all");
    }
}
