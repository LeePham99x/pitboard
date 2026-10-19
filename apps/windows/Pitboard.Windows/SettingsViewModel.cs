using System.Collections.ObjectModel;
using System.ComponentModel;
using Pitboard.Core;

namespace Pitboard.Windows;

public sealed record SettingsLink(string Title, string Url);

// What the settings page shows and sends, by the macOS Settings' rules. A setter sends only what differs from what the
// model says, so a control set to the model's value sends nothing.
public sealed partial class SettingsViewModel : INotifyPropertyChanged
{
    public const string SignInNote = "Settings > Apps > Startup can turn it off too.";

    public const string TurnedOffNote = "Turned off in Settings > Apps > Startup, where you can turn it on again.";

    // Settings > Apps > Startup, by the URI Microsoft's "Launch the Windows Settings app" lists.
    public const string StartupSettings = "ms-settings:startupapps";

    public const string BetaNote = "A beta updates itself to 1.0.0 once that is out. Install a later beta by hand.";

    public const string NewTerminals = "Pitboard’s installer puts pitboard on your PATH, which terminals opened after it ran see.";

    public const string WhatItDoes =
        "The command line does everything the app does, and more: pitboard status in a script, and pitboard repair "
        + "for a parked login Pitboard’s records have lost track of.";

    public const string Copyright = "Copyright 2026 the Pitboard authors";

    public const string NoWarmAccount = "No Claude Code account is enrolled yet.";

    private const int MinutesInADay = 24 * 60;

    public static readonly SettingsLink[] Links =
    [
        new("usepitboard.com", "https://usepitboard.com"),
        new("Source code on GitHub", "https://github.com/datlechin/pitboard"),
        new("Report a problem", "https://github.com/datlechin/pitboard/issues"),
        new("Apache License 2.0", "https://github.com/datlechin/pitboard/blob/main/LICENSE"),
    ];

    private readonly AppModel _model;
    private readonly StartAtSignIn _start;
    private readonly IUpdates _updates;

    public SettingsViewModel(AppModel model, StartAtSignIn start, IUpdates updates, string version, string? notificationsOff)
    {
        _model = model;
        _start = start;
        _updates = updates;
        Version = version;
        NotificationsOff = notificationsOff ?? "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private MachineShown Machine => _model.Machine;

    private AutoSwitchShown Auto => Machine.AutoSwitch;

    private WarmShown Warm => Machine.Warm;

    private ScheduleShown Schedule => Machine.Schedule;

    private CommandLineShown CommandLine => Machine.CommandLine;

    public bool StartsAtSignIn => _start.On;

    public bool CanStartAtSignIn => _start.Refusal is null && !_start.TurnedOff;

    public string StartupNote => _start.Refusal ?? (_start.TurnedOff ? TurnedOffNote : SignInNote);

    public string? StartupFailed => _start.Failed;

    public bool OffersStartupSettings => _start.TurnedOff;

    // Why a Release build posts no notifications; empty where it posts them.
    public string NotificationsOff { get; }

    public bool HasNotificationsOff => NotificationsOff.Length > 0;

    public bool AutoSwitch => Auto.On;

    public bool CanAutoSwitch => Auto.Enabled;

    public string AutoSwitchStanding => Auto.Standing ?? "";

    public bool HasAutoSwitchStanding => Auto.Standing is not null;

    public string AutoSwitchNote => Auto.Note;

    public double Share => Auto.At;

    public double LowestShare => Auto.Lowest;

    public double HighestShare => Auto.Highest;

    // The control's name, which says the share it holds; its card's header stays put.
    public string ShareLabel => Auto.AtLabel;

    public string ShareRange => $"From {Auto.Lowest}% to {Auto.Highest}%";

    public bool CanSetShare => Auto.Enabled && Auto.On;

    public string WarmRange => Warm.RangeLabel;

    public string WarmStanding => Warm.Standing ?? "";

    public bool HasWarmStanding => Warm.Standing is not null;

    public TimeSpan? WarmFrom => TimeOfDay(Warm.From);

    public TimeSpan? WarmTo => TimeOfDay(Warm.To);

    public bool CanWarm => Warm.Enabled;

    public ObservableCollection<WarmAccountRow> WarmAccounts { get; } = [];

    public bool NoWarmAccounts => Warm.Accounts.Length == 0;

    public string WarmNote => Warm.Note;

    public bool RenewsDaily => Schedule.On;

    public bool CanRenewDaily => Schedule.Enabled;

    public string RenewalAbout => Machine.Renewal.About;

    public string ScheduleNote => Schedule.Note ?? "";

    public bool HasScheduleNote => Schedule.Note is not null;

    public string? ScheduleFailed => Schedule.Failed;

    public bool HasRuns => Schedule.Runs is not null;

    public string Runs => Schedule.Runs ?? "";

    public bool HasScheduledIn => Schedule.ScheduledIn is not null;

    public string ScheduledIn => Schedule.ScheduledIn ?? "";

    public string RenewalNote => Machine.Renewal.Note;

    public bool CanRenewNow => !Machine.Renewal.Renewing;

    public bool LookingForCommandLine => CommandLine.InTerminal is null;

    public string InTerminal => CommandLine.InTerminal ?? "";

    public string UpdateNote => CommandLine.UpdateNote ?? "";

    public bool HasUpdateNote => CommandLine.UpdateNote is not null;

    public string TerminalNote => TerminalNoteFor(CommandLine) ?? "";

    public bool HasTerminalNote => TerminalNoteFor(CommandLine) is not null;

    public bool UpdatesAvailable => _updates.Available;

    public bool ChecksAutomatically => _updates.ChecksAutomatically;

    public string UpdatesState => _updates.Waiting is { } waiting ? $"Pitboard {waiting} is ready to install." : Versions.Named(Version);

    public string[] UpdatesNoteLines => UpdatesNotes(_updates, Version);

    public string Version { get; }

    public static string[] UpdatesNotes(IUpdates updates, string version) =>
        [.. updates.Refusal is { } refusal ? new[] { refusal } : [], .. Versions.IsBeta(version) ? new[] { BetaNote } : []];

    public static string? TerminalNoteFor(CommandLineShown shown) => shown.Found is FoundCommandLine.Nowhere ? NewTerminals : null;

    // 24:00, the end of a day, shows as midnight, as the macOS pickers show it.
    public static TimeSpan TimeOfDay(ushort minutes) => TimeSpan.FromMinutes(minutes % MinutesInADay);

    public static ushort Minutes(TimeSpan time) => (ushort)(((time.Hours * 60) + time.Minutes) % MinutesInADay);

    // A NumberBox holds NaN once its text is cleared.
    public static byte? ShareOf(double value, AutoSwitchShown auto) =>
        double.IsNaN(value) ? null : (byte)Math.Clamp(Math.Round(value), auto.Lowest, auto.Highest);

    public void Listen()
    {
        Stop();
        _model.PropertyChanged += OnModelChanged;
        _start.Changed += OnChanged;
        _updates.Changed += OnChanged;
        Tell();
    }

    public void Stop()
    {
        _model.PropertyChanged -= OnModelChanged;
        _start.Changed -= OnChanged;
        _updates.Changed -= OnChanged;
    }

    // Every visit: a terminal can change the schedule and the command line, and another program the Run key.
    public void Shown()
    {
        ReadWindowsSettings();
        _model.Send(new Intent.ReadSchedule());
        _model.Send(new Intent.LookForCommandLine());
        _updates.LookForFeed();
    }

    // Also as the window comes back, as from Settings > Apps > Startup, where the macOS settings read again too.
    public void ReadWindowsSettings() => _start.Read();

    public void SetStartsAtSignIn(bool on)
    {
        if (on != _start.On)
        {
            _start.Set(on);
        }
    }

    public void SetAutoSwitch(bool on)
    {
        if (on != Auto.On)
        {
            _model.Send(new Intent.SetAutoSwitch(on, Auto.At));
        }
    }

    // False for a cleared box, which then shows the share again.
    public bool SetShare(double value)
    {
        if (ShareOf(value, Auto) is not { } at)
        {
            return false;
        }

        if (at != Auto.At)
        {
            _model.Send(new Intent.SetAutoSwitch(Auto.On, at));
        }

        return true;
    }

    // Each picker sends the model's own other end, so a span to 24:00 is not cut to midnight.
    public void SetWarmFrom(TimeSpan picked)
    {
        if (picked != TimeOfDay(Warm.From))
        {
            _model.Send(new Intent.SetWarmRange(Minutes(picked), Warm.To));
        }
    }

    public void SetWarmTo(TimeSpan picked)
    {
        if (picked != TimeOfDay(Warm.To))
        {
            _model.Send(new Intent.SetWarmRange(Warm.From, Minutes(picked)));
        }
    }

    public void SetWarm(string qualified, bool on)
    {
        if (Warm.Accounts.FirstOrDefault(account => account.Qualified == qualified) is { } account && account.On != on)
        {
            _model.Send(new Intent.SetWarm(qualified, on));
        }
    }

    public void SetRenewsDaily(bool on)
    {
        if (on != Schedule.On)
        {
            _model.Send(new Intent.SetSchedule(on));
        }
    }

    public void RenewNow() => _model.Send(new Intent.RenewNow());

    public void SetChecksAutomatically(bool on)
    {
        if (on != _updates.ChecksAutomatically)
        {
            _updates.ChecksAutomatically = on;
        }
    }

    public void CheckNow() => _updates.Check();

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AppModel.Machine))
        {
            Tell();
        }
    }

    private void OnChanged(object? sender, EventArgs args) => Tell();

    private void Tell()
    {
        Keyed.Sync(
            WarmAccounts, Warm.Accounts, account => account.Qualified, shown => shown.Qualified,
            account => new WarmAccountRow(account, Warm.Enabled), (shown, account) => shown.Show(account, Warm.Enabled));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
