using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pitboard.Core;

namespace Pitboard.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Select(Pane.Accounts);
    }

    // Null while the settings page is shown.
    public Pane? Selected =>
        Navigation.SelectedItem is NavigationViewItem { Tag: string tag } && Enum.TryParse<Pane>(tag, out var pane)
            ? pane
            : null;

    public void Select(Pane? pane) =>
        Navigation.SelectedItem = pane is { } shown
            ? Navigation.MenuItems.OfType<NavigationViewItem>().First(item => item.Tag as string == shown.ToString())
            : Navigation.SettingsItem;

    public void Show()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args) =>
        Navigation.Header = (args.SelectedItemContainer as NavigationViewItem)?.Content;
}
