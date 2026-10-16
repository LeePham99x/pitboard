using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Pitboard.Core;
using Pitboard.Windows;

namespace Pitboard.Views;

public sealed partial class ActivityPage : Page
{
    public ActivityPage()
    {
        InitializeComponent();
        Lines.ContainerContentChanging += NameLine;
        Loaded += (_, _) => ViewModel?.Listen();
        Unloaded += (_, _) => ViewModel?.Stop();
    }

    public ActivityViewModel? ViewModel { get; private set; }

    // A change that went as asked is said quietly, and one that did not stands out.
    public static Style ResultStyle(bool done) =>
        (Style)Application.Current.Resources[done ? "SecondaryTextBlockStyle" : "BodyTextBlockStyle"];

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (ViewModel is null)
        {
            ViewModel = new ActivityViewModel(((PaneContext)e.Parameter).Model);
            Bindings.Update();
        }

        ViewModel.Listen();
        ViewModel.Read();
    }

    // Narrator reads a change as one line, in the columns' order.
    private static void NameLine(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is ActivityLine line)
        {
            AutomationProperties.SetName(
                args.ItemContainer, string.Join(", ", line.Date, line.Change, line.Account, line.Result, line.AskedBy));
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs args) => ViewModel?.Read();
}
