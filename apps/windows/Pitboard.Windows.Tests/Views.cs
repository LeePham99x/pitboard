using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Pitboard.Views;

namespace Pitboard.Windows.Tests;

// A view in the test app's window, laid out and loaded, read as UI Automation and a person would read it.
internal static class Views
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    public static async Task<T> Shown<T>(T view)
        where T : FrameworkElement
    {
        var loaded = new TaskCompletionSource();
        view.Loaded += (_, _) => loaded.TrySetResult();
        UnitTestApp.Window!.Content = view;
        await loaded.Task.WaitAsync(Patience);
        view.UpdateLayout();
        return view;
    }

    // A page of the main window, in a frame of the test app's window, navigated to as the window's own frame does.
    public static async Task<T> Navigated<T>(PaneContext context)
        where T : Page =>
        await Navigate<T>(await Shown(new Frame { IsNavigationStackEnabled = false }), context);

    public static async Task<T> Navigate<T>(Frame frame, PaneContext context)
        where T : Page
    {
        Assert.IsTrue(frame.Navigate(typeof(T), context), $"the frame did not navigate to {typeof(T).Name}");
        var page = (T)frame.Content;
        if (!page.IsLoaded)
        {
            var loaded = new TaskCompletionSource();
            page.Loaded += (_, _) => loaded.TrySetResult();
            await loaded.Task.WaitAsync(Patience);
        }

        page.UpdateLayout();
        return page;
    }

    // What the main window navigates its pages with, over a model the test made.
    public static PaneContext Context(
        AppModel model, StartAtSignIn? start = null, IUpdates? updates = null, string version = "0.0.0",
        Action<Uri>? open = null, TimeProvider? time = null, Action<string>? copy = null, string? notificationsOff = null) =>
        new(
            model, new DialogHost(() => UnitTestApp.Window?.Content?.XamlRoot), start ?? new StartAtSignIn(new FixtureStartupItem()),
            updates ?? new NoUpdates(), version)
        {
            Open = open ?? (_ => { }),
            Time = time ?? TimeProvider.System,
            Copy = copy ?? (_ => { }),
            NotificationsOff = notificationsOff,
        };

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var waiting = new Queue<DependencyObject>([root]);
        while (waiting.TryDequeue(out var next))
        {
            yield return next;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(next); index++)
            {
                waiting.Enqueue(VisualTreeHelper.GetChild(next, index));
            }
        }
    }

    private static bool InIcon(DependencyObject element, DependencyObject root)
    {
        for (var at = VisualTreeHelper.GetParent(element); at is not null && at != root; at = VisualTreeHelper.GetParent(at))
        {
            if (at is IconElement)
            {
                return true;
            }
        }

        return false;
    }

    // Shown, and not inside anything collapsed.
    public static IEnumerable<T> Visible<T>(DependencyObject root)
        where T : UIElement =>
        Descendants(root).OfType<T>().Where(element => IsVisible(element, root));

    // An icon draws its glyph with a TextBlock of its own, which is no text a person reads.
    public static string[] Texts(DependencyObject root) =>
        [.. Visible<TextBlock>(root).Where(text => !InIcon(text, root)).Select(text => text.Text).Where(text => text.Length > 0)];

    public static string? Description(TextBox field) => (field.Description as TextBlock)?.Text;

    // What shows its first keyboard accelerator as a tooltip and is no command: a panel then shows it wherever the pointer
    // rests on it.
    public static string[] StrayShortcuts(DependencyObject root) =>
    [
        .. Descendants(root).OfType<UIElement>()
            .Where(element => element.KeyboardAccelerators.Count > 0
                && element.KeyboardAcceleratorPlacementMode != KeyboardAcceleratorPlacementMode.Hidden
                && element is not ButtonBase and not MenuFlyoutItemBase)
            .Select(element => $"{element.GetType().Name} {(element as FrameworkElement)?.Name}"),
    ];

    public static Button Button(DependencyObject root, string content) =>
        Visible<Button>(root).FirstOrDefault(button => button.Content as string == content)
        ?? throw new AssertFailedException($"no button '{content}' among: {string.Join(", ", Texts(root))}");

    public static T Element<T>(DependencyObject root, string automationId)
        where T : DependencyObject =>
        Descendants(root).OfType<T>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == automationId)
        ?? throw new AssertFailedException($"no {typeof(T).Name} {automationId}");

    public static void Press(Button button) => new ButtonAutomationPeer(button).Invoke();

    // A window of its own, activated and laid out.
    public static async Task<T> Activated<T>(T window)
        where T : Window
    {
        var root = (FrameworkElement)window.Content;
        var loaded = new TaskCompletionSource();
        root.Loaded += (_, _) => loaded.TrySetResult();
        window.Activate();
        await loaded.Task.WaitAsync(Webs.Patience);
        return window;
    }

    // Once what the dispatcher has queued ahead of its lowest priority has run, so what was not done meanwhile is not done.
    public static Task Drained()
    {
        var drained = new TaskCompletionSource();
        Assert.IsTrue(
            DispatcherQueue.GetForCurrentThread().TryEnqueue(DispatcherQueuePriority.Low, () => drained.TrySetResult()),
            "the dispatcher took nothing");
        return drained.Task.WaitAsync(Patience);
    }

    public static Task Until(Func<bool> condition, string what, TimeSpan? patience = null) =>
        Until(condition, () => what, patience);

    // What failed is said as it stood at the end.
    public static async Task Until(Func<bool> condition, Func<string> what, TimeSpan? patience = null)
    {
        var waited = patience ?? Patience;
        var deadline = DateTime.UtcNow + waited;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new AssertFailedException($"{what()}, not within {waited}");
            }

            await Task.Delay(50);
        }
    }

    private static bool IsVisible(DependencyObject element, DependencyObject root)
    {
        for (var at = element; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (at is UIElement { Visibility: Visibility.Collapsed })
            {
                return false;
            }

            if (at == root)
            {
                return true;
            }
        }

        return true;
    }
}
