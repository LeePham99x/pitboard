using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Pitboard.Core;
using Pitboard.Windows;
using Windows.System;
using Windows.UI.Core;

namespace Pitboard.Views;

public sealed partial class MachinePage : Page
{
    private readonly MenuFlyout _detailMenu;
    private Action<string> _copy = _ => { };

    public MachinePage()
    {
        InitializeComponent();
        _detailMenu = (MenuFlyout)Resources["DetailMenu"];
        Lines.ContainerContentChanging += NameLine;
        Loaded += (_, _) => ViewModel?.Listen();
        Unloaded += (_, _) => ViewModel?.Stop();
    }

    public MachineViewModel? ViewModel { get; private set; }

    public static InfoBarSeverity Severity(Level level) => level switch
    {
        Level.Fail => InfoBarSeverity.Error,
        Level.Warn => InfoBarSeverity.Warning,
        _ => InfoBarSeverity.Success,
    };

    public static string LevelGlyph(Level level) => level switch
    {
        Level.Ok => Glyphs.Passed,
        Level.Warn => Glyphs.Warning,
        _ => Glyphs.Problem,
    };

    public static Style LevelStyle(Level level) => (Style)Application.Current.Resources[level switch
    {
        Level.Ok => "PassedMarkStyle",
        Level.Warn => "WarningMarkStyle",
        _ => "FailedMarkStyle",
    }];

    public static Visibility Said(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (ViewModel is null)
        {
            var context = (PaneContext)e.Parameter;
            ViewModel = new MachineViewModel(context.Model);
            _copy = context.Copy;
            Bindings.Update();
        }

        ViewModel.Listen();
        ViewModel.Check();
    }

    // Narrator reads a check as one line: its level, its name, what it found and what to do.
    private static void NameLine(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is CheckLine line)
        {
            AutomationProperties.SetName(
                args.ItemContainer, string.Join(", ", new[] { line.SpokenLevel, line.Name, line.Detail, line.Advice }.OfType<string>()));
        }
    }

    private void OnCheckAgain(object sender, RoutedEventArgs args) => ViewModel?.Check();

    private void OnDetailKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (control && args.Key is (VirtualKey.C or VirtualKey.Insert) && sender is TextBlock { SelectedText.Length: > 0 } detail)
        {
            args.Handled = true;
            _copy(PathText.Copied(detail.SelectedText));
        }
    }

    private void OnDetailMenuOpening(object sender, object args) =>
        _detailMenu.Items[0].IsEnabled = _detailMenu.Target is TextBlock { SelectedText.Length: > 0 };

    private void OnCopyDetail(object sender, RoutedEventArgs args)
    {
        if (_detailMenu.Target is TextBlock detail)
        {
            _copy(PathText.Copied(detail.SelectedText));
        }
    }

    private void OnSelectAllDetail(object sender, RoutedEventArgs args) => (_detailMenu.Target as TextBlock)?.SelectAll();
}
