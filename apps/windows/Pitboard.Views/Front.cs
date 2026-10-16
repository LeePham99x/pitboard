using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Pitboard.Views;

public static class Front
{
    // In front, restored first if it was minimized.
    public static void Bring(this Window window)
    {
        if (window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        window.Activate();
    }
}
