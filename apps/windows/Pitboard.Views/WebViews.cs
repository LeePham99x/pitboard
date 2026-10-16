using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Pitboard.Views;

internal static class WebViews
{
    // WebView2 makes its page once the control is in a window's tree.
    public static async Task<CoreWebView2> Made(
        WebView2 view, CoreWebView2Environment environment, CoreWebView2ControllerOptions options)
    {
        if (!view.IsLoaded)
        {
            var loaded = new TaskCompletionSource();
            view.Loaded += (_, _) => loaded.TrySetResult();
            await loaded.Task;
        }

        // A page it could not make, such as one whose profile is still being deleted, is reported here alone.
        Exception? failed = null;
        void Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args) => failed ??= args.Exception;
        view.CoreWebView2Initialized += Initialized;
        try
        {
            await view.EnsureCoreWebView2Async(environment, options);
        }
        finally
        {
            view.CoreWebView2Initialized -= Initialized;
        }

        return view.CoreWebView2 ?? throw failed ?? new InvalidOperationException("WebView2 made no page.");
    }
}
