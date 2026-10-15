using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Pitboard.Core;

namespace Pitboard.Views;

// One Claude Code account under the settings' warm-up, with whether it is warmed up and where that stands.
internal sealed partial class WarmRow : UserControl
{
    private readonly ToggleSwitch _switch = new();
    private readonly TextBlock _title = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _standing;

    public WarmRow(string qualified, Style explanatory, Action<string, bool> turned)
    {
        Qualified = qualified;
        _standing = new TextBlock { Style = explanatory };
        AutomationProperties.SetAutomationId(_switch, $"settings.warm.{qualified}");
        _switch.Toggled += (_, _) => turned(Qualified, _switch.IsOn);
        var said = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Children = { _title, _standing } };
        var row = new Grid
        {
            ColumnSpacing = 24,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Children = { said, _switch },
        };
        Grid.SetColumn(_switch, 1);
        Content = row;
    }

    public string Qualified { get; }

    public void Show(WarmAccountShown account, bool enabled)
    {
        _title.Text = account.Plan is { } plan ? $"{account.Title} · {plan}" : account.Title;
        AutomationProperties.SetName(_switch, _title.Text);
        _standing.Text = account.Standing ?? "";
        _standing.Visibility = Look.Shown(account.Standing is not null);
        _switch.IsOn = account.On;
        _switch.IsEnabled = enabled;
    }
}
