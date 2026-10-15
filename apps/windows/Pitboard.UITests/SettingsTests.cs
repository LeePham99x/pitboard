using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace Pitboard.UITests;

// The macOS PanesAndSettingsTests that are about Settings, on the window's settings page.
[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed class SettingsTests
{
    private static readonly TimeSpan Shortly = TimeSpan.FromSeconds(10);

    private static void OpenSettings(Launched launched)
    {
        launched.OpenWindow();
        launched.Select("SettingsItem");
    }

    private static AutomationElement Control(Launched launched, string automationId) =>
        launched.Find(found => found.ByAutomationId(automationId), automationId);

    private static ToggleState State(AutomationElement toggle) => toggle.Patterns.Toggle.Pattern.ToggleState.Value;

    // The model answers a change after the click, so each read after one waits.
    private static bool Becomes(AutomationElement toggle, ToggleState state) =>
        Retry.WhileFalse(() => State(toggle) == state, Shortly, TimeSpan.FromMilliseconds(250), ignoreException: true).Result;

    private static bool Enables(AutomationElement control, bool enabled) =>
        Retry.WhileFalse(() => control.IsEnabled == enabled, Shortly, TimeSpan.FromMilliseconds(250), ignoreException: true).Result;

    private static bool Says(Launched launched, string start, TimeSpan within) =>
        Retry.WhileFalse(
            () => Launched.Lines(launched.Window("Pitboard")).Any(line => line.StartsWith(start, StringComparison.Ordinal)),
            within, TimeSpan.FromMilliseconds(250), ignoreException: true).Result;

    // The fixture's stand-in takes it, so nothing is written to the Run key.
    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void StartAtSignIn()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        OpenSettings(launched);
        var toggle = Control(launched, "settings.startAtSignIn");

        Assert.AreEqual(ToggleState.Off, State(toggle));
        toggle.Patterns.Toggle.Pattern.Toggle();
        Assert.IsTrue(Becomes(toggle, ToggleState.On));
        toggle.Patterns.Toggle.Pattern.Toggle();
        Assert.IsTrue(Becomes(toggle, ToggleState.Off));
    }

    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void SwitchingBeforeAnAccountRunsOut()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        OpenSettings(launched);
        var toggle = Control(launched, "settings.autoSwitch");
        var share = Control(launched, "settings.autoSwitchAt");

        // Both are held back until the app's preferences have been read.
        Assert.IsTrue(Enables(toggle, true));
        Assert.AreEqual(ToggleState.Off, State(toggle));
        Assert.IsFalse(share.IsEnabled, "no share to change while it is off");
        Assert.IsTrue(launched.Gone(found => found.ByAutomationId("settings.autoSwitchStanding")), "nothing said while it is off");

        toggle.Patterns.Toggle.Pattern.Toggle();
        Assert.IsTrue(Becomes(toggle, ToggleState.On));
        Assert.IsTrue(Enables(share, true));
        launched.Named("Switch when a limit reaches 95%");
        Control(launched, "settings.autoSwitchStanding");

        share.Patterns.RangeValue.Pattern.SetValue(96);
        launched.Named("Switch when a limit reaches 96%");

        toggle.Patterns.Toggle.Pattern.Toggle();
        Assert.IsTrue(Becomes(toggle, ToggleState.Off));
        Assert.IsTrue(Enables(share, false));
        Assert.IsTrue(launched.Gone(found => found.ByAutomationId("settings.autoSwitchStanding")));
    }

    // The fixture's personal is on for warm-ups with no five-hour period running, so it warms up as the app starts.
    [TestMethod]
    [Ignore("W-warm: warming up Claude Code accounts on Windows")]
    public void WarmingUpAccounts()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "warmUp");
        launched.OpenWindow();
        Assert.IsTrue(Says(launched, "Warms up again at ", TimeSpan.FromSeconds(30)), "personal is not warmed up");

        launched.Select("SettingsItem");
        Control(launched, "settings.warm").Patterns.ExpandCollapse.Pattern.Expand();
        var personal = Control(launched, "settings.warm.claude/personal");
        var work = Control(launched, "settings.warm.claude/work");

        Assert.IsTrue(Enables(personal, true));
        Assert.AreEqual(ToggleState.On, State(personal));
        Assert.AreEqual(ToggleState.Off, State(work));
        Control(launched, "settings.warmFrom");
        Control(launched, "settings.warmStanding");

        work.Patterns.Toggle.Pattern.Toggle();
        Assert.IsTrue(Becomes(work, ToggleState.On));
    }

    [TestMethod]
    [Ignore("W25: Task Scheduler, which renews parked logins daily")]
    public void DailyRenewal()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        OpenSettings(launched);

        Control(launched, "settings.renewDaily").Patterns.Toggle.Pattern.Toggle();
        launched.Named("Every day");
        launched.Invoke("Renew Now");
        launched.Named("Renewed one.");
    }

    // The fixture puts no pitboard where a terminal looks, and the installer, not the app, puts one on the PATH.
    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void TheCommandLineSectionSaysWherePitboardIs()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        OpenSettings(launched);

        launched.Named("Not installed");
        Assert.IsTrue(Says(launched, "Pitboard’s installer puts pitboard on your PATH", Shortly));
        Assert.IsTrue(launched.Gone(found => found.ByName("Install Command Line Tool…")), "the section offers a button");
    }

    // A Debug build carries no update key, and the Updates section says so.
    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void UpdatesSayWhenTheBuildCannotUpdate()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        OpenSettings(launched);

        Assert.IsTrue(Says(launched, "This copy of Pitboard can", Shortly));
    }
}
