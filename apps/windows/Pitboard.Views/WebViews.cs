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

        await view.EnsureCoreWebView2Async(environment, options);
        return view.CoreWebView2;
    }
}
