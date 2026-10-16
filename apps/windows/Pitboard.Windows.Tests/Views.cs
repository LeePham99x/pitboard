using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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

    // Shown, and not inside anything collapsed.
    public static IEnumerable<T> Visible<T>(DependencyObject root)
        where T : UIElement =>
        Descendants(root).OfType<T>().Where(element => IsVisible(element, root));

    public static string[] Texts(DependencyObject root) =>
        [.. Visible<TextBlock>(root).Select(text => text.Text).Where(text => text.Length > 0)];

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

    public static async Task Until(Func<bool> condition, string what, TimeSpan? patience = null)
    {
        var waited = patience ?? Patience;
        var deadline = DateTime.UtcNow + waited;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new AssertFailedException($"{what}, not within {waited}");
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
