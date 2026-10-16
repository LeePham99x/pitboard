using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pitboard.Windows;

namespace Pitboard.Views;

// A window alert said inside the sheet, which is the ContentDialog already showing.
public sealed partial class AlertBar : UserControl
{
    private readonly AppModel _model;
    private WindowAlert? _shown;

    public AlertBar(AppModel model)
    {
        _model = model;
        InitializeComponent();
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

    // Filled before it opens, which is when the InfoBar is announced.
    private void Show()
    {
        var alert = WindowAlert.InSheet(_model) ? WindowAlert.Of(_model) : null;
        if (Equals(alert, _shown))
        {
            return;
        }

        _shown = alert;
        Answers.Visibility = Look.Shown(alert is WindowAlert.QuitAsked or WindowAlert.ReplaceAsked);
        switch (alert)
        {
            case WindowAlert.Failed failed:
                Bar.Severity = InfoBarSeverity.Error;
                Bar.Title = failed.Text.Title;
                Bar.Message = failed.Text.Message;
                Bar.IsClosable = true;
                break;
            case WindowAlert.QuitAsked asked:
                Bar.Severity = InfoBarSeverity.Warning;
                Bar.Title = asked.Question.Title;
                Bar.Message = asked.Question.Message;
                Bar.IsClosable = false;
                ConfirmButton.Content = asked.Question.Confirm;
                break;
            case WindowAlert.ReplaceAsked asked:
                Bar.Severity = InfoBarSeverity.Warning;
                Bar.Title = asked.Question.Title;
                Bar.Message = asked.Question.Message;
                Bar.IsClosable = false;
                ConfirmButton.Content = asked.Question.Confirm;
                break;
        }

        Bar.IsOpen = alert is not null;
    }

    private void OnConfirm(object sender, RoutedEventArgs args) => Answer(confirmed: true);

    private void OnCancel(object sender, RoutedEventArgs args) => Answer(confirmed: false);

    private void Answer(bool confirmed)
    {
        if (_shown is { } alert)
        {
            _model.Send(alert.Answered(confirmed));
        }
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
