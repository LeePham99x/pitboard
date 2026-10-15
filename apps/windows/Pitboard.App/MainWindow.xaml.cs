using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Pitboard.Core;
using Pitboard.Views;
using Pitboard.Windows;
using Windows.System;

namespace Pitboard.App;

public sealed partial class MainWindow : Window
{
    private readonly AppModel _model;
    private readonly DialogHost _dialogs;
    private readonly AccountsPane _accounts;
    private readonly ActivityPane _activity;
    private readonly MachinePane _machine;
    private readonly SettingsPane _settings;
    private WindowAlerts? _alerts;
    private WindowSheets? _sheets;

    public MainWindow(AppModel model, StartAtSignIn startAtSignIn, IUpdates updates, string version)
    {
        _model = model;
        InitializeComponent();
        _dialogs = new DialogHost(() => Navigation.XamlRoot);
        _accounts = new AccountsPane(model, _dialogs);
        _activity = new ActivityPane(model);
        _machine = new MachinePane(model);
        _settings = new SettingsPane(model, startAtSignIn, updates, version);
        // A dialog needs the window's XamlRoot, which its content has once loaded.
        Navigation.Loaded += (_, _) =>
        {
            _sheets ??= new WindowSheets(model, _dialogs);
            _alerts ??= new WindowAlerts(model, _dialogs);
        };
        Closed += (_, _) =>
        {
            _sheets?.Dispose();
            _alerts?.Dispose();
        };
        // VK_OEM_COMMA, which VirtualKey has no name for.
        var settings = new KeyboardAccelerator { Key = (VirtualKey)0xBC, Modifiers = VirtualKeyModifiers.Control };
        settings.Invoked += (_, args) =>
        {
            args.Handled = true;
            Select(null);
        };
        Navigation.KeyboardAccelerators.Add(settings);
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

    public void ShowAbout()
    {
        Select(null);
        _settings.ShowAbout();
    }

    public void Show()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Navigation.Header = (args.SelectedItemContainer as NavigationViewItem)?.Content;
        Navigation.Content = Selected switch
        {
            Pane.Accounts => _accounts,
            Pane.Activity => _activity,
            Pane.Machine => _machine,
            _ => _settings,
        };
    }

    // Ctrl+N adds an account from any pane; the model then shows the accounts.
    private void OnAddAccount(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _model.Send(new Intent.PresentSheet(new Sheet.Add(Provider: null)));
    }
}
