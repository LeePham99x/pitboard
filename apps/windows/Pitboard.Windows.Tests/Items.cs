using Pitboard.Core;

namespace Pitboard.Windows.Tests;

// What the model presents, made by hand, so each view test says only what it is about.
internal static class Items
{
    public static AccountItem Account(string provider, string label, bool inUse = false) =>
        new(
            Id: $"{provider}:{label}", Provider: provider, Qualified: $"{provider}/{label}", Title: label,
            Email: $"{label}@example.com", Plan: null, MenuTitle: label, SpokenName: label,
            Spoken: inUse ? $"{label}, in use" : label, InUse: inUse, NeedsSignIn: false, Unplaced: false,
            Switching: false,
            Action: inUse ? null : new ItemAction("Use", $"Use {label}", new Intent.SwitchTo($"{provider}/{label}")),
            Summary: "", Problem: null, StaleNote: null, Pace: null, ParkedNote: null, WarmNote: null, Help: null,
            Limits: [], Offers: [], Windows: [], Forget: null);

    public static LimitRow Limit(double percent, PaceStanding standing = PaceStanding.Even) =>
        new(
            Name: "5-hour", Short: "5h", Percent: percent, Figure: $"{percent:0}%",
            Level: percent >= 90 ? UsageLevel.Out : percent >= 70 ? UsageLevel.Low : UsageLevel.Plenty,
            Resets: "resets in 2h 10m",
            Pace: new LimitPace(Expected: 30, Standing: standing, Said: standing == PaceStanding.Over ? "over pace" : "", Help: "Even use would be at 30% by now"),
            Spoken: $"5-hour limit {percent:0}% used, resets in 2 hours 10 minutes");

    public static ItemOffer Forget(string qualified) =>
        new(
            Title: "Forget…", Intent: new Intent.Forget(qualified), Enabled: true,
            Confirm: new Question("Forget this account?", "Its parked login is deleted.", "Forget"), Checked: null);

    public static PanelNotice Notice(string id, params NoticeAction[] actions) =>
        new(
            Id: id, Severity: Severity.Info, SpokenSeverity: "Note", Title: "Switched Codex to spare",
            Lines: ["Any codex session started before this switch keeps using main."], Until: null, UntilLabel: null,
            Actions: actions);

    public static NoticeAction Dismiss(string provider) =>
        new(Title: "Dismiss", Intent: new Intent.DismissSwitch(provider), Dismisses: true, Switches: false, Enabled: true, Confirm: null);

    public static AccountSection Section(string provider, string? heading, params AccountItem[] accounts) =>
        new(Id: provider, Heading: heading, Accounts: accounts);
}
