using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Pitboard.Core;
using Pitboard.Windows;
using Windows.System;

namespace Pitboard.Views;

public sealed partial class AccountsPane : UserControl
{
    private static readonly HashSet<string> Parts =
    [
        nameof(AppModel.AccountsShown), nameof(AppModel.Notices), nameof(AppModel.Setup), nameof(AppModel.Footing),
        nameof(AppModel.Sections), nameof(AppModel.UpdatedWindow), nameof(AppModel.Reading),
    ];

    private readonly AppModel _model;
    private readonly DialogHost _dialogs;
    private readonly TimeProvider _time;
    private readonly ObservableCollection<ShownAccount> _rows = [];
    private readonly ObservableCollection<ShownSection> _sections = [];
    private readonly CollectionViewSource _grouped;
    private bool _grouping;
    private Action? _emptyChoice;
    private SetupStep? _setup;

    public AccountsPane(AppModel model, DialogHost dialogs, TimeProvider? time = null)
    {
        _model = model;
        _dialogs = dialogs;
        _time = time ?? TimeProvider.System;
        InitializeComponent();
        _grouped = new CollectionViewSource { IsSourceGrouped = true, Source = _sections };
        AccountList.ItemsSource = _rows;
        // An item's own Enter and Delete come here too, after the list has looked at them.
        AccountList.AddHandler(KeyDownEvent, new KeyEventHandler(OnListKeyDown), handledEventsToo: true);
        Loaded += OnLoaded;
        Unloaded += (_, _) => _model.PropertyChanged -= OnModelChanged;
        Show();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _model.PropertyChanged -= OnModelChanged;
        _model.PropertyChanged += OnModelChanged;
        Show();
        _model.Send(new Intent.PaneShown(Pane.Accounts));
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is { } part && Parts.Contains(part))
        {
            Show();
        }
    }

    private void Show()
    {
        Updated.Text = _model.UpdatedWindow;
        RefreshButton.IsEnabled = !_model.Reading;
        var shown = _model.AccountsShown;
        AccountList.Visibility = Look.Shown(shown is AccountsShown.List);
        Empty.Visibility = Look.Shown(shown is not AccountsShown.List);
        switch (shown)
        {
            case AccountsShown.NoTool none:
                ShowEmpty(Glyphs.Terminal, none.Title, none.Detail, none.LinkTitle, prominent: true, enabled: true, () =>
                {
                    if (Uri.TryCreate(none.Link, UriKind.Absolute, out var link))
                    {
                        _ = Launcher.LaunchUriAsync(link);
                    }
                });
                break;
            case AccountsShown.ReadFailed failed:
                ShowEmpty(
                    Glyphs.Problem, failed.Title, failed.Detail, failed.Retry.Title, prominent: false, failed.Retry.Enabled,
                    () => _model.Send(failed.Retry.Intent));
                break;
            case AccountsShown.NoAccounts none:
                ShowEmpty(
                    Glyphs.Accounts, none.Title, none.Detail, none.Add.Title, prominent: true, none.Add.Enabled,
                    () => _model.Send(none.Add.Intent));
                break;
            case AccountsShown.Reading reading:
                ShowReading(reading.Title);
                break;
            default:
                ShowNotices();
                ShowSetup();
                ShowSections();
                break;
        }
    }

    private void ShowEmpty(string mark, string title, string detail, string choice, bool prominent, bool enabled, Action chosen)
    {
        EmptyMark.Glyph = mark;
        EmptyMark.Visibility = Visibility.Visible;
        EmptyProgress.IsActive = false;
        EmptyProgress.Visibility = Visibility.Collapsed;
        EmptyTitle.Text = title;
        EmptyDetail.Text = detail;
        EmptyDetail.Visibility = Visibility.Visible;
        EmptyChoice.Content = choice;
        EmptyChoice.IsEnabled = enabled;
        EmptyChoice.Style = prominent ? (Style)Resources["Prominent"] : null;
        EmptyChoice.Visibility = Visibility.Visible;
        _emptyChoice = chosen;
    }

    private void ShowReading(string title)
    {
        EmptyMark.Visibility = Visibility.Collapsed;
        EmptyProgress.IsActive = true;
        EmptyProgress.Visibility = Visibility.Visible;
        EmptyTitle.Text = title;
        EmptyDetail.Visibility = Visibility.Collapsed;
        EmptyChoice.Visibility = Visibility.Collapsed;
        _emptyChoice = null;
    }

    private void ShowNotices() =>
        Keyed.Sync<PanelNotice, UIElement>(
            Notices.Children, _model.Notices, notice => notice.Id, shown => ((NoticeBar)shown).Key,
            notice => new NoticeBar(notice, action => Ask(action.Confirm, action.Intent), _time),
            (shown, notice) => ((NoticeBar)shown).Show(notice));

    private void ShowSetup()
    {
        Setup.IsOpen = _model.Setup is not null;
        if (_model.Setup is not { } step || Structural.Equal(step, _setup))
        {
            return;
        }

        _setup = step;
        Setup.Title = step.Title;
        Setup.Message = step.Detail;
        SetupIcon.Glyph = _model.Footing is Footing.Unnamed ? Glyphs.Name : Glyphs.Switch;
        SetupChoices.Children.Clear();
        for (var index = 0; index < step.Actions.Length; index++)
        {
            var choice = step.Actions[index];
            var button = new Button { Content = choice.Title, IsEnabled = choice.Enabled };
            if (index == 0)
            {
                button.Style = (Style)Resources["Prominent"];
            }

            button.Click += (_, _) => _model.Send(choice.Intent);
            SetupChoices.Children.Add(button);
        }
    }

    // A heading per tool once there is more than one, and no group at all before: an empty header still takes room.
    private void ShowSections()
    {
        var sections = _model.Sections;
        var grouping = sections.Any(section => section.Heading is not null);
        if (grouping)
        {
            _rows.Clear();
            Keyed.Sync(
                _sections, sections, section => section.Id, shown => shown.Key,
                section =>
                {
                    var shown = new ShownSection(section.Id, section.Heading);
                    shown.Show(section);
                    return shown;
                },
                (shown, section) => shown.Show(section));
        }
        else
        {
            _sections.Clear();
            Keyed.Sync(
                _rows, sections.SelectMany(section => section.Accounts).ToArray(), item => item.Id, shown => shown.Key,
                item => new ShownAccount(item), (shown, item) => shown.Show(item));
        }

        if (grouping != _grouping)
        {
            _grouping = grouping;
            AccountList.ItemsSource = grouping ? _grouped.View : _rows;
        }
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not ShownAccount shown)
        {
            return;
        }

        AutomationProperties.SetAutomationId(args.ItemContainer, $"account.{shown.Item.Qualified ?? shown.Item.Id}");
        if (args.ItemContainer.ContentTemplateRoot is AccountRow row)
        {
            row.Perform = _model.Send;
        }
    }

    // Right click, the menu key, Shift+F10 and a long press all ask here.
    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (Look.Ancestor<ListViewItem>(args.OriginalSource as DependencyObject) is not { Content: ShownAccount shown } item)
        {
            return;
        }

        var menu = AccountMenu.For(shown.Item, Ask);
        if (args.TryGetPosition(item, out var point))
        {
            menu.ShowAt(item, point);
        }
        else
        {
            menu.ShowAt(item);
        }

        args.Handled = true;
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        var source = args.OriginalSource as DependencyObject;
        if (Look.Ancestor<ButtonBase>(source) is null
            && Look.Ancestor<ListViewItem>(source) is { Content: ShownAccount { Item.Action: { } action } })
        {
            args.Handled = true;
            _model.Send(action.Intent);
        }
    }

    private void OnListKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.OriginalSource is not ListViewItem { Content: ShownAccount shown })
        {
            return;
        }

        if (AccountKeys.Pressed(args.Key, shown.Item) is var (confirm, intent))
        {
            args.Handled = true;
            Ask(confirm, intent);
        }
    }

    private async void Ask(Question? question, Intent intent)
    {
        if (question is null || await _dialogs.Confirm(question))
        {
            _model.Send(intent);
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs args) => _model.Send(new Intent.Refresh(Asked: true));

    private void OnAdd(object sender, RoutedEventArgs args) =>
        _model.Send(new Intent.PresentSheet(new Sheet.Add(Provider: null)));

    private void OnEmptyChoice(object sender, RoutedEventArgs args) => _emptyChoice?.Invoke();
}
