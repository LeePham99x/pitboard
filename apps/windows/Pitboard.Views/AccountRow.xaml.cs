using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Pitboard.Core;

namespace Pitboard.Views;

public sealed partial class AccountRow : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(AccountItem), typeof(AccountRow), new PropertyMetadata(null, (row, _) => ((AccountRow)row).Show()));

    private const double PaceMarkHeight = 12;

    public AccountRow() => InitializeComponent();

    public AccountItem? Item
    {
        get => (AccountItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public Action<Intent>? Perform { get; set; }

    private void Show()
    {
        if (Item is not { } item)
        {
            return;
        }

        Mark.Glyph = item.Unplaced ? Glyphs.Warning
            : item.NeedsSignIn ? Glyphs.SignIn
            : item.InUse ? Glyphs.InUse
            : Glyphs.Account;
        Mark.Style = Styled(item.InUse ? "InUseMark" : "AccountMark");
        TitleText.Text = item.Title;
        PlanText.Text = item.Plan ?? "";
        PlanTag.Visibility = Look.Shown(item.Plan is not null);
        EmailText.Text = item.Email;
        EmailText.Visibility = Look.Shown(item.Email.Length > 0 && item.Email != item.Title);
        SwitchingRing.IsActive = item.Switching;
        SwitchingRing.Visibility = Look.Shown(item.Switching);
        InUseLabel.Visibility = Look.Shown(!item.Switching && item.InUse);
        ActionButton.Visibility = Look.Shown(!item.Switching && !item.InUse && item.Action is not null);
        if (item.Action is { } action)
        {
            ActionButton.Content = action.Title;
            AutomationProperties.SetName(ActionButton, action.Spoken);
        }

        ShowLimits(item.Limits);
        Notes.Children.Clear();
        foreach (var note in new[] { item.Problem, item.StaleNote, item.Pace, item.ParkedNote, item.WarmNote })
        {
            if (note is not null)
            {
                Notes.Children.Add(new TextBlock { Text = note, Style = Styled("Secondary"), TextWrapping = TextWrapping.Wrap });
            }
        }
    }

    // One grid for every limit, so the bars and figures line up whatever each limit is called.
    private void ShowLimits(LimitRow[] limits)
    {
        Limits.Children.Clear();
        Limits.RowDefinitions.Clear();
        Limits.ColumnDefinitions.Clear();
        Limits.Visibility = Look.Shown(limits.Length > 0);
        GridLength[] widths =
            [GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto];
        foreach (var width in widths)
        {
            Limits.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }

        for (var row = 0; row < limits.Length; row++)
        {
            var limit = limits[row];
            Limits.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = Column(limit.Short, "Secondary");
            // Narrator reads the limit once, as a sentence, and not the bar and each figure on their own.
            AutomationProperties.SetName(name, limit.Spoken);
            Place(name, row, 0);
            Place(Bar(limit), row, 1);
            var figure = Column(limit.Figure, "Secondary");
            figure.MinWidth = 36;
            figure.TextAlignment = TextAlignment.Right;
            Place(Unspoken(figure), row, 2);
            var resets = Column(limit.Resets, "Secondary");
            resets.TextAlignment = TextAlignment.Right;
            Place(Unspoken(resets), row, 3);
            var over = limit.Pace?.Standing == PaceStanding.Over;
            Place(Unspoken(Column(limit.Pace?.Said ?? "", over ? "Over" : "Secondary")), row, 4);
        }
    }

    // The mark is where an even use of the limit would be by now, and none at an even pace.
    private Grid Bar(LimitRow limit)
    {
        var bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(limit.Percent, 0, 100),
            MinWidth = 80,
            VerticalAlignment = VerticalAlignment.Center,
            Style = Styled(limit.Level switch
            {
                UsageLevel.Low => "Low",
                UsageLevel.Out => "Out",
                _ => "Plenty",
            }),
        };
        var cell = new Grid { Children = { Unspoken(bar) } };
        if (limit.Pace is { Standing: not PaceStanding.Even } pace)
        {
            var mark = new Rectangle
            {
                Width = 2,
                Height = PaceMarkHeight,
                Style = Styled(pace.Standing == PaceStanding.Over ? "OverPace" : "UnderPace"),
            };
            var marks = new Canvas { Children = { mark } };
            cell.Children.Add(Unspoken(marks));
            cell.SizeChanged += (_, size) =>
            {
                Canvas.SetLeft(mark, (size.NewSize.Width * Math.Clamp(pace.Expected, 0, 100) / 100) - 1);
                Canvas.SetTop(mark, (size.NewSize.Height - PaceMarkHeight) / 2);
            };
            ToolTipService.SetToolTip(cell, pace.Help);
        }

        return cell;
    }

    private TextBlock Column(string text, string style) =>
        new() { Text = text, Style = Styled(style), TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };

    private void Place(FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Limits.Children.Add(element);
    }

    private Style Styled(string key) => (Style)Resources[key];

    private static T Unspoken<T>(T element)
        where T : UIElement
    {
        AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);
        return element;
    }

    private void OnAction(object sender, RoutedEventArgs args)
    {
        if (Item?.Action is { } action)
        {
            Perform?.Invoke(action.Intent);
        }
    }
}
