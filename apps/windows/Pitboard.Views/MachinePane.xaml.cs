using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Pitboard.Core;
using Pitboard.Windows;

namespace Pitboard.Views;

// The checks pitboard doctor makes of this PC, for when a failed switch's one line is not enough to act on.
public sealed partial class MachinePane : UserControl
{
    private readonly AppModel _model;
    private ChecksShown? _shown;

    public MachinePane(AppModel model)
    {
        _model = model;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => _model.PropertyChanged -= OnModelChanged;
        Show();
    }

    // Every visit: a check fixed in a terminal since would otherwise still read as failing.
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _model.PropertyChanged -= OnModelChanged;
        _model.PropertyChanged += OnModelChanged;
        Show();
        _model.Send(new Intent.PaneShown(Pane.Machine));
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AppModel.Machine))
        {
            Show();
        }
    }

    private void Show()
    {
        var checks = _model.Machine.Checks;
        if (Structural.Equal(checks, _shown))
        {
            return;
        }

        if (!Structural.Equal(checks.Lines, _shown?.Lines))
        {
            Lines.ItemsSource = checks.Lines;
        }

        _shown = checks;
        CheckAgain.IsEnabled = !checks.Checking;
        Heading.Visibility = Look.Shown(checks.Summary is not null);
        Summary.Text = checks.Summary ?? "";
        Checking.IsActive = checks.Checking;
        Checking.Visibility = Look.Shown(checks.Checking);
        Checked.Text = checks.Checked ?? "";
        Checked.Visibility = Look.Shown(!checks.Checking && checks.Checked is not null);
        Waiting.Visibility = Look.Shown(checks.Waiting is not null);
        WaitingRing.IsActive = checks.Waiting is not null;
        WaitingText.Text = checks.Waiting ?? "";
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not CheckLine line)
        {
            return;
        }

        AutomationProperties.SetName(
            args.ItemContainer, string.Join(", ", new[] { line.SpokenLevel, line.Name, line.Detail, line.Advice }.OfType<string>()));
        if (args.ItemContainer.ContentTemplateRoot is not FrameworkElement row)
        {
            return;
        }

        // The shapes differ as well as the colours.
        if (row.FindName("LevelMark") is FontIcon mark)
        {
            (mark.Glyph, mark.Style) = line.Level switch
            {
                Level.Ok => (Glyphs.Passed, (Style)Resources["Ok"]),
                Level.Warn => (Glyphs.Warning, (Style)Resources["Warn"]),
                _ => (Glyphs.Problem, (Style)Resources["Fail"]),
            };
            AutomationProperties.SetName(mark, line.SpokenLevel);
        }

        if (row.FindName("AdviceText") is TextBlock advice)
        {
            advice.Visibility = Look.Shown(line.Advice is not null);
        }
    }

    private void OnCheckAgain(object sender, RoutedEventArgs args) => _model.Send(new Intent.PaneShown(Pane.Machine));
}
