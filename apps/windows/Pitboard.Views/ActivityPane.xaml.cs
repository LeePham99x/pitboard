using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Pitboard.Core;
using Pitboard.Windows;

namespace Pitboard.Views;

// Pitboard's log, newest first, whichever front end made each change.
public sealed partial class ActivityPane : UserControl
{
    private readonly AppModel _model;
    private ActivityShown? _shown;

    public ActivityPane(AppModel model)
    {
        _model = model;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => _model.PropertyChanged -= OnModelChanged;
        Show();
    }

    // Every visit: the log is read each time the pane is shown.
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _model.PropertyChanged -= OnModelChanged;
        _model.PropertyChanged += OnModelChanged;
        Show();
        _model.Send(new Intent.PaneShown(Pane.Activity));
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
        var activity = _model.Machine.Activity;
        if (Structural.Equal(activity, _shown))
        {
            return;
        }

        _shown = activity;
        Lines.ItemsSource = activity.Lines;
        Columns.Visibility = Look.Shown(activity.Empty is null);
        Lines.Visibility = Look.Shown(activity.Empty is null);
        Empty.Visibility = Look.Shown(activity.Empty is not null);
        EmptyTitle.Text = activity.Empty?.Title ?? "";
        EmptyDetail.Text = activity.Empty?.Detail ?? "";
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not ActivityLine line)
        {
            return;
        }

        AutomationProperties.SetName(
            args.ItemContainer, string.Join(", ", line.Date, line.Change, line.Account, line.Result, line.AskedBy));
        // A change that went as asked is said quietly, and one that did not stands out.
        if (args.ItemContainer.ContentTemplateRoot is FrameworkElement row && row.FindName("ResultText") is TextBlock result)
        {
            result.Style = line.Done ? (Style)Resources["Done"] : null;
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs args) => _model.Send(new Intent.PaneShown(Pane.Activity));
}
