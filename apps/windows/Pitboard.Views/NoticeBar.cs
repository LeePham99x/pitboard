using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Pitboard.Core;
using Pitboard.Windows;

namespace Pitboard.Views;

// A notice as Windows shows a change of the app's state: an InfoBar, its buttons under its message and its dismissal the close button.
public sealed partial class NoticeBar : UserControl
{
    private readonly InfoBar _bar = new() { IsOpen = true };
    private readonly TextBlock _countdown = new();
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly StackPanel _below;
    private readonly Action<NoticeAction> _perform;
    private readonly TimeProvider _time;
    private readonly DispatcherQueueTimer _timer;
    private PanelNotice _notice;
    private bool _shown;

    public NoticeBar(PanelNotice notice, Action<NoticeAction> perform, TimeProvider time)
    {
        _notice = notice;
        _perform = perform;
        _time = time;
        _below = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12), Children = { _countdown, _buttons } };
        _bar.Content = _below;
        _bar.Closing += OnClosing;
        Content = _bar;
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Count();
        Loaded += (_, _) =>
        {
            _shown = true;
            Count();
        };
        Unloaded += (_, _) =>
        {
            _shown = false;
            _timer.Stop();
        };
        Fill(notice);
    }

    public string Key => _notice.Id;

    public void Show(PanelNotice notice)
    {
        if (!Structural.Equal(notice, _notice))
        {
            Fill(notice);
        }
    }

    private void Fill(PanelNotice notice)
    {
        var buttonsChanged = !ReferenceEquals(notice, _notice) && !Structural.Equal(notice.Actions, _notice.Actions);
        _notice = notice;
        AutomationProperties.SetAutomationId(_bar, $"notice.{notice.Id}");
        _bar.Severity = notice.Severity switch
        {
            Severity.Warning => InfoBarSeverity.Warning,
            Severity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational,
        };
        _bar.Title = notice.Title;
        _bar.Message = string.Join("\n\n", notice.Lines);
        _bar.IsClosable = notice.Actions.Any(action => action.Dismisses);
        if (buttonsChanged || _buttons.Children.Count == 0)
        {
            _buttons.Children.Clear();
            foreach (var action in notice.Actions.Where(action => !action.Dismisses))
            {
                var button = new Button { Content = action.Title, IsEnabled = action.Enabled };
                button.Click += (_, _) => _perform(action);
                _buttons.Children.Add(button);
            }
        }

        _buttons.Visibility = Look.Shown(_buttons.Children.Count > 0);
        Count();
        _below.Visibility = Look.Shown(_buttons.Children.Count > 0 || notice.Until is not null);
    }

    // Counts down by itself between snapshots, and stops at nought.
    private void Count()
    {
        if (_notice is not { Until: { } until, UntilLabel: { } label })
        {
            _countdown.Visibility = Visibility.Collapsed;
            _timer.Stop();
            return;
        }

        var now = _time.GetUtcNow();
        _countdown.Text = $"{label} {Countdown.Left(until, now)}";
        _countdown.Visibility = Visibility.Visible;
        if (Countdown.Ended(until, now) || !_shown)
        {
            _timer.Stop();
        }
        else if (!_timer.IsRunning)
        {
            _timer.Start();
        }
    }

    // The model takes the notice away once it is dismissed.
    private void OnClosing(InfoBar sender, InfoBarClosingEventArgs args)
    {
        if (args.Reason != InfoBarCloseReason.CloseButton)
        {
            return;
        }

        args.Cancel = true;
        if (_notice.Actions.FirstOrDefault(action => action.Dismisses) is { } dismiss)
        {
            _perform(dismiss);
        }
    }
}
