using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Pitboard.Core;
using Pitboard.Windows;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace Pitboard.Views;

// The Open Link window: what the model says of the link waiting, the accounts that can open it, and its buttons in a
// dialog's order. Every link from outside waits here until somebody chooses, even with one account, so no page can open
// an account's window by handing Pitboard a link.
public sealed partial class OpenLinkWindow : Window
{
    private const double ContentWidth = 440;

    private readonly Action<Intent> _send;
    private readonly Action<WindowAccount> _open;
    private readonly Action<string> _inBrowser;
    private readonly Grid _root = new() { Padding = new Thickness(24), RowSpacing = 24 };
    private readonly StackPanel _content = new() { Spacing = 12 };
    private readonly StackPanel _buttons = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        Spacing = 8,
    };

    private LinkPicker? _picker;
    private string? _chosen;
    private Button? _openButton;

    public OpenLinkWindow(Action<Intent> send, Action<WindowAccount> open, Action<string> inBrowser)
    {
        _send = send;
        _open = open;
        _inBrowser = inBrowser;
        Title = "Open Link";
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_buttons, 1);
        _root.Children.Add(_content);
        _root.Children.Add(_buttons);
        AutomationProperties.SetAutomationId(_root, "account-picker");
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, args) =>
        {
            args.Handled = true;
            Cancel();
        };
        _root.KeyboardAccelerators.Add(escape);
        _root.Loaded += (_, _) => Fit(center: true);
        Content = _root;
        AppWindow.SetPresenter(OverlappedPresenter.CreateForDialog());
        Closed += (_, _) =>
        {
            if (_picker is { } picker)
            {
                _send(new Intent.DismissLink(picker.Arrival));
            }
        };
        Show(null);
    }

    // The link it shows, which the model names each link by.
    public ulong? Arrival => _picker?.Arrival;

    public void Follow(LinkPicker? picker)
    {
        if (picker is not null && _picker is not null && picker.Arrival == _picker.Arrival
            && Structural.Equal(picker.Shown, _picker.Shown))
        {
            _picker = picker;
            if (_openButton is not null)
            {
                _openButton.IsEnabled = picker.Armed;
            }

            return;
        }

        if (picker?.Arrival != _picker?.Arrival)
        {
            _chosen = null;
        }

        _picker = picker;
        Show(picker);
        Fit(center: false);
    }

    // Brought forward by each link that arrives: another app handed it over, and is in front.
    public void Bring()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    // Closed once no link waits, so closing dismisses nothing.
    public void Withdraw()
    {
        _picker = null;
        Close();
    }

    private static TextBlock Heading(string text) =>
        new()
        {
            Text = text,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap,
        };

    private static TextBlock Explanatory(string text) =>
        new()
        {
            Text = text,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        };

    // On one line; the whole link is its tooltip, and what Narrator reads after it.
    private static TextBlock LinkLine(string text, string url)
    {
        var line = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsTextSelectionEnabled = true,
        };
        ToolTipService.SetToolTip(line, url);
        AutomationProperties.SetHelpText(line, url);
        return line;
    }

    private static Button Command(string title, Action chosen, bool enabled = true, bool accent = false)
    {
        var button = new Button { Content = title, IsEnabled = enabled, MinWidth = 96 };
        if (accent)
        {
            button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        }

        button.Click += (_, _) => chosen();
        return button;
    }

    private void Show(LinkPicker? picker)
    {
        _content.Children.Clear();
        _buttons.Children.Clear();
        _openButton = null;
        switch (picker?.Shown)
        {
            case null:
                Unavailable();
                _buttons.Children.Add(Command("Close", Close, accent: true));
                break;
            case PickerShown.Reading reading:
                var waiting = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                waiting.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20 });
                waiting.Children.Add(new TextBlock { Text = reading.Title, VerticalAlignment = VerticalAlignment.Center });
                _content.Children.Add(waiting);
                _buttons.Children.Add(Command("Cancel", Cancel));
                break;
            case PickerShown.ReadFailed failed:
                _content.Children.Add(Heading(failed.Title));
                _content.Children.Add(Explanatory(failed.Detail));
                _buttons.Children.Add(Command(failed.Retry.Title, () => _send(failed.Retry.Intent), failed.Retry.Enabled, accent: true));
                _buttons.Children.Add(Command("Cancel", Cancel));
                break;
            case PickerShown.Refused refused:
                _content.Children.Add(Heading(refused.Title));
                _content.Children.Add(Explanatory(refused.Reason));
                _buttons.Children.Add(Command("OK", Cancel, accent: true));
                break;
            case PickerShown.NoAccount none:
                _content.Children.Add(Heading(none.Title));
                _content.Children.Add(LinkLine(none.LinkText, none.Link.Url));
                _content.Children.Add(Explanatory(none.Detail));
                _buttons.Children.Add(Command(none.Add.Title, () => _send(none.Add.Intent), none.Add.Enabled, accent: true));
                _buttons.Children.Add(Command(none.InBrowser, () =>
                {
                    _inBrowser(none.Link.Url);
                    Cancel();
                }));
                _buttons.Children.Add(Command("Cancel", Cancel));
                break;
            case PickerShown.Choose choose:
                _content.Children.Add(Heading(choose.Title));
                _content.Children.Add(LinkLine(choose.LinkText, choose.Link.Url));
                _content.Children.Add(Accounts(choose));
                _openButton = Command(choose.Open, OpenChosen, picker!.Armed, accent: true);
                _buttons.Children.Add(_openButton);
                _buttons.Children.Add(Command("Cancel", Cancel));
                break;
        }
    }

    private void Unavailable()
    {
        var shown = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        shown.Children.Add(new FontIcon { Glyph = Glyphs.Site, FontSize = 32 });
        shown.Children.Add(new TextBlock
        {
            Text = "No Link to Open",
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        var detail = Explanatory(
            $"Share a {PitboardFfiMethods.SiteNames(Conjunction.Or)} page with Pitboard from your browser’s Share menu to open it as one of your accounts.");
        detail.TextAlignment = TextAlignment.Center;
        shown.Children.Add(detail);
        _content.Children.Add(shown);
    }

    private ListView Accounts(PickerShown.Choose choose)
    {
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MinHeight = 96, MaxHeight = 360 };
        AutomationProperties.SetAutomationId(list, "picker.accounts");
        AutomationProperties.SetName(list, "Accounts");
        foreach (var account in choose.Accounts)
        {
            var row = new StackPanel { Spacing = 2, Padding = new Thickness(0, 6, 0, 6) };
            row.Children.Add(new TextBlock { Text = account.Window.Label });
            var detail = Explanatory(account.Detail);
            detail.Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"];
            row.Children.Add(detail);
            var item = new ListViewItem { Content = row, Tag = account.Window };
            // One element for Narrator, its label and its line read together.
            AutomationProperties.SetName(item, $"{account.Window.Label}, {account.Detail}");
            AutomationProperties.SetAutomationId(item, $"picker.account.{account.Window.Label}");
            list.Items.Add(item);
        }

        var chosen = _chosen ?? choose.Chosen;
        list.SelectedItem = list.Items.OfType<ListViewItem>()
            .FirstOrDefault(item => item.Tag is WindowAccount account && Same(account.Store, chosen))
            ?? list.Items.FirstOrDefault();
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ListViewItem { Tag: WindowAccount account })
            {
                _chosen = account.Store;
            }
        };
        list.DoubleTapped += (_, _) => OpenChosen();
        list.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                OpenChosen();
            }
        };
        return list;
    }

    private static bool Same(string store, string other) => string.Equals(store, other, StringComparison.OrdinalIgnoreCase);

    // Only once Open answers, and only as an account the window offers, as the model takes it.
    private void OpenChosen()
    {
        if (_picker is not { Armed: true, Shown: PickerShown.Choose choose } picker)
        {
            return;
        }

        var chosen = _chosen ?? choose.Chosen;
        if (choose.Accounts.FirstOrDefault(account => Same(account.Window.Store, chosen)) is not { } account)
        {
            return;
        }

        _send(new Intent.OpenLink(picker.Arrival, account.Window.Store));
        _open(account.Window);
    }

    // The model answers with no link waiting, and the window is withdrawn.
    private void Cancel()
    {
        if (_picker is { } picker)
        {
            _send(new Intent.DismissLink(picker.Arrival));
        }
        else
        {
            Close();
        }
    }

    // As tall as what it shows, at its screen's scale.
    private void Fit(bool center)
    {
        if (!_root.IsLoaded)
        {
            return;
        }

        _root.Measure(new global::Windows.Foundation.Size(ContentWidth, double.PositiveInfinity));
        var scale = Shell.Scale(WindowNative.GetWindowHandle(this));
        AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling(ContentWidth * scale), (int)Math.Ceiling(_root.DesiredSize.Height * scale)));
        if (center)
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            AppWindow.Move(new PointInt32(
                area.X + ((area.Width - AppWindow.Size.Width) / 2), area.Y + ((area.Height - AppWindow.Size.Height) / 2)));
        }
    }
}
