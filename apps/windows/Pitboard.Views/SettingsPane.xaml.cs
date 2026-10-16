using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Pitboard.Core;
using Pitboard.Windows;

namespace Pitboard.Views;

public sealed partial class SettingsPane : Page
{
    private PaneContext? _context;

    // A control set to what the model says raises its change event as a person's change would.
    private bool _showing;

    public SettingsPane()
    {
        InitializeComponent();
        WhatItDoes.Text = SettingsPage.WhatItDoes;
        Copyright.Text = SettingsPage.Copyright;
        foreach (var link in SettingsPage.Links)
        {
            var button = new HyperlinkButton { Content = link.Title };
            button.Click += (_, _) => Context.Open(new Uri(link.Url));
            Links.Children.Add(button);
        }

        Loaded += OnLoaded;
        Unloaded += (_, _) =>
        {
            Model.PropertyChanged -= OnModelChanged;
            StartAtSignIn.Changed -= OnStartAtSignInChanged;
            Updates.Changed -= OnUpdatesChanged;
        };
    }

    private PaneContext Context =>
        _context ?? throw new InvalidOperationException("The page was shown before its frame navigated to it.");

    private AppModel Model => Context.Model;

    private StartAtSignIn StartAtSignIn => Context.StartAtSignIn;

    private IUpdates Updates => Context.Updates;

    // Every visit: a terminal can change the schedule and the command line, and another program the Run key.
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _context ??= (PaneContext)e.Parameter;
        StartAtSignIn.Read();
        Show();
        Model.Send(new Intent.ReadSchedule());
        Model.Send(new Intent.LookForCommandLine());
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Model.PropertyChanged -= OnModelChanged;
        Model.PropertyChanged += OnModelChanged;
        StartAtSignIn.Changed -= OnStartAtSignInChanged;
        StartAtSignIn.Changed += OnStartAtSignInChanged;
        Updates.Changed -= OnUpdatesChanged;
        Updates.Changed += OnUpdatesChanged;
        Show();
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
            var machine = Model.Machine;
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
        StartAtSignInSwitch.IsOn = StartAtSignIn.On;
        StartAtSignInSwitch.IsEnabled = StartAtSignIn.Refusal is null;
        StartupNote.Text = StartAtSignIn.Refusal ?? SettingsPage.StartupNote;
        Announce(StartupFailed, StartAtSignIn.Failed);
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
        AutomaticChecksCard.Visibility = Look.Shown(Updates.Available);
        AutomaticChecks.IsOn = Updates.ChecksAutomatically;
        UpdatesState.Text = Updates.Waiting is { } waiting
            ? $"Pitboard {waiting} is ready to install."
            : SettingsPage.Named(Context.Version);
        CheckNow.IsEnabled = Updates.Available;
        UpdatesNotes.Children.Clear();
        foreach (var note in SettingsPage.UpdatesNotes(Updates, Context.Version))
        {
            UpdatesNotes.Children.Add(new TextBlock { Text = note, Style = (Style)Resources["Footnote"] });
        }

        AboutVersion.Text = Context.Version;
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
            StartAtSignIn.Set(StartAtSignInSwitch.IsOn);
        }
    }

    private void OnAutoSwitchToggled(object sender, RoutedEventArgs args)
    {
        if (!_showing)
        {
            Model.Send(new Intent.SetAutoSwitch(AutoSwitchSwitch.IsOn, Model.Machine.AutoSwitch.At));
        }
    }

    private void OnShareChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_showing)
        {
            return;
        }

        var auto = Model.Machine.AutoSwitch;
        if (SettingsPage.Share(args.NewValue, auto) is { } at)
        {
            Model.Send(new Intent.SetAutoSwitch(auto.On, at));
        }
        else
        {
            // A cleared box shows the share again once its change has run.
            DispatcherQueue.TryEnqueue(Ui.Guarded(Show));
        }
    }

    // Each picker sends the model's own other end, so a span to 24:00 is not cut to midnight.
    private void OnWarmRangeChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
    {
        if (_showing || args.NewTime is not { } picked)
        {
            return;
        }

        var warm = Model.Machine.Warm;
        var minutes = SettingsPage.Minutes(picked);
        Model.Send(sender == WarmFrom ? new Intent.SetWarmRange(minutes, warm.To) : new Intent.SetWarmRange(warm.From, minutes));
    }

    private void OnWarmTurned(string qualified, bool on)
    {
        if (!_showing)
        {
            Model.Send(new Intent.SetWarm(qualified, on));
        }
    }

    private void OnRenewDailyToggled(object sender, RoutedEventArgs args)
    {
        if (!_showing)
        {
            Model.Send(new Intent.SetSchedule(RenewDaily.IsOn));
        }
    }

    private void OnRenewNow(object sender, RoutedEventArgs args) => Model.Send(new Intent.RenewNow());

    private void OnAutomaticChecksToggled(object sender, RoutedEventArgs args)
    {
        if (!_showing)
        {
            Updates.ChecksAutomatically = AutomaticChecks.IsOn;
        }
    }

    private void OnCheckNow(object sender, RoutedEventArgs args) => Updates.Check();
}
