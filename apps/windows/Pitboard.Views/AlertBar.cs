using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Pitboard.Windows;

namespace Pitboard.Views;

// A window alert said inside the sheet, which is the ContentDialog already showing.
public sealed partial class AlertBar : UserControl
{
    private readonly AppModel _model;
    private readonly InfoBar _bar = new();
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private WindowAlert? _shown;

    public AlertBar(AppModel model)
    {
        _model = model;
        AutomationProperties.SetAutomationId(_bar, "alert");
        _bar.Content = _buttons;
        _bar.Closing += OnClosing;
        Content = _bar;
        Loaded += (_, _) =>
        {
            _model.PropertyChanged -= OnModelChanged;
            _model.PropertyChanged += OnModelChanged;
            Show();
        };
        Unloaded += (_, _) => _model.PropertyChanged -= OnModelChanged;
        Show();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args) => Show();

    private void Show()
    {
        var alert = WindowAlert.InSheet(_model) ? WindowAlert.Of(_model) : null;
        if (Equals(alert, _shown))
        {
            return;
        }

        _shown = alert;
        _bar.IsOpen = alert is not null;
        _buttons.Children.Clear();
        _buttons.Visibility = Look.Shown(alert is WindowAlert.QuitAsked);
        switch (alert)
        {
            case WindowAlert.Failed failed:
                _bar.Severity = InfoBarSeverity.Error;
                _bar.Title = failed.Text.Title;
                _bar.Message = failed.Text.Message;
                _bar.IsClosable = true;
                break;
            case WindowAlert.QuitAsked asked:
                _bar.Severity = InfoBarSeverity.Warning;
                _bar.Title = asked.Question.Title;
                _bar.Message = asked.Question.Message;
                _bar.IsClosable = false;
                _buttons.Children.Add(Answer(asked.Question.Confirm, asked, confirmed: true));
                _buttons.Children.Add(Answer("Cancel", asked, confirmed: false));
                break;
        }
    }

    private Button Answer(string title, WindowAlert alert, bool confirmed)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 0, 0, 12) };
        button.Click += (_, _) => _model.Send(alert.Answered(confirmed));
        return button;
    }

    // The model takes the alert away once it is answered.
    private void OnClosing(InfoBar sender, InfoBarClosingEventArgs args)
    {
        if (args.Reason == InfoBarCloseReason.CloseButton && _shown is { } alert)
        {
            args.Cancel = true;
            _model.Send(alert.Answered(confirmed: false));
        }
    }
}
