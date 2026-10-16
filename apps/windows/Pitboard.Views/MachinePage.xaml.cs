using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Pitboard.Core;
using Pitboard.Windows;

namespace Pitboard.Views;

public sealed partial class MachinePage : Page
{
    public MachinePage()
    {
        InitializeComponent();
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
            ViewModel = new MachineViewModel(((PaneContext)e.Parameter).Model);
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
}
