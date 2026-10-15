using Microsoft.Web.WebView2.Core;
using Pitboard.Core;
using Windows.Foundation;
using WinRT.Interop;

namespace Pitboard.Windows.Tests;

// WebView2 in the test app's window, as the app makes it: a user data folder of the test's own, one browser process
// over it, and a page per profile name. Pages a test sends elsewhere are kept, and opened nowhere.
internal sealed class Webs : IAsyncDisposable
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly DirectoryInfo _folder;
    private readonly List<CoreWebView2Controller> _controllers = [];
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Webs(DirectoryInfo folder, WebEnvironment web, CoreWebView2Environment environment, List<string> elsewhere)
    {
        _folder = folder;
        Web = web;
        Elsewhere = elsewhere;
        environment.BrowserProcessExited += (_, _) => _exited.TrySetResult();
    }

    public WebEnvironment Web { get; }

    public List<string> Elsewhere { get; }

    // Set once every page of the first browser process has closed and it has let go of the folder.
    public Task Exited => _exited.Task;

    public static nint Window => WindowNative.GetWindowHandle(UnitTestApp.Window);

    public static async Task<Webs> Make(string scheme)
    {
        var folder = Directory.CreateTempSubdirectory("pitboard-webview2-");
        var elsewhere = new List<string>();
        var web = new WebEnvironment(scheme, Path.Combine(folder.FullName, "WebView2"), devTools: false, elsewhere.Add);
        return new Webs(folder, web, await web.Made().WaitAsync(Patience), elsewhere);
    }

    // A page in the profile named, which has loaded nothing and answers to nothing of the app's yet.
    public async Task<CoreWebView2> Page(string profile)
    {
        var environment = await Web.Made().WaitAsync(Patience);
        var controller = await environment.CreateCoreWebView2ControllerAsync(
            CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)Window),
            WebEnvironment.Options(environment, profile)).AsTask().WaitAsync(Patience);
        controller.Bounds = new Rect(0, 0, 800, 600);
        _controllers.Add(controller);
        return controller.CoreWebView2;
    }

    // Whether a page loads after `start`. A navigation stopped before it, which may end later, does not count.
    public static async Task<bool> Loads(CoreWebView2 page, Func<Task> start)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess)
            {
                done.TrySetResult();
            }
        }

        page.NavigationCompleted += Completed;
        try
        {
            await start();
            return await Task.WhenAny(done.Task, Task.Delay(Patience)) == done.Task;
        }
        finally
        {
            page.NavigationCompleted -= Completed;
        }
    }

    public static Task<bool> Loads(CoreWebView2 page, string address) =>
        Loads(page, () =>
        {
            page.Navigate(address);
            return Task.CompletedTask;
        });

    public static async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new AssertFailedException($"{what}, not within {Patience}");
            }

            await Task.Delay(50);
        }
    }

    public static OpenWindow Opened(WindowAccount account, string address) =>
        new(
            Store: account.Store, Account: account, Load: new PageLoad(1, address), Note: null,
            ClearDownloads: new Choice("Clear", new Intent.ClearDownloads(account.Store), Enabled: false));

    public async ValueTask DisposeAsync()
    {
        foreach (var controller in _controllers)
        {
            try
            {
                controller.Close();
            }
            catch (Exception closed) when (closed is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
            }
        }

        if (_controllers.Count > 0)
        {
            await Task.WhenAny(Exited, Task.Delay(Patience));
        }

        try
        {
            _folder.Delete(recursive: true);
        }
        catch (Exception held) when (held is IOException or UnauthorizedAccessException)
        {
        }
    }
}
