using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Pitboard.Core;
using Pitboard.Windows;
using Launcher = Windows.System.Launcher;

namespace Pitboard.Views;

public sealed partial class SettingsPane : UserControl
{
    private readonly AppModel _model;
    private readonly StartAtSignIn _startAtSignIn;
    private readonly IUpdates _updates;
    private readonly string _version;
    private readonly Action<Uri> _open;

    // A control set to what the model says raises its change event as a person's change would.
    private bool _showing;

    public SettingsPane(AppModel model, StartAtSignIn startAtSignIn, IUpdates updates, string version, Action<Uri>? open = null)
    {
        _model = model;
        _startAtSignIn = startAtSignIn;
        _updates = updates;
        _version = version;
        _open = open ?? (page => _ = Launcher.LaunchUriAsync(page));
        InitializeComponent();
        WhatItDoes.Text = SettingsPage.WhatItDoes;
        Copyright.Text = SettingsPage.Copyright;
        foreach (var link in SettingsPage.Links)
        {
            var button = new HyperlinkButton { Content = link.Title };
            button.Click += (_, _) => _open(new Uri(link.Url));
            Links.Children.Add(button);
        }

        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            _model.PropertyChanged -= OnModelChanged;
            _startAtSignIn.Changed -= OnStartAtSignInChanged;
            _updates.Changed -= OnUpdatesChanged;
        };
        Show();
    }

    // Every visit: a terminal can change the schedule and the command line, and another program the Run key.
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _model.PropertyChanged -= OnModelChanged;
        _model.PropertyChanged += OnModelChanged;
        _startAtSignIn.Changed -= OnStartAtSignInChanged;
        _startAtSignIn.Changed += OnStartAtSignInChanged;
        _updates.Changed -= OnUpdatesChanged;
        _updates.Changed += OnUpdatesChanged;
        _startAtSignIn.Read();
        _model.Send(new Intent.ReadSchedule());
        _model.Send(new Intent.LookForCommandLine());
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AppModel.Machine))
        {
            Show();
        }
    }

    private void OnStartAtSignInChanged(object? sender, EventArgs args) => Show();

    private void OnUpdatesChanged(object? sender, EventArgs args) => Show();

    private void Show()
    {
        _showing = true;
        try
        {
            var machine = _model.Machine;
            ShowStartAtSignIn();
            ShowLimits(machine.AutoSwitch, machine.Warm);
            ShowRenewal(machine.Schedule, machine.Renewal);
            ShowCommandLine(machine.CommandLine);
            ShowUpdates();
        }
        finally
        {
            _showing = false;
        }
    }

    private void ShowStartAtSignIn()
    {
        StartAtSignInSwitch.IsOn = _startAtSignIn.On;
        StartAtSignInSwitch.IsEnabled = _startAtSignIn.Refusal is null;
        StartupNote.Text = _startAtSignIn.Refusal ?? SettingsPage.StartupNote;
        Announce(StartupFailed, _startAtSignIn.Failed);
    }

    private void ShowLimits(AutoSwitchShown auto, WarmShown warm)
    {
        AutoSwitchSwitch.IsOn = auto.On;
        AutoSwitchSwitch.IsEnabled = auto.Enabled;
        Say(AutoSwitchStanding, auto.Standing);
        AutoSwitchAt.Text = auto.AtLabel;
        AutomationProperties.SetName(AutoSwitchShare, auto.AtLabel);
        AutoSwitchShare.Minimum = auto.Lowest;
        AutoSwitchShare.Maximum = auto.Highest;
        AutoSwitchShare.Value = auto.At;
        AutoSwitchShare.IsEnabled = auto.Enabled && auto.On;
        AutoSwitchNote.Text = auto.Note;

        WarmRange.Text = warm.RangeLabel;
        Say(WarmStanding, warm.Standing);
        WarmFrom.SelectedTime = SettingsPage.TimeOfDay(warm.From);
        WarmTo.SelectedTime = SettingsPage.TimeOfDay(warm.To);
        WarmFrom.IsEnabled = warm.Enabled;
        WarmTo.IsEnabled = warm.Enabled;
        WarmEmpty.Visibility = Look.Shown(warm.Accounts.Length == 0);
        var explanatory = (Style)Resources["Explanatory"];
        Keyed.Sync(
            WarmAccounts.Children,
            warm.Accounts,
            account => account.Qualified,
            shown => ((WarmRow)shown).Qualified,
            account =>
            {
                var row = new WarmRow(account.Qualified, explanatory, OnWarmTurned);
                row.Show(account, warm.Enabled);
                return row;
            },
            (shown, account) => ((WarmRow)shown).Show(account, warm.Enabled));
        WarmNote.Text = warm.Note;
    }

    private void ShowRenewal(ScheduleShown schedule, RenewalShown renewal)
    {
        RenewDaily.IsOn = schedule.On;
        RenewDaily.IsEnabled = schedule.Enabled;
        Say(ScheduleNote, schedule.Note);
        Announce(ScheduleFailed, schedule.Failed);
        RunsCard.Visibility = Look.Shown(schedule.Runs is not null);
        Runs.Text = schedule.Runs ?? "";
        ScheduledInCard.Visibility = Look.Shown(schedule.ScheduledIn is not null);
        ScheduledIn.Text = schedule.ScheduledIn ?? "";
        ToolTipService.SetToolTip(ScheduledIn, schedule.ScheduledIn);
        RenewalNote.Text = renewal.Note;
        RenewNow.IsEnabled = !renewal.Renewing;
        RenewalAbout.Text = renewal.About;
    }

    // No button: the installer puts the command line on the PATH, and takes it off again.
    private void ShowCommandLine(CommandLineShown shown)
    {
        Say(InTerminal, shown.InTerminal);
        LookingForCommandLine.IsActive = shown.InTerminal is null;
        LookingForCommandLine.Visibility = Look.Shown(shown.InTerminal is null);
        Say(UpdateNote, shown.UpdateNote);
        Say(TerminalNote, SettingsPage.TerminalNote(shown));
    }

    private void ShowUpdates()
    {
        AutomaticChecksCard.Visibility = Look.Shown(_updates.Available);
        AutomaticChecks.IsOn = _updates.ChecksAutomatically;
        UpdatesState.Text = _updates.Waiting is { } waiting
            ? $"Pitboard {waiting} is ready to install."
            : SettingsPage.Named(_version);
        CheckNow.IsEnabled = _updates.Available;
        UpdatesNotes.Children.Clear();
        foreach (var note in SettingsPage.UpdatesNotes(_updates, _version))
        {
            UpdatesNotes.Children.Add(new TextBlock { Text = note, Style = (Style)Resources["Footnote"] });
        }

        AboutVersion.Text = _version;
    }

    private static void Say(TextBlock text, string? said)
    {
        text.Text = said ?? "";
        text.Visibility = Look.Shown(said is not null);
    }

    // WinUI's live region: Narrator reads the text again only when its peer says it changed.
    private static void Announce(TextBlock text, string? said)
    {
        var changed = said is not null && said != text.Text;
        Say(text, said);
        if (changed)
        {
            (FrameworkElementAutomationPeer.FromElement(text) ?? FrameworkElementAutomationPeer.CreatePeerForElement(text))
                .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void OnStartAtSignInToggled(object sender, RoutedEventArgs args)
    {
        if (!_showing)
        {
            _startAtSignIn.Set(StartAtSignInSwitch.IsOn);
        }
    }

    private void OnAutoSwitchToggled(object sender, RoutedEventArgs args)
    {
        if (!_showing)
        {
            _model.Send(new Intent.SetAutoSwitch(AutoSwitchSwitch.IsOn, _model.Machine.AutoSwitch.At));
        }
    }

    private void OnShareChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_showing)
        {
            return;
        }

        var auto = _model.Machine.AutoSwitch;
        if (SettingsPage.Share(args.NewValue, auto) is { } at)
        {
            _model.Send(new Intent.SetAutoSwitch(auto.On, at));
        }
        else
        {
            // A cleared box shows the share again once its change has run.
            DispatcherQueue.TryEnqueue(Show);
        }
    }

    // Each picker sends the model's own other end, so a span to 24:00 is not cut to midnight.
    private void OnWarmRangeChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
    {
        if (_showing || args.NewTime is not { } picked)
        {
            return;
        }

        var warm = _model.Machine.Warm;
        var minutes = SettingsPage.Minutes(picked);
        _model.Send(sender == WarmFrom ? new Intent.SetWarmRange(minutes, warm.To) : new Intent.SetWarmRange(warm.From, minutes));
    }

    private void OnWarmTurned(string qualified, bool on)
    {
        if (!_showing)
        {
            _model.Send(new Intent.SetWarm(qualified, on));
        }
    }

    private void OnRenewDailyToggled(object sender, RoutedEventArgs args)
    {
        if (!_showing)
        {
            _model.Send(new Intent.SetSchedule(RenewDaily.IsOn));
        }
    }

    private void OnRenewNow(object sender, RoutedEventArgs args) => _model.Send(new Intent.RenewNow());

    private void OnAutomaticChecksToggled(object sender, RoutedEventArgs args)
    {
        if (!_showing)
        {
            _updates.ChecksAutomatically = AutomaticChecks.IsOn;
        }
    }

    private void OnCheckNow(object sender, RoutedEventArgs args) => _updates.Check();
}
