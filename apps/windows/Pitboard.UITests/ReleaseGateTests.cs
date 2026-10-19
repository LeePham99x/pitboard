using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using Microsoft.Win32;

namespace Pitboard.UITests;

[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed class ReleaseGateTests
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string Notifier = @"Software\Classes\AppUserModelId\Pitboard.Pitboard";

    private static readonly TimeSpan Shortly = TimeSpan.FromSeconds(30);

    [TestMethod]
    public void TheReleaseBuildRefusesUntilPitboardForWindowsIsReleased()
    {
        if (Builds.ProductMajor() > 0)
        {
            Assert.Inconclusive(
                "the release gate is open at 1.x, where TheReleasedBuildTakesBackWhatItRegisteredOnUnregister starts the Release build");
        }

        using var launched = Launched.Start(Builds.Release);

        var window = launched.Window("Pitboard");
        StringAssert.Contains(Launched.Said(window), "Pitboard for Windows is not released yet.");
        Launched.Dismiss(window);
        Assert.IsTrue(launched.Process.WaitForExit(TimeSpan.FromSeconds(10)), "Pitboard went on running");
        Assert.AreEqual(1, launched.Process.ExitCode);
        Assert.AreEqual("", string.Join(", ", launched.Written()));
    }

    // What the MSI's full uninstall finds: the notifications registered and start at sign-in on, which --unregister
    // takes back, and the data folder, which RemoveFolderEx takes after it. AGENTS.md allows it only on the fresh
    // account test-as-standard-user.ps1 makes, so anything there before the start is somebody else's and stays.
    [TestMethod]
    public void TheReleasedBuildTakesBackWhatItRegisteredOnUnregister()
    {
        if (Builds.ProductMajor() == 0)
        {
            Assert.Inconclusive("the Release build refuses at 0.x, which TheReleaseBuildRefusesUntilPitboardForWindowsIsReleased holds");
        }

        var dataFolder = Launched.DataFolders[0];
        Assert.AreEqual("", string.Join(", ", Registered(activator: null)), "this account is not a fresh one");
        Assert.IsFalse(Directory.Exists(dataFolder), $"this account is not a fresh one: {dataFolder} is there");

        using var launched = Launched.Start(Builds.Release);
        launched.Window("Pitboard");
        launched.Select("SettingsItem");
        var toggle = launched.Find(found => found.ByAutomationId("settings.startAtSignIn"), "settings.startAtSignIn");
        toggle.Patterns.Toggle.Pattern.Toggle();
        Assert.IsTrue(
            Retry.WhileFalse(
                () => toggle.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On, Shortly, TimeSpan.FromMilliseconds(250),
                ignoreException: true).Result,
            $"start at sign-in stayed off: {launched.Shown()}");
        var activator = Activator() ?? throw new AssertFailedException($"Pitboard registered no notifications: {launched.Shown()}");
        CollectionAssert.AreEquivalent(
            new[] { $@"{Run}\Pitboard", Notifier, Class(activator) }, Registered(activator).ToArray());

        HiddenWindows.AskToEnd(launched.Process);
        Assert.IsTrue(launched.Process.WaitForExit(Shortly), $"Pitboard went on running: {launched.State()}");
        Assert.AreEqual(0, launched.Process.ExitCode, launched.State());
        using var unregistered = launched.Again("--unregister");
        Assert.IsTrue(unregistered.Process.WaitForExit(Shortly), $"--unregister went on running: {unregistered.State()}");
        Assert.AreEqual(0, unregistered.Process.ExitCode, unregistered.State());

        Assert.AreEqual("", string.Join(", ", Registered(activator)));
        if (Directory.Exists(dataFolder))
        {
            Directory.Delete(dataFolder, recursive: true);
        }
    }

    // The class Register names for a click, which Register alone writes.
    private static Guid? Activator()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Notifier);
        return Guid.TryParse(key?.GetValue("CustomActivator") as string, out var named) ? named : null;
    }

    private static string Class(Guid activator) => $@"Software\Classes\CLSID\{activator:B}";

    private static IEnumerable<string> Registered(Guid? activator)
    {
        using (var run = Registry.CurrentUser.OpenSubKey(Run))
        {
            if (run?.GetValue("Pitboard") is not null)
            {
                yield return $@"{Run}\Pitboard";
            }
        }

        string[] names = activator is { } named ? [Notifier, Class(named)] : [Notifier];
        foreach (var name in names)
        {
            using var key = Registry.CurrentUser.OpenSubKey(name);
            if (key is not null)
            {
                yield return name;
            }
        }
    }
}
